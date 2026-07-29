namespace StaminaManager.Tests.Application;

[TestClass]
public sealed class AppTimerVisibilityWiringTests
{
    [TestMethod]
    public void App_WiresPageWindowAndShutdownStateToTimerController()
    {
        string root = FindRepositoryRoot();
        string source = File.ReadAllText(Path.Combine(
            root,
            "StaminaManager",
            "App.xaml.cs"));

        StringAssert.Contains(
            source,
            "_shellViewModel.PropertyChanged +=");
        StringAssert.Contains(source, "OnShellViewModelPropertyChanged");
        StringAssert.Contains(
            source,
            "_trayService.WindowVisibilityChanged +=");
        StringAssert.Contains(source, "OnTrayWindowVisibilityChanged");
        StringAssert.Contains(
            source,
            ".SetWindowShownAsync(true)");
        StringAssert.Contains(
            source,
            ".DisposeAsync()");
    }

    [TestMethod]
    public void MainWindow_ReentrantCloseAwaitsSharedShutdownSequence()
    {
        string root = FindRepositoryRoot();
        string source = File.ReadAllText(Path.Combine(
            root,
            "StaminaManager",
            "MainWindow.xaml.cs"));

        StringAssert.Contains(source, "shutdownSequence.IsRequested");
        StringAssert.Contains(source, "shutdownSequence.IsCompleted");
        StringAssert.Contains(source, "await shutdownSequence.RequestAsync()");
        StringAssert.Contains(source, "args.Cancel = true");
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
