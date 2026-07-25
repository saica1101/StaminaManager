using Microsoft.UI.Dispatching;
using StaminaManager.Application;

namespace StaminaManager.Infrastructure.Windows;

public sealed class DispatcherQueueUiDispatcher : IUiDispatcher
{
    private readonly DispatcherQueue _dispatcherQueue;

    public DispatcherQueueUiDispatcher(DispatcherQueue dispatcherQueue)
    {
        ArgumentNullException.ThrowIfNull(dispatcherQueue);
        _dispatcherQueue = dispatcherQueue;
    }

    public async Task InvokeAsync(
        Action action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        cancellationToken.ThrowIfCancellationRequested();
        if (_dispatcherQueue.HasThreadAccess)
        {
            action();
            return;
        }

        TaskCompletionSource completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using CancellationTokenRegistration registration =
            cancellationToken.Register(
                () => completion.TrySetCanceled(cancellationToken));
        bool queued = _dispatcherQueue.TryEnqueue(() =>
        {
            if (cancellationToken.IsCancellationRequested)
            {
                completion.TrySetCanceled(cancellationToken);
                return;
            }

            try
            {
                action();
                completion.TrySetResult();
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        });
        if (!queued)
        {
            throw new InvalidOperationException(
                "UIスレッドへ処理を送信できませんでした。");
        }

        await completion.Task.ConfigureAwait(false);
    }
}
