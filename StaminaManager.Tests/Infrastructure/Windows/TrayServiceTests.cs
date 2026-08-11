using StaminaManager.Core.Models;
using StaminaManager.Infrastructure.Resources;
using StaminaManager.Infrastructure.Windows;

namespace StaminaManager.Tests.Infrastructure.Windows;

[TestClass]
public sealed class TrayServiceTests
{
    [TestMethod]
    public void CreateDefinitions_リソース文言とAutomationNameを使用する()
    {
        IReadOnlyList<TrayMenuItemDefinition> definitions =
            TrayMenuItemFactory.CreateDefinitions(
                (resourceId, _) => resourceId switch
                {
                    "TrayOpenText" => "Open",
                    "TrayExitText" => "Exit",
                    "TrayOpenAutomationName" => "Open Stamina Manager",
                    "TrayExitAutomationName" => "Exit Stamina Manager",
                    _ => resourceId,
                });

        CollectionAssert.AreEqual(
            new[]
            {
                new TrayMenuItemDefinition(
                    "Open",
                    "TrayOpenMenuItem",
                    "Open Stamina Manager"),
                new TrayMenuItemDefinition(
                    "Exit",
                    "TrayExitMenuItem",
                    "Exit Stamina Manager"),
            },
            definitions.ToArray());
    }

    [TestMethod]
    public void CreateDefinitions_リソース失敗時も人間可読fallbackを使う()
    {
        IReadOnlyList<TrayMenuItemDefinition> definitions =
            TrayMenuItemFactory.CreateDefinitions(
                (_, fallback) => fallback);

        CollectionAssert.AreEqual(
            new[]
            {
                new TrayMenuItemDefinition(
                    LateBoundResourceText.GetFallback(
                        "TrayOpenText"),
                    "TrayOpenMenuItem",
                    LateBoundResourceText.GetFallback(
                        "TrayOpenAutomationName")),
                new TrayMenuItemDefinition(
                    LateBoundResourceText.GetFallback(
                        "TrayExitText"),
                    "TrayExitMenuItem",
                    LateBoundResourceText.GetFallback(
                        "TrayExitAutomationName")),
            },
            definitions.ToArray());
    }

    [TestMethod]
    public void Fallbacks_日本語と英語の人間可読文言を持つ()
    {
        Assert.AreEqual(
            "開く",
            LateBoundResourceText.GetFallback(
                "TrayOpenText",
                AppLanguage.Japanese));
        Assert.AreEqual(
            "Open",
            LateBoundResourceText.GetFallback(
                "TrayOpenText",
                AppLanguage.English));
        Assert.AreEqual(
            "Stamina Managerを終了する",
            LateBoundResourceText.GetFallback(
                "TrayExitAutomationName",
                AppLanguage.Japanese));
        Assert.AreEqual(
            "Exit Stamina Manager",
            LateBoundResourceText.GetFallback(
                "TrayExitAutomationName",
                AppLanguage.English));
    }

    [TestMethod]
    public void Resolve_リソース値をfallbackより優先する()
    {
        Assert.AreEqual(
            "from-resource",
            LateBoundResourceText.Resolve(
                "TrayOpenText",
                "Tray",
                _ => "from-resource"));
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
