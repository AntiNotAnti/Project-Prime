using System;
using System.Globalization;
using System.IO;
using MphRead.Mods;

namespace MphRead.Mods.Launcher
{
    /// <summary>
    /// Launcher-only preferences, kept in their own file beside the
    /// executable.
    ///
    /// Deliberately not folded into upstream's MenuSettings: that type is
    /// serialized by GameState and gains fields as upstream develops, so
    /// adding mod-specific keys to it would guarantee a merge conflict. A
    /// separate file costs nothing and keeps the fetch clean.
    /// </summary>
    public static class LauncherPrefs
    {
        public static string BackendAddress { get; set; } = "";
        /// <summary>
        /// Where launcher.txt lives. Beside the executable, which is where the
        /// rest of a portable install keeps its files -- except where the
        /// program does not own that folder. An Android package's own
        /// directory is read-only, so the head there points this at the app's
        /// data directory before anything reads.
        /// </summary>
        public static string Directory { get; set; } = AppContext.BaseDirectory;

        private static string Path => System.IO.Path.Combine(Directory, "launcher.txt");

        public static int LastRole { get; set; }
        public static string PlayerName { get; set; } = "Player";
        /// <summary>Hunter last chosen, possibly <see cref="Hunter.Random"/>.</summary>
        public static Hunter LastHunter { get; set; } = Hunter.Samus;
        /// <summary>Bots in an offline match.</summary>
        public static int Bots { get; set; } = 3;
        /// <summary>0 easy, 1 normal, 2 hard -- PlayerEntity.BotLevel.</summary>
        public static int BotLevel { get; set; } = 1;
        /// <summary>Last LaunchKind, so the front screen can offer it again.</summary>
        public static int LastKind { get; set; }

        /// <summary>
        /// Whether the front screen looks for a new release.
        ///
        /// Looks only. Finding one puts "Update now" on the screen, and that
        /// opens the release page in a browser; the download and the unpacking
        /// are the player's. On by default because a server refuses a client on
        /// a different protocol version outright, so an out-of-date copy is not
        /// a slightly worse copy, it is one that cannot join anything -- and
        /// nobody should have to work that out from a failed connection.
        /// </summary>
        public static bool AutoUpdate { get; set; } = true;

        /// <summary>
        /// How the game window opens. Kept here rather than in MenuSettings
        /// for the same reason as everything else in this file, and read by
        /// Mods.WindowMode, which is where the window itself lives.
        /// </summary>
        public static WindowStartMode WindowMode { get; set; } = WindowStartMode.Windowed;

        /// <summary>
        /// Whether the program writes a file of everything it can say about
        /// itself. See <see cref="Mods.DebugLog"/>.
        ///
        /// Off, and asked for rather than offered: it is here for the reports
        /// that cannot be answered any other way -- a crash while a map loads,
        /// on a machine nobody here can plug in -- and it costs a directory
        /// that grows and a lock on every line the program prints. One switch,
        /// in the corner of the front screen, kept where it was left.
        /// </summary>
        public static bool DebugLogs { get; set; }


        public static void Load()
        {
            if (!File.Exists(Path))
            {
                return;
            }
            try
            {
                foreach (string raw in File.ReadAllLines(Path))
                {
                    string line = raw.Trim();
                    int split = line.IndexOf('=');
                    if (line.Length == 0 || line[0] == '#' || split <= 0)
                    {
                        continue;
                    }
                    string key = line[..split].Trim();
                    string value = line[(split + 1)..].Trim();
                    switch (key)
                    {
                        case "player_name":
                            if (value.Length > 0)
                            {
                                PlayerName = value;
                            }
                            break;
                        case "last_role":
                            if (Int32.TryParse(value, NumberStyles.Integer,
                                CultureInfo.InvariantCulture, out int role))
                            {
                                LastRole = role;
                            }
                            break;
                        case "hunter":
                            if (Enum.TryParse(value, ignoreCase: true, out Hunter hunter))
                            {
                                LastHunter = hunter;
                            }
                            break;
                        case "bots":
                            if (Int32.TryParse(value, NumberStyles.Integer,
                                CultureInfo.InvariantCulture, out int bots))
                            {
                                Bots = bots;
                            }
                            break;
                        case "bot_level":
                            if (Int32.TryParse(value, NumberStyles.Integer,
                                CultureInfo.InvariantCulture, out int level))
                            {
                                BotLevel = level;
                            }
                            break;
                        case "window_mode":
                            WindowMode = Mods.WindowMode.Parse(value, WindowMode);
                            break;
                        case "backend_address":
                            BackendAddress = value;
                            break;
                        case "auto_update":
                            if (Boolean.TryParse(value, out bool autoUpdate))
                            {
                                AutoUpdate = autoUpdate;
                            }
                            break;
                        case "debug_logs":
                            if (Boolean.TryParse(value, out bool debugLogs))
                            {
                                DebugLogs = debugLogs;
                            }
                            break;
                        case "last_kind":
                            if (Int32.TryParse(value, NumberStyles.Integer,
                                CultureInfo.InvariantCulture, out int kind))
                            {
                                LastKind = kind;
                            }
                            break;
                    }
                }
            }
            catch (Exception)
            {
                // Preferences are a convenience; a unreadable file must not
                // stop the launcher from opening.
            }
        }

        public static void Save()
        {
            try
            {
                File.WriteAllLines(Path, new[]
                {
                    $"# {Branding.Name} launcher preferences.",
                    $"backend_address={BackendAddress}",
                    $"last_role={LastRole.ToString(CultureInfo.InvariantCulture)}",
                    $"player_name={PlayerName}",
                    $"hunter={LastHunter}",
                    $"bots={Bots.ToString(CultureInfo.InvariantCulture)}",
                    $"bot_level={BotLevel.ToString(CultureInfo.InvariantCulture)}",
                    $"last_kind={LastKind.ToString(CultureInfo.InvariantCulture)}",
                    $"auto_update={AutoUpdate.ToString().ToLowerInvariant()}",
                    $"debug_logs={DebugLogs.ToString().ToLowerInvariant()}",
                    $"window_mode={(WindowMode == WindowStartMode.BorderlessFullscreen ? "borderless" : "windowed")}"
                });
            }
            catch (Exception)
            {
                // Same rationale as Load: never block launching over this.
            }
        }
    }
}
