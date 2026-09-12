using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MphRead.Entities;
using MphRead.Mods;
using MphRead.Mods.Accounts;
using MphRead.Mods.Network;
using MphRead.Mods.Update;

namespace MphRead.Mods.Launcher
{
    /// <summary>
    /// Terminal launcher for local setup and diagnostics. Public multiplayer
    /// is selected through the GUI Node browser; the portable menu reports
    /// that migration instead of accepting direct endpoints.
    /// </summary>
    public static class TextLauncher
    {
        public static void Run()
        {
            LauncherPrefs.Load();
            AccountSessions.UseSecureStore(
                SecureSessionStoreFactory.CreateDefault());
            if (LauncherPrefs.UpdatePolicy != UpdatePolicy.Off && Update.Updater.Configured)
            {
                // Started in the background and then waited on briefly. This
                // screen is printed once and then blocks on a keypress, so a
                // check that lands afterwards has no line to appear on until
                // the menu is drawn again.
                Update.Updater.CheckInBackground(_ => { }, TryInstallAutomaticUpdate);
                Update.Updater.WaitForCheck(TimeSpan.FromSeconds(2));
            }
            if (GameFiles.Ready)
            {
                // Upstream's CheckSetup does this before anything runs; the
                // launcher is dispatched before that check, so it does it here
                // -- and tolerates the files being absent, which is the whole
                // reason it goes first.
                GameFiles.ApplyPaths();
                // A map added after the install was set up has no picture and
                // no sweep coming to give it one.
                Mods.DesktopThumbnailGenerator.EnsureCustomPreviews();
            }
            IReadOnlyList<string> rooms = Array.Empty<string>();

            // One launcher, then a match, then the launcher again, the same
            // loop the window runs. Settings and preferences are re-read each
            // time round because a match can commit its own copy of both.
            while (true)
            {
                TryInstallAutomaticUpdate();
                MenuSettings settings = ClientSettings.LoadSettings();
                Mods.GameSettings.Apply(settings);
                LauncherPrefs.Load();
                Mods.WindowMode.Startup = LauncherPrefs.WindowMode;
                if (rooms.Count == 0 && GameFiles.Ready)
                {
                    // Needs the game files: the room list is read out of them.
                    // Deferred rather than done up front so a fresh install can
                    // reach the entry that fixes that.
                    rooms = ThumbnailGenerator.MultiplayerRooms();
                }
                // See GuiLauncher: the roll behind "Random" is held for one
                // launch, and this is where a launch begins.
                Hunters.Reroll();
                if (!Home(settings, rooms, out LaunchPlan plan))
                {
                    return;
                }
                if (plan.Kind == LaunchKind.None)
                {
                    continue;
                }
                try
                {
                    var result = MatchStart.Run(settings, plan);
                    if (result.Message != null) Console.WriteLine(result.Message);
                    if (result.Reason == MatchExitReason.QuitApplication) return;
                }
                catch (Exception ex)
                {
                    Console.WriteLine();
                    Console.WriteLine($"The game could not start: {ex.Message}");
                    Console.WriteLine(ex.StackTrace);
                    return;
                }
                finally
                {
                    // The Node session owns control-plane state and NetSession
                    // owns the Worker UDP client. Stop only the client side;
                    // public hosting is never a local process.
                    NetSession.Stop();
                }
                // No PauseMenu.QuitProgram check, unlike the window's loop:
                // the pause menu is WinForms and cannot run in a build that
                // reaches this screen, so the flag could only ever be false.
                // Quitting is [q] here.
            }
        }

        private static void TryInstallAutomaticUpdate()
        {
            if (LauncherPrefs.UpdatePolicy != UpdatePolicy.Automatic
                || Update.Updater.Coordinator.Status.State is not
                    (UpdateState.Staged or UpdateState.WaitingForSafePoint))
                return;
            bool started = Update.Updater.InstallStagedAsync()
                .GetAwaiter().GetResult();
            if (started && Update.UpdateInstall.Current?.ExitAfterInstall == true)
            {
                // The staged updater is waiting for this process ID before it
                // mutates the installation. A terminal launcher has no window
                // close event to use as its safe exit boundary.
                Environment.Exit(0);
            }
        }

        /// <summary>
        /// "Update now": use the same coordinator path as the graphical
        /// launcher. This keeps NotifyOnly explicit while avoiding a second
        /// unverified browser/download flow.
        /// </summary>
        private static void UpdateNow(UpdateInfo update)
        {
            Console.WriteLine();
            Console.WriteLine($"  {Update.Updater.Describe(update)}");
            bool started = Update.Updater.DownloadAndInstallAsync()
                .GetAwaiter().GetResult();
            Console.WriteLine(started
                ? "  The verified update was staged and the installer was started."
                : "  Update did not start: "
                    + (Update.Updater.Coordinator.Status.Message
                        ?? "the package could not be staged."));
            Console.WriteLine();
        }

        /// <summary>
        /// The entries. Returns false when the answer was "quit"; a true with
        /// <see cref="LaunchKind.None"/> means "nothing to launch, show the
        /// screen again", which is what Settings, Credits and Game files do.
        /// </summary>
        private static bool Home(MenuSettings settings, IReadOnlyList<string> rooms,
            out LaunchPlan plan)
        {
            plan = default;
            string? problem = GameFiles.Problem();
            while (true)
            {
                Console.WriteLine();
                Console.WriteLine($"  {Mods.Branding.NameAndVersion}");
                Console.WriteLine("  --------------------------------------------");
                Console.WriteLine($"  Game files : {GameFiles.Describe()}");
                Console.WriteLine($"  Player     : {LauncherPrefs.PlayerName}"
                    + $" as {LauncherPrefs.LastHunter}");
                Console.WriteLine();
                // Before there is anything to play, the setup is the whole
                // screen. A menu that offers five things and refuses four of
                // them is a worse first impression than one that asks for the
                // one thing it needs.
                if (problem != null)
                {
                    Console.WriteLine($"  {problem}.");
                    Console.WriteLine();
                    Console.WriteLine("  [1] Game files       point this at your .nds dump");
                    Console.WriteLine("  [q] Quit");
                    Console.WriteLine();
                    Console.WriteLine($"  {Mods.Credits.Summary}");
                    Console.WriteLine();
                    string only = Ask("  Choose", "1").ToLowerInvariant();
                    if (only == "q" || only == "quit")
                    {
                        return false;
                    }
                    SetUpGameFiles();
                    problem = GameFiles.Problem();
                    if (problem == null)
                    {
                        // Straight into the launcher proper: there are map
                        // previews to show now, and every entry works.
                        return true;
                    }
                    continue;
                }
                if (Update.Updater.Configured && Update.Updater.Available != null)
                {
                    Console.WriteLine($"  {Update.Updater.Describe(Update.Updater.Available.Value)}");
                    Console.WriteLine();
                }
                Console.WriteLine("  [1] Join public game use the GUI Node browser");
                Console.WriteLine("  [2] Host public game use the GUI Node browser");
                Console.WriteLine("  [3] Settings         name, hunter, window");
                Console.WriteLine("  [4] Game files       point this at your .nds dump");
                if (Update.Updater.Configured && Update.Updater.Available != null)
                {
                    Console.WriteLine("  [u] Update now       download and install the verified update");
                }
                Console.WriteLine("  [q] Quit");
                Console.WriteLine();
                Console.WriteLine($"  {Mods.Credits.Summary} -credits for the full list.");
                Console.WriteLine();
                string choice = Ask("  Choose", "1").ToLowerInvariant();
                if (choice == "q" || choice == "quit")
                {
                    return false;
                }
                if (choice == "3")
                {
                    Settings();
                    continue;
                }
                if (choice == "4")
                {
                    SetUpGameFiles();
                    problem = GameFiles.Problem();
                    return true;
                }
                if (choice == "u" && Update.Updater.Configured && Update.Updater.Available != null)
                {
                    UpdateNow(Update.Updater.Available.Value);
                    continue;
                }
                if (problem != null)
                {
                    Console.WriteLine();
                    Console.WriteLine($"  {problem}. Use [4] first.");
                    continue;
                }
                switch (choice)
                {
                    case "1":
                        Console.WriteLine(NodeMigrationMessage);
                        continue;
                    case "2":
                        Console.WriteLine(NodeMigrationMessage);
                        continue;
                    default:
                        continue;
                }
            }
        }

        internal const string NodeMigrationMessage =
            "  Public multiplayer now uses the GUI Node browser. Start without -text to browse Nodes, create a public lobby, or join one.";

        /// <summary>
        /// The handful of settings this screen owns. Everything else --
        /// volumes, match rules, cheats, bugfixes -- is upstream's console
        /// menu (`-menu`), which reads and writes the same settings.json this
        /// screen has already applied.
        /// </summary>
        private static void Settings()
        {
            Console.WriteLine();
            LauncherPrefs.PlayerName = AskName();
            LauncherPrefs.LastHunter = AskHunter();
            LauncherPrefs.WindowMode = AskYesNo("  Start fullscreen",
                LauncherPrefs.WindowMode == WindowStartMode.BorderlessFullscreen)
                ? WindowStartMode.BorderlessFullscreen
                : WindowStartMode.Windowed;
            LauncherPrefs.Save();
            Console.WriteLine("  Saved.");
            Console.WriteLine("  Volumes, controls, match rules and public Node multiplayer are in the GUI.");
        }

        /// <summary>
        /// First run. The window opens a file picker; here the path is typed,
        /// and the extraction is the same child process either way.
        /// </summary>
        private static void SetUpGameFiles()
        {
            Console.WriteLine();
            Console.WriteLine($"  {Mods.Branding.Name} needs your own Metroid Prime Hunters cartridge");
            Console.WriteLine("  dump. It unpacks what it needs next to this program and");
            Console.WriteLine("  leaves the file alone. No game data is included or");
            Console.WriteLine("  downloaded.");
            Console.WriteLine();
            string path = Ask("  Path to the .nds file (blank to cancel)", "");
            if (path.Length == 0)
            {
                return;
            }
            // A path pasted from a file manager often arrives quoted, and the
            // quotes are not part of it.
            path = path.Trim().Trim('"', '\'');
            if (!System.IO.File.Exists(path))
            {
                Console.WriteLine($"  There is no file at {path}");
                return;
            }
            Console.WriteLine();
            var progress = new SetupProgress();
            bool redraw = !Console.IsOutputRedirected;
            bool ok = GameFiles.RunSetup(path, line =>
            {
                if (!progress.Observe(line))
                {
                    return;
                }
                if (redraw)
                {
                    // Over the top of itself, so a five-minute extraction is
                    // one line rather than a thousand. Only when the output is
                    // a terminal: into a pipe or a log, carriage returns just
                    // make an unreadable file.
                    Console.Write($"\r  {progress.Bar()}  {progress.Stage,-22}");
                }
                else
                {
                    Console.WriteLine($"  {progress.Bar()}  {progress.Stage}");
                }
            });
            progress.Finish(ok);
            if (redraw)
            {
                Console.Write($"\r  {progress.Bar()}  {progress.Stage,-22}");
            }
            Console.WriteLine();
            Console.WriteLine();
            Console.WriteLine(ok ? "  Ready to play." : "  Setup did not finish.");
            if (ok)
            {
                GenerateMissingThumbnails();
            }
        }

        private static void GenerateMissingThumbnails()
        {
            IReadOnlyList<string> missing = ThumbnailGenerator.MissingThumbnails();
            if (missing.Count == 0)
            {
                return;
            }
            Console.WriteLine("  Rendering map previews...");
            bool redraw = !Console.IsOutputRedirected;
            ThumbnailBatch.Run(missing, ThumbnailBatch.DefaultParallelism,
                ThumbnailGenerator.ThumbnailWidth, ThumbnailGenerator.ThumbnailHeight,
                line =>
                {
                    if (redraw)
                    {
                        Console.Write($"\r  {line,-70}");
                    }
                    else
                    {
                        Console.WriteLine($"  {line}");
                    }
                });
            if (redraw)
            {
                Console.WriteLine();
            }
        }

        private static string AskName()
        {
            string name = Ask("  Your name", LauncherPrefs.PlayerName);
            if (name.Length == 0)
            {
                name = LauncherPrefs.PlayerName;
            }
            LauncherPrefs.PlayerName = name;
            return name;
        }

        private static Hunter AskHunter()
        {
            // Seven playable hunters plus Random, which is what the picker on
            // the window offers; the enum carries entries past those.
            var hunters = new List<Hunter>();
            for (int i = 0; i < 7; i++)
            {
                hunters.Add((Hunter)i);
            }
            hunters.Add(Hunter.Random);
            int current = Math.Max(0, hunters.IndexOf(LauncherPrefs.LastHunter));
            Console.WriteLine();
            Console.WriteLine("  " + String.Join("  ", hunters.Select(
                (h, i) => $"[{i + 1}] {h}")));
            string answer = Ask("  Hunter", (current + 1).ToString(CultureInfo.InvariantCulture));
            Hunter hunter = Int32.TryParse(answer, NumberStyles.Integer,
                CultureInfo.InvariantCulture, out int index)
                && index >= 1 && index <= hunters.Count
                ? hunters[index - 1]
                : LauncherPrefs.LastHunter;
            LauncherPrefs.LastHunter = hunter;
            return hunter;
        }

        private static bool AskYesNo(string prompt, bool current)
        {
            string answer = Ask($"{prompt} (y/n)", current ? "y" : "n").ToLowerInvariant();
            return answer.Length > 0 ? answer[0] == 'y' : current;
        }

        /// <summary>
        /// Prompt, showing the remembered answer, and take a blank line to mean
        /// "keep it". A null from ReadLine means stdin closed -- a piped or
        /// backgrounded run -- and must not become an endless loop over EOF, so
        /// it reads as the default too.
        /// </summary>
        private static string Ask(string prompt, string fallback)
        {
            Console.Write(fallback.Length > 0 ? $"{prompt} [{fallback}]: " : $"{prompt}: ");
            string? line = Console.ReadLine();
            if (line == null)
            {
                Console.WriteLine();
                return fallback;
            }
            line = line.Trim();
            return line.Length == 0 ? fallback : line;
        }

    }
}
