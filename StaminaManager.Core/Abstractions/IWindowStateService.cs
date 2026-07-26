using StaminaManager.Core.Models;

namespace StaminaManager.Core.Abstractions;

public interface IWindowStateService
{
    AppDisplayMode CurrentDisplayMode { get; }

    void ApplyDisplayMode(AppDisplayMode displayMode);

    void CaptureCurrent();
}
