using StaminaManager.Application;

namespace StaminaManager.Tests.TestDoubles;

internal sealed class RecordingUiDispatcher : IUiDispatcher
{
    public int InvocationCount { get; private set; }

    public bool IsExecuting { get; private set; }

    public Task InvokeAsync(
        Action action,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        InvocationCount++;
        IsExecuting = true;
        try
        {
            action();
            return Task.CompletedTask;
        }
        finally
        {
            IsExecuting = false;
        }
    }
}
