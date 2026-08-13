using StaminaManager.Core.Calculations;
using StaminaManager.Core.Models;
using StaminaManager.Infrastructure.Windows;

namespace StaminaManager.Tests.Infrastructure.Windows;

[TestClass]
public sealed class WindowStateServiceTests
{
    [TestMethod]
    public void ApplyDisplayMode_表示前に補正済み境界を適用する()
    {
        FakeWindowStateAdapter adapter = new()
        {
            CurrentBounds = new WindowBounds(20, 30, 800, 600),
            CurrentDpi = 96,
            WorkArea = new WindowWorkArea(0, 0, 1920, 1040),
        };
        MemoryWindowSnapshotStore store = new();
        store.Set(
            AppDisplayMode.Standard,
            new WindowBoundsSnapshot(
                new WindowBounds(5000, -1500, 900, 700),
                SavedDpi: 96));
        WindowStateService service = new(adapter, store, _ => { });

        service.ApplyDisplayMode(AppDisplayMode.Standard);

        Assert.AreEqual(new WindowSize(520, 520), adapter.MinimumSize);
        Assert.AreEqual(
            new WindowBounds(1020, 0, 900, 700),
            adapter.CurrentBounds);
    }

    [TestMethod]
    [DataRow(AppDisplayMode.Standard, 1680, 1140)]
    [DataRow(AppDisplayMode.Compact, 630, 780)]
    public void ApplyDisplayMode_snapshotなしの初期サイズはeffectivePxとして現在Dpiへ変換する(
        AppDisplayMode displayMode,
        int expectedWidth,
        int expectedHeight)
    {
        FakeWindowStateAdapter adapter = new()
        {
            CurrentBounds = new WindowBounds(20, 30, 800, 600),
            CurrentDpi = 144,
            WorkArea = new WindowWorkArea(0, 0, 2560, 1440),
        };
        WindowStateService service = new(
            adapter,
            new MemoryWindowSnapshotStore(),
            _ => { });

        service.ApplyDisplayMode(displayMode);

        Assert.AreEqual(expectedWidth, adapter.CurrentBounds.Width);
        Assert.AreEqual(expectedHeight, adapter.CurrentBounds.Height);
    }

    [TestMethod]
    public void ApplyDisplayMode_通常最大化からcompactを経て通常最大化へ戻す()
    {
        FakeWindowStateAdapter adapter = new()
        {
            CurrentBounds = new WindowBounds(100, 120, 900, 600),
            CurrentDpi = 96,
            WorkArea = new WindowWorkArea(0, 0, 1920, 1040),
        };
        MemoryWindowSnapshotStore store = new();
        WindowStateService service = new(adapter, store, _ => { });
        service.ApplyDisplayMode(AppDisplayMode.Standard);
        adapter.PresenterState = WindowPresenterState.Maximized;

        service.ApplyDisplayMode(AppDisplayMode.Compact);

        WindowBoundsSnapshot standard = store.Get(
            AppDisplayMode.Standard)!.Value;
        Assert.AreEqual(
            new WindowBounds(100, 120, 1120, 760),
            standard.Bounds);
        Assert.IsTrue(standard.IsMaximized);
        Assert.AreEqual(new WindowSize(360, 480), adapter.MinimumSize);
        WindowBoundsSnapshot compact = store.Get(
            AppDisplayMode.Compact)!.Value;
        Assert.AreEqual(
            new WindowBounds(100, 120, 420, 520),
            compact.Bounds);
        Assert.AreEqual(96d, compact.SavedDpi);
        Assert.IsFalse(compact.IsMaximized);

        service.ApplyDisplayMode(AppDisplayMode.Standard);

        Assert.AreEqual(
            new WindowBounds(100, 120, 1120, 760),
            adapter.CurrentBounds);
        Assert.AreEqual(1, adapter.MaximizeCount);
    }

    [TestMethod]
    public void CaptureCurrent_保存失敗は型名だけ診断して継続する()
    {
        FakeWindowStateAdapter adapter = new()
        {
            CurrentBounds = new WindowBounds(20, 30, 800, 600),
            CurrentDpi = 96,
            WorkArea = new WindowWorkArea(0, 0, 1920, 1040),
        };
        MemoryWindowSnapshotStore store = new();
        List<Type> diagnostics = [];
        WindowStateService service = new(
            adapter,
            store,
            diagnostics.Add);
        service.ApplyDisplayMode(AppDisplayMode.Standard);

        store.SaveException = new IOException("sensitive detail");
        service.CaptureCurrent();

        CollectionAssert.AreEqual(
            new[] { typeof(IOException) },
            diagnostics);
    }

    private sealed class FakeWindowStateAdapter : IWindowStateAdapter
    {
        public event EventHandler? BoundsChanged;

        public WindowBounds CurrentBounds { get; set; }

        public double CurrentDpi { get; set; }

        public WindowWorkArea WorkArea { get; set; }

        public WindowPresenterState PresenterState { get; set; }

        public WindowSize MinimumSize { get; private set; }

        public int MaximizeCount { get; private set; }

        public WindowWorkArea GetNearestWorkArea(WindowBounds bounds) =>
            WorkArea;

        public void SetMinimumSize(WindowSize minimumSize) =>
            MinimumSize = minimumSize;

        public void MoveAndResize(WindowBounds bounds)
        {
            CurrentBounds = bounds;
            PresenterState = WindowPresenterState.Restored;
        }

        public void Restore() =>
            PresenterState = WindowPresenterState.Restored;

        public void Maximize()
        {
            MaximizeCount++;
            PresenterState = WindowPresenterState.Maximized;
        }

        public void RaiseBoundsChanged() =>
            BoundsChanged?.Invoke(this, EventArgs.Empty);
    }

    private sealed class MemoryWindowSnapshotStore : IWindowSnapshotStore
    {
        private readonly Dictionary<AppDisplayMode, WindowBoundsSnapshot>
            _snapshots = [];

        public Exception? SaveException { get; set; }

        public WindowBoundsSnapshot? Get(AppDisplayMode displayMode) =>
            _snapshots.TryGetValue(displayMode, out WindowBoundsSnapshot value)
                ? value
                : null;

        public WindowBoundsSnapshot? Load(AppDisplayMode displayMode) =>
            Get(displayMode);

        public void Save(
            AppDisplayMode displayMode,
            WindowBoundsSnapshot snapshot)
        {
            if (SaveException is not null)
            {
                throw SaveException;
            }

            _snapshots[displayMode] = snapshot;
        }

        public void Set(
            AppDisplayMode displayMode,
            WindowBoundsSnapshot snapshot) =>
            _snapshots[displayMode] = snapshot;
    }
}
