using StaminaManager.Application;
using StaminaManager.Core.Models;
using StaminaManager.ViewModels;

namespace StaminaManager.Tests.ViewModels;

[TestClass]
public sealed class ShellViewModelTests
{
    [TestMethod]
    public void ShowAboutCommand_NavigatesToAboutInStandardMode()
    {
        ShellViewModel viewModel = new();
        AppNavigationRequest? request = null;
        viewModel.NavigationRequested += value => request = value;

        viewModel.ShowAboutCommand.Execute(null);

        Assert.IsNotNull(request);
        Assert.AreEqual(AppPage.About, request.Page);
        Assert.AreEqual(AppDisplayMode.Standard, request.DisplayMode);
        Assert.IsNull(request.GameId);
        Assert.AreEqual(AppPage.About, viewModel.CurrentPage);
    }
}
