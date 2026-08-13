using StaminaManager.Core.Models;

namespace StaminaManager.Views;

internal sealed class SettingsAppearanceChangeRouter
{
    private readonly DeferredSettingsChangeExecutor _executor;
    private readonly Func<AppTheme, Task> _changeTheme;
    private readonly Func<BackdropKind, Task> _changeBackdrop;
    private readonly Func<int, Task<bool>> _previewAcrylicOpacity;
    private readonly Func<int, Task<bool>> _commitAcrylicOpacity;
    private readonly Action _reportFailure;
    private readonly Action _synchronizeControls;

    public SettingsAppearanceChangeRouter(
        DeferredSettingsChangeExecutor executor,
        Func<AppTheme, Task> changeTheme,
        Func<BackdropKind, Task> changeBackdrop,
        Action reportFailure,
        Action synchronizeControls,
        Func<int, Task<bool>>? previewAcrylicOpacity = null,
        Func<int, Task<bool>>? commitAcrylicOpacity = null)
    {
        ArgumentNullException.ThrowIfNull(executor);
        ArgumentNullException.ThrowIfNull(changeTheme);
        ArgumentNullException.ThrowIfNull(changeBackdrop);
        ArgumentNullException.ThrowIfNull(reportFailure);
        ArgumentNullException.ThrowIfNull(synchronizeControls);

        _executor = executor;
        _changeTheme = changeTheme;
        _changeBackdrop = changeBackdrop;
        _previewAcrylicOpacity = previewAcrylicOpacity
            ?? (_ => Task.FromResult(false));
        _commitAcrylicOpacity = commitAcrylicOpacity
            ?? (_ => Task.FromResult(false));
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

    public bool PreviewAcrylicTintOpacity(int percent)
    {
        bool previewed;
        try
        {
            previewed = _previewAcrylicOpacity(percent)
                .GetAwaiter()
                .GetResult();
        }
        catch (OperationCanceledException)
        {
            previewed = false;
        }
        catch (Exception)
        {
            previewed = false;
            _reportFailure();
        }

        if (!previewed)
        {
            _synchronizeControls();
        }

        return previewed;
    }

    public Task<bool> PreviewAcrylicTintOpacityAsync(int percent) =>
        ExecuteOpacityAsync(percent, _previewAcrylicOpacity);

    public Task<bool> CommitAcrylicTintOpacityAsync(int percent) =>
        ExecuteOpacityAsync(percent, _commitAcrylicOpacity);

    private async Task<bool> ExecuteOpacityAsync(
        int percent,
        Func<int, Task<bool>> change)
    {
        TaskCompletionSource<bool> result = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Task execution = _executor.ExecuteAsync(
            async () =>
            {
                try
                {
                    result.TrySetResult(await change(percent));
                }
                catch (OperationCanceledException)
                {
                    result.TrySetResult(false);
                }
                catch (Exception)
                {
                    result.TrySetResult(false);
                    throw;
                }
            },
            () =>
            {
                result.TrySetResult(false);
                _reportFailure();
            },
            _synchronizeControls);
        await execution;
        return await result.Task;
    }
}
