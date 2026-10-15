using MphRead.Entities;

namespace MphRead.Mods.Campaign
{
    /// <summary>
    /// Additional non-touch navigation for the original single-player
    /// cartridge dialog. Pointer hit testing still belongs to PlayerDialog.
    /// A press is a fresh edge, not a held button, so closing a tutorial does
    /// not repeatedly confirm the following dialog or mutate gameplay binds.
    /// </summary>
    internal static class CampaignDialogInput
    {
        internal enum Action
        {
            Okay,
            Yes,
            No,
            PreviousPage,
            NextPage
        }

        /// <summary>
        /// Raw desktop/touch coordinates are measured in the client framebuffer.
        /// The cartridge dialog rectangles are in its fixed 256x192 HUD grid.
        /// Converting to a 0..1 fraction but comparing with HUD pixels made
        /// every physical pointer confirmation impossible.
        /// </summary>
        internal static bool PointerHits(int width, int height,
            float rawX, float rawY, float left, float right, float top, float bottom)
        {
            if (width <= 0 || height <= 0 || !float.IsFinite(rawX) || !float.IsFinite(rawY))
                return false;
            float x = rawX * 256f / width;
            float y = rawY * 192f / height;
            return x >= left && x < right && y >= top && y < bottom;
        }

        internal static bool Pressed(DialogType dialog, bool dialogPaused, Action action,
            bool confirm, bool cancel, bool previousPage, bool nextPage)
        {
            if (!dialogPaused)
                return false;
            return dialog switch
            {
                DialogType.Okay or DialogType.Scan or DialogType.Event =>
                    action switch
                    {
                        Action.Okay => confirm,
                        Action.PreviousPage => previousPage,
                        Action.NextPage => nextPage,
                        _ => false
                    },
                DialogType.YesNo => action switch
                {
                    Action.Yes => confirm,
                    Action.No => cancel,
                    Action.PreviousPage => previousPage,
                    Action.NextPage => nextPage,
                    _ => false
                },
                _ => false
            };
        }
    }
}
