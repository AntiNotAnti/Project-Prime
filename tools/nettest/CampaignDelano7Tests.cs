using System;
using MphRead.Entities;
using MphRead.Mods.Campaign;

namespace MphRead.NetTest;

internal static class CampaignDelano7Tests
{
    internal static int Run()
    {
        int passed = 0;
        void Check(bool ok, string what)
        {
            if (!ok) throw new InvalidOperationException("DELANO7 FAIL " + what);
            passed++;
            Console.WriteLine("DELANO7 PASS " + what);
        }
        try
        {
            PlatformFlags gunship = PlatformFlags.SamusShip;
            Check(CampaignShipVisualPolicy.UseDelano7(GameMode.SinglePlayer, gunship,
                "SamusShip", Hunter.Sylux, replica: false),
                "Sylux solo adventure sees Delano 7 in Samus's scripted landing slot");
            foreach (Hunter hunter in Enum.GetValues<Hunter>())
            {
                if (hunter == Hunter.Sylux) continue;
                Check(!CampaignShipVisualPolicy.UseDelano7(GameMode.SinglePlayer,
                    gunship, "SamusShip", hunter, replica: false),
                    hunter + " preserves the original story gunship");
            }
            Check(!CampaignShipVisualPolicy.UseDelano7(GameMode.Battle, gunship,
                "SamusShip", Hunter.Sylux, replica: false)
                && !CampaignShipVisualPolicy.UseDelano7(GameMode.SinglePlayer, gunship,
                    "SamusShip", Hunter.Sylux, replica: true),
                "multiplayer and private replay scenes never swap world models");
            Check(!CampaignShipVisualPolicy.UseDelano7(GameMode.SinglePlayer,
                PlatformFlags.None, "SamusShip", Hunter.Sylux, replica: false)
                && !CampaignShipVisualPolicy.UseDelano7(GameMode.SinglePlayer,
                    gunship, "SyluxShip", Hunter.Sylux, replica: false)
                && !CampaignShipVisualPolicy.UseDelano7(GameMode.SinglePlayer,
                    gunship, null, Hunter.Sylux, replica: false)
                && !CampaignShipVisualPolicy.UseDelano7(GameMode.SinglePlayer,
                    gunship, "SamusShip", null, replica: false),
                "only the authentic SamusShip platform may use the visual replacement");
            Check(Metadata.GetPlatformById(8)?.Name == CampaignShipVisualPolicy.DelanoModel
                && Metadata.GetPlatformById(21)?.Name == CampaignShipVisualPolicy.DelanoTurretModel
                && Metadata.GetPlatformById(23)?.Name == CampaignShipVisualPolicy.StoryShipModel,
                "replacement and original are existing cartridge model references");
            Console.WriteLine($"DELANO7 PASS total={passed}; model loading and actual animations require original files");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
