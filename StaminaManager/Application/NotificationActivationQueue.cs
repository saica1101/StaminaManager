using System.Collections.Concurrent;
using System.Diagnostics;

namespace StaminaManager.Application;

internal sealed class NotificationActivationQueue
{
    private readonly ConcurrentQueue<Guid> _pending = new();
    private readonly ConcurrentDictionary<Guid, byte> _pendingIds = new();
    private int _isDraining;

    public bool Enqueue(Guid gameId)
    {
        if (!_pendingIds.TryAdd(gameId, 0))
        {
            return false;
        }

        _pending.Enqueue(gameId);
        return true;
    }

    public async Task DrainAsync(
        Func<Guid, Task> activationHandler,
        Func<Guid, Exception, Task> failureHandler)
    {
        ArgumentNullException.ThrowIfNull(activationHandler);
        ArgumentNullException.ThrowIfNull(failureHandler);
        if (Interlocked.Exchange(ref _isDraining, 1) != 0)
        {
            return;
        }

        while (true)
        {
            try
            {
                while (_pending.TryDequeue(out Guid gameId))
                {
                    try
                    {
                        await activationHandler(gameId);
                    }
                    catch (Exception exception) when (
                        !IsProcessFatal(exception))
                    {
                        try
                        {
                            await failureHandler(gameId, exception);
                        }
                        catch (Exception failureException) when (
                            !IsProcessFatal(failureException))
                        {
                            Debug.WriteLine(
                                "Notification activation failure reporting "
                                + "failed: "
                                + failureException.GetType().Name);
                        }
                    }
                    finally
                    {
                        _pendingIds.TryRemove(gameId, out _);
                    }
                }
            }
            finally
            {
                Interlocked.Exchange(ref _isDraining, 0);
            }

            if (_pending.IsEmpty
                || Interlocked.Exchange(ref _isDraining, 1) != 0)
            {
                return;
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
