namespace StaminaManager.Core.Abstractions;

public interface ITrayService : IDisposable
{
    event EventHandler? OpenRequested;

    event EventHandler? ExitRequested;

    void Initialize();

    void HideWindow();

    void ShowWindow();
}
