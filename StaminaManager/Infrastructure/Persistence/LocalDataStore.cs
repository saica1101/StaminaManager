using StaminaManager.Core.Abstractions;
using StaminaManager.Core.Models;
using StaminaManager.Core.Persistence;
using System.Diagnostics;
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
    private readonly AppLanguage _legacyLanguage;

    public LocalDataStore(
        IAppDataPathProvider pathProvider,
        IAppLanguageService? appLanguageService = null)
    {
        ArgumentNullException.ThrowIfNull(pathProvider);
        _pathProvider = pathProvider;
        _writeGate = AppDataWriteGate.Get(pathProvider);
        _legacyLanguage = appLanguageService?.GetEffectiveLanguage()
            ?? AppLanguage.Japanese;
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

            DecodedDataEnvelope? missingPrimaryRecovery =
                await TryReadValidEnvelopeAsync(
                    recoveryPath,
                    cancellationToken).ConfigureAwait(false);
            return new DataLoadResult(
                missingPrimaryRecovery is null
                    ? DataLoadStatus.Corrupt
                    : DataLoadStatus.Recovery,
                missingPrimaryRecovery?.Envelope,
                primaryPath,
                recoveryPath);
        }

        DecodedDataEnvelope? primary = await TryReadValidEnvelopeAsync(
            primaryPath,
            cancellationToken).ConfigureAwait(false);
        if (primary is not null)
        {
            DataLoadWarning warning = DataLoadWarning.None;
            if (primary.RequiresWriteback)
            {
                bool wasPersisted = await TryPersistMigratedPrimaryAsync(
                    primaryPath,
                    recoveryPath,
                    cancellationToken).ConfigureAwait(false);
                if (!wasPersisted)
                {
                    warning = DataLoadWarning.SchemaMigrationWritebackFailed;
                }
            }

            return new DataLoadResult(
                DataLoadStatus.Primary,
                primary.Envelope,
                primaryPath,
                recoveryPath,
                warning);
        }

        DecodedDataEnvelope? recovery = File.Exists(recoveryPath)
            ? await TryReadValidEnvelopeAsync(
                recoveryPath,
                cancellationToken).ConfigureAwait(false)
            : null;
        return new DataLoadResult(
            recovery is null
                ? DataLoadStatus.Corrupt
                : DataLoadStatus.Recovery,
            recovery?.Envelope,
            primaryPath,
            recoveryPath);
    }

    public async Task SaveAsync(
        DataEnvelope envelope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        DataEnvelope normalizedEnvelope =
            DataEnvelopeCodec.NormalizeAndValidate(envelope);
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

            DecodedDataEnvelope? recovery = File.Exists(recoveryPath)
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
                recovery.Envelope,
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
                recovery.Envelope,
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

    private async Task<DecodedDataEnvelope?> TryReadValidEnvelopeAsync(
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

            using JsonDocument document = await JsonDocument.ParseAsync(
                stream,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            return DataEnvelopeCodec.Deserialize(
                document.RootElement,
                _legacyLanguage);
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

    private async Task<bool> TryPersistMigratedPrimaryAsync(
        string primaryPath,
        string recoveryPath,
        CancellationToken cancellationToken)
    {
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        string temporaryPath = GetPath(TemporaryFileName);
        bool ownsTemporaryFile = false;
        try
        {
            DecodedDataEnvelope? current =
                await TryReadValidEnvelopeAsync(
                    primaryPath,
                    cancellationToken).ConfigureAwait(false);
            if (current is null || !current.RequiresWriteback)
            {
                return current is not null;
            }

            DeleteStaleTemporaryFile(temporaryPath);
            await WriteTemporaryEnvelopeAsync(
                current.Envelope,
                temporaryPath,
                cancellationToken).ConfigureAwait(false);
            ownsTemporaryFile = true;
            cancellationToken.ThrowIfCancellationRequested();
            File.Replace(
                temporaryPath,
                primaryPath,
                recoveryPath,
                ignoreMetadataErrors: true);
            return true;
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidDataException)
        {
            Debug.WriteLine(
                "Schema migration writeback failed: "
                + exception.GetType().Name);
            return false;
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
