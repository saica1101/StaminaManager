using StaminaManager.Infrastructure.Windows;

namespace StaminaManager.Tests.Infrastructure.Windows;

[TestClass]
public sealed class TrayServiceTests
{
    [TestMethod]
    public void Initialize_一度だけtrayを作成して操作を転送する()
    {
        FakeTrayPlatformAdapter adapter = new();
        TrayService service = new(adapter);
        int openCount = 0;
        int exitCount = 0;
        service.OpenRequested += (_, _) => openCount++;
        service.ExitRequested += (_, _) => exitCount++;

        service.Initialize();
        service.Initialize();
        adapter.RaiseOpenRequested();
        adapter.RaiseExitRequested();
        service.HideWindow();
        service.ShowWindow();

        Assert.AreEqual(1, adapter.InitializeCount);
        Assert.AreEqual(1, openCount);
        Assert.AreEqual(1, exitCount);
        Assert.AreEqual(1, adapter.HideCount);
        Assert.AreEqual(1, adapter.ShowCount);
    }

    [TestMethod]
    public void Dispose_tray参照を一度だけ破棄する()
    {
        FakeTrayPlatformAdapter adapter = new();
        TrayService service = new(adapter);
        service.Initialize();

        service.Dispose();
        service.Dispose();

        Assert.AreEqual(1, adapter.DisposeCount);
    }

    private sealed class FakeTrayPlatformAdapter : ITrayPlatformAdapter
    {
        public event EventHandler? OpenRequested;

        public event EventHandler? ExitRequested;

        public int InitializeCount { get; private set; }

        public int HideCount { get; private set; }

        public int ShowCount { get; private set; }

        public int DisposeCount { get; private set; }

        public void Initialize() => InitializeCount++;

        public void HideWindow() => HideCount++;

        public void ShowWindow() => ShowCount++;

        public void Dispose() => DisposeCount++;

        public void RaiseOpenRequested() =>
            OpenRequested?.Invoke(this, EventArgs.Empty);

        public void RaiseExitRequested() =>
            ExitRequested?.Invoke(this, EventArgs.Empty);
    }
}
