using StaminaManager.Infrastructure.Windows;

namespace StaminaManager.Views;

internal sealed class DeferredSettingsChangeExecutor
{
    private readonly IUiWorkQueue _uiWorkQueue;
    private readonly SemaphoreSlim _executionGate = new(1, 1);

    public DeferredSettingsChangeExecutor(IUiWorkQueue uiWorkQueue)
    {
        ArgumentNullException.ThrowIfNull(uiWorkQueue);
        _uiWorkQueue = uiWorkQueue;
    }

    public Task ExecuteAsync(
        Func<Task> settingChange,
        Action reportFailure,
        Action synchronizeControls)
    {
        ArgumentNullException.ThrowIfNull(settingChange);
        ArgumentNullException.ThrowIfNull(reportFailure);
        ArgumentNullException.ThrowIfNull(synchronizeControls);

        TaskCompletionSource completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        bool isQueued = _uiWorkQueue.TryEnqueue(
            () => _ = ExecuteQueuedAsync(
                settingChange,
                reportFailure,
                synchronizeControls,
                completion));
        if (!isQueued)
        {
            CompleteEnqueueFailure(
                reportFailure,
                synchronizeControls,
                completion);
        }

        return completion.Task;
    }

    private async Task ExecuteQueuedAsync(
        Func<Task> settingChange,
        Action reportFailure,
        Action synchronizeControls,
        TaskCompletionSource completion)
    {
        await _executionGate.WaitAsync();
        try
        {
            await SettingsChangeExecutor.ExecuteAsync(
                settingChange,
                reportFailure,
                synchronizeControls);
        }
        catch (Exception exception)
        {
            completion.TrySetException(exception);
            return;
        }
        finally
        {
            _executionGate.Release();
        }

        completion.TrySetResult();
    }

    private static void CompleteEnqueueFailure(
        Action reportFailure,
        Action synchronizeControls,
        TaskCompletionSource completion)
    {
        try
        {
            reportFailure();
        }
        catch (Exception exception)
        {
            completion.TrySetException(exception);
            return;
        }
        finally
        {
            try
            {
                synchronizeControls();
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        }

        completion.TrySetResult();
    }
}
