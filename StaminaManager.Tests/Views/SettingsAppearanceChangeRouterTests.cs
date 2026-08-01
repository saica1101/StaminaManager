using StaminaManager.Core.Models;
using StaminaManager.Infrastructure.Windows;
using StaminaManager.Views;

namespace StaminaManager.Tests.Views;

[TestClass]
public sealed class SettingsAppearanceChangeRouterTests
{
    [TestMethod]
    public async Task ChangeThemeAsync_投入時のテーマを順に適用する()
    {
        RecordingUiWorkQueue queue = new();
        List<AppTheme> applied = [];
        SettingsAppearanceChangeRouter router = CreateRouter(
            queue,
            theme =>
            {
                applied.Add(theme);
                return Task.CompletedTask;
            });

        Task first = router.ChangeThemeAsync(AppTheme.Dark);
        Task second = router.ChangeThemeAsync(AppTheme.Light);

        queue.RunNext();
        queue.RunNext();
        await Task.WhenAll(first, second);

        CollectionAssert.AreEqual(
            new[] { AppTheme.Dark, AppTheme.Light },
            applied);
    }

    [TestMethod]
    public async Task ChangeBackdropAsync_投入時の背景を順に適用する()
    {
        RecordingUiWorkQueue queue = new();
        List<BackdropKind> applied = [];
        SettingsAppearanceChangeRouter router = CreateRouter(
            queue,
            applyBackdrop: backdrop =>
            {
                applied.Add(backdrop);
                return Task.CompletedTask;
            });

        Task first = router.ChangeBackdropAsync(BackdropKind.Acrylic);
        Task second = router.ChangeBackdropAsync(BackdropKind.Blur);

        queue.RunNext();
        queue.RunNext();
        await Task.WhenAll(first, second);

        CollectionAssert.AreEqual(
            new[] { BackdropKind.Acrylic, BackdropKind.Blur },
            applied);
    }

    [TestMethod]
    public async Task ChangeThemeAsync_投入失敗は通知と同期を各1回行う()
    {
        RecordingUiWorkQueue queue = new(isEnqueueAccepted: false);
        int failureCount = 0;
        int synchronizationCount = 0;
        SettingsAppearanceChangeRouter router = CreateRouter(
            queue,
            reportFailure: () => failureCount++,
            synchronizeControls: () => synchronizationCount++);

        await router.ChangeThemeAsync(AppTheme.Dark);

        Assert.AreEqual(1, failureCount);
        Assert.AreEqual(1, synchronizationCount);
    }

    [TestMethod]
    public async Task ChangeBackdropAsync_投入失敗は通知と同期を各1回行う()
    {
        RecordingUiWorkQueue queue = new(isEnqueueAccepted: false);
        int failureCount = 0;
        int synchronizationCount = 0;
        SettingsAppearanceChangeRouter router = CreateRouter(
            queue,
            reportFailure: () => failureCount++,
            synchronizeControls: () => synchronizationCount++);

        await router.ChangeBackdropAsync(BackdropKind.Transparent);

        Assert.AreEqual(1, failureCount);
        Assert.AreEqual(1, synchronizationCount);
    }

    private static SettingsAppearanceChangeRouter CreateRouter(
        RecordingUiWorkQueue queue,
        Func<AppTheme, Task>? applyTheme = null,
        Func<BackdropKind, Task>? applyBackdrop = null,
        Action? reportFailure = null,
        Action? synchronizeControls = null) => new(
            new DeferredSettingsChangeExecutor(queue),
            applyTheme ?? (_ => Task.CompletedTask),
            applyBackdrop ?? (_ => Task.CompletedTask),
            reportFailure ?? (() => { }),
            synchronizeControls ?? (() => { }));

    private sealed class RecordingUiWorkQueue(
        bool isEnqueueAccepted = true) : IUiWorkQueue
    {
        private readonly Queue<Action> _actions = [];

        public bool TryEnqueue(Action action)
        {
            if (!isEnqueueAccepted)
            {
                return false;
            }

            _actions.Enqueue(action);
            return true;
        }

        public void RunNext() => _actions.Dequeue()();
    }
}
