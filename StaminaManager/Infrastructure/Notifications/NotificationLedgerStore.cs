using StaminaManager.Core.Abstractions;
using StaminaManager.Core.Models;
using StaminaManager.Core.Validation;
using StaminaManager.Infrastructure.Persistence;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace StaminaManager.Infrastructure.Notifications;

public sealed class NotificationLedgerStore : INotificationLedgerStore
{
    private const int CurrentSchemaVersion = 1;
    private const int MaxJsonBytes = 1024 * 1024;
    private const string FileName = "notification-state.json";
    private const string TemporaryFileName = "notification-state.json.tmp";
    private static readonly ConcurrentDictionary<string, SemaphoreSlim>
        WriteGates = new(StringComparer.OrdinalIgnoreCase);
    private readonly IAppDataPathProvider _pathProvider;
    private readonly SemaphoreSlim _writeGate;

    public NotificationLedgerStore(IAppDataPathProvider pathProvider)
    {
        ArgumentNullException.ThrowIfNull(pathProvider);
        _pathProvider = pathProvider;
        string dataRootPath = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(pathProvider.DataRootPath));
        _writeGate = WriteGates.GetOrAdd(
            dataRootPath,
            static _ => new SemaphoreSlim(1, 1));
    }

    public async Task<IReadOnlyList<NotificationLedgerEntry>> LoadAsync(
        CancellationToken cancellationToken)
    {
        string path = GetPath(FileName);
        if (!File.Exists(path))
        {
            return Array.Empty<NotificationLedgerEntry>();
        }

        try
        {
            await using FileStream stream = new(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read | FileShare.Delete,
                bufferSize: 16 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (stream.Length > MaxJsonBytes)
            {
                throw new InvalidDataException(
                    "通知台帳が許容サイズを超えています。");
            }

            NotificationLedgerDocument? document =
                await JsonSerializer.DeserializeAsync(
                    stream,
                    NotificationLedgerJsonContext.Configured
                        .NotificationLedgerDocument,
                    cancellationToken).ConfigureAwait(false);
            if (document is null)
            {
                throw new InvalidDataException("通知台帳が空です。");
            }

            Validate(document);
            return document.Entries;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "通知台帳のJSONが正しくありません。",
                exception);
        }
        catch (NotSupportedException exception)
        {
            throw new InvalidDataException(
                "通知台帳の形式を読み取れません。",
                exception);
        }
    }

    public async Task SaveAsync(
        IReadOnlyCollection<NotificationLedgerEntry> entries,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entries);
        NotificationLedgerDocument document = new(
            CurrentSchemaVersion,
            entries.ToImmutableArray());
        Validate(document);

        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        string temporaryPath = GetPath(TemporaryFileName);
        bool ownsTemporaryFile = false;
        try
        {
            Directory.CreateDirectory(_pathProvider.DataRootPath);
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }

            await using (FileStream stream = new(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 16 * 1024,
                FileOptions.Asynchronous))
            {
                ownsTemporaryFile = true;
                await JsonSerializer.SerializeAsync(
                    stream,
                    document,
                    NotificationLedgerJsonContext.Configured
                        .NotificationLedgerDocument,
                    cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken)
                    .ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
                if (stream.Length > MaxJsonBytes)
                {
                    throw new InvalidDataException(
                        "通知台帳が許容サイズを超えています。");
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, GetPath(FileName), overwrite: true);
            ownsTemporaryFile = false;
        }
        finally
        {
            if (ownsTemporaryFile && File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }

            _writeGate.Release();
        }
    }

    private static void Validate(NotificationLedgerDocument document)
    {
        if (document.SchemaVersion != CurrentSchemaVersion
            || document.Entries.IsDefault
            || document.Entries.Length
                > NotificationLedgerLimits.MaxRetainedEntryCount)
        {
            throw new InvalidDataException("通知台帳の形式が正しくありません。");
        }

        HashSet<string> keys = new(StringComparer.Ordinal);
        foreach (NotificationLedgerEntry? entry in document.Entries)
        {
            if (entry is null
                || !IsValidEntry(entry)
                || !keys.Add(entry.Key))
            {
                throw new InvalidDataException(
                    "通知台帳のエントリが正しくありません。");
            }
        }
    }

    private static bool IsValidEntry(NotificationLedgerEntry entry)
    {
        if (entry.GameId == Guid.Empty
            || entry.LeadMinutes is < AppSettings.MinNotificationLeadMinutes
                or > AppSettings.MaxNotificationLeadMinutes
            || !Enum.IsDefined(entry.State)
            || entry.FullAtUtcTicks < DateTimeOffset.MinValue.UtcTicks
            || entry.FullAtUtcTicks > DateTimeOffset.MaxValue.UtcTicks
            || entry.NotificationAtUtcTicks
                < DateTimeOffset.MinValue.UtcTicks
            || !IsValidFingerprint(entry.ContentFingerprint))
        {
            return false;
        }

        long expectedNotificationTicks;
        try
        {
            expectedNotificationTicks = checked(
                entry.FullAtUtcTicks
                - checked(
                    (long)entry.LeadMinutes
                    * TimeSpan.TicksPerMinute));
        }
        catch (OverflowException)
        {
            return false;
        }

        string expectedKey = string.Create(
            CultureInfo.InvariantCulture,
            $"{entry.GameId:N}/{entry.FullAtUtcTicks}/"
                + $"{entry.LeadMinutes}");
        return entry.NotificationAtUtcTicks == expectedNotificationTicks
            && string.Equals(
                entry.Key,
                expectedKey,
                StringComparison.Ordinal);
    }

    private static bool IsValidFingerprint(string? fingerprint)
    {
        if (fingerprint is null || fingerprint.Length != 64)
        {
            return false;
        }

        try
        {
            return Convert.FromHexString(fingerprint).Length == 32
                && string.Equals(
                    fingerprint,
                    fingerprint.ToUpperInvariant(),
                    StringComparison.Ordinal);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private string GetPath(string fileName) => Path.Combine(
        _pathProvider.DataRootPath,
        fileName);
}

internal sealed record NotificationLedgerDocument(
    int SchemaVersion,
    ImmutableArray<NotificationLedgerEntry> Entries);

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    GenerationMode = JsonSourceGenerationMode.Metadata,
    RespectRequiredConstructorParameters = true,
    WriteIndented = false)]
[JsonSerializable(typeof(NotificationLedgerDocument))]
internal sealed partial class NotificationLedgerJsonContext
    : JsonSerializerContext
{
    public static NotificationLedgerJsonContext Configured { get; } =
        new(CreateOptions());

    private static JsonSerializerOptions CreateOptions()
    {
        JsonSerializerOptions options = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            RespectRequiredConstructorParameters = true,
            WriteIndented = false,
        };
        options.Converters.Add(
            new StrictStringEnumConverter<NotificationState>());
        return options;
    }
}
