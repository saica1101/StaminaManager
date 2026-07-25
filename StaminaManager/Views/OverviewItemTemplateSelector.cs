using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using StaminaManager.ViewModels;

namespace StaminaManager.Views;

public sealed class OverviewItemTemplateSelector : DataTemplateSelector
{
    public DataTemplate? GameTemplate { get; set; }

    public DataTemplate? AddGameTemplate { get; set; }

    protected override DataTemplate? SelectTemplateCore(object item) =>
        item switch
        {
            GameCardViewModel => GameTemplate,
            AddGameItemViewModel => AddGameTemplate,
            _ => throw new ArgumentException(
                "未対応のOverview項目です。",
                nameof(item)),
        };

    protected override DataTemplate? SelectTemplateCore(
        object item,
        DependencyObject container) => SelectTemplateCore(item);
}
