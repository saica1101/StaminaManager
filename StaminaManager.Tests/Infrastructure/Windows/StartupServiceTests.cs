using StaminaManager.Core.Abstractions;
using StaminaManager.Infrastructure.Windows;
using WindowsStartupState = Windows.ApplicationModel.StartupTaskState;

namespace StaminaManager.Tests.Infrastructure.Windows;

[TestClass]
public sealed class StartupServiceTests
{
    [TestMethod]
    [DataRow(WindowsStartupState.Disabled, StartupState.Disabled)]
    [DataRow(WindowsStartupState.DisabledByUser, StartupState.DisabledByUser)]
    [DataRow(WindowsStartupState.Enabled, StartupState.Enabled)]
    [DataRow(WindowsStartupState.DisabledByPolicy, StartupState.DisabledByPolicy)]
    [DataRow(WindowsStartupState.EnabledByPolicy, StartupState.EnabledByPolicy)]
    public async Task GetStatusAsync_Windowsの5状態を型付き状態へ写像する(
        WindowsStartupState windowsState,
        StartupState expected)
    {
        FakeStartupTaskAdapter adapter = new(windowsState);
        StartupService service = new(adapter);

        StartupStatus status = await service.GetStatusAsync(
            CancellationToken.None);

        Assert.AreEqual(expected, status.State);
    }

    [TestMethod]
    [DataRow(
        WindowsStartupState.DisabledByUser,
        StartupFailureReason.DisabledByUser)]
    [DataRow(
        WindowsStartupState.DisabledByPolicy,
        StartupFailureReason.DisabledByPolicy)]
    public async Task SetEnabledAsync_再有効化不能状態ではRequestしない(
        WindowsStartupState windowsState,
        StartupFailureReason expectedReason)
    {
        FakeStartupTaskAdapter adapter = new(windowsState);
        StartupService service = new(adapter);

        StartupChangeResult result = await service.SetEnabledAsync(
            isEnabled: true,
            CancellationToken.None);

        Assert.IsFalse(result.IsApplied);
        Assert.AreEqual(expectedReason, result.FailureReason);
        Assert.AreEqual(0, adapter.RequestEnableCount);
    }

    [TestMethod]
    public async Task SetEnabledAsync_DisabledではRequest結果を返す()
    {
        FakeStartupTaskAdapter adapter = new(WindowsStartupState.Disabled)
        {
            RequestResult = WindowsStartupState.Enabled,
        };
        StartupService service = new(adapter);

        StartupChangeResult result = await service.SetEnabledAsync(
            isEnabled: true,
            CancellationToken.None);

        Assert.IsTrue(result.IsApplied);
        Assert.AreEqual(StartupState.Enabled, result.Status.State);
        Assert.AreEqual(1, adapter.RequestEnableCount);
    }

    [TestMethod]
    public async Task SetEnabledAsync_EnabledではDisable後の実状態を返す()
    {
        FakeStartupTaskAdapter adapter = new(WindowsStartupState.Enabled)
        {
            StateAfterDisable = WindowsStartupState.Disabled,
        };
        StartupService service = new(adapter);

        StartupChangeResult result = await service.SetEnabledAsync(
            isEnabled: false,
            CancellationToken.None);

        Assert.IsTrue(result.IsApplied);
        Assert.AreEqual(StartupState.Disabled, result.Status.State);
        Assert.AreEqual(1, adapter.DisableCount);
    }

    [TestMethod]
    public async Task SetEnabledAsync_EnabledByPolicyはDisableしない()
    {
        FakeStartupTaskAdapter adapter = new(
            WindowsStartupState.EnabledByPolicy);
        StartupService service = new(adapter);

        StartupChangeResult result = await service.SetEnabledAsync(
            isEnabled: false,
            CancellationToken.None);

        Assert.IsFalse(result.IsApplied);
        Assert.AreEqual(
            StartupFailureReason.EnabledByPolicy,
            result.FailureReason);
        Assert.AreEqual(0, adapter.DisableCount);
    }

    private sealed class FakeStartupTaskAdapter : IStartupTaskAdapter
    {
        private WindowsStartupState _state;

        public FakeStartupTaskAdapter(WindowsStartupState state)
        {
            _state = state;
            RequestResult = state;
            StateAfterDisable = state;
        }

        public WindowsStartupState RequestResult { get; init; }

        public WindowsStartupState StateAfterDisable { get; init; }

        public int RequestEnableCount { get; private set; }

        public int DisableCount { get; private set; }

        public Task<WindowsStartupState> GetStateAsync(
            CancellationToken cancellationToken) => Task.FromResult(_state);

        public Task<WindowsStartupState> RequestEnableAsync(
            CancellationToken cancellationToken)
        {
            RequestEnableCount++;
            _state = RequestResult;
            return Task.FromResult(_state);
        }

        public void Disable()
        {
            DisableCount++;
            _state = StateAfterDisable;
        }
    }
}
