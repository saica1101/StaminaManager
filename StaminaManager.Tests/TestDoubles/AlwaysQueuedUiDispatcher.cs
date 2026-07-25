using StaminaManager.Application;
using System.Collections.Concurrent;

namespace StaminaManager.Tests.TestDoubles;

internal sealed class AlwaysQueuedUiDispatcher : IUiDispatcher
{
    private readonly ConcurrentQueue<QueuedInvocation> _invocations = new();
    private readonly TaskCompletionSource _invocationQueued = CreateSource();

    public Task InvocationQueued => _invocationQueued.Task;


    public Task InvokeAsync(
        Action action,
        CancellationToken cancellationToken = default)
    {
        TaskCompletionSource completion = CreateSource();
        _invocations.Enqueue(new QueuedInvocation(
            action,
            cancellationToken,
            completion));
        _invocationQueued.TrySetResult();
        return completion.Task;
    }

    public void DrainOne()
    {
        if (!_invocations.TryDequeue(out QueuedInvocation? invocation))
        {
            throw new InvalidOperationException("No invocation is queued.");
        }

        try
        {
            invocation.CancellationToken.ThrowIfCancellationRequested();
            invocation.Action();
            invocation.Completion.TrySetResult();
        }
        catch (Exception exception)
        {
            invocation.Completion.TrySetException(exception);
        }
    }

    private static TaskCompletionSource CreateSource() => new(
        TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed record QueuedInvocation(
        Action Action,
        CancellationToken CancellationToken,
        TaskCompletionSource Completion);
}
