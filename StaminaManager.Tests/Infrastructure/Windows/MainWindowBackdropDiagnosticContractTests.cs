namespace StaminaManager.Tests.Infrastructure.Windows;

[TestClass]
public sealed class MainWindowBackdropDiagnosticContractTests
{
    [TestMethod]
    public void Acrylic色調更新はBackdrop更新後に診断を同期する()
    {
        string method = ExtractMethod(
            LoadMainWindowSource(),
            "internal bool TryUpdateAcrylicTintOpacity(",
            "internal string GetActualBackdropDiagnostic()");

        int opacityUpdate = method.IndexOf(
            "backdrop.SetTintOpacityPercent(tintOpacityPercent);",
            StringComparison.Ordinal);
        int diagnosticUpdate = method.IndexOf(
            "UpdateBackdropDiagnostic();",
            StringComparison.Ordinal);

        Assert.IsGreaterThanOrEqualTo(0, opacityUpdate);
        Assert.IsGreaterThan(opacityUpdate, diagnosticUpdate);
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

        throw new AssertFailedException("リポジトリ ルートを検出できません。");
    }
}
