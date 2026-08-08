using Microsoft.UI.Xaml;
using StaminaManager.Infrastructure.Windows;

namespace StaminaManager.Tests.Infrastructure.Windows;

[TestClass]
public sealed class AdjustableAcrylicBackdropTests
{
    [TestMethod]
    public void Lifecycle_0_50_100PercentをTintOpacityへ変換する()
    {
        RecordingController controller = new();
        MutableStateSource stateSource = new();
        AdjustableAcrylicLifecycle lifecycle = new(
            controller,
            stateSource,
            static () => { },
            static () => { });

        lifecycle.Connect();
        lifecycle.SetTintOpacityPercent(0);
        Assert.AreEqual(0.0f, controller.TintOpacity);

        lifecycle.SetTintOpacityPercent(50);
        Assert.AreEqual(0.5f, controller.TintOpacity);

        lifecycle.SetTintOpacityPercent(100);
        Assert.AreEqual(1.0f, controller.TintOpacity);
    }

    [TestMethod]
    public void Lifecycle_ThemeChangeはReset後にTintOpacityを再適用する()
    {
        RecordingController controller = new();
        MutableStateSource stateSource = new();
        AdjustableAcrylicLifecycle lifecycle = new(
            controller,
            stateSource,
            static () => { },
            static () => { });

        lifecycle.Connect();
        lifecycle.SetTintOpacityPercent(80);
        stateSource.SetTheme(ElementTheme.Dark);

        Assert.AreEqual(1, controller.ResetPropertiesCallCount);
        Assert.AreEqual(0.8f, controller.TintOpacity);
        Assert.AreEqual(ElementTheme.Dark, controller.LastState.Theme);
    }

    [TestMethod]
    public void Lifecycle_DisconnectはTargetとEventとControllerを解放する()
    {
        RecordingController controller = new();
        MutableStateSource stateSource = new();
        int attachCount = 0;
        int detachCount = 0;
        AdjustableAcrylicLifecycle lifecycle = new(
            controller,
            stateSource,
            () => attachCount++,
            () => detachCount++);

        lifecycle.Connect();
        lifecycle.Disconnect();
        stateSource.SetTheme(ElementTheme.Dark);
        lifecycle.Disconnect();

        Assert.AreEqual(1, attachCount);
        Assert.AreEqual(1, detachCount);
        Assert.AreEqual(1, controller.DisposeCallCount);
        Assert.AreEqual(0, controller.ResetPropertiesCallCount);
        Assert.AreEqual(0, stateSource.SubscriberCount);
    }

    private sealed class RecordingController : IAdjustableAcrylicController
    {
        public float TintOpacity { get; set; }

        public int ResetPropertiesCallCount { get; private set; }

        public int DisposeCallCount { get; private set; }

        public AdjustableAcrylicState LastState { get; private set; } =
            new(true, false, ElementTheme.Light);

        public void ApplyState(AdjustableAcrylicState state) =>
            LastState = state;

        public void ResetProperties() => ResetPropertiesCallCount++;

        public void Dispose() => DisposeCallCount++;
    }

    private sealed class MutableStateSource : IAdjustableAcrylicStateSource
    {
        private EventHandler? _stateChanged;

        public AdjustableAcrylicState Current { get; private set; } =
            new(true, false, ElementTheme.Light);

        public int SubscriberCount => _stateChanged?.GetInvocationList()
            .Length ?? 0;

        public event EventHandler? StateChanged
        {
            add => _stateChanged += value;
            remove => _stateChanged -= value;
        }

        public void SetTheme(ElementTheme theme)
        {
            Current = Current with { Theme = theme };
            _stateChanged?.Invoke(this, EventArgs.Empty);
        }

        public void Dispose() { }
    }
}
