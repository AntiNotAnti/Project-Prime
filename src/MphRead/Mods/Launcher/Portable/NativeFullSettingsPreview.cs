#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using System.Linq;
using System.Globalization;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Render.Hud;

namespace MphRead.Mods.Launcher;

internal sealed partial class NativeFullSettingsController
{
    // Preview is temporary; storage, window/restart options, topology and bindings
    // are excluded even when ordinary settings use automatic saving.
    private static bool Previewable(NativeSettingRow row) => !row.IsBinding &&
        (row.Category == "Display" && row.Group is "View" or "Frame pacing" or "HUD" or "HUD preferences" or "Accessibility" or "Outline colors"
        || row.Category == "Graphics"
        || row.Category == "Audio" && row.Group is "Mixer" or "General"
        || row.Category == "Controls" && row.Group is "Mouse and keyboard" or "Custom scope (all aim devices)" or "Stylus" or "Stylus guide");

    internal Action ApplyPreview() => ApplyPreviewCore(null,null);
    internal Action ApplyHudPreview(HudProfile profile,HudEditorOption[] options) => ApplyPreviewCore(profile,options);
    private Action ApplyPreviewCore(HudProfile? proposedHud,HudEditorOption[]? options)
    {
        if (Closed || Busy || Capturing || ConfirmingVideo || Confirmation.Length != 0)
            throw new InvalidOperationException("Finish the current settings operation before previewing.");
        var rows = Rows.Where(Previewable).ToArray();
        var values=rows.Select(row=>row.Value).ToArray();
        if(options!=null)
            foreach(var option in options)
                if(int.TryParse(option.Id,NumberStyles.None,CultureInfo.InvariantCulture,out int index)
                    &&index>=0&&index<Rows.Count&&IsHudEditorOption(Rows[index]))
                {
                    int previewIndex=Array.IndexOf(rows,Rows[index]);
                    if(previewIndex>=0)values[previewIndex]=option.Value;
                }
        for(int i=0;i<rows.Length;i++)
            if(rows[i].Validate(values[i]) is { } error)throw new FormatException(rows[i].Label+": "+error);
        var before = rows.Select(row => row.Read()).ToArray();
        var hud = HudProfiles.CopyCurrent();
        var restorePlacement = Input.StylusZone.CapturePlacement();
        void Restore()
        {
            // Publication also bridges HUD flags into legacy runtime owners.
            // Restore that profile first, then recover their exact pre-preview values.
            HudProfiles.Publish(hud);
            for (int i = rows.Length - 1; i >= 0; i--)
                if (rows[i].Read() != before[i]) rows[i].Write(before[i]);
            restorePlacement();
            GameSettings.Apply(_settings);
            InputSettings.ApplyToPlayers();
        }
        try
        {
            // Even writing an unchanged placement can clamp a saved rectangle
            // against today's aspect ratio. Only invoke setters for actual changes.
            for (int i = 0; i < rows.Length; i++)
                if (rows[i].Read() != values[i]) rows[i].Write(values[i]);
            GameSettings.Apply(_settings);
            InputSettings.ApplyToPlayers();
            HudProfiles.Publish(proposedHud?.DeepClone()??CopyHud());
            return Restore;
        }
        catch { Restore(); throw; }
    }
}
#endif
