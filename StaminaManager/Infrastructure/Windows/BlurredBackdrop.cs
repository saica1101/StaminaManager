using Windows.UI.Composition;
using WinUIEx;

namespace StaminaManager.Infrastructure.Windows;

public sealed class BlurredBackdrop : CompositionBrushBackdrop
{
    protected override CompositionBrush CreateBrush(
        Compositor compositor) => compositor.CreateHostBackdropBrush();
}
