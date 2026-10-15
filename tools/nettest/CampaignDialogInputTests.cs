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

            // The original hitbox is 111..145 x 173..191 DS pixels.
            // Actual cursor input arrives in the game's framebuffer pixels.
            foreach (var (width, height) in new[] { (256, 192), (1920, 1080),
                (3840, 1080), (854, 480) })
            {
                float clickX = 128f * width / 256f;
                float clickY = 182f * height / 192f;
                Check(CampaignDialogInput.PointerHits(width, height,
                    clickX, clickY, 111, 145, 173, 191),
                    $"native tutorial OK accepts a real mouse/touch tap at {width}x{height}");
                Check(!CampaignDialogInput.PointerHits(width, height,
                    clickX, 0, 111, 145, 173, 191),
                    $"top-of-screen click cannot accept the bottom dialog at {width}x{height}");
            }
            Check(!CampaignDialogInput.PointerHits(0, 0, 0, 0, 111, 145, 173, 191)
                && !CampaignDialogInput.PointerHits(1920, 1080, float.NaN, 925,
                    111, 145, 173, 191),
                "missing framebuffer and invalid pointer coordinates cannot confirm");
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
