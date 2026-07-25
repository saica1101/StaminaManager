using StaminaManager.Core.Models;

namespace StaminaManager.Core.Abstractions;

public sealed record ThemeResult(
    AppTheme RequestedTheme,
    AppTheme ActualTheme,
    bool IsApplied,
    string? ErrorMessage);

public interface IThemeService
{
    AppTheme ResolveInitialTheme();

    ThemeResult Apply(AppTheme requestedTheme);
}
