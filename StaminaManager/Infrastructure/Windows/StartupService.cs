using StaminaManager.Core.Abstractions;
using Windows.ApplicationModel;
using WindowsStartupState = Windows.ApplicationModel.StartupTaskState;

namespace StaminaManager.Infrastructure.Windows;

public sealed class StartupService : IStartupService
{
    private const string StartupTaskId = "StaminaManagerStartup";
    private readonly IStartupTaskAdapter _adapter;

    public StartupService()
        : this(new PackagedStartupTaskAdapter(StartupTaskId))
    {
    }

    internal StartupService(IStartupTaskAdapter adapter)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        _adapter = adapter;
    }

    public async Task<StartupStatus> GetStatusAsync(
        CancellationToken cancellationToken)
    {
        WindowsStartupState state = await _adapter.GetStateAsync(
            cancellationToken);
        return MapStatus(state);
    }

    public async Task<StartupChangeResult> SetEnabledAsync(
        bool isEnabled,
        CancellationToken cancellationToken)
    {
        StartupStatus before = await GetStatusAsync(cancellationToken);
        if (before.IsEnabled == isEnabled)
        {
            return Applied(before);
        }

        if (isEnabled)
        {
            if (before.State is StartupState.DisabledByUser
                or StartupState.DisabledByPolicy)
            {
                return Rejected(before, GetFailureReason(before.State));
            }

            WindowsStartupState requested =
                await _adapter.RequestEnableAsync(cancellationToken);
            return CreateResult(MapStatus(requested), isEnabled);
        }

        if (before.State == StartupState.EnabledByPolicy)
        {
            return Rejected(
                before,
                StartupFailureReason.EnabledByPolicy);
        }

        cancellationToken.ThrowIfCancellationRequested();
        _adapter.Disable();
        StartupStatus after = await GetStatusAsync(cancellationToken);
        return CreateResult(after, isEnabled);
    }

    private static StartupChangeResult CreateResult(
        StartupStatus status,
        bool requestedEnabled) => status.IsEnabled == requestedEnabled
        ? Applied(status)
        : Rejected(status, GetFailureReason(status.State));

    private static StartupChangeResult Applied(StartupStatus status) => new(
        status,
        IsApplied: true,
        StartupFailureReason.None);

    private static StartupChangeResult Rejected(
        StartupStatus status,
        StartupFailureReason reason) => new(
            status,
            IsApplied: false,
            reason);

    private static StartupFailureReason GetFailureReason(
        StartupState state) => state switch
    {
        StartupState.DisabledByUser =>
            StartupFailureReason.DisabledByUser,
        StartupState.DisabledByPolicy =>
            StartupFailureReason.DisabledByPolicy,
        StartupState.EnabledByPolicy =>
            StartupFailureReason.EnabledByPolicy,
        _ => StartupFailureReason.OperationFailed,
    };

    private static StartupStatus MapStatus(
        WindowsStartupState state) => new(state switch
    {
        WindowsStartupState.Disabled => StartupState.Disabled,
        WindowsStartupState.DisabledByUser => StartupState.DisabledByUser,
        WindowsStartupState.Enabled => StartupState.Enabled,
        WindowsStartupState.DisabledByPolicy =>
            StartupState.DisabledByPolicy,
        WindowsStartupState.EnabledByPolicy => StartupState.EnabledByPolicy,
        _ => throw new ArgumentOutOfRangeException(nameof(state)),
    });
}

internal interface IStartupTaskAdapter
{
    Task<WindowsStartupState> GetStateAsync(
        CancellationToken cancellationToken);

    Task<WindowsStartupState> RequestEnableAsync(
        CancellationToken cancellationToken);

    void Disable();
}

internal sealed class PackagedStartupTaskAdapter(string taskId)
    : IStartupTaskAdapter
{
    private StartupTask? _startupTask;

    public async Task<WindowsStartupState> GetStateAsync(
        CancellationToken cancellationToken)
    {
        StartupTask task = await GetTaskAsync(cancellationToken);
        return task.State;
    }

    public async Task<WindowsStartupState> RequestEnableAsync(
        CancellationToken cancellationToken)
    {
        StartupTask task = await GetTaskAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        WindowsStartupState state = await task.RequestEnableAsync();
        cancellationToken.ThrowIfCancellationRequested();
        return state;
    }

    public void Disable()
    {
        StartupTask task = _startupTask ?? throw new InvalidOperationException(
            "StartupTaskの状態を先に読み込む必要があります。");
        task.Disable();
    }

    private async Task<StartupTask> GetTaskAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _startupTask ??= await StartupTask.GetAsync(taskId);
        cancellationToken.ThrowIfCancellationRequested();
        return _startupTask;
    }
}
