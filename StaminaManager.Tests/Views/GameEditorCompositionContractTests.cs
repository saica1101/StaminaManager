using StaminaManager.Core.Abstractions;
using StaminaManager.ViewModels;
using System.Reflection;

namespace StaminaManager.Tests.Views;

[TestClass]
public sealed class GameEditorCompositionContractTests
{
    [TestMethod]
    public void MainPageConstructor_RequiresAppResourceService()
    {
        ConstructorInfo? constructor = typeof(global::StaminaManager.MainPage)
            .GetConstructors()
            .SingleOrDefault(value => value.GetParameters()
                .Any(parameter =>
                    parameter.ParameterType == typeof(IAppResourceService)));

        Assert.IsNotNull(constructor);
    }

    [TestMethod]
    public void CompositionPassesTheAppResourceServiceToTheGameEditor()
    {
        string root = FindRepositoryRoot();
        string mainPageSource = File.ReadAllText(Path.Combine(
            root,
            "StaminaManager",
            "MainPage.xaml.cs"));
        string appSource = File.ReadAllText(Path.Combine(
            root,
            "StaminaManager",
            "App.xaml.cs"));
        mainPageSource = mainPageSource.Replace(
            "\r\n",
            "\n",
            StringComparison.Ordinal);
        appSource = appSource.Replace(
            "\r\n",
            "\n",
            StringComparison.Ordinal);

        StringAssert.Contains(
            mainPageSource,
            "IAppResourceService appResourceService");
        StringAssert.Contains(
            mainPageSource,
            "_appResourceService = appResourceService;");
        StringAssert.Contains(
            mainPageSource,
            "_appResourceService,\n                entry);");
        StringAssert.Contains(
            appSource,
            "assetStore,\n            _appResourceService,\n            versionProvider");
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
