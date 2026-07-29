using StaminaManager.Infrastructure.Windows;

namespace StaminaManager.Tests.Infrastructure.Windows;

[TestClass]
public sealed class TrayServiceTests
{
    [TestMethod]
    public void CreateDefinitions_動的項目へ実値のAutomation情報を定義する()
    {
        IReadOnlyList<TrayMenuItemDefinition> definitions =
            TrayMenuItemFactory.CreateDefinitions(
                (_, fallback) => fallback);

        CollectionAssert.AreEqual(
            new[]
            {
                new TrayMenuItemDefinition(
                    "開く",
                    "TrayOpenMenuItem",
                    "Stamina Managerを開く"),
                new TrayMenuItemDefinition(
                    "終了",
                    "TrayExitMenuItem",
                    "Stamina Managerを終了する"),
            },
            definitions.ToArray());
    }

    [TestMethod]
    public void Initialize_一度だけtrayを作成して操作を転送する()
    {
        FakeTrayPlatformAdapter adapter = new();
        TrayService service = new(adapter);
        int openCount = 0;
        int exitCount = 0;
        List<bool> visibilityChanges = [];
        service.OpenRequested += (_, _) => openCount++;
        service.ExitRequested += (_, _) => exitCount++;
        service.WindowVisibilityChanged += (_, args) =>
            visibilityChanges.Add(args.IsShown);

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
        CollectionAssert.AreEqual(
            new[] { false, true },
            visibilityChanges);
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
