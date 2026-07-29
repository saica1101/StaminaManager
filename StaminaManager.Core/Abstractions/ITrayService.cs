namespace StaminaManager.Core.Abstractions;

public interface ITrayService : IDisposable
{
    event EventHandler? OpenRequested;

    event EventHandler? ExitRequested;

    event EventHandler<WindowVisibilityChangedEventArgs>?
        WindowVisibilityChanged;

    void Initialize();

    void HideWindow();

    void ShowWindow();
}

public sealed class WindowVisibilityChangedEventArgs(bool isShown)
    : EventArgs
{
    public bool IsShown { get; } = isShown;
}
