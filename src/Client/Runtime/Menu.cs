using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using MphRead.Formats.Sound;
using MphRead.Sound;
using MphRead.Text;

namespace MphRead
{
    public static class Menu
    {
        private static string _mode = "auto-select";
        private static decimal _musicVolume = 0.50m;

        private static void PrintSoundInfo(SoundCapability soundCapability)
        {
            if (soundCapability == SoundCapability.None)
            {
                Console.WriteLine();
                Console.WriteLine("WARNING: Audio system could not be loaded. " +
                    "Sound effects will not be played.");
                Console.WriteLine("You may need to install OpenAL Soft on your system.");
                Console.WriteLine("Music and video playback will not be affected.");
            }
            else if (soundCapability == SoundCapability.Unsupported)
            {
                Console.WriteLine();
                Console.WriteLine("WARNING: Audio system was loaded, " +
                    "but an unsupported version of OpenAL was used.");
                Console.WriteLine("You may need to install OpenAL Soft on your system for sounds to play correctly.");
                Console.WriteLine("Music and video playback will not be affected.");
            }
        }


        private static bool _teams = false;
        // point goal is a decimal just to share code for advancing the value
        private static decimal _pointGoal = 0;
        private static decimal _timeGoal = 0;
        private static decimal _timeLimit = 0;
        private static bool _octolithReset = true;
        private static bool _radarPlayers = true;
        private static int _damageLevel = 1;
        private static bool _friendlyFire = false;
        private static bool _affinityWeapons = false;

        private static string _goalType = "";

        private static void ReadTimeGoal(string? input)
        {
            if (!String.IsNullOrWhiteSpace(input))
            {
                input = input.Trim().ToUpper();
                if (_goalType == "Time Goal")
                {
                    if (GetTime(input, out decimal result))
                    {
                        _timeGoal = result;
                    }
                }
                else if (Decimal.TryParse(input, out decimal result))
                {
                    _pointGoal = Math.Clamp((int)result, _goalType == "Extra Lives" ? 0 : 1, 99999);
                }
            }
        }

        private static void ReadTimeLimit(string? input)
        {
            if (!String.IsNullOrWhiteSpace(input))
            {
                if (GetTime(input.Trim().ToUpper(), out decimal result))
                {
                    _timeLimit = result;
                }
            }
        }

        private static bool GetTime(string input, out decimal result)
        {
            result = 0;
            string[] split = input.Split(':');
            if (split.Length == 1)
            {
                if (Int32.TryParse(split[0], out int minutes))
                {
                    result = minutes * 60;
                    return true;
                }
            }
            else if (split.Length == 2)
            {
                if (Int32.TryParse(split[0], out int minutes)
                    && Int32.TryParse(split[1], out int seconds))
                {
                    result = minutes * 60 + seconds;
                    return true;
                }
            }
            else if (split.Length == 3)
            {
                if (Int32.TryParse(split[0], out int hours)
                    && Int32.TryParse(split[1], out int minutes)
                    && Int32.TryParse(split[2], out int seconds))
                {
                    result = hours * 60 * 60 + minutes * 60 + seconds;
                    return true;
                }
            }
            return false;
        }

        private static string FormatTime(decimal value)
        {
            var time = TimeSpan.FromSeconds((float)value);
            if (time.Hours > 0)
            {
                return $"{time:h\\:mm\\:ss}";
            }
            if (time.Minutes > 0)
            {
                return $"{time:m\\:ss}";
            }
            return $"0:{time:ss}";
        }

        private static bool ShowSettingsPrompts()
        {
            int prompt = 0;
            int selection = 0;
            IReadOnlyList<string> damageLevels = new string[]
            {
                "Low", "Medium", "High"
            };

            string X(int index)
            {
                return $"[{(selection == index ? "x" : " ")}]";
            }

            static string OnOff(bool value)
            {
                return value ? "On" : "Off";
            }

            static decimal Advance(decimal current, IReadOnlyList<decimal> values, int direction)
            {
                decimal update;
                if (direction == 1)
                {
                    update = Decimal.MaxValue;
                    for (int i = 0; i < values.Count; i++)
                    {
                        decimal value = values[i];
                        if (value < update && value > current)
                        {
                            update = value;
                        }
                    }
                    return update == Decimal.MaxValue ? values[0] : update;
                }
                update = Decimal.MinValue;
                for (int i = values.Count - 1; i >= 0; i--)
                {
                    decimal value = values[i];
                    if (value > update && value < current)
                    {
                        update = value;
                    }
                }
                return update == Decimal.MinValue ? values[^1] : update;
            }

            while (true)
            {
                int s = 0;
                string modeString;
                if (_mode == "auto-select")
                {
                    modeString = "Battle";
                }
                else
                {
                    modeString = _mode.Replace(" Teams", "");
                }
                modeString += " Mode Settings";
                string goalString;
                if (_mode.StartsWith("Defender") || _mode == "Prime Hunter")
                {
                    goalString = FormatTime(_timeGoal);
                }
                else
                {
                    goalString = _pointGoal.ToString();
                }
                string timeString = FormatTime(_timeLimit);
                string resetString = "N/A";
                if (_mode == "Capture" || _mode.StartsWith("Bounty"))
                {
                    resetString = OnOff(_octolithReset);
                }
                string weaponsString = _affinityWeapons ? "Affinity Weapons" : "Default Weapons";
                Console.Clear();
                Console.WriteLine($"{Mods.Branding.Name} {Program.Version}");
                Console.WriteLine();
                Console.WriteLine("Choose a setting using up/down or with the key indicated.");
                Console.WriteLine("Press Space to specify, Backspace to clear, or left/right to advance the setting.");
                Console.WriteLine("When finished, press Enter or use the last option to return. Press Escape to exit.");
                Console.WriteLine();
                Console.WriteLine(modeString);
                Console.WriteLine();
                Console.WriteLine($"{X(s++)} (P) {_goalType}: {goalString}");
                Console.WriteLine($"{X(s++)} (L) Time Limit: {timeString}");
                Console.WriteLine($"{X(s++)} (A) Auto Reset: {resetString}");
                Console.WriteLine($"{X(s++)} (T) Team Play: {OnOff(_teams)}");
                Console.WriteLine($"{X(s++)} (S) Show Hunters On Radar: {OnOff(_radarPlayers)}");
                Console.WriteLine($"{X(s++)} (D) Damage Level: {damageLevels[_damageLevel]}");
                Console.WriteLine($"{X(s++)} (F) Friendly Fire: {OnOff(_friendlyFire)}");
                Console.WriteLine($"{X(s++)} (W) Available Weapons: {weaponsString}");
                Console.WriteLine($"{X(s++)} (X) Reset Match Settings");
                Console.WriteLine($"{X(s++)} (B) Go Back");
                s--;
                if (prompt == 0)
                {
                    ConsoleKeyInfo keyInfo = Console.ReadKey();
                    if (keyInfo.Key == ConsoleKey.Escape)
                    {
                        return false;
                    }
                    if (keyInfo.Key == ConsoleKey.Enter || keyInfo.Key == ConsoleKey.B
                        || keyInfo.Key == ConsoleKey.Spacebar && selection == s)
                    {
                        break;
                    }
                    if (keyInfo.Key == ConsoleKey.Spacebar)
                    {
                        if (selection == s - 1)
                        {
                            _radarPlayers = true;
                            _damageLevel = 1;
                            _friendlyFire = false;
                            _affinityWeapons = false;
                            UpdateSettings();
                            continue;
                        }
                        prompt = selection + 1;
                    }
                    else if (keyInfo.Key == ConsoleKey.P)
                    {
                        selection = 0;
                    }
                    else if (keyInfo.Key == ConsoleKey.L)
                    {
                        selection = 1;
                    }
                    else if (keyInfo.Key == ConsoleKey.A)
                    {
                        selection = 2;
                    }
                    else if (keyInfo.Key == ConsoleKey.T)
                    {
                        selection = 3;
                    }
                    else if (keyInfo.Key == ConsoleKey.S)
                    {
                        selection = 4;
                    }
                    else if (keyInfo.Key == ConsoleKey.D)
                    {
                        selection = 5;
                    }
                    else if (keyInfo.Key == ConsoleKey.F)
                    {
                        selection = 6;
                    }
                    else if (keyInfo.Key == ConsoleKey.W)
                    {
                        selection = 7;
                    }
                    else if (keyInfo.Key == ConsoleKey.X)
                    {
                        selection = 8;
                    }
                    else if (keyInfo.Key == ConsoleKey.UpArrow || keyInfo.Key == ConsoleKey.W)
                    {
                        selection--;
                        if (selection < 0)
                        {
                            selection = s;
                        }
                    }
                    else if (keyInfo.Key == ConsoleKey.DownArrow || keyInfo.Key == ConsoleKey.S)
                    {
                        selection++;
                        if (selection > s)
                        {
                            selection = 0;
                        }
                    }
                    else if (keyInfo.Key == ConsoleKey.Backspace || keyInfo.Key == ConsoleKey.Delete)
                    {
                        if (selection == 0)
                        {
                            ResetGoal();
                        }
                        else if (selection == 1)
                        {
                            ResetTimeLimit();
                        }
                        else if (selection == 2)
                        {
                            _octolithReset = true;
                        }
                        else if (selection == 3)
                        {
                            _teams = _mode == "Capture";
                        }
                        else if (selection == 4)
                        {
                            _radarPlayers = true;
                        }
                        else if (selection == 5)
                        {
                            _damageLevel = 1;
                        }
                        else if (selection == 6)
                        {
                            _friendlyFire = false;
                        }
                        else if (selection == 7)
                        {
                            _affinityWeapons = false;
                        }
                    }
                    else if (keyInfo.Key == ConsoleKey.Add || keyInfo.Key == ConsoleKey.OemPlus
                        || keyInfo.Key == ConsoleKey.RightArrow || keyInfo.Key == ConsoleKey.Subtract
                        || keyInfo.Key == ConsoleKey.OemMinus || keyInfo.Key == ConsoleKey.LeftArrow)
                    {
                        int direction = keyInfo.Key == ConsoleKey.Add || keyInfo.Key == ConsoleKey.OemPlus
                            || keyInfo.Key == ConsoleKey.RightArrow ? 1 : -1;
                        if (selection == 0)
                        {
                            if (_mode == "auto-select" || _mode.StartsWith("Battle"))
                            {
                                _pointGoal = Advance(_pointGoal, _battlePoints, direction);
                            }
                            else if (_mode.StartsWith("Survival"))
                            {
                                _pointGoal = Advance(_pointGoal, _extraLives, direction);
                            }
                            else if (_mode == "Capture" || _mode.StartsWith("Bounty"))
                            {
                                _pointGoal = Advance(_pointGoal, _octolithPoints, direction);
                            }
                            else if (_mode.StartsWith("Nodes"))
                            {
                                _pointGoal = Advance(_pointGoal, _nodePoints, direction);
                            }
                            else if (_mode.StartsWith("Defender") || _mode == "Prime Hunter")
                            {
                                _timeGoal = Advance(_timeGoal, _timeGoals, direction);
                            }
                        }
                        else if (selection == 1)
                        {
                            _timeLimit = Advance(_timeLimit, _timeLimits, direction);
                        }
                        else if (selection == 2)
                        {
                            _octolithReset = !_octolithReset;
                        }
                        else if (selection == 3)
                        {
                            if (_mode == "Capture")
                            {
                                _teams = true;
                            }
                            else if (_mode == "Prime Hunter")
                            {
                                _teams = false;
                            }
                            else
                            {
                                _teams = !_teams;
                            }
                        }
                        else if (selection == 4)
                        {
                            _radarPlayers = !_radarPlayers;
                        }
                        else if (selection == 5)
                        {
                            _damageLevel += direction;
                            if (_damageLevel >= damageLevels.Count)
                            {
                                _damageLevel = 0;
                            }
                            else if (_damageLevel < 0)
                            {
                                _damageLevel = damageLevels.Count - 1;
                            }
                        }
                        else if (selection == 6)
                        {
                            _friendlyFire = !_friendlyFire;
                        }
                        else if (selection == 7)
                        {
                            _affinityWeapons = !_affinityWeapons;
                        }
                        if (_teams && _mode != "Capture" && !_mode.EndsWith("Teams"))
                        {
                            if (_mode == "auto-select")
                            {
                                _mode = "Battle";
                            }
                            _mode += " Teams";
                        }
                        else if (!_teams && _mode.EndsWith("Teams"))
                        {
                            _mode = _mode.Replace(" Teams", "");
                        }
                    }
                }
                else
                {
                    if (prompt == 1)
                    {
                        Console.WriteLine($"Enter {_goalType.ToLower()}.");
                        if (_goalType == "Time Goal")
                        {
                            Console.WriteLine("Examples: 7, 2:30, 0:45");
                        }
                        else
                        {
                            Console.WriteLine("Examples: 5, 66, 100");
                        }
                        ReadTimeGoal(Console.ReadLine());
                    }
                    else if (prompt == 2)
                    {
                        Console.WriteLine("Enter time limit.");
                        Console.WriteLine("Examples: 7, 2:30, 0:45");
                        ReadTimeLimit(Console.ReadLine());
                    }
                    prompt = 0;
                }
            }
            return true;
        }

        private static bool ShowFeaturePrompts()
        {
            int screen = 0;
            int selection = 0;

            string X(int index)
            {
                return $"[{(selection == index ? "x" : " ")}]";
            }

            static string OnOff(bool value)
            {
                return value ? "On" : "Off";
            }

            static string PrintOpacity(float value)
            {
                return value switch
                {
                    0 => "zero",
                    >= 1 => "full",
                    _ => "partial"
                };
            }

            while (true)
            {
                int s = 0;
                Console.Clear();
                Console.WriteLine($"{Mods.Branding.Name} {Program.Version}");
                Console.WriteLine();
                Console.WriteLine("Choose a setting using up/down or with the key indicated.");
                Console.WriteLine("Press Space to specify, Backspace to clear, or left/right to advance the setting.");
                Console.WriteLine("When finished, press Enter or use the last option to return. Press Escape to exit.");
                Console.WriteLine();
                if (screen == 0)
                {
                    Console.WriteLine("Features, Cheats, and Bugfixes");
                    Console.WriteLine();
                    Console.WriteLine($"{X(s++)} (F) Features...");
                    Console.WriteLine($"{X(s++)} (C) Cheats...");
                    Console.WriteLine($"{X(s++)} (G) Bugfixes...");
                    Console.WriteLine($"{X(s++)} (X) Reset Features, Cheats, and Bugfixes");
                }
                else if (screen == 1)
                {
                    Console.WriteLine("Features");
                    Console.WriteLine();
                    Console.WriteLine($"{X(s++)} (E) No Repeat Encounters: {OnOff(Features.NoRepeatEncounters)}");
                    Console.WriteLine($"{X(s++)} (T) Allow Invalid Teams: {OnOff(Features.AllowInvalidTeams)}");
                    Console.WriteLine($"{X(s++)} (I) Target Info On Top Screen: {OnOff(Features.TopScreenTargetInfo)}");
                    Console.WriteLine($"{X(s++)} (H) Helmet Opacity: {PrintOpacity(Features.HelmetOpacity)}");
                    Console.WriteLine($"{X(s++)} (V) Visor Opacity: {PrintOpacity(Features.VisorOpacity)}");
                    Console.WriteLine($"{X(s++)} (D) HUD Opacity: {PrintOpacity(Features.HudOpacity)}");
                    Console.WriteLine($"{X(s++)} (C) Reticle Opacity: {PrintOpacity(Features.ReticleOpacity)}");
                    Console.WriteLine($"{X(s++)} (S) HUD Sway: {OnOff(Features.HudSway)}");
                    Console.WriteLine($"{X(s++)} (F) Target Info Sway: {OnOff(Features.TargetInfoSway)}");
                    Console.WriteLine($"{X(s++)} (W) Delayed Idle Sway: {OnOff(Features.DelayedIdleSway)}");
                    Console.WriteLine($"{X(s++)} (N) No Idle Sway: {OnOff(Features.NoIdleSway)}");
                    Console.WriteLine($"{X(s++)} (M) No Map Centering: {OnOff(Features.NoMapCentering)}");
                    Console.WriteLine($"{X(s++)} (R) Maximum Room Detail: {OnOff(Features.MaxRoomDetail)}");
                    Console.WriteLine($"{X(s++)} (P) Maximum Player Detail: {OnOff(Features.MaxPlayerDetail)}");
                    Console.WriteLine($"{X(s++)} (L) Logarithmic Spatial Audio: {OnOff(Features.LogSpatialAudio)}");
                    Console.WriteLine($"{X(s++)} (A) Consistent Alarm Interval: {OnOff(Features.HalfSecondAlarm)}");
                    Console.WriteLine($"{X(s++)} (G) Full Boost Charge: {OnOff(Features.FullBoostCharge)}");
                    Console.WriteLine($"{X(s++)} (B) Boost Opens Doors: {OnOff(Features.BoostOpensDoors)}");
                    Console.WriteLine($"{X(s++)} (1) Update Adventure Mode For Other Hunters: {OnOff(Features.AlternateHunters1P)}");
                }
                else if (screen == 2)
                {
                    Console.WriteLine("Cheats");
                    Console.WriteLine();
                    Console.WriteLine($"{X(s++)} (W) Free Weapon Selection: {OnOff(Cheats.FreeWeaponSelect)}");
                    Console.WriteLine($"{X(s++)} (J) Unlimited Jumps: {OnOff(Cheats.UnlimitedJumps)}");
                    Console.WriteLine($"{X(s++)} (E) No Random Encounters: {OnOff(Cheats.NoRandomEncounters)}");
                    Console.WriteLine($"{X(s++)} (D) All Doors Unlocked: {OnOff(Cheats.UnlockAllDoors)}");
                    Console.WriteLine($"{X(s++)} (R) Retry From Current Room: {OnOff(Cheats.ContinueFromCurrentRoom)}");
                    Console.WriteLine($"{X(s++)} (I) Skip Planet Intros: {OnOff(Cheats.SkipPlanetIntros)}");
                    Console.WriteLine($"{X(s++)} (U) Start With All Upgrades: {OnOff(Cheats.StartWithAllUpgrades)}");
                    Console.WriteLine($"{X(s++)} (O) Start With All Octoliths: {OnOff(Cheats.StartWithAllOctoliths)}");
                    Console.WriteLine($"{X(s++)} (G) Walk Through Walls: {OnOff(Cheats.WalkThroughWalls)}");
                    Console.WriteLine($"{X(s++)} (2) Always Fight Gorea 2: {OnOff(Cheats.AlwaysFightGorea2)}");
                    Console.WriteLine($"{X(s++)} (Q) Quadruple Damage: {OnOff(Cheats.QuadrupleDamage)}");
                }
                else if (screen == 3)
                {
                    Console.WriteLine("Bugfixes");
                    Console.WriteLine();
                    Console.WriteLine($"{X(s++)} (C) Smooth Camera Sequence Handoff: {OnOff(Bugfixes.SmoothCamSeqHandoff)}");
                    Console.WriteLine($"{X(s++)} (N) Better Camera Sequence Node Refs: {OnOff(Bugfixes.BetterCamSeqNodeRef)}");
                    Console.WriteLine($"{X(s++)} (R) No Stray Respawn Text: {OnOff(Bugfixes.NoStrayRespawnText)}");
                    Console.WriteLine($"{X(s++)} (S) Correct Bounty SFX: {OnOff(Bugfixes.CorrectBountySfx)}");
                    Console.WriteLine($"{X(s++)} (E) Fix Double Enemy Death: {OnOff(Bugfixes.NoDoubleEnemyDeath)}");
                    Console.WriteLine($"{X(s++)} (T) Fix Slench Roll Timer Underflow: {OnOff(Bugfixes.NoSlenchRollTimerUnderflow)}");
                }
                Console.WriteLine($"{X(s++)} (B) Go Back");
                s--;
                ConsoleKeyInfo keyInfo = Console.ReadKey();
                if (keyInfo.Key == ConsoleKey.UpArrow)
                {
                    selection--;
                    if (selection < 0)
                    {
                        selection = s;
                    }
                }
                else if (keyInfo.Key == ConsoleKey.DownArrow)
                {
                    selection++;
                    if (selection > s)
                    {
                        selection = 0;
                    }
                }
                else if (keyInfo.Key == ConsoleKey.Escape)
                {
                    return false;
                }
                else if (screen == 0)
                {
                    if (keyInfo.Key == ConsoleKey.Enter || keyInfo.Key == ConsoleKey.B
                        || keyInfo.Key == ConsoleKey.Spacebar && selection == s)
                    {
                        break;
                    }
                    if (keyInfo.Key == ConsoleKey.F
                        || keyInfo.Key == ConsoleKey.Spacebar && selection == 0)
                    {
                        screen = 1;
                        selection = 0;
                    }
                    else if (keyInfo.Key == ConsoleKey.C
                        || keyInfo.Key == ConsoleKey.Spacebar && selection == 1)
                    {
                        screen = 2;
                        selection = 0;
                    }
                    else if (keyInfo.Key == ConsoleKey.G
                        || keyInfo.Key == ConsoleKey.Spacebar && selection == 2)
                    {
                        screen = 3;
                        selection = 0;
                    }
                    else if (keyInfo.Key == ConsoleKey.X)
                    {
                        selection = 3;
                    }
                    else if (keyInfo.Key == ConsoleKey.Spacebar && selection == 3)
                    {
                        ResetFeatures();
                    }
                }
                else if (screen == 1)
                {
                    if (keyInfo.Key == ConsoleKey.Enter || keyInfo.Key == ConsoleKey.B
                        || keyInfo.Key == ConsoleKey.Spacebar && selection == s)
                    {
                        screen = 0;
                        selection = 0;
                    }
                    else if (keyInfo.Key == ConsoleKey.E)
                    {
                        selection = 0;
                    }
                    else if (keyInfo.Key == ConsoleKey.T)
                    {
                        selection = 1;
                    }
                    else if (keyInfo.Key == ConsoleKey.I)
                    {
                        selection = 2;
                    }
                    else if (keyInfo.Key == ConsoleKey.H)
                    {
                        selection = 3;
                    }
                    else if (keyInfo.Key == ConsoleKey.V)
                    {
                        selection = 4;
                    }
                    else if (keyInfo.Key == ConsoleKey.D)
                    {
                        selection = 5;
                    }
                    else if (keyInfo.Key == ConsoleKey.C)
                    {
                        selection = 6;
                    }
                    else if (keyInfo.Key == ConsoleKey.S)
                    {
                        selection = 7;
                    }
                    else if (keyInfo.Key == ConsoleKey.F)
                    {
                        selection = 8;
                    }
                    else if (keyInfo.Key == ConsoleKey.W)
                    {
                        selection = 9;
                    }
                    else if (keyInfo.Key == ConsoleKey.N)
                    {
                        selection = 10;
                    }
                    else if (keyInfo.Key == ConsoleKey.R)
                    {
                        selection = 11;
                    }
                    else if (keyInfo.Key == ConsoleKey.P)
                    {
                        selection = 12;
                    }
                    else if (keyInfo.Key == ConsoleKey.L)
                    {
                        selection = 13;
                    }
                    else if (keyInfo.Key == ConsoleKey.A)
                    {
                        selection = 14;
                    }
                    else if (keyInfo.Key == ConsoleKey.G)
                    {
                        selection = 15;
                    }
                    else if (keyInfo.Key == ConsoleKey.D1 || keyInfo.Key == ConsoleKey.NumPad1)
                    {
                        selection = 16;
                    }
                    else if (keyInfo.Key == ConsoleKey.Backspace || keyInfo.Key == ConsoleKey.Delete)
                    {
                        if (selection == 0)
                        {
                            Features.NoRepeatEncounters = true;
                        }
                        else if (selection == 1)
                        {
                            Features.AllowInvalidTeams = true;
                        }
                        else if (selection == 2)
                        {
                            Features.TopScreenTargetInfo = true;
                        }
                        else if (selection == 3)
                        {
                            Features.HelmetOpacity = 1;
                        }
                        else if (selection == 4)
                        {
                            Features.VisorOpacity = 0.5f;
                        }
                        else if (selection == 5)
                        {
                            Features.HudOpacity = 1;
                        }
                        else if (selection == 6)
                        {
                            Features.ReticleOpacity = 1;
                        }
                        else if (selection == 7)
                        {
                            Features.HudSway = true;
                        }
                        else if (selection == 8)
                        {
                            Features.TargetInfoSway = false;
                        }
                        else if (selection == 9)
                        {
                            Features.DelayedIdleSway = true;
                        }
                        else if (selection == 10)
                        {
                            Features.NoIdleSway = false;
                        }
                        else if (selection == 11)
                        {
                            Features.NoMapCentering = false;
                        }
                        else if (selection == 12)
                        {
                            Features.MaxRoomDetail = false;
                        }
                        else if (selection == 13)
                        {
                            Features.MaxPlayerDetail = true;
                        }
                        else if (selection == 14)
                        {
                            Features.LogSpatialAudio = false;
                        }
                        else if (selection == 15)
                        {
                            Features.HalfSecondAlarm = false;
                        }
                        else if (selection == 16)
                        {
                            Features.FullBoostCharge = false;
                        }
                        else if (selection == 17)
                        {
                            Features.BoostOpensDoors = false;
                        }
                        else if (selection == 18)
                        {
                            Features.AlternateHunters1P = true;
                        }
                    }
                    else if (keyInfo.Key == ConsoleKey.Add || keyInfo.Key == ConsoleKey.OemPlus
                        || keyInfo.Key == ConsoleKey.RightArrow || keyInfo.Key == ConsoleKey.Subtract
                        || keyInfo.Key == ConsoleKey.OemMinus || keyInfo.Key == ConsoleKey.LeftArrow)
                    {
                        int direction = keyInfo.Key == ConsoleKey.Add || keyInfo.Key == ConsoleKey.OemPlus
                            || keyInfo.Key == ConsoleKey.RightArrow ? 1 : -1;

                        float UpdateOpacity(float value)
                        {
                            if (direction == 1)
                            {
                                if (value <= 0)
                                {
                                    value = 0.5f;
                                }
                                else if (value >= 1)
                                {
                                    value = 0;
                                }
                                else
                                {
                                    value = 1;
                                }
                            }
                            else
                            {
                                if (value <= 0)
                                {
                                    value = 1;
                                }
                                else if (value >= 1)
                                {
                                    value = 0.5f;
                                }
                                else
                                {
                                    value = 0;
                                }
                            }
                            return value;
                        }

                        if (selection == 0)
                        {
                            Features.NoRepeatEncounters = !Features.NoRepeatEncounters;
                        }
                        else if (selection == 1)
                        {
                            Features.AllowInvalidTeams = !Features.AllowInvalidTeams;
                        }
                        else if (selection == 2)
                        {
                            Features.TopScreenTargetInfo = !Features.TopScreenTargetInfo;
                        }
                        else if (selection == 3)
                        {
                            Features.HelmetOpacity = UpdateOpacity(Features.HelmetOpacity);
                        }
                        else if (selection == 4)
                        {
                            Features.VisorOpacity = UpdateOpacity(Features.VisorOpacity);
                        }
                        else if (selection == 5)
                        {
                            Features.HudOpacity = UpdateOpacity(Features.HudOpacity);
                        }
                        else if (selection == 6)
                        {
                            Features.ReticleOpacity = UpdateOpacity(Features.ReticleOpacity);
                        }
                        else if (selection == 7)
                        {
                            Features.HudSway = !Features.HudSway;
                        }
                        else if (selection == 8)
                        {
                            Features.TargetInfoSway = !Features.TargetInfoSway;
                        }
                        else if (selection == 9)
                        {
                            Features.DelayedIdleSway = !Features.DelayedIdleSway;
                        }
                        else if (selection == 10)
                        {
                            Features.NoIdleSway = !Features.NoIdleSway;
                        }
                        else if (selection == 11)
                        {
                            Features.NoMapCentering = !Features.NoMapCentering;
                        }
                        else if (selection == 12)
                        {
                            Features.MaxRoomDetail = !Features.MaxRoomDetail;
                        }
                        else if (selection == 13)
                        {
                            Features.MaxPlayerDetail = !Features.MaxPlayerDetail;
                        }
                        else if (selection == 14)
                        {
                            Features.LogSpatialAudio = !Features.LogSpatialAudio;
                        }
                        else if (selection == 15)
                        {
                            Features.HalfSecondAlarm = !Features.HalfSecondAlarm;
                        }
                        else if (selection == 16)
                        {
                            Features.FullBoostCharge = !Features.FullBoostCharge;
                        }
                        else if (selection == 17)
                        {
                            Features.BoostOpensDoors = !Features.BoostOpensDoors;
                        }
                        else if (selection == 18)
                        {
                            Features.AlternateHunters1P = !Features.AlternateHunters1P;
                        }
                    }
                }
                else if (screen == 2)
                {
                    if (keyInfo.Key == ConsoleKey.Enter || keyInfo.Key == ConsoleKey.B
                        || keyInfo.Key == ConsoleKey.Spacebar && selection == s)
                    {
                        screen = 0;
                        selection = 1;
                    }
                    else if (keyInfo.Key == ConsoleKey.W)
                    {
                        selection = 0;
                    }
                    else if (keyInfo.Key == ConsoleKey.J)
                    {
                        selection = 1;
                    }
                    else if (keyInfo.Key == ConsoleKey.E)
                    {
                        selection = 2;
                    }
                    else if (keyInfo.Key == ConsoleKey.D)
                    {
                        selection = 3;
                    }
                    else if (keyInfo.Key == ConsoleKey.R)
                    {
                        selection = 4;
                    }
                    else if (keyInfo.Key == ConsoleKey.I)
                    {
                        selection = 5;
                    }
                    else if (keyInfo.Key == ConsoleKey.U)
                    {
                        selection = 6;
                    }
                    else if (keyInfo.Key == ConsoleKey.O)
                    {
                        selection = 7;
                    }
                    else if (keyInfo.Key == ConsoleKey.G)
                    {
                        selection = 8;
                    }
                    else if (keyInfo.Key == ConsoleKey.D2 || keyInfo.Key == ConsoleKey.NumPad2)
                    {
                        selection = 9;
                    }
                    else if (keyInfo.Key == ConsoleKey.Q)
                    {
                        selection = 10;
                    }
                    else if (keyInfo.Key == ConsoleKey.Backspace || keyInfo.Key == ConsoleKey.Delete)
                    {
                        if (selection == 0)
                        {
                            Cheats.FreeWeaponSelect = false;
                        }
                        else if (selection == 1)
                        {
                            Cheats.UnlimitedJumps = false;
                        }
                        else if (selection == 2)
                        {
                            Cheats.NoRandomEncounters = false;
                        }
                        else if (selection == 3)
                        {
                            Cheats.UnlockAllDoors = false;
                        }
                        else if (selection == 4)
                        {
                            Cheats.ContinueFromCurrentRoom = false;
                        }
                        else if (selection == 5)
                        {
                            Cheats.SkipPlanetIntros = false;
                        }
                        else if (selection == 6)
                        {
                            Cheats.StartWithAllUpgrades = false;
                        }
                        else if (selection == 7)
                        {
                            Cheats.StartWithAllOctoliths = false;
                        }
                        else if (selection == 8)
                        {
                            Cheats.WalkThroughWalls = false;
                        }
                        else if (selection == 9)
                        {
                            Cheats.AlwaysFightGorea2 = false;
                        }
                        else if (selection == 10)
                        {
                            Cheats.QuadrupleDamage = false;
                        }
                    }
                    else if (keyInfo.Key == ConsoleKey.Add || keyInfo.Key == ConsoleKey.OemPlus
                        || keyInfo.Key == ConsoleKey.RightArrow || keyInfo.Key == ConsoleKey.Subtract
                        || keyInfo.Key == ConsoleKey.OemMinus || keyInfo.Key == ConsoleKey.LeftArrow)
                    {
                        int direction = keyInfo.Key == ConsoleKey.Add || keyInfo.Key == ConsoleKey.OemPlus
                            || keyInfo.Key == ConsoleKey.RightArrow ? 1 : -1;
                        if (selection == 0)
                        {
                            Cheats.FreeWeaponSelect = !Cheats.FreeWeaponSelect;
                        }
                        else if (selection == 1)
                        {
                            Cheats.UnlimitedJumps = !Cheats.UnlimitedJumps;
                        }
                        else if (selection == 2)
                        {
                            Cheats.NoRandomEncounters = !Cheats.NoRandomEncounters;
                        }
                        else if (selection == 3)
                        {
                            Cheats.UnlockAllDoors = !Cheats.UnlockAllDoors;
                        }
                        else if (selection == 4)
                        {
                            Cheats.ContinueFromCurrentRoom = !Cheats.ContinueFromCurrentRoom;
                        }
                        else if (selection == 5)
                        {
                            Cheats.SkipPlanetIntros = !Cheats.SkipPlanetIntros;
                        }
                        else if (selection == 6)
                        {
                            Cheats.StartWithAllUpgrades = !Cheats.StartWithAllUpgrades;
                        }
                        else if (selection == 7)
                        {
                            Cheats.StartWithAllOctoliths = !Cheats.StartWithAllOctoliths;
                        }
                        else if (selection == 8)
                        {
                            Cheats.WalkThroughWalls = !Cheats.WalkThroughWalls;
                        }
                        else if (selection == 9)
                        {
                            Cheats.AlwaysFightGorea2 = !Cheats.AlwaysFightGorea2;
                        }
                        else if (selection == 10)
                        {
                            Cheats.QuadrupleDamage = !Cheats.QuadrupleDamage;
                        }
                    }
                }
                else if (screen == 3)
                {
                    if (keyInfo.Key == ConsoleKey.Enter || keyInfo.Key == ConsoleKey.B
                        || keyInfo.Key == ConsoleKey.Spacebar && selection == s)
                    {
                        screen = 0;
                        selection = 2;
                    }
                    else if (keyInfo.Key == ConsoleKey.C)
                    {
                        selection = 0;
                    }
                    else if (keyInfo.Key == ConsoleKey.N)
                    {
                        selection = 1;
                    }
                    else if (keyInfo.Key == ConsoleKey.R)
                    {
                        selection = 2;
                    }
                    else if (keyInfo.Key == ConsoleKey.S)
                    {
                        selection = 3;
                    }
                    else if (keyInfo.Key == ConsoleKey.E)
                    {
                        selection = 4;
                    }
                    else if (keyInfo.Key == ConsoleKey.T)
                    {
                        selection = 5;
                    }
                    else if (keyInfo.Key == ConsoleKey.Backspace || keyInfo.Key == ConsoleKey.Delete)
                    {
                        if (selection == 0)
                        {
                            Bugfixes.SmoothCamSeqHandoff = false;
                        }
                        else if (selection == 1)
                        {
                            Bugfixes.BetterCamSeqNodeRef = true;
                        }
                        else if (selection == 2)
                        {
                            Bugfixes.NoStrayRespawnText = false;
                        }
                        else if (selection == 3)
                        {
                            Bugfixes.CorrectBountySfx = true;
                        }
                        else if (selection == 4)
                        {
                            Bugfixes.NoDoubleEnemyDeath = true;
                        }
                        else if (selection == 5)
                        {
                            Bugfixes.NoSlenchRollTimerUnderflow = true;
                        }
                    }
                    else if (keyInfo.Key == ConsoleKey.Add || keyInfo.Key == ConsoleKey.OemPlus
                        || keyInfo.Key == ConsoleKey.RightArrow || keyInfo.Key == ConsoleKey.Subtract
                        || keyInfo.Key == ConsoleKey.OemMinus || keyInfo.Key == ConsoleKey.LeftArrow)
                    {
                        int direction = keyInfo.Key == ConsoleKey.Add || keyInfo.Key == ConsoleKey.OemPlus
                            || keyInfo.Key == ConsoleKey.RightArrow ? 1 : -1;
                        if (selection == 0)
                        {
                            Bugfixes.SmoothCamSeqHandoff = !Bugfixes.SmoothCamSeqHandoff;
                        }
                        else if (selection == 1)
                        {
                            Bugfixes.BetterCamSeqNodeRef = !Bugfixes.BetterCamSeqNodeRef;
                        }
                        else if (selection == 2)
                        {
                            Bugfixes.NoStrayRespawnText = !Bugfixes.NoStrayRespawnText;
                        }
                        else if (selection == 3)
                        {
                            Bugfixes.CorrectBountySfx = !Bugfixes.CorrectBountySfx;
                        }
                        else if (selection == 4)
                        {
                            Bugfixes.NoDoubleEnemyDeath = !Bugfixes.NoDoubleEnemyDeath;
                        }
                        else if (selection == 5)
                        {
                            Bugfixes.NoSlenchRollTimerUnderflow = !Bugfixes.NoSlenchRollTimerUnderflow;
                        }
                    }
                }
            }
            return true;
        }

        private enum MusicType
        {
            Music = 0,
            Seq = 1,
            Stream = 2
        }

        // todo?: other enemy hunter combinations
        private static readonly ImmutableArray<(MusicType Type, int Id, string Name)> _musicList =
        [
            (MusicType.Seq,    (int)SeqId.DRONE,                  "Intro"), // *
            (MusicType.Stream, (int)VoiceId.STRM_TITLE_SCREEN,    "Title"),
            (MusicType.Seq,    (int)SeqId.CHUTNEY,                "Menu"),
            (MusicType.Seq,    (int)SeqId.MENU1,                  "Menu (Unused)"),
            (MusicType.Music,  (int)MusicId.SEQ_MP2_M15,          "Celestial Archives VS."), // renamed from Celestial Gateway
            (MusicType.Music,  (int)MusicId.SEQ_MP2_M40,          "Celestial Archives VS. (Octolith)"), // *
            (MusicType.Music,  (int)MusicId.SEQ_MP2_M6,           "Celestial Archives VS. (Node)"), // *
            (MusicType.Music,  (int)MusicId.SEQ_MP1_M12,          "Alinos VS."), // renamed from Alinos Gateway
            (MusicType.Music,  (int)MusicId.SEQ_MP1_M11,          "Alinos VS. (Octolith)"), // *
            (MusicType.Music,  (int)MusicId.SEQ_MP1_M43,          "Alinos VS. (Node)"), // *
            (MusicType.Music,  (int)MusicId.SEQ_DILL_M33,         "VDO VS."), // renamed from Vesper Gateway
            (MusicType.Music,  (int)MusicId.SEQ_DILL_M42,         "VDO VS. (Octolith)"), // *
            (MusicType.Music,  (int)MusicId.SEQ_DILL_M47,         "VDO VS. (Node)"), // *
            (MusicType.Music,  (int)MusicId.SEQ_PEPPER_M36,       "Arcterra VS."), // renamed from Arcterra Gateway
            (MusicType.Music,  (int)MusicId.SEQ_PEPPER_M38,       "Arcterra VS. (Octolith)"), // *
            (MusicType.Music,  (int)MusicId.SEQ_PEPPER_M45,       "Arcterra VS. (Node)"), // *
            (MusicType.Seq,    (int)SeqId.RESULTS,                "Results"),
            (MusicType.Seq,    (int)SeqId.NEW_GAME,               "Story"), // *
            (MusicType.Seq,    (int)SeqId.SHIP,                   "Tetra Galaxy"), // *
            (MusicType.Seq,    (int)SeqId.FLY_IN_2,               "Landing (Celestial Archives)"),
            (MusicType.Seq,    (int)SeqId.SHIP_LAND2,             "Ship Cockpit (Celestial Archives)"),
            (MusicType.Music,  (int)MusicId.SEQ_GREY_M17,         "Shadows"), // *
            //(MusicType.Music,  (int)MusicId.SEQ_GREY_M52,       "Shadows (Unused)"), // identical to previous
            //(MusicType.Music,  (int)MusicId.SEQ_GREY_M58,       "Shadows (Unused)"), // identical to previous
            //(MusicType.Music,  (int)MusicId.SEQ_GREY_M61,       "Shadows (Unused)"), // identical to previous
            (MusicType.Music,  (int)MusicId.SEQ_ENEMY_1_M28,      "Enemies"),
            //(MusicType.Music,  (int)MusicId.SEQ_ENEMY_2_M46,    "Enemies (Unused)"), // identical to previous with a delay
            (MusicType.Music,  (int)MusicId.SEQ_YELLOW_M1,        "The Archives"),
            (MusicType.Music,  (int)MusicId.SEQ_INTRO_KANDEN_M30, "Pursuit"),
            (MusicType.Music,  (int)MusicId.SEQ_GUMBO_M3,         "Kanden"),
            (MusicType.Music,  (int)MusicId.SEQ_AMBIENT_1_M2,     "Foreboding"),
            (MusicType.Music,  (int)MusicId.SEQ_GARLIC_M4,        "Cretaphid"),
            (MusicType.Music,  (int)MusicId.SEQ_TELEPORT_M5,      "Aftermath"),
            (MusicType.Music,  (int)MusicId.SEQ_OREGANO_M55,      "Escape"),
            (MusicType.Music,  (int)MusicId.SEQ_OREGANO_M56,      "Escape (Alarm)"), // *
            //(MusicType.Music, (int)MusicId.SEQ_ENERGY_TIMER_M51, "Fuel Stack (Race)"), // identical to VDO VS.
            //(MusicType.Music, (int)MusicId.SEQ_ENERGY_TIMER_M57, "Fuel Stack (Failure"), // silent
            (MusicType.Seq,    (int)SeqId.FLY_IN_1,               "Landing (Alinos)"),
            (MusicType.Seq,    (int)SeqId.SHIP_LAND1,             "Ship Cockpit (Alinos)"),
            (MusicType.Music,  (int)MusicId.SEQ_RED_M13,          "Alinos"),
            //(MusicType.Music,  (int)MusicId.SEQ_INTRO_SPIRE_M9, "Spire Intro"), // identical to Kanden intro (Pursuit)
            (MusicType.Music,  (int)MusicId.SEQ_GUMBO_M37,        "Spire"),
            (MusicType.Music,  (int)MusicId.SEQ_SAFFRON_M29,      "Slench"),
            (MusicType.Music,  (int)MusicId.SEQ_GUMBO_M10,        "Weavel"),
            (MusicType.Seq,    (int)SeqId.FLY_IN_3,               "Landing (VDO)"),
            (MusicType.Seq,    (int)SeqId.SHIP_LAND3,             "Ship Cockpit (VDO)"),
            (MusicType.Music,  (int)MusicId.SEQ_GREEN_M19,        "The Outpost"),
            (MusicType.Music,  (int)MusicId.SEQ_GREEN_M50,        "The Outpost (Race)"), // *
            (MusicType.Music,  (int)MusicId.SEQ_GUARDIAN_M18,     "Guardians"),
            (MusicType.Music,  (int)MusicId.SEQ_GUMBO_M39,        "Sylux"),
            (MusicType.Seq,    (int)SeqId.FLY_IN_4,               "Landing (Arcterra)"),
            (MusicType.Seq,    (int)SeqId.SHIP_LAND4,             "Ship Cockpit (Arcterra)"),
            (MusicType.Music,  (int)MusicId.SEQ_WHITE_M48,        "Desolation"),
            (MusicType.Music,  (int)MusicId.SEQ_WHITE_M54,        "Desolation (Maze)"), // *
            //(MusicType.Music,  (int)MusicId.SEQ_WHITE_M63,      "Desolation (Unused)"), // identical to previous
            (MusicType.Music,  (int)MusicId.SEQ_BLUE_M14,         "Arcterra"),
            (MusicType.Music,  (int)MusicId.SEQ_BLUE_M44,         "Arcterra (Puzzle)"), // *
            (MusicType.Music,  (int)MusicId.SEQ_GUMBO_M49,        "Noxus & Trace"), // *
            (MusicType.Music,  (int)MusicId.SEQ_GUMBO_M7,         "Noxus"),
            (MusicType.Music,  (int)MusicId.SEQ_GUMBO_M41,        "Trace"),
            (MusicType.Music,  (int)MusicId.SEQ_RED_M60,          "The Elders"),
            //(MusicType.Music,  (int)MusicId.SEQ_RED_M62,        "The Elders (Unused)"), // identical to previous
            (MusicType.Music,  (int)MusicId.SEQ_BRINSTAR_M67,     "Magma Drop"), // *
            (MusicType.Music,  (int)MusicId.SEQ_PARASITE_M16,     "Demon Spawn"),
            //(MusicType.Music,  (int)MusicId.SEQ_PARASITE_X_M8,  "Demon Spawn (Unused)"), // nearly silent
            (MusicType.Music,  (int)MusicId.SEQ_INDIGO_M59,       "Space Decay"),
            (MusicType.Music,  (int)MusicId.SEQ_BLACK_M53,        "Watching"),
            //(MusicType.Music,  (int)MusicId.SEQ_BLACK_M64,      "Watching (Unused)"), // identical to previous
            (MusicType.Seq,    (int)SeqId.FLY_IN_GOREA,           "Landing (Oubliette)"),
            (MusicType.Music,  (int)MusicId.SEQ_GOREA_1_M20,      "Gorea"),
            (MusicType.Music,  (int)MusicId.SEQ_GOREA_1_M22,      "Gorea (Battlehammer)"),
            (MusicType.Music,  (int)MusicId.SEQ_GOREA_1_M26,      "Gorea (Volt Driver)"),
            (MusicType.Music,  (int)MusicId.SEQ_GOREA_1_M27,      "Gorea (Mamgmaul)"),
            (MusicType.Music,  (int)MusicId.SEQ_GOREA_1_M24,      "Gorea (Judicator)"),
            (MusicType.Music,  (int)MusicId.SEQ_GOREA_1_M23,      "Gorea (Imperialist)"),
            (MusicType.Music,  (int)MusicId.SEQ_GOREA_1_M25,      "Gorea (Shock Coil)"),
            (MusicType.Music,  (int)MusicId.SEQ_GOREA_1_M21,      "Gorea (Seal Sphere)"),
            (MusicType.Music,  (int)MusicId.SEQ_CREDITS_M65,      "Hunters (Credits)"), // renamed from Hunters
            //(MusicType.Music,  (int)MusicId.SEQ_CREDITS_M66,    "Hunters (Unused)"), // identical to previous
            (MusicType.Music,  (int)MusicId.SEQ_GOREA_2_M34,      "Oubliette"), // renamed from Gorea Returns
            //(MusicType.Music,  (int)MusicId.SEQ_GOREA_2_M33,    "Oubliette (Unused)"), // identical to previous
            (MusicType.Music,  (int)MusicId.SEQ_GOREA_2_M35,      "Oubliette (Node)"), // *
            (MusicType.Seq,    (int)SeqId.GET_WEAPON,             "Get Weapon"), // *
            (MusicType.Seq,    (int)SeqId.GET_OCTOLITH,           "Get Octolith") // *
        ];

        private static bool ShowSoundTest(SoundCapability soundCapability)
        {
            int selection = 0;
            IReadOnlyList<MusicTrack> info = SoundRead.ReadInterMusicInfo();
            if (soundCapability != SoundCapability.None)
            {
                Sfx.Load(scene: null!);
            }
            int playlist = 0;
            MusicId music = MusicId.SEQ_YELLOW_M1;
            MusicTrack track = info[(int)music];
            string musicStr = $"{music} [SEQ_{(SeqId)track.SeqId}]";
            SeqId seq = SeqId.BRINSTAR;
            ushort tracks = UInt16.MaxValue;
            SfxId sfx = SfxId.LID_CLOSE;
            VoiceId stream = VoiceId.VOICE_CONSECUTIVE_KILLS;

            void UpdatePlaylist(int dir)
            {
                playlist += dir;
                if (playlist < 0)
                {
                    playlist = _musicList.Length - 1;
                }
                else if (playlist >= _musicList.Length)
                {
                    playlist = 0;
                }
            }

            string GetPlaylistString()
            {
                (MusicType Type, int Id, string Name) item = _musicList[playlist];
                string id = item.Type switch
                {
                    MusicType.Stream => ((VoiceId)item.Id).ToString(),
                    MusicType.Seq => $"SEQ_{((SeqId)item.Id)}",
                    _ => ((MusicId)item.Id).ToString()
                };
                return $"{item.Name} [{id}]";
            }

            void UpdateMusic(int dir)
            {
                int id = (int)music + dir;
                if (id < 1)
                {
                    id = 68;
                }
                else if (id > 68)
                {
                    id = 1;
                }
                music = (MusicId)id;
                MusicTrack track = info[id];
                musicStr = $"{music} [SEQ_{(SeqId)track.SeqId}]";
            }

            void UpdateSeq(int dir)
            {
                int id = (int)seq + dir;
                if (id < 0)
                {
                    id = 59;
                }
                else if (id > 59)
                {
                    id = 0;
                }
                seq = (SeqId)id;
            }

            void UpdateSfx(int dir)
            {
                int id = (int)sfx + dir;
                if (id < 0)
                {
                    id = 48 | 0x8000;
                }
                else if (id > (48 | 0x8000))
                {
                    id = 0;
                }
                else if (id == 528)
                {
                    id = 0 | 0x8000;
                }
                else if (id == (0 | 0x8000) - 1)
                {
                    id = 527;
                }
                sfx = (SfxId)id;
            }

            string GetSfxString()
            {
                if (((int)sfx & 0x4000) != 0)
                {
                    return $"Script {(int)sfx & ~0x4000,-3} - {sfx}";
                }
                if (((int)sfx & 0x8000) != 0)
                {
                    return $"DGN {(int)sfx & ~0x8000,-2} - {sfx}";
                }
                return $"SFX {(int)sfx,-3:0} - SFX_{sfx}";
            }

            void UpdateStream(int dir)
            {
                int id = (int)stream + dir;
                if (id < 0)
                {
                    id = 11;
                }
                else if (id > 11)
                {
                    id = 0;
                }
                stream = (VoiceId)id;
            }

            void Stop()
            {
                MusicPlayer.Stop();
                Sfx.Instance?.StopAllSound(force: true);
            }

            void PlayPlaylist()
            {
                MusicPlayer.Stop();
                Sfx.Instance?.StopAllSound(force: true);
                (MusicType Type, int Id, string Name) list = _musicList[playlist];
                SeqId seqToPlay;
                if (list.Type == MusicType.Stream)
                {
                    Sfx.Instance?.PlayFreeStream(list.Id);
                    return;
                }
                if (list.Type == MusicType.Seq)
                {
                    seqToPlay = (SeqId)list.Id;
                    tracks = UInt16.MaxValue;
                }
                else // if (list.Type == MusicType.Music)
                {
                    MusicTrack item = info[list.Id];
                    tracks = item.Tracks;
                    seqToPlay = item.SeqId;
                }
                MusicPlayer.Load(seqToPlay, tracks);
                MusicPlayer.PlayWhenLoaded((float)_musicVolume);
            }

            void PlayMusic()
            {
                MusicPlayer.Stop();
                Sfx.Instance?.StopAllSound(force: true);
                MusicTrack item = info[(int)music];
                tracks = item.Tracks;
                MusicPlayer.Load(item.SeqId, tracks);
                MusicPlayer.PlayWhenLoaded((float)_musicVolume);
            }

            void PlaySeq()
            {
                MusicPlayer.Stop();
                Sfx.Instance?.StopAllSound(force: true);
                tracks = UInt16.MaxValue;
                MusicPlayer.Load(seq);
                MusicPlayer.PlayWhenLoaded((float)_musicVolume);
            }

            void PlaySfx()
            {
                MusicPlayer.Stop();
                Sfx.Instance?.StopAllSound(force: true);
                int id = (int)sfx;
                if ((id & 0x4000) != 0)
                {
                    // todo?: support playing SFX scripts
                    return;
                }
                if ((id & 0x8000) != 0)
                {
                    int amountA = Random.Shared.Next(0xFFFF);
                    int amountB = Random.Shared.Next(0xFFFF);
                    Sfx.Instance?.PlayDgn(id, source: null, loop: false, noUpdate: false, recency: -1, cancellable: false, amountA, amountB);
                }
                else
                {
                    Sfx.Instance?.PlaySample(id, source: null, loop: null, noUpdate: false,
                        recency: -1, sourceOnly: false, cancellable: false);
                }
            }

            void PlayStream()
            {
                MusicPlayer.Stop();
                Sfx.Instance?.StopAllSound(force: true);
                Sfx.Instance?.PlayFreeStream((int)stream);
            }

            string X(int index)
            {
                return $"[{(selection == index ? "x" : " ")}]";
            }

            while (true)
            {
                int s = 0;
                Console.Clear();
                Console.WriteLine($"{Mods.Branding.Name} {Program.Version}");
                Console.WriteLine();
                Console.WriteLine("Choose a setting using up/down or with the key indicated.");
                Console.WriteLine("Press Space to specify, Backspace to clear, or left/right to advance the setting.");
                Console.WriteLine("When finished, press Enter or use the last option to return. Press Escape to exit.");
                Console.WriteLine();
                Console.WriteLine("Sound Test");
                Console.WriteLine();
                Console.WriteLine($"{X(s++)} (P) MPH Playlist: {playlist,2} - {GetPlaylistString()}");
                Console.WriteLine($"{X(s++)} (M) Music Tracks: {(int)music,2} - {musicStr}");
                Console.WriteLine($"{X(s++)} (S) Raw Sequence: {(int)seq,2} - SEQ_{seq}");
                Console.WriteLine($"{X(s++)} (V) Voice/Stream: {(int)stream,2} - {stream}");
                Console.WriteLine($"{X(s++)} (X) Sound Effect: {GetSfxString()}");
                Console.WriteLine($"{X(s++)} (C) Stop");
                Console.WriteLine($"{X(s++)} (B) Go Back");
                s--;
                PrintSoundInfo(soundCapability);
                ConsoleKeyInfo keyInfo = Console.ReadKey();
                if (keyInfo.Key == ConsoleKey.UpArrow)
                {
                    selection--;
                    if (selection < 0)
                    {
                        selection = s;
                    }
                }
                else if (keyInfo.Key == ConsoleKey.DownArrow)
                {
                    selection++;
                    if (selection > s)
                    {
                        selection = 0;
                    }
                }
                else if (keyInfo.Key == ConsoleKey.Escape)
                {
                    MusicPlayer.Stop();
                    Sfx.ShutDown();
                    return false;
                }
                else if (keyInfo.Key == ConsoleKey.B || selection == s
                    && (keyInfo.Key == ConsoleKey.Enter || keyInfo.Key == ConsoleKey.Spacebar))
                {
                    break;
                }
                if (keyInfo.Key == ConsoleKey.P)
                {
                    selection = 0;
                }
                else if (keyInfo.Key == ConsoleKey.M)
                {
                    selection = 1;
                }
                else if (keyInfo.Key == ConsoleKey.S)
                {
                    selection = 2;
                }
                else if (keyInfo.Key == ConsoleKey.V)
                {
                    selection = 3;
                }
                else if (keyInfo.Key == ConsoleKey.X)
                {
                    selection = 4;
                }
                else if (keyInfo.Key == ConsoleKey.C || selection == 5 && keyInfo.Key == ConsoleKey.Spacebar)
                {
                    Stop();
                }
                else if (keyInfo.Key == ConsoleKey.Backspace)
                {
                    if (selection == 0)
                    {
                        playlist = 0;
                        UpdatePlaylist(0);
                    }
                    else if (selection == 1)
                    {
                        music = MusicId.SEQ_YELLOW_M1;
                        UpdateMusic(0);
                    }
                    else if (selection == 2)
                    {
                        seq = SeqId.BRINSTAR;
                        UpdateSeq(0);
                    }
                    else if (selection == 3)
                    {
                        stream = VoiceId.VOICE_CONSECUTIVE_KILLS;
                    }
                    else if (selection == 4)
                    {
                        sfx = SfxId.LID_CLOSE;
                    }
                }
                else if (keyInfo.Key == ConsoleKey.LeftArrow)
                {
                    if (selection == 0)
                    {
                        UpdatePlaylist(-1);
                    }
                    else if (selection == 1)
                    {
                        UpdateMusic(-1);
                    }
                    else if (selection == 2)
                    {
                        UpdateSeq(-1);
                    }
                    else if (selection == 3)
                    {
                        UpdateStream(-1);
                    }
                    else if (selection == 4)
                    {
                        UpdateSfx(-1);
                    }
                }
                else if (keyInfo.Key == ConsoleKey.RightArrow)
                {
                    if (selection == 0)
                    {
                        UpdatePlaylist(1);
                    }
                    else if (selection == 1)
                    {
                        UpdateMusic(1);
                    }
                    else if (selection == 2)
                    {
                        UpdateSeq(1);
                    }
                    else if (selection == 3)
                    {
                        UpdateStream(1);
                    }
                    else if (selection == 4)
                    {
                        UpdateSfx(1);
                    }
                }
                else if (keyInfo.Key == ConsoleKey.Spacebar)
                {
                    if (selection == 0)
                    {
                        Console.WriteLine();
                        Console.WriteLine("Enter playlist index.");
                        string? entry = Console.ReadLine();
                        if (Int32.TryParse(entry, out int id))
                        {
                            if (id >= 0 && id < _musicList.Length)
                            {
                                playlist = id;
                                UpdatePlaylist(0);
                                PlayPlaylist();
                            }
                        }
                    }
                    else if (selection == 1)
                    {
                        Console.WriteLine();
                        Console.WriteLine("Enter music name or ID.");
                        string? entry = Console.ReadLine();
                        if (Int32.TryParse(entry, out int id))
                        {
                            if (id >= 1 && id <= 68)
                            {
                                music = (MusicId)id;
                                UpdateMusic(0);
                                PlayMusic();
                            }
                        }
                        else if (Enum.TryParse<MusicId>(entry?.ToUpper(), out MusicId musicId)
                            && musicId != MusicId.Invalid && musicId != MusicId.None)
                        {
                            music = musicId;
                            UpdateMusic(0);
                            PlayMusic();
                        }
                    }
                    else if (selection == 2)
                    {
                        Console.WriteLine();
                        Console.WriteLine("Enter sequence name or ID.");
                        string? entry = Console.ReadLine();
                        if (Int32.TryParse(entry, out int id))
                        {
                            if (id >= 0 && id <= 59)
                            {
                                seq = (SeqId)id;
                                PlaySeq();
                            }
                        }
                        else if (Enum.TryParse<SeqId>(entry?.ToUpper()?.Replace("SEQ_", ""), out SeqId seqId) && seqId != SeqId.None)
                        {
                            seq = seqId;
                            PlaySeq();
                        }
                    }
                    else if (selection == 3)
                    {
                        Console.WriteLine();
                        Console.WriteLine("Enter voice/stream name or ID.");
                        string? entry = Console.ReadLine();
                        if (Int32.TryParse(entry, out int id))
                        {
                            if (id >= 0 && id <= 11)
                            {
                                stream = (VoiceId)id;
                                PlayStream();
                            }
                        }
                        else if (Enum.TryParse<VoiceId>(entry?.ToUpper(), out VoiceId streamId) && streamId != VoiceId.None)
                        {
                            stream = streamId;
                            PlayStream();
                        }
                    }
                    else if (selection == 4)
                    {
                        Console.WriteLine();
                        Console.WriteLine("Enter SFX name or ID.");
                        string? entry = Console.ReadLine();
                        if (Int32.TryParse(entry, out int id))
                        {
                            if (id >= 0 && id <= 527)
                            {
                                sfx = (SfxId)id;
                                PlaySfx();
                            }
                        }
                        else if (Enum.TryParse<SfxId>(entry?.ToUpper()?.Replace("SFX_", ""), out SfxId sfxId) && sfxId != SfxId.None)
                        {
                            int value = (int)sfxId;
                            if (value < (0 | 0x4000) || value > (104 | 0x4000)) // scripts are not supported
                            {
                                sfx = sfxId;
                                PlaySfx();
                            }
                        }
                    }
                }
                else if (keyInfo.Key == ConsoleKey.Enter)
                {
                    if (selection == 0)
                    {
                        PlayPlaylist();
                    }
                    else if (selection == 1)
                    {
                        PlayMusic();
                    }
                    else if (selection == 2)
                    {
                        PlaySeq();
                    }
                    else if (selection == 3)
                    {
                        PlayStream();
                    }
                    else if (selection == 4)
                    {
                        PlaySfx();
                    }
                }
            }
            MusicPlayer.Stop();
            Sfx.ShutDown();
            return true;
        }

        private static void ResetFeatures()
        {
            Features.NoRepeatEncounters = false;
            Features.AllowInvalidTeams = true;
            Features.TopScreenTargetInfo = true;
            Features.ProHud = false;
            Features.ProHudFixedWeapon = true;
            Features.ProHudSize = ProHudSize.Standard;
            Features.ProHudSafeArea = true;
            Features.ProHudHighContrast = false;
            Features.HelmetOpacity = 1;
            Features.VisorOpacity = 0.5f;
            Features.HudOpacity = 1;
            Features.ReticleOpacity = 1;
            Features.HudSway = true;
            Features.TargetInfoSway = false;
            Features.DelayedIdleSway = true;
            Features.NoIdleSway = false;
            Features.NoMapCentering = false;
            Features.MaxRoomDetail = false;
            Features.MaxPlayerDetail = true;
            Features.LogSpatialAudio = false;
            Features.HalfSecondAlarm = false;
            Features.FullBoostCharge = false;
            Features.BoostOpensDoors = false;
            Features.AlternateHunters1P = true;
            Cheats.FreeWeaponSelect = false;
            Cheats.UnlimitedJumps = false;
            Cheats.NoRandomEncounters = false;
            Cheats.UnlockAllDoors = false;
            Cheats.ContinueFromCurrentRoom = false;
            Cheats.SkipPlanetIntros = false;
            Cheats.StartWithAllUpgrades = false;
            Cheats.StartWithAllOctoliths = false;
            Cheats.WalkThroughWalls = false;
            Cheats.AlwaysFightGorea2 = false;
            Cheats.QuadrupleDamage = false;
            Bugfixes.SmoothCamSeqHandoff = false;
            Bugfixes.BetterCamSeqNodeRef = true;
            Bugfixes.NoStrayRespawnText = false;
            Bugfixes.CorrectBountySfx = true;
            Bugfixes.NoDoubleEnemyDeath = true;
            Bugfixes.NoSlenchRollTimerUnderflow = true;
        }

        private static readonly IReadOnlyList<decimal> _battlePoints = new decimal[]
        {
            1, 5, 7, 10, 15, 20, 25, 30, 40, 50, 60, 70, 80, 90, 100
        };

        private static readonly IReadOnlyList<decimal> _octolithPoints = new decimal[]
        {
            1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 15, 20, 25
        };

        private static readonly IReadOnlyList<decimal> _nodePoints = new decimal[]
        {
            40, 50, 60, 70, 80, 90, 100, 120, 140, 160, 180, 190, 200, 250
        };

        private static readonly IReadOnlyList<decimal> _extraLives = new decimal[]
        {
            0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10
        };

        private static readonly IReadOnlyList<decimal> _timeGoals = new decimal[]
        {
            1 * 60, 1.5m * 60, 2 * 60, 2.5m * 60, 3 * 60, 3.5m * 60, 4 * 60,
            4.5m * 60, 5 * 60, 6 * 60, 7 * 60, 8 * 60, 9 * 60, 10 * 60
        };

        private static readonly IReadOnlyList<decimal> _timeLimits = new decimal[]
        {
            3 * 60, 5 * 60, 7 * 60, 9 * 60, 10 * 60, 15 * 60, 20 * 60, 25 * 60,
            30 * 60, 35 * 60, 40 * 60, 45 * 60, 50 * 60, 55 * 60, 60 * 60
        };

        private static void ResetGoal()
        {
            if (_mode == "auto-select" || _mode.StartsWith("Battle"))
            {
                _pointGoal = 7;
            }
            else if (_mode.StartsWith("Survival"))
            {
                _pointGoal = 2;
            }
            else if (_mode == "Capture")
            {
                _pointGoal = 5;
            }
            else if (_mode.StartsWith("Bounty"))
            {
                _pointGoal = 3;
            }
            else if (_mode.StartsWith("Nodes"))
            {
                _pointGoal = 70;
            }
            else if (_mode.StartsWith("Defender") || _mode == "Prime Hunter")
            {
                _timeGoal = 1.5m * 60;
            }
        }

        private static void ResetTimeLimit()
        {
            if (_mode == "auto-select" || _mode.StartsWith("Battle"))
            {
                _timeLimit = 7 * 60;
            }
            else
            {
                _timeLimit = 15 * 60;
            }
        }

        private static void UpdateSettings()
        {
            _teams = false;
            _pointGoal = 0;
            _timeGoal = 0;
            _timeLimit = 0;
            _octolithReset = true;
            ResetGoal();
            ResetTimeLimit();
            if (_mode == "auto-select" || _mode.StartsWith("Battle") || _mode.StartsWith("Nodes"))
            {
                _goalType = "Point Goal";
            }
            else if (_mode.StartsWith("Survival"))
            {
                _goalType = "Extra Lives";
            }
            else if (_mode == "Capture" || _mode.StartsWith("Bounty"))
            {
                _goalType = "Octolith Goal";
            }
            else if (_mode.StartsWith("Defender") || _mode == "Prime Hunter")
            {
                _goalType = "Time Goal";
            }
            if (_mode == "Capture" || _mode.EndsWith("Teams"))
            {
                _teams = true;
            }
        }

    }
}
