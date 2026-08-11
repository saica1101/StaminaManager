using StaminaManager.Core.Abstractions;
using StaminaManager.Infrastructure.Resources;

namespace StaminaManager.Tests.Infrastructure.Windows;

[TestClass]
public sealed class MainWindowCaptionColorContractTests
{
    [TestMethod]
    public void Constructor_Content初期化後にタイトルバー更新を構築する()
    {
        string source = LoadMainWindowSource();
        int contentIndex = source.IndexOf(
            "Content = _windowContent;",
            StringComparison.Ordinal);
        int updateIndex = source.IndexOf(
            "_captionColorUpdate = new CoalescingUiAction(",
            StringComparison.Ordinal);

        Assert.IsGreaterThanOrEqualTo(0, contentIndex);
        Assert.IsGreaterThan(contentIndex, updateIndex);
        StringAssert.Contains(
            source,
            "new DispatcherQueueUiWorkQueue(DispatcherQueue)");
        StringAssert.Contains(
            source,
            "ActualThemeChanged += OnActualThemeChanged;");
    }

    [TestMethod]
    public void ThemeChangedHandler_更新を遅延要求する()
    {
        string handler = ExtractMethod(
            LoadMainWindowSource(),
            "private void OnActualThemeChanged(",
            "private void ApplyCaptionButtonColors()");

        StringAssert.Contains(handler, "_captionColorUpdate.Request()");
        StringAssert.Contains(handler, "Debug.WriteLine(");
        Assert.DoesNotContain("ApplyCaptionButtonColors();", handler);
    }

    [TestMethod]
    public void ApplyCaptionButtonColors_実行時のテーマを読み例外を診断する()
    {
        string method = ExtractMethod(
            LoadMainWindowSource(),
            "private void ApplyCaptionButtonColors()",
            "internal void ConfigureLifecycle(");

        StringAssert.Contains(
            method,
            "_windowContent.ActualTheme == ElementTheme.Dark");
        StringAssert.Contains(method, "exception is COMException");
        StringAssert.Contains(method, "or ArgumentException");
        StringAssert.Contains(method, "or InvalidOperationException");
        StringAssert.Contains(method, "exception.GetType().Name");
    }

    [TestMethod]
    public void ResolveAppTitle_UsesInjectedResource()
    {
        IAppResourceService resources = new AppResourceService(
            resourceId => resourceId == "AppTitle"
                ? "Localized title"
                : resourceId);

        Assert.AreEqual("Localized title", MainWindow.ResolveAppTitle(resources));
    }

    [TestMethod]
    public void ResolveAppTitle_UsesBrandFallbackWhenResourceIsMissingOrThrows()
    {
        IAppResourceService missing = new AppResourceService(_ => string.Empty);
        IAppResourceService throwing = new ThrowingResourceService(
            new InvalidOperationException("private loader detail"));

        Assert.AreEqual("Stamina Manager", MainWindow.ResolveAppTitle(missing));
        Assert.AreEqual("Stamina Manager", MainWindow.ResolveAppTitle(throwing));
    }

    [TestMethod]
    public void ResolveAppTitle_WhenResourceThrowsOutOfMemory_Propagates()
    {
        Assert.ThrowsExactly<OutOfMemoryException>(() =>
            MainWindow.ResolveAppTitle(
                new ThrowingResourceService(
                    new OutOfMemoryException("private loader detail"))));
    }

    private static string ExtractMethod(
        string source,
        string startMarker,
        string endMarker)
    {
        int start = source.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, start);
        int end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.IsGreaterThan(start, end);
        return source[start..end];
    }

    private static string LoadMainWindowSource() => File.ReadAllText(
        Path.Combine(
            FindRepositoryRoot(),
            "StaminaManager",
            "MainWindow.xaml.cs"));

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

    private sealed class ThrowingResourceService(Exception exception)
        : IAppResourceService
    {
        public string GetString(string resourceId) => throw exception;

        public string Format(string resourceId, params object?[] args) =>
            throw exception;
    }
}
