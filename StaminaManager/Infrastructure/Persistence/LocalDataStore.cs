using StaminaManager.Core.Abstractions;
using StaminaManager.Core.Models;
using StaminaManager.Core.Persistence;
using StaminaManager.Core.Validation;
using System.Collections.Immutable;
using System.Text.Json;

namespace StaminaManager.Infrastructure.Persistence;

public sealed class LocalDataStore : ILocalDataStore
{
    public const int MaxJsonBytes = 4 * 1024 * 1024;

    private const string PrimaryFileName = "data.json";
    private const string RecoveryFileName = "data.recovery.json";
    private const string TemporaryFileName = "data.json.tmp";
    private readonly IAppDataPathProvider _pathProvider;
    private readonly SemaphoreSlim _writeGate;

    public LocalDataStore(IAppDataPathProvider pathProvider)
    {
        ArgumentNullException.ThrowIfNull(pathProvider);
        _pathProvider = pathProvider;
        _writeGate = AppDataWriteGate.Get(pathProvider);
    }

    public async Task<DataLoadResult> LoadAsync(
        CancellationToken cancellationToken)
    {
        string primaryPath = GetPath(PrimaryFileName);
        string recoveryPath = GetPath(RecoveryFileName);
        if (!File.Exists(primaryPath))
        {
            if (!File.Exists(recoveryPath))
            {
                return new DataLoadResult(
                    DataLoadStatus.Empty,
                    null,
                    primaryPath,
                    recoveryPath);
            }

            DataEnvelope? missingPrimaryRecovery =
                await TryReadValidEnvelopeAsync(
                    recoveryPath,
                    cancellationToken).ConfigureAwait(false);
            return new DataLoadResult(
                missingPrimaryRecovery is null
                    ? DataLoadStatus.Corrupt
                    : DataLoadStatus.Recovery,
                missingPrimaryRecovery,
                primaryPath,
                recoveryPath);
        }

        DataEnvelope? primary = await TryReadValidEnvelopeAsync(
            primaryPath,
            cancellationToken).ConfigureAwait(false);
        if (primary is not null)
        {
            return new DataLoadResult(
                DataLoadStatus.Primary,
                primary,
                primaryPath,
                recoveryPath);
        }

        DataEnvelope? recovery = File.Exists(recoveryPath)
            ? await TryReadValidEnvelopeAsync(
                recoveryPath,
                cancellationToken).ConfigureAwait(false)
            : null;
        return new DataLoadResult(
            recovery is null
                ? DataLoadStatus.Corrupt
                : DataLoadStatus.Recovery,
            recovery,
            primaryPath,
            recoveryPath);
    }

    public async Task SaveAsync(
        DataEnvelope envelope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        DataEnvelope normalizedEnvelope = NormalizeAndValidate(envelope);
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        string temporaryPath = GetPath(TemporaryFileName);
        bool ownsTemporaryFile = false;
        try
        {
            Directory.CreateDirectory(_pathProvider.DataRootPath);
            DeleteStaleTemporaryFile(temporaryPath);
            string primaryPath = GetPath(PrimaryFileName);
            string recoveryPath = GetPath(RecoveryFileName);
            bool hasPrimary = File.Exists(primaryPath);
            if (hasPrimary && await TryReadValidEnvelopeAsync(
                primaryPath,
                cancellationToken).ConfigureAwait(false) is null)
            {
                throw new InvalidDataException(
                    "The primary data file is invalid and was preserved.");
            }

            await WriteTemporaryEnvelopeAsync(
                normalizedEnvelope,
                temporaryPath,
                cancellationToken).ConfigureAwait(false);
            ownsTemporaryFile = true;
            cancellationToken.ThrowIfCancellationRequested();
            if (hasPrimary)
            {
                File.Replace(
                    temporaryPath,
                    primaryPath,
                    recoveryPath,
                    ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temporaryPath, primaryPath);
            }
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

    public async Task<RecoveryPromotionResult> PromoteRecoveryAsync(
        CancellationToken cancellationToken)
    {
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        string temporaryPath = GetPath(TemporaryFileName);
        bool ownsTemporaryFile = false;
        try
        {
            Directory.CreateDirectory(_pathProvider.DataRootPath);
            DeleteStaleTemporaryFile(temporaryPath);
            string primaryPath = GetPath(PrimaryFileName);
            string recoveryPath = GetPath(RecoveryFileName);
            if (File.Exists(primaryPath)
                && await TryReadValidEnvelopeAsync(
                    primaryPath,
                    cancellationToken).ConfigureAwait(false) is not null)
            {
                throw new InvalidOperationException(
                    "A valid primary data file cannot be replaced by recovery.");
            }

            DataEnvelope? recovery = File.Exists(recoveryPath)
                ? await TryReadValidEnvelopeAsync(
                    recoveryPath,
                    cancellationToken).ConfigureAwait(false)
                : null;
            if (recovery is null)
            {
                throw new InvalidDataException(
                    "A valid recovery snapshot is not available.");
            }

            await WriteTemporaryEnvelopeAsync(
                recovery,
                temporaryPath,
                cancellationToken).ConfigureAwait(false);
            ownsTemporaryFile = true;
            cancellationToken.ThrowIfCancellationRequested();
            string? diagnosticBackupPath = null;
            if (File.Exists(primaryPath))
            {
                diagnosticBackupPath = GetUniqueDiagnosticPath();
                File.Replace(
                    temporaryPath,
                    primaryPath,
                    diagnosticBackupPath,
                    ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temporaryPath, primaryPath);
            }

            return new RecoveryPromotionResult(
                recovery,
                primaryPath,
                recoveryPath,
                diagnosticBackupPath);
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

    private static DataEnvelope NormalizeAndValidate(DataEnvelope envelope)
    {
        if (envelope.SchemaVersion != DataEnvelope.CurrentSchemaVersion
            || envelope.Games.IsDefault
            || envelope.Games.Length > GameEntryValidator.MaxGameCount
            || envelope.Settings is null)
        {
            throw new InvalidDataException("The data envelope is invalid.");
        }

        ValidateSettings(envelope.Settings);
        HashSet<Guid> ids = [];
        HashSet<int> sortOrders = [];
        ImmutableArray<GameEntry>.Builder games =
            ImmutableArray.CreateBuilder<GameEntry>(envelope.Games.Length);
        foreach (GameEntry? game in envelope.Games)
        {
            if (game is null
                || game.Id == Guid.Empty
                || !ids.Add(game.Id)
                || game.SortOrder < 0
                || game.SortOrder >= envelope.Games.Length
                || !sortOrders.Add(game.SortOrder)
                || !IsValidAssetId(game.ImageAssetId))
            {
                throw new InvalidDataException("A game entry is invalid.");
            }

            DateTimeOffset recordedAtUtc =
                game.RecordedAtUtc.ToUniversalTime();
            ValidationResult validation = GameEntryValidator.Validate(
                new GameDraft(
                    game.Name,
                    game.BaseStamina,
                    game.MaxStamina,
                    game.RecoveryMinutes,
                    game.ImageAssetId,
                    RecoverySeconds: 0,
                    IsNotificationEnabled: true),
                recordedAtUtc);
            if (!validation.IsValid)
            {
                throw new InvalidDataException("A game entry is invalid.");
            }

            games.Add(game with { RecordedAtUtc = recordedAtUtc });
        }

        return envelope with { Games = games.MoveToImmutable() };
    }

    private static void ValidateSettings(AppSettings settings)
    {
        if (!Enum.IsDefined(settings.Theme)
            || !Enum.IsDefined(settings.Backdrop)
            || !Enum.IsDefined(settings.CloseBehavior)
            || !Enum.IsDefined(settings.LastDisplayMode)
            || settings.SelectedCompactGameId == Guid.Empty
            || settings.NotificationLeadMinutes is < 0
                or > GameEntryValidator.MaxRecoveryMinutes)
        {
            throw new InvalidDataException("The app settings are invalid.");
        }
    }

    private static bool IsValidAssetId(string? assetId)
    {
        if (assetId is null)
        {
            return true;
        }

        return Guid.TryParseExact(assetId, "N", out Guid parsed)
            && string.Equals(
                assetId,
                parsed.ToString("N"),
                StringComparison.Ordinal);
    }

    private async Task<DataEnvelope?> TryReadValidEnvelopeAsync(
        string path,
        CancellationToken cancellationToken)
    {
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
                return null;
            }

            DataEnvelope? envelope = await JsonSerializer.DeserializeAsync(
                stream,
                JsonSerializationContext.Configured.DataEnvelope,
                cancellationToken).ConfigureAwait(false);
            return envelope is null
                ? null
                : NormalizeAndValidate(envelope);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    private static async Task WriteTemporaryEnvelopeAsync(
        DataEnvelope envelope,
        string temporaryPath,
        CancellationToken cancellationToken)
    {
        bool ownsTemporaryFile = false;
        try
        {
            await using FileStream stream = new(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 16 * 1024,
                FileOptions.Asynchronous);
            ownsTemporaryFile = true;
            SizeLimitedWriteStream limited = new(stream, MaxJsonBytes);
            await JsonSerializer.SerializeAsync(
                limited,
                envelope,
                JsonSerializationContext.Configured.DataEnvelope,
                cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            stream.Flush(flushToDisk: true);
        }
        catch
        {
            if (ownsTemporaryFile && File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }

            throw;
        }
    }

    private static void DeleteStaleTemporaryFile(string temporaryPath)
    {
        if (File.Exists(temporaryPath))
        {
            File.Delete(temporaryPath);
        }
    }

    private string GetUniqueDiagnosticPath() => Path.Combine(
        _pathProvider.DataRootPath,
        $"data.corrupt.{Guid.NewGuid():N}.json");

    private string GetPath(string fileName) =>
        Path.Combine(_pathProvider.DataRootPath, fileName);

    private sealed class SizeLimitedWriteStream(
        Stream inner,
        long maxBytes) : Stream
    {
        private long _written;

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => _written;

        public override long Position
        {
            get => _written;
            set => throw new NotSupportedException();
        }

        public override void Flush() => inner.Flush();

        public override Task FlushAsync(CancellationToken cancellationToken) =>
            inner.FlushAsync(cancellationToken);

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
        {
            EnsureWithinLimit(count);
            inner.Write(buffer, offset, count);
            _written += count;
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            EnsureWithinLimit(buffer.Length);
            inner.Write(buffer);
            _written += buffer.Length;
        }

        public override async Task WriteAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
        {
            EnsureWithinLimit(count);
            await inner.WriteAsync(
                buffer.AsMemory(offset, count),
                cancellationToken).ConfigureAwait(false);
            _written += count;
        }

        public override async ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            EnsureWithinLimit(buffer.Length);
            await inner.WriteAsync(buffer, cancellationToken)
                .ConfigureAwait(false);
            _written += buffer.Length;
        }

        private void EnsureWithinLimit(int count)
        {
            if (_written + count > maxBytes)
            {
                throw new InvalidDataException(
                    "JSON data must be 4 MiB or smaller.");
            }
        }
    }
}
