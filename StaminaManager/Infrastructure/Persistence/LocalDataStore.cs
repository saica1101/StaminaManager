using StaminaManager.Core.Abstractions;
using StaminaManager.Core.Models;
using StaminaManager.Core.Persistence;
using System.Collections.Immutable;
using System.Text.Json;

namespace StaminaManager.Infrastructure.Persistence;

public sealed class LocalDataStore : ILocalDataStore
{
    private const string PrimaryFileName = "data.json";
    private const string RecoveryFileName = "data.recovery.json";
    private const string TemporaryFileName = "data.json.tmp";
    private readonly IAppDataPathProvider _pathProvider;

    public LocalDataStore(IAppDataPathProvider pathProvider)
    {
        ArgumentNullException.ThrowIfNull(pathProvider);
        _pathProvider = pathProvider;
    }

    public async Task<DataLoadResult> LoadAsync(
        CancellationToken cancellationToken)
    {
        string primaryPath = GetPath(PrimaryFileName);
        string recoveryPath = GetPath(RecoveryFileName);
        if (!File.Exists(primaryPath))
        {
            return new DataLoadResult(
                DataLoadStatus.Empty,
                null,
                primaryPath,
                recoveryPath);
        }

        DataEnvelope? primary = await TryReadValidEnvelopeAsync(
            primaryPath,
            cancellationToken);
        if (primary is not null)
        {
            return new DataLoadResult(
                DataLoadStatus.Primary,
                primary,
                primaryPath,
                recoveryPath);
        }

        DataEnvelope? recovery = File.Exists(recoveryPath)
            ? await TryReadValidEnvelopeAsync(recoveryPath, cancellationToken)
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
        DataEnvelope normalizedEnvelope = Normalize(envelope);
        Directory.CreateDirectory(_pathProvider.DataRootPath);

        string primaryPath = GetPath(PrimaryFileName);
        string recoveryPath = GetPath(RecoveryFileName);
        string temporaryPath = GetPath(TemporaryFileName);
        bool hasPrimary = File.Exists(primaryPath);
        if (hasPrimary && await TryReadValidEnvelopeAsync(
            primaryPath,
            cancellationToken) is null)
        {
            throw new InvalidDataException(
                "The primary data file is invalid and was preserved.");
        }

        bool ownsTemporaryFile = false;
        try
        {
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
                    normalizedEnvelope,
                    JsonSerializationContext.Default.DataEnvelope,
                    cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }

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
        }
    }

    private static DataEnvelope Normalize(DataEnvelope envelope)
    {
        if (!IsSupported(envelope))
        {
            throw new InvalidDataException(
                $"Schema version {envelope.SchemaVersion} is not supported.");
        }

        ImmutableArray<GameEntry>.Builder games =
            ImmutableArray.CreateBuilder<GameEntry>(envelope.Games.Length);
        foreach (GameEntry game in envelope.Games)
        {
            games.Add(game with
            {
                RecordedAtUtc = game.RecordedAtUtc.ToUniversalTime(),
            });
        }

        return envelope with { Games = games.MoveToImmutable() };
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
                FileShare.Read,
                bufferSize: 16 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            DataEnvelope? envelope = await JsonSerializer.DeserializeAsync(
                stream,
                JsonSerializationContext.Default.DataEnvelope,
                cancellationToken);
            return envelope is not null && IsSupported(envelope)
                ? envelope
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    private static bool IsSupported(DataEnvelope envelope) =>
        envelope.SchemaVersion == DataEnvelope.CurrentSchemaVersion
        && !envelope.Games.IsDefault
        && envelope.Settings is not null;

    private string GetPath(string fileName) =>
        Path.Combine(_pathProvider.DataRootPath, fileName);
}
