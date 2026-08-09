using StaminaManager.Core.Abstractions;
using StaminaManager.Controls;
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
    public void GameEditorDialogConstructor_RequiresAppResourceService()
    {
        ConstructorInfo? constructor = typeof(GameEditorDialog)
            .GetConstructors()
            .SingleOrDefault(value => value.GetParameters()
                .Any(parameter =>
                    parameter.ParameterType == typeof(IAppResourceService)));

        Assert.IsNotNull(constructor);
    }
}
