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

internal sealed class CoalescingUiAction
{
    private readonly IUiWorkQueue _uiWorkQueue;
    private readonly Action _action;
    private bool _isPending;

    public CoalescingUiAction(
        IUiWorkQueue uiWorkQueue,
        Action action)
    {
        ArgumentNullException.ThrowIfNull(uiWorkQueue);
        ArgumentNullException.ThrowIfNull(action);
        _uiWorkQueue = uiWorkQueue;
        _action = action;
    }

    public bool Request()
    {
        if (_isPending)
        {
            return true;
        }

        _isPending = true;
        try
        {
            bool isQueued = _uiWorkQueue.TryEnqueue(Execute);
            if (!isQueued)
            {
                _isPending = false;
            }

            return isQueued;
        }
        catch
        {
            _isPending = false;
            throw;
        }
    }

    private void Execute()
    {
        _isPending = false;
        _action();
    }
}
