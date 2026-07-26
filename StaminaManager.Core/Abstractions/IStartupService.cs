namespace StaminaManager.Core.Abstractions;

public enum StartupState
{
    Disabled,
    DisabledByUser,
    Enabled,
    DisabledByPolicy,
    EnabledByPolicy,
}

public enum StartupFailureReason
{
    None,
    DisabledByUser,
    DisabledByPolicy,
    EnabledByPolicy,
    OperationFailed,
}

public sealed record StartupStatus(StartupState State)
{
    public bool IsEnabled => State is StartupState.Enabled
        or StartupState.EnabledByPolicy;
}

public sealed record StartupChangeResult(
    StartupStatus Status,
    bool IsApplied,
    StartupFailureReason FailureReason);

public interface IStartupService
{
    Task<StartupStatus> GetStatusAsync(
        CancellationToken cancellationToken);

    Task<StartupChangeResult> SetEnabledAsync(
        bool isEnabled,
        CancellationToken cancellationToken);
}
