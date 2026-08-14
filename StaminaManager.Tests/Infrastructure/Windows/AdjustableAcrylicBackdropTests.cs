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
        AdjustableAcrylicLifecycle lifecycle = CreateLifecycle(controller);

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
    [DataRow(ElementTheme.Dark)]
    [DataRow(ElementTheme.Light)]
    public void Lifecycle_Theme変更はReset後にOpacityを再適用する(
        ElementTheme theme)
    {
        RecordingController controller = new();
        AdjustableAcrylicLifecycle lifecycle = CreateLifecycle(controller);

        lifecycle.Connect();
        lifecycle.SetTintOpacityPercent(80);
        controller.ClearOperations();
        controller.SystemTheme = theme;

        lifecycle.OnDefaultSystemBackdropConfigurationChanged();

        CollectionAssert.AreEqual(
            new[]
            {
                $"ResetProperties:{theme}",
                "TintOpacity:-1.0",
                "LuminosityOpacity:-1.0",
                "TintOpacity:0.8",
                "LuminosityOpacity:0.8",
            },
            controller.Operations);
    }

    [TestMethod]
    public void Lifecycle_LightDarkを反復しても35Percentを維持する()
    {
        RecordingController controller = new();
        AdjustableAcrylicLifecycle lifecycle = CreateLifecycle(controller);

        lifecycle.Connect();
        lifecycle.SetTintOpacityPercent(35);

        foreach (ElementTheme theme in new[]
                 {
                     ElementTheme.Dark,
                     ElementTheme.Light,
                     ElementTheme.Dark,
                 })
        {
            controller.SystemTheme = theme;
            lifecycle.OnDefaultSystemBackdropConfigurationChanged();
            Assert.AreEqual(0.35f, controller.TintOpacity);
            Assert.AreEqual(0.35f, controller.LuminosityOpacity);
        }

        Assert.AreEqual(3, controller.ResetPropertiesCallCount);
    }

    [TestMethod]
    public void ControllerはWinUI既定構成を使い色を固定しない()
    {
        string source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "StaminaManager",
            "Infrastructure",
            "Windows",
            "AdjustableAcrylicBackdrop.cs"));

        StringAssert.Contains(
            source,
            "GetDefaultSystemBackdropConfiguration(");
        Assert.DoesNotContain("XamlBackdropStateSource", source);
        Assert.DoesNotContain("TintColor =", source);
        Assert.DoesNotContain("FallbackColor =", source);
        Assert.DoesNotContain("new SystemBackdropConfiguration", source);

        string resetMethod = source[
            source.IndexOf(
                "public void ResetProperties()",
                StringComparison.Ordinal)..source.IndexOf(
                "public void Dispose()",
                StringComparison.Ordinal)];
        StringAssert.Contains(
            resetMethod,
            "_controller.ResetProperties();");
        Assert.DoesNotContain(
            "_controller.SetSystemBackdropConfiguration(_configuration);",
            resetMethod);
        Assert.DoesNotContain(
            "_controller.AddSystemBackdropTarget(_target)",
            resetMethod);

        string attachMethod = source[
            source.IndexOf(
                "public void AttachTarget()",
                StringComparison.Ordinal)..source.IndexOf(
                "public void DetachTarget()",
                StringComparison.Ordinal)];
        int attachTargetIndex = attachMethod.IndexOf(
            "_controller.AddSystemBackdropTarget(_target)",
            StringComparison.Ordinal);
        int attachConfigurationIndex = attachMethod.IndexOf(
            "_controller.SetSystemBackdropConfiguration(_configuration)",
            StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(attachConfigurationIndex, 0);
        Assert.IsGreaterThanOrEqualTo(attachTargetIndex, 0);
        Assert.IsLessThan(attachConfigurationIndex, attachTargetIndex);
    }

    [TestMethod]
    public void Lifecycle_DefaultConfigurationChange失敗時はTargetとControllerを解放する()
    {
        RecordingController controller = new();
        int detachCount = 0;
        AdjustableAcrylicLifecycle lifecycle = new(
            controller,
            static () => { },
            () => detachCount++);

        lifecycle.Connect();
        controller.ResetPropertiesException = new InvalidOperationException(
            "default configuration update failure");

        lifecycle.OnDefaultSystemBackdropConfigurationChanged();

        Assert.IsFalse(lifecycle.IsConnected);
        Assert.AreEqual(1, detachCount);
        Assert.AreEqual(1, controller.DisposeCallCount);
    }

    [TestMethod]
    public void Lifecycle_DisconnectはTargetとControllerを一度だけ解放する()
    {
        RecordingController controller = new();
        int attachCount = 0;
        int detachCount = 0;
        AdjustableAcrylicLifecycle lifecycle = new(
            controller,
            () => attachCount++,
            () => detachCount++);

        lifecycle.Connect();
        lifecycle.Disconnect();
        lifecycle.Disconnect();

        Assert.AreEqual(1, attachCount);
        Assert.AreEqual(1, detachCount);
        Assert.AreEqual(1, controller.DisposeCallCount);
    }

    [TestMethod]
    public void Connection_Attach失敗時はControllerを解放する()
    {
        RecordingController controller = new();

        Assert.ThrowsExactly<InvalidOperationException>(
            () => AdjustableAcrylicConnection.Connect(
                controller,
                () => throw new InvalidOperationException("attach failure"),
                static () => { },
                tintOpacityPercent: 80));

        Assert.AreEqual(1, controller.DisposeCallCount);
    }

    private static AdjustableAcrylicLifecycle CreateLifecycle(
        RecordingController controller) => new(
            controller,
            static () => { },
            static () => { });

    private sealed class RecordingController : IAdjustableAcrylicController
    {
        private float _tintOpacity;
        private float _luminosityOpacity;

        public List<string> Operations { get; } = [];

        public ElementTheme SystemTheme { get; set; } = ElementTheme.Light;

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

        public Exception? ResetPropertiesException { get; set; }

        public void ResetProperties()
        {
            if (ResetPropertiesException is Exception exception)
            {
                throw exception;
            }

            ResetPropertiesCallCount++;
            Operations.Add($"ResetProperties:{SystemTheme}");
            TintOpacity = -1;
            LuminosityOpacity = -1;
        }

        public void Dispose() => DisposeCallCount++;

        public void ClearOperations() => Operations.Clear();
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(
                directory.FullName,
                "StaminaManager.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new AssertFailedException(
            "リポジトリ ルートを検出できません。");
    }
}
