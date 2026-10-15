using System;
using MphRead.Entities;
using MphRead.Mods.Campaign;

namespace MphRead.NetTest;

internal static class CampaignDialogInputTests
{
    internal static int Run()
    {
        int count = 0;
        void Check(bool pass, string name)
        {
            if (!pass) throw new InvalidOperationException("CAMP DIALOG FAIL " + name);
            count++;
            Console.WriteLine("CAMP DIALOG PASS " + name);
        }

        static bool Hit(DialogType dialog, bool paused, CampaignDialogInput.Action action,
            bool confirm = false, bool cancel = false, bool previous = false, bool next = false)
            => CampaignDialogInput.Pressed(dialog, paused, action, confirm, cancel, previous, next);

        try
        {
            foreach (DialogType dialog in new[] { DialogType.Okay, DialogType.Event, DialogType.Scan })
            {
                Check(Hit(dialog, true, CampaignDialogInput.Action.Okay, confirm: true),
                    dialog + " accepts fresh keyboard Enter, Space or controller A");
                Check(!Hit(dialog, false, CampaignDialogInput.Action.Okay, confirm: true),
                    dialog + " cannot capture gameplay when unpaused");
                Check(!Hit(dialog, true, CampaignDialogInput.Action.Yes, confirm: true)
                    && !Hit(dialog, true, CampaignDialogInput.Action.No, cancel: true),
                    dialog + " never accidentally chooses Yes/No");
                Check(Hit(dialog, true, CampaignDialogInput.Action.PreviousPage, previous: true)
                    && Hit(dialog, true, CampaignDialogInput.Action.NextPage, next: true),
                    dialog + " supports paginated scan/hint text");
            }
            Check(Hit(DialogType.YesNo, true, CampaignDialogInput.Action.Yes, confirm: true)
                && Hit(DialogType.YesNo, true, CampaignDialogInput.Action.No, cancel: true),
                "ship/quit yes-no honors A/Enter and B/Escape");
            Check(!Hit(DialogType.YesNo, true, CampaignDialogInput.Action.No, confirm: true)
                && !Hit(DialogType.YesNo, true, CampaignDialogInput.Action.Yes, cancel: true),
                "yes-no cannot cross-activate opposite choice");
            Check(!Hit(DialogType.YesNo, true, CampaignDialogInput.Action.Okay, confirm: true),
                "yes-no cannot accidentally accept an OK button");
            foreach (DialogType dialog in new[] { DialogType.None, DialogType.Hud, DialogType.Overlay })
                Check(!Hit(dialog, true, CampaignDialogInput.Action.Okay, confirm: true),
                    dialog + " does not consume menu or gameplay keys");
            Check(!Hit(DialogType.Okay, true, CampaignDialogInput.Action.Okay),
                "holding no key cannot auto-dismiss tutorial");
            Console.WriteLine($"CAMP DIALOG PASS total={count} (no cartridge or GL)");
            return 0;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(e);
            return 1;
        }
    }
}
