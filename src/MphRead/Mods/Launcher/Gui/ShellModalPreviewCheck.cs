#if MPHREAD_AVALONIA
using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace MphRead.Mods.Launcher.Gui;

/// <summary>One-shot shell capture oracle; no renderer hooks or retained owners.</summary>
internal static class ShellModalPreviewCheck
{
    internal static bool CreateLobby(Control? root, bool overlayOpen,
        Func<bool>? canPresent, bool wanted, bool drawn, out string reason)
    {
        reason = "";
        if (root == null || !overlayOpen)
        { reason = "create lobby overlay is not open in the current root"; return false; }
        Control modal = ControllerNav.ModalRoot(root);
        if (ReferenceEquals(modal, root))
        { reason = "create lobby has no active modal layer"; return false; }
        var screens = modal.GetVisualDescendants().OfType<CreateServerScreen>()
            .Where(screen => screen.IsEffectivelyVisible).ToArray();
        if (screens.Length != 1)
        { reason = "active modal does not own exactly one visible create lobby screen"; return false; }
        var stands = screens[0].GetVisualDescendants().OfType<HunterStand>()
            .Where(stand => stand.IsEffectivelyVisible).ToArray();
        if (stands.Length != 1 || !stands[0].CanPresentPreview()
            || !ReferenceEquals(TopLevel.GetTopLevel(stands[0]), root))
        { reason = "create lobby stand is not eligible in the current modal/root"; return false; }
        HunterStand owner = stands[0];
        foreach (HunterStand background in root.GetVisualDescendants().OfType<HunterStand>())
        {
            if (!ReferenceEquals(background, owner) && background.CanPresentPreview())
            { reason = "background stand remains eligible while create lobby modal is active"; return false; }
        }
        if ((wanted || drawn) && (canPresent == null
            || !ReferenceEquals(canPresent.Target, owner)
            || !canPresent.Equals((Func<bool>)owner.CanPresentPreview)
            || !canPresent()))
        { reason = "native preview request/draw is not owned by the active create lobby stand"; return false; }
        return true;
    }
}
#endif
