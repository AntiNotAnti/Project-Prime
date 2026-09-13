using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using MphRead.Mods.Launcher.Gui;
using Xunit;

namespace MphRead.Tests.Client;

[Collection(AvaloniaUiCollection.Name)]
public sealed class AccountViewPresentationTests
{
    [AvaloniaFact]
    public void LegacyAccountOverlayUsesProjectPrimeFrameAndSignInPriority()
    {
        var view = new AccountView();
        var window = new Window { Width = 940, Height = 700, Content = view };
        window.Show();
        try
        {
            PrimeTechFrame frame = Assert.Single(view.GetVisualDescendants()
                .OfType<PrimeTechFrame>());
            Assert.Contains("prime-account-frame", frame.Classes);
            Assert.Contains(view.GetVisualDescendants().OfType<MenuEntry>(),
                entry => entry.Title == "Sign in" && entry.Primary);
            Assert.Contains(view.GetVisualDescendants().OfType<MenuEntry>(),
                entry => entry.Title == "Continue as guest" && !entry.Primary);
            Expander advanced = Assert.Single(view.GetVisualDescendants()
                .OfType<Expander>(), item => Equals(item.Header, "Advanced"));
            Assert.False(advanced.IsExpanded);
        }
        finally
        {
            window.Content = null;
            window.Close();
            view.Content = null;
        }
    }
}
