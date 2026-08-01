using Microsoft.UI.Dispatching;

namespace StaminaManager.Infrastructure.Windows;

internal interface IUiWorkQueue
{
    bool TryEnqueue(Action action);
}

internal sealed class DispatcherQueueUiWorkQueue : IUiWorkQueue
{
    private readonly DispatcherQueue _dispatcherQueue;

    public DispatcherQueueUiWorkQueue(DispatcherQueue dispatcherQueue)
    {
        ArgumentNullException.ThrowIfNull(dispatcherQueue);
        _dispatcherQueue = dispatcherQueue;
    }

    public bool TryEnqueue(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return _dispatcherQueue.TryEnqueue(
            DispatcherQueuePriority.Normal,
            new DispatcherQueueHandler(action));
    }
}
