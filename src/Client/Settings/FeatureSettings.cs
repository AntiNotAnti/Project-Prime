using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Globalization;
namespace MphRead
{
public static class BugfixesSettings
{

        public static void Load(IReadOnlyDictionary<string, string> values)
        {
            if (values.TryGetValue(nameof(Bugfixes.SmoothCamSeqHandoff), out string? value) && Boolean.TryParse(value, out bool result))
            {
                Bugfixes.SmoothCamSeqHandoff = result;
            }
            if (values.TryGetValue(nameof(Bugfixes.BetterCamSeqNodeRef), out value) && Boolean.TryParse(value, out result))
            {
                Bugfixes.BetterCamSeqNodeRef = result;
            }
            if (values.TryGetValue(nameof(Bugfixes.NoStrayRespawnText), out value) && Boolean.TryParse(value, out result))
            {
                Bugfixes.NoStrayRespawnText = result;
            }
            if (values.TryGetValue(nameof(Bugfixes.CorrectBountySfx), out value) && Boolean.TryParse(value, out result))
            {
                Bugfixes.CorrectBountySfx = result;
            }
            if (values.TryGetValue(nameof(Bugfixes.NoDoubleEnemyDeath), out value) && Boolean.TryParse(value, out result))
            {
                Bugfixes.NoDoubleEnemyDeath = result;
            }
            if (values.TryGetValue(nameof(Bugfixes.NoSlenchRollTimerUnderflow), out value) && Boolean.TryParse(value, out result))
            {
                Bugfixes.NoSlenchRollTimerUnderflow = result;
            }
        }

        public static FrozenDictionary<string, string> Commit()
        {
            return Frozen.Create<string, string>(
            [
                new(nameof(Bugfixes.SmoothCamSeqHandoff), Bugfixes.SmoothCamSeqHandoff.ToString().ToLower()),
                new(nameof(Bugfixes.BetterCamSeqNodeRef), Bugfixes.BetterCamSeqNodeRef.ToString().ToLower()),
                new(nameof(Bugfixes.NoStrayRespawnText), Bugfixes.NoStrayRespawnText.ToString().ToLower()),
                new(nameof(Bugfixes.CorrectBountySfx), Bugfixes.CorrectBountySfx.ToString().ToLower()),
                new(nameof(Bugfixes.NoDoubleEnemyDeath), Bugfixes.NoDoubleEnemyDeath.ToString().ToLower()),
                new(nameof(Bugfixes.NoSlenchRollTimerUnderflow), Bugfixes.NoSlenchRollTimerUnderflow.ToString().ToLower())
            ]);
        }
}
public static class FeaturesSettings
{

        public static void Load(IReadOnlyDictionary<string, string> values)
        {
            if (values.TryGetValue(nameof(Features.ReticleOpacity), out string? value)
                && Single.TryParse(value, CultureInfo.InvariantCulture, out float single))
            {
                Features.ReticleOpacity = single;
            }
            if (values.TryGetValue(nameof(Features.ReticleScale), out value)
                && Single.TryParse(value, CultureInfo.InvariantCulture, out single))
            {
                Features.ReticleScale = single;
            }
            if (values.TryGetValue(nameof(Features.ProHud), out value) && Boolean.TryParse(value, out bool boolean))
            {
                Features.ProHud = boolean;
            }
            if (values.TryGetValue(nameof(Features.ProHudFixedWeapon), out value) && Boolean.TryParse(value, out boolean))
            {
                Features.ProHudFixedWeapon = boolean;
            }
            // Pro mode's crosshair: which shape, and how big. Both persist,
            // because a crosshair is a thing a player picks once and then does
            // not want to think about again.
            if (values.TryGetValue("CrosshairStyle", out value))
            {
                Mods.Render.Crosshair.Style = Mods.Render.Crosshair.ParseStyle(value,
                    Mods.Render.Crosshair.Style);
            }
            if (values.TryGetValue("CrosshairSize", out value))
            {
                Mods.Render.Crosshair.Size = Mods.Render.Crosshair.ParseSize(value,
                    Mods.Render.Crosshair.Size);
            }
        }

        /// <summary>
        /// What the launcher can still be asked about: the Pro HUD switch,
        /// its independent weapon-motion preference, and crosshair choices.
        ///
        /// Everything else was reachable either through the generic
        /// reflection-built "Features" page or through a Display-page row of
        /// its own, and both are gone: the HUD is <see cref="Features.ProHud"/>'s
        /// decision and the rest sit at their code defaults. What is left out
        /// of this is left out of <see cref="Load"/> too, so an old
        /// settings.json cannot go on answering a question nobody is asked
        /// any more.
        /// </summary>
        public static FrozenDictionary<string, string> Commit()
        {
            return Frozen.Create<string, string>(
            [
                new(nameof(Features.ReticleOpacity), Features.ReticleOpacity.ToString(CultureInfo.InvariantCulture)),
                new(nameof(Features.ReticleScale), Features.ReticleScale.ToString(CultureInfo.InvariantCulture)),
                new(nameof(Features.ProHud), Features.ProHud.ToString().ToLower()),
                new(nameof(Features.ProHudFixedWeapon), Features.ProHudFixedWeapon.ToString().ToLower()),
                new("CrosshairStyle", Mods.Render.Crosshair.Style.ToString()),
                new("CrosshairSize", Mods.Render.Crosshair.Size.ToString())
            ]);
        }
}
public static class CheatsSettings
{

        public static void Load(IReadOnlyDictionary<string, string> values)
        {
            if (values.TryGetValue(nameof(Cheats.FreeWeaponSelect), out string? value) && Boolean.TryParse(value, out bool boolean))
            {
                Cheats.FreeWeaponSelect = boolean;
            }
            if (values.TryGetValue(nameof(Cheats.UnlimitedJumps), out value) && Boolean.TryParse(value, out boolean))
            {
                Cheats.UnlimitedJumps = boolean;
            }
            if (values.TryGetValue(nameof(Cheats.NoRandomEncounters), out value) && Boolean.TryParse(value, out boolean))
            {
                Cheats.NoRandomEncounters = boolean;
            }
            if (values.TryGetValue(nameof(Cheats.UnlockAllDoors), out value) && Boolean.TryParse(value, out boolean))
            {
                Cheats.UnlockAllDoors = boolean;
            }
            if (values.TryGetValue(nameof(Cheats.ContinueFromCurrentRoom), out value) && Boolean.TryParse(value, out boolean))
            {
                Cheats.ContinueFromCurrentRoom = boolean;
            }
            if (values.TryGetValue(nameof(Cheats.SkipPlanetIntros), out value) && Boolean.TryParse(value, out boolean))
            {
                Cheats.SkipPlanetIntros = boolean;
            }
            if (values.TryGetValue(nameof(Cheats.StartWithAllUpgrades), out value) && Boolean.TryParse(value, out boolean))
            {
                Cheats.StartWithAllUpgrades = boolean;
            }
            if (values.TryGetValue(nameof(Cheats.StartWithAllOctoliths), out value) && Boolean.TryParse(value, out boolean))
            {
                Cheats.StartWithAllOctoliths = boolean;
            }
            if (values.TryGetValue(nameof(Cheats.WalkThroughWalls), out value) && Boolean.TryParse(value, out boolean))
            {
                Cheats.WalkThroughWalls = boolean;
            }
            if (values.TryGetValue(nameof(Cheats.AlwaysFightGorea2), out value) && Boolean.TryParse(value, out boolean))
            {
                Cheats.AlwaysFightGorea2 = boolean;
            }
            if (values.TryGetValue(nameof(Cheats.QuadrupleDamage), out value) && Boolean.TryParse(value, out boolean))
            {
                Cheats.QuadrupleDamage = boolean;
            }
        }

        public static FrozenDictionary<string, string> Commit()
        {
            return Frozen.Create<string, string>(
            [
                new(nameof(Cheats.FreeWeaponSelect), Cheats.FreeWeaponSelect.ToString().ToLower()),
                new(nameof(Cheats.UnlimitedJumps), Cheats.UnlimitedJumps.ToString().ToLower()),
                new(nameof(Cheats.NoRandomEncounters), Cheats.NoRandomEncounters.ToString()),
                new(nameof(Cheats.UnlockAllDoors), Cheats.UnlockAllDoors.ToString()),
                new(nameof(Cheats.ContinueFromCurrentRoom), Cheats.ContinueFromCurrentRoom.ToString()),
                new(nameof(Cheats.SkipPlanetIntros), Cheats.SkipPlanetIntros.ToString()),
                new(nameof(Cheats.StartWithAllUpgrades), Cheats.StartWithAllUpgrades.ToString()),
                new(nameof(Cheats.StartWithAllOctoliths), Cheats.StartWithAllOctoliths.ToString().ToLower()),
                new(nameof(Cheats.WalkThroughWalls), Cheats.WalkThroughWalls.ToString().ToLower()),
                new(nameof(Cheats.AlwaysFightGorea2), Cheats.AlwaysFightGorea2.ToString().ToLower()),
                new(nameof(Cheats.QuadrupleDamage), Cheats.QuadrupleDamage.ToString().ToLower())
            ]);
        }
}
}
