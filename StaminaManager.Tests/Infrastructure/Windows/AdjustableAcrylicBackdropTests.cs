using Microsoft.UI.Xaml;
using StaminaManager.Infrastructure.Windows;

namespace StaminaManager.Tests.Infrastructure.Windows;

[TestClass]
public sealed class AdjustableAcrylicBackdropTests
{
    [TestMethod]
    public void Lifecycle_0_50_100PercentをTintとLuminosityへ変換する()
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
        Assert.AreEqual(0.0f, controller.LuminosityOpacity);

        lifecycle.SetTintOpacityPercent(50);
        Assert.AreEqual(0.5f, controller.TintOpacity);
        Assert.AreEqual(0.5f, controller.LuminosityOpacity);

        lifecycle.SetTintOpacityPercent(100);
        Assert.AreEqual(1.0f, controller.TintOpacity);
        Assert.AreEqual(1.0f, controller.LuminosityOpacity);
    }

    [TestMethod]
    public void Lifecycle_ThemeChangeはReset後にTintとLuminosityを再適用する()
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
        controller.ClearOperations();
        stateSource.SetTheme(ElementTheme.Dark);

        Assert.AreEqual(1, controller.ResetPropertiesCallCount);
        Assert.AreEqual(0.8f, controller.TintOpacity);
        Assert.AreEqual(0.8f, controller.LuminosityOpacity);
        Assert.AreEqual(ElementTheme.Dark, controller.LastState.Theme);
        CollectionAssert.AreEqual(
            new[]
            {
                "ResetProperties",
                "TintOpacity:-1.0",
                "ApplyState:Dark",
                "TintOpacity:0.8",
                "LuminosityOpacity:0.8",
            },
            controller.Operations);
    }

    [TestMethod]
    public void Lifecycle_DefaultConfigurationChangeは同じThemeでも再適用する()
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
        controller.ClearOperations();
        lifecycle.OnDefaultSystemBackdropConfigurationChanged();

        Assert.AreEqual(1, controller.ResetPropertiesCallCount);
        Assert.AreEqual(0.8f, controller.TintOpacity);
        Assert.AreEqual(0.8f, controller.LuminosityOpacity);
        Assert.AreEqual(ElementTheme.Light, controller.LastState.Theme);
        CollectionAssert.AreEqual(
            new[]
            {
                "ResetProperties",
                "TintOpacity:-1.0",
                "ApplyState:Light",
                "TintOpacity:0.8",
                "LuminosityOpacity:0.8",
            },
            controller.Operations);
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

    [TestMethod]
    public void Connection_StateSourceFactory失敗時はControllerを解放する()
    {
        RecordingController controller = new();
        StateSourceInitializationProbe probe = new();

        Assert.ThrowsExactly<InvalidOperationException>(
            () => AdjustableAcrylicConnection.Connect(
                controller,
                probe.CreateThenThrow,
                static () => { },
                static () => { },
                tintOpacityPercent: 80));

        Assert.AreEqual(1, controller.DisposeCallCount);
        Assert.AreEqual(0, probe.SubscriberCount);
    }

    [TestMethod]
    public void Connection_Attach失敗時はStateとControllerを解放する()
    {
        RecordingController controller = new();
        MutableStateSource stateSource = new();

        Assert.ThrowsExactly<InvalidOperationException>(
            () => AdjustableAcrylicConnection.Connect(
                controller,
                () => stateSource,
                () => throw new InvalidOperationException("attach failure"),
                static () => { },
                tintOpacityPercent: 80));

        Assert.IsTrue(stateSource.IsDisposed);
        Assert.AreEqual(0, stateSource.SubscriberCount);
        Assert.AreEqual(1, controller.DisposeCallCount);
    }

    private sealed class RecordingController : IAdjustableAcrylicController
    {
        private float _tintOpacity;
        private float _luminosityOpacity;

        public List<string> Operations { get; } = [];

        public float TintOpacity
        {
            get => _tintOpacity;
            set
            {
                _tintOpacity = value;
                Operations.Add($"TintOpacity:{value:0.0}");
            }
        }

        public float LuminosityOpacity
        {
            get => _luminosityOpacity;
            set
            {
                _luminosityOpacity = value;
                Operations.Add($"LuminosityOpacity:{value:0.0}");
            }
        }

        public int ResetPropertiesCallCount { get; private set; }

        public int DisposeCallCount { get; private set; }

        public AdjustableAcrylicState LastState { get; private set; } =
            new(true, false, ElementTheme.Light);

        public void ApplyState(AdjustableAcrylicState state)
        {
            LastState = state;
            Operations.Add($"ApplyState:{state.Theme}");
        }

        public void ResetProperties()
        {
            ResetPropertiesCallCount++;
            Operations.Add("ResetProperties");
            TintOpacity = -1;
        }

        public void Dispose() => DisposeCallCount++;

        public void ClearOperations() => Operations.Clear();
    }

    private sealed class MutableStateSource : IAdjustableAcrylicStateSource
    {
        private EventHandler? _stateChanged;

        public AdjustableAcrylicState Current { get; private set; } =
            new(true, false, ElementTheme.Light);

        public int SubscriberCount => _stateChanged?.GetInvocationList()
            .Length ?? 0;

        public bool IsDisposed { get; private set; }

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

        public void Dispose() => IsDisposed = true;
    }

    private sealed class StateSourceInitializationProbe
    {
        private EventHandler? _stateChanged;

        public int SubscriberCount => _stateChanged?.GetInvocationList()
            .Length ?? 0;

        public IAdjustableAcrylicStateSource CreateThenThrow()
        {
            _stateChanged += OnStateChanged;
            try
            {
                throw new InvalidOperationException(
                    "state source initialization failure");
            }
            finally
            {
                _stateChanged -= OnStateChanged;
            }
        }

        private void OnStateChanged(object? sender, EventArgs args) { }
    }
}
