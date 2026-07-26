namespace StaminaManager.Application;

internal sealed class ActivationQueue<T>
    where T : notnull
{
    private readonly Lock _gate = new();
    private readonly Queue<T> _pending = new();
    private Action<T>? _handler;

    public void Enqueue(T activation)
    {
        Action<T>? handler;
        lock (_gate)
        {
            handler = _handler;
            if (handler is null)
            {
                _pending.Enqueue(activation);
                return;
            }
        }

        handler(activation);
    }

    public void Attach(Action<T> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        T[] pending;
        lock (_gate)
        {
            if (_handler is not null)
            {
                throw new InvalidOperationException(
                    "Activation handlerは既に登録されています。");
            }

            _handler = handler;
            pending = [.. _pending];
            _pending.Clear();
        }

        foreach (T activation in pending)
        {
            handler(activation);
        }
    }
}
