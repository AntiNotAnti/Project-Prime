#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System.Collections.Immutable;
using System.Globalization;
using MphRead.Mods.Launcher.Core;

namespace MphRead.Mods.Launcher;

internal sealed partial class NativeFullSettingsController
{
    // Stylus mode stays a canonical Controls preference. Its dependent display
    // layout also offers the same master beside the stacked-screen controls.
    private static bool IsHudEditorOption(NativeSettingRow row) => row.HudOnly
        || row.Category == "Controls" && row.Group == "Stylus" && row.Label == "Stylus Mode";

    private static HudEditorOption CreateHudOption(NativeSettingRow row,int index)
    {
        int section=2,element=-1;string group="HUD-wide",label=row.Label,hint=row.Hint;
        if(row.Group=="HUD preferences")
        {
            section=2;group="Stacked dual screens";
            switch(row.Label)
            {
                case "Dual-screen DS layout":
                    label="Stacked dual-screen layout";
                    hint="Desktop player view only. Turn on Stylus input mode below to use stacked screens; replay and free-camera views keep their normal layout.";
                    break;
                case "Dual-screen aspect ratio":
                    label="Screen proportions (4:3 / 5:3)";
                    hint="Each stacked screen uses these proportions. 4:3 is the original shape; 5:3 widens both screens. Requires stacked layout and Stylus input mode.";
                    break;
                case "Dual-screen radar size":section=1;element=4;label="Radar size";group="Bottom-screen radar";break;
                case "Dual-screen radar X":section=1;element=4;label="Radar horizontal position";group="Bottom-screen radar";hint="0–1 across the available bottom-screen space; 0.5 centers the full radar.";break;
                case "Dual-screen radar Y":section=1;element=4;label="Radar vertical position";group="Bottom-screen radar";hint="0–1 across the available bottom-screen space; 0.5 centers the full radar.";break;
                case "Touchscreen weapon selector size":label="Weapon-select touch target size";group="Touchscreen";break;
            }
        }
        else if(row.Group=="Stylus"&&row.Label=="Stylus Mode")
        {
            section=2;group="Stacked dual screens";label="Stylus input mode";
            hint="Required for stacked dual screens on desktop. Enables absolute pen/touchscreen input; turning on the layout alone does not change your input mode.";
        }
        else if(row.Label=="Smooth Native Hud")
        {section=2;group="Original HUD artwork";label="Smooth original artwork";}
        else if(row.Group=="Stylus guide")
        {
            section=2;group="Touchscreen appearance";
            label=row.Label switch {"Native Ui"=>"Original touchscreen artwork","Native Ui Opacity"=>"Artwork opacity","Cursor Opacity"=>"Pointer opacity","Outline Opacity"=>"Guide outline opacity","Button Opacity"=>"Touch target opacity","Guide Color"=>"Guide color",_=>row.Label};
        }
        var range=row.HasSlider?new HudEditorNumericRange((float)row.Minimum!.Value,(float)row.Maximum!.Value,row.WholeNumber?1:.01f):null;
        return new(index.ToString(CultureInfo.InvariantCulture),label,row.Value,row.Choices.ToImmutableArray(),hint,row.Validate,section,group,range,element);
    }
}
#endif
