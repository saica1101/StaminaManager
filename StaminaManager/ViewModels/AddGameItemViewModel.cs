using System.Windows.Input;

namespace StaminaManager.ViewModels;

public sealed class AddGameItemViewModel : OverviewItemViewModel
{
    public AddGameItemViewModel(ICommand addGameCommand)
    {
        ArgumentNullException.ThrowIfNull(addGameCommand);
        AddGameCommand = addGameCommand;
    }

    public ICommand AddGameCommand { get; }
}
