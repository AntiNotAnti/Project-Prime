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
