using StaminaManager.Core.Models;

namespace StaminaManager.Views;

internal sealed class SettingsAppearanceChangeRouter
{
    private readonly DeferredSettingsChangeExecutor _executor;
    private readonly Func<AppTheme, Task> _changeTheme;
    private readonly Func<BackdropKind, Task> _changeBackdrop;
    private readonly Action _reportFailure;
    private readonly Action _synchronizeControls;

    public SettingsAppearanceChangeRouter(
        DeferredSettingsChangeExecutor executor,
        Func<AppTheme, Task> changeTheme,
        Func<BackdropKind, Task> changeBackdrop,
        Action reportFailure,
        Action synchronizeControls)
    {
        ArgumentNullException.ThrowIfNull(executor);
        ArgumentNullException.ThrowIfNull(changeTheme);
        ArgumentNullException.ThrowIfNull(changeBackdrop);
        ArgumentNullException.ThrowIfNull(reportFailure);
        ArgumentNullException.ThrowIfNull(synchronizeControls);

        _executor = executor;
        _changeTheme = changeTheme;
        _changeBackdrop = changeBackdrop;
        _reportFailure = reportFailure;
        _synchronizeControls = synchronizeControls;
    }

    public Task ChangeThemeAsync(AppTheme requestedTheme) =>
        _executor.ExecuteAsync(
            requestedTheme,
            _changeTheme,
            _reportFailure,
            _synchronizeControls);

    public Task ChangeBackdropAsync(BackdropKind requestedBackdrop) =>
        _executor.ExecuteAsync(
            requestedBackdrop,
            _changeBackdrop,
            _reportFailure,
            _synchronizeControls);
}
