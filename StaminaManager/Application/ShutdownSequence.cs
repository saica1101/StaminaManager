namespace StaminaManager.Application;

internal sealed class ShutdownSequence
{
    private readonly Func<Task> _cleanupAsync;
    private readonly Action<Exception> _reportFailure;
    private readonly Action _completeExit;
    private readonly object _stateLock = new();
    private Task? _shutdownTask;
    private bool _isRequested;
    private bool _isCompleted;
    private Exception? _lastError;

    public ShutdownSequence(
        Func<Task> cleanupAsync,
        Action<Exception> reportFailure,
        Action completeExit)
    {
        ArgumentNullException.ThrowIfNull(cleanupAsync);
        ArgumentNullException.ThrowIfNull(reportFailure);
        ArgumentNullException.ThrowIfNull(completeExit);
        _cleanupAsync = cleanupAsync;
        _reportFailure = reportFailure;
        _completeExit = completeExit;
    }

    public bool IsRequested
    {
        get
        {
            lock (_stateLock)
            {
                return _isRequested;
            }
        }
    }

    public bool IsCompleted
    {
        get
        {
            lock (_stateLock)
            {
                return _isCompleted;
            }
        }
    }

    public Exception? LastError
    {
        get
        {
            lock (_stateLock)
            {
                return _lastError;
            }
        }
    }

    public Task RequestAsync()
    {
        lock (_stateLock)
        {
            _isRequested = true;
            return _shutdownTask ??= CompleteAsync();
        }
    }

    private async Task CompleteAsync()
    {
        try
        {
            await _cleanupAsync();
        }
        catch (Exception exception) when (!IsProcessFatal(exception))
        {
            RecordFailure(exception);
        }

        lock (_stateLock)
        {
            _isCompleted = true;
        }

        _completeExit();
    }

    private void RecordFailure(Exception exception)
    {
        lock (_stateLock)
        {
            _lastError = exception;
        }

        try
        {
            _reportFailure(exception);
        }
        catch (Exception reportException)
            when (!IsProcessFatal(reportException))
        {
            lock (_stateLock)
            {
                _lastError = new AggregateException(
                    exception,
                    reportException);
            }
        }
    }

    private static bool IsProcessFatal(Exception exception) =>
        exception is OutOfMemoryException
            or StackOverflowException
            or AccessViolationException
            or AppDomainUnloadedException
            or BadImageFormatException
            or CannotUnloadAppDomainException
            or InvalidProgramException;
}
