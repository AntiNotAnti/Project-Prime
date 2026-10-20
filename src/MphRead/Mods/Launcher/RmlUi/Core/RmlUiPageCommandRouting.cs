using MphRead.Mods.Launcher.RmlUi.Host;

namespace MphRead.Mods.Launcher.RmlUi.Core;

/// <summary>Commands delegated to the active native page before root route handling.</summary>
public static class RmlUiPageCommandRouting
{
    public static bool Community(RmlUiIntentKind kind) => kind is
        RmlUiIntentKind.CommunityRefresh or RmlUiIntentKind.CommunitySearch
        or RmlUiIntentKind.CommunitySort or RmlUiIntentKind.CommunityPage
        or RmlUiIntentKind.CommunitySelect or RmlUiIntentKind.CommunityImport
        or RmlUiIntentKind.CommunityDetailAction or RmlUiIntentKind.CommunityCancelWork
        or RmlUiIntentKind.CommunityTab or RmlUiIntentKind.CommunityCreatorAction
        or RmlUiIntentKind.CommunityConfirm or RmlUiIntentKind.CommunityCancel
        or RmlUiIntentKind.CommunitySelectRevision or RmlUiIntentKind.CommunityRevisionPage
        or RmlUiIntentKind.CommunityVisibility
        or RmlUiIntentKind.CommunityReport or RmlUiIntentKind.CommunityReportReason
        or RmlUiIntentKind.CommunityTransferAction;

    public static bool Hunter(RmlUiIntentKind kind) => kind is
        RmlUiIntentKind.HunterSelect or RmlUiIntentKind.HunterSuit
        or RmlUiIntentKind.HunterApply or RmlUiIntentKind.HunterCancel
        or RmlUiIntentKind.HunterResetPreview or RmlUiIntentKind.HunterOpen
        or RmlUiIntentKind.HunterSkinNext or RmlUiIntentKind.HunterArmorNext
        or RmlUiIntentKind.HunterDeathNext
        or RmlUiIntentKind.HunterBeamNext or RmlUiIntentKind.HunterBeamColorNext
        or RmlUiIntentKind.HunterBeamSelect or RmlUiIntentKind.HunterBeamColorSelect
        or RmlUiIntentKind.HunterSkinSelect or RmlUiIntentKind.HunterArmorSelect
        or RmlUiIntentKind.HunterDeathSelect or RmlUiIntentKind.HunterCosmeticCategory
        or RmlUiIntentKind.HunterCosmeticsReset
        or RmlUiIntentKind.HunterCosmeticsSave or RmlUiIntentKind.HunterPreviewMode
        or RmlUiIntentKind.HunterPreviewDeath or RmlUiIntentKind.HunterRotate
        or RmlUiIntentKind.HunterZoom or RmlUiIntentKind.HunterLoopDeath
        or RmlUiIntentKind.HunterPreviewReset or RmlUiIntentKind.HunterCompareNative
        or RmlUiIntentKind.HunterLoadoutDefault;
}
