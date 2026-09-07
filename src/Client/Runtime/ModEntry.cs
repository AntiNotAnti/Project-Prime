using System;
using System.Collections.Generic;
using System.Linq;
using MphRead.Entities;
using MphRead.Mods.Network;

namespace MphRead.Mods
{
    /// <summary>
    /// Single dispatch point for everything under Mods/.
    ///
    /// Upstream is touched in exactly one place (a call to TryHandle in
    /// Program.Main) so that pulling from NoneGiven/MphRead stays a fast
    /// forward instead of a conflict hunt. Every new mod command is added
    /// here, not in Program.cs.
    /// </summary>
    public static class ModEntry
    {
        /// <summary>
        /// Returns true if a mod command handled this invocation and the
        /// program should exit without running the normal paths.
        ///
        /// Takes the raw argv rather than Program's parsed Argument type,
        /// which is private: matching on the raw strings keeps the upstream
        /// hook to a single line and adds no coupling to internals that may
        /// be refactored later.
        /// </summary>
        /// <summary>
        /// Commands that run before interactive game-file setup. Simulation
        /// accepts an explicit content directory; directory and metadata tools
        /// can run without game files or a graphics device.
        /// </summary>
        public static bool TryHandleHeadless(string[] args)
        {
            string[] serverCommands = ["server", "dedicated", "authoritative-server", "masterserver", "server-apply-update", "authoritative-server-validate"];
            if (serverCommands.Any(command => HasFlag(args, command)))
            {
                Console.Error.WriteLine("Run this command with FruityPrimeServer; dedicated servers use their own executable.");
                Environment.ExitCode = 2;
                return true;
            }
            string[] toolCommands = ["servercontent", "mapbundle", "mapgen", "q3maps", "q3convert", "q3shaders", "mapmaterials", "mechanics", "setup", "extract", "x", "export", "e"];
            if (toolCommands.Any(command => HasFlag(args, command)))
            {
                Console.Error.WriteLine("Run this command with FruityPrimeTools; asset operations use their own executable.");
                Environment.ExitCode = 2;
                return true;
            }
            if (new[] { "combatcheck", "spectatorcheck", "combatduel", "headlesscheck", "server-sim" }.Any(command => HasFlag(args, command)))
            {
                Console.Error.WriteLine("Server integration diagnostics are available through nettest and the test suite.");
                Environment.ExitCode = 2;
                return true;
            }

            // Update maintenance must not load settings, clean another stage,
            // generate content or open a transport before handling its command.


            // Where the maps are. Read for every invocation and before
            // anything reads the map list, which is loaded once -- and against
            // the directory the command was typed in rather than the one the
            // process moved itself to (see ConsoleSetup.LaunchDirectory), so
            // `-mapdir maps` from a checkout means that checkout's maps.
            string? mapDir = ValueAfter(args, "mapdir");
            if (mapDir != null)
            {
                MapGen.CustomRooms.MapDirectory = System.IO.Path.GetFullPath(
                    System.IO.Path.Combine(ConsoleSetup.LaunchDirectory, mapDir));
            }




            // Keys and mouse feel, before anything creates a player. Called
            // here because this runs for every invocation, launcher or not.
            InputSettings.Load();
            // And the file of everything the program can say about itself, if
            // the player has asked for one. Read the preferences here rather
            // than waiting for the launcher to: a crash while a map loads
            // happens on paths that never open one, and the point of the log
            // is to be already running when that happens. -debuglog turns it
            // on for a single run without the setting, for the case where the
            // launcher itself is what will not start.
            Launcher.LauncherPrefs.Load();
            if (HasFlag(args, "debuglog"))
            {
                DebugLog.Force();
            }
            DebugLog.Attach();
            Update.Updater.Disabled = HasFlag(args, "noupdate");
            ApplyRenderOverrides(args);

            // The copying half of a desktop update, which is this build
            // started by the *previous* one. First, and before anything reads
            // a file or draws a window: it is not the game, it waits for the
            // old process to exit and copies itself over the installation.
            // See Mods/Update/DesktopUpdate.cs.
            int applyAt = IndexOfFlag(args, Update.DesktopUpdate.ApplyFlag);
            if (applyAt >= 0 && applyAt + 2 < args.Length)
            {
                // Two values, read by position rather than by name: the first
                // is a directory, and a directory is exactly the kind of
                // argument that can begin with a dash.
                Environment.ExitCode = Update.DesktopUpdate.Apply(args[applyAt + 1],
                    Int32.TryParse(args[applyAt + 2], out int parsed) ? parsed : -1);
                return true;
            }
            // Whatever the last update left behind. Here rather than in the
            // copying process, which cannot delete the directory it is running
            // from, and cheap when there is nothing there.
            Update.DesktopUpdate.Clean();
            // And the desktop's own installer, unless a platform head has
            // already put its own in place.
            Update.UpdateInstall.UseDesktopIfPossible();

            // A bad line, asked for. Before anything opens a socket, and for
            // every path that has one -- the game, the harness client and the
            // dedicated server alike -- so a fault that only shows up at 200
            // ms can be reproduced against the real server rather than only
            // behind a proxy in front of a local one. See Mods/Network/NetLag.
            string? netLag = ValueAfter(args, "netlag");
            if (netLag != null && !Network.NetLag.Configure(netLag))
            {
                Console.WriteLine($"[net] -netlag {netLag} is not a number of "
                    + "milliseconds (try -netlag 200 or -netlag 200:40)");
                return true;
            }
            string? netLoss = ValueAfter(args, "netloss");
            if (netLoss != null && !Network.NetLag.ConfigureLoss(netLoss))
            {
                Console.WriteLine($"[net] -netloss {netLoss} is not a percentage");
                return true;
            }
            if (Network.NetLag.Active)
            {
                Console.WriteLine($"[net] simulating a bad line: {Network.NetLag.Describe()}");
            }





            if (HasFlag(args, "credits"))
            {
                Credits.Print();
                return true;
            }

            // Cooking a bundle is here, before the game-file check, for the
            // reason the dedicated server is: it reads a recipe, the level
            // beside it and the textures baked from it, and touches no
            // extracted game data at all. The workflow runs it on a runner
            // that has none, where the check exits with "press any key" on a
            // console nobody is looking at -- and then throws, because there
            // is no console to read a key from either.



            // The explicit check, so there is always one command that answers
            // "am I on the latest build". Nothing is downloaded here either:
            // it prints the release page and opens it if there is a desktop to
            // open it on.
            if (HasFlag(args, "update"))
            {
                Update.Updater.Disabled = false;
                Update.UpdateInfo? update = Update.Updater.Check();
                if (update == null)
                {
                    Console.WriteLine($"[update] {Update.UpdateCheck.LastReason}");
                    return true;
                }
                Console.WriteLine($"[update] {Update.Updater.Describe(update.Value)}");
                Console.WriteLine($"[update] {update.Value.PageUrl}");
                Update.Updater.OpenPage(update.Value);
                return true;
            }
            // Before the game-file check, not after: a fresh install has no
            // paths.txt, and the check exits with "press any key" on a console
            // nobody is looking at. The launcher is the screen that fixes
            // that, so it has to be reachable first.
            // The launcher. The window first, the text screen when there is no
            // display to put it on -- an SSH login, a container, a machine with
            // no X or Wayland session. -text asks for the text one on a machine
            // that has both.
            //
            // No arguments means somebody double-clicked the binary, and on the
            // platforms where that is how a program is normally started that
            // has to be the launcher: the console menu behind it is for people
            // who typed something, and -menu is how they still get it. On Linux
            // a bare invocation has always opened upstream's console menu and
            // still does -- that is a screen people are already using, not an
            // empty spot to fill.
            bool doubleClicked = args.Length == 0
                && (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS());
            if ((HasFlag(args, "launcher") || doubleClicked) && !HasFlag(args, "menu"))
            {
                if (!HasFlag(args, "text") && Launcher.Gui.GuiLauncher.TryRun())
                {
                    return true;
                }
                // The window could not be opened. On Windows that means the
                // process has no console either -- it is a GUI binary -- so the
                // text launcher would print into nothing.
                if (OperatingSystem.IsWindows())
                {
                    Mods.ConsoleWindow.Show();
                }
                Launcher.TextLauncher.Run();
                return true;
            }
            // The server directory: -masterserver. Same binary as the game
            // server on purpose -- the machine that runs one usually runs the
            // other, and a second thing to install is a second thing to forget
            // to restart.

            // The server list, printed. Same two calls the launcher's browser
            // makes -- ask the directory, then ask each server it named -- so
            // this is how that data path gets checked on a machine with no
            // WinForms, which is every machine that is not Windows.
            if (HasFlag(args, "servers"))
            {
                ListServers(ValueAfter(args, "master") ?? NetMasterConfig.DefaultHost,
                    ValueAfter(args, "masterport"));
                return true;
            }
            return false;
        }


        private static void ListServers(string masterHost, string? portValue)
        {
            int port = NetMasterConfig.DefaultPort;
            if (portValue != null && Int32.TryParse(portValue, out int parsed))
            {
                port = parsed;
            }
            Console.WriteLine($"[servers] asking {masterHost}:{port}");
            MasterListResult result = NetMasterClient.Query(masterHost, port);
            if (!result.Answered)
            {
                Console.WriteLine($"[servers] no answer from {masterHost}:{port} -- "
                    + "it may be down, or UDP may not reach it");
                return;
            }
            if (result.Servers.Count == 0)
            {
                Console.WriteLine("[servers] the directory is up and has nobody listed");
                return;
            }
            Console.WriteLine($"[servers] {result.Servers.Count} listed; asking each one");
            foreach (MasterListing listing in result.Servers)
            {
                // Directly, not through the directory: the round trip that
                // matters is this machine's, and the answer also proves the
                // server is reachable from here rather than only from there.
                ServerStatus status = NetStatus.Query(listing.Address, listing.Port,
                    allowJoinProbe: false);
                string name = status.ServerName.Length > 0
                    ? status.ServerName
                    : listing.ServerName.Length > 0 ? listing.ServerName : listing.Endpoint;
                if (!status.Online)
                {
                    Console.WriteLine($"  {name,-24} {listing.Endpoint,-26} did not answer");
                    continue;
                }
                string players = status.MaxPlayers > 0
                    ? $"{status.Players}/{status.MaxPlayers}"
                    : status.Players.ToString();
                string ping = status.Latency >= 0 ? $"{status.Latency} ms" : "-- ms";
                Console.WriteLine($"  {name,-24} {listing.Endpoint,-26} "
                    + $"{status.RoomKey,-20} {NetStatus.ModeName(status.Mode),-14} "
                    + $"{players,-6} {ping}");
            }
        }

        public static bool TryHandle(string[] args)
        {
            (int width, int height) = ParseSize(args);

            // Custom maps are registered as rooms from their JSON at startup,
            // but a room whose binaries are not on disk crashes the moment
            // something tries to load it. Generating what is missing here --
            // the one place every entry point passes through, launcher
            // included, and after the game-file check -- means a map file is
            // enough to have a working room.
            if (!HasFlag(args, "mapgen"))
            {
                MapGen.MapPreparation.GenerateMissing();
            }

            // Opt-in per-second report of what this process believes about a
            // networked session -- slot occupancy, scoreboard count, which
            // remote slots have state. The failure worth catching is not
            // visible on the wire: two correctly connected clients can each
            // hold a scene containing only themselves.
            // Display flags, for the paths that never open a launcher.
            if (HasFlag(args, "fullscreen") || HasFlag(args, "borderless"))
            {
                WindowMode.Startup = WindowStartMode.BorderlessFullscreen;
            }
            else if (HasFlag(args, "windowed"))
            {
                WindowMode.Startup = WindowStartMode.Windowed;
            }
            if (HasFlag(args, "nohelmet"))
            {
                // Both of them: the helmet is drawn as three layers and the
                // visor is one of them, so zeroing only HelmetOpacity leaves a
                // tinted pane over the view that reads as a bug rather than as
                // a setting. The settings window ties the two together for the
                // same reason.
                Features.HelmetOpacity = 0;
                Features.VisorOpacity = 0;
            }
            if (HasFlag(args, "netdebug"))
            {
                Network.NetDiagnostics.Enabled = true;
                Network.MapAudit.Diagnostic = true;
            }

            // Print the game's own tables as markdown, so the mechanics
            // documentation is generated from the data rather than kept by
            // hand and quietly going stale.


            // What a connected pad is doing, with no match in the way. The
            // only way to tell "not connected" from "connected but not
            // mapped" from "the dead zone is eating it" apart.
            if (HasFlag(args, "gamepad"))
            {
                double seconds = 15;
                string? given = ValueAfter(args, "seconds");
                if (given != null && Double.TryParse(given, out double parsed) && parsed > 0)
                {
                    seconds = parsed;
                }
                Environment.ExitCode = Input.GamepadProbe.Run(seconds);
                return true;
            }

            // The multiplayer room list, one per line, so a shell loop can
            // walk every map without hard-coding the names.
            if (HasFlag(args, "rooms"))
            {
                foreach (string room in ThumbnailGenerator.MultiplayerRooms())
                {
                    Console.WriteLine(room);
                }
                return true;
            }

            // Load one room with a full house of players and report what it
            // contains and whether it survived.
            string? dpsTest = ValueAfter(args, "dpstest");
            if (dpsTest != null)
            {
                Hunter dpsHunter = Hunter.Sylux;
                string? dpsHunterValue = ValueAfter(args, "hunter");
                if (dpsHunterValue != null && Enum.TryParse(dpsHunterValue, ignoreCase: true, out Hunter parsedDpsHunter))
                {
                    dpsHunter = parsedDpsHunter;
                }
                BeamType dpsBeam = BeamType.ShockCoil;
                string? dpsBeamValue = ValueAfter(args, "weapon");
                if (dpsBeamValue != null && Enum.TryParse(dpsBeamValue, ignoreCase: true, out BeamType parsedDpsBeam))
                {
                    dpsBeam = parsedDpsBeam;
                }
                double dpsSeconds = 10;
                string? dpsSecondsValue = ValueAfter(args, "seconds");
                if (dpsSecondsValue != null && Double.TryParse(dpsSecondsValue,
                    System.Globalization.CultureInfo.InvariantCulture, out double parsedDpsSeconds))
                {
                    dpsSeconds = parsedDpsSeconds;
                }
                float dpsDistance = 2.2f;
                string? dpsDistanceValue = ValueAfter(args, "distance");
                if (dpsDistanceValue != null && Single.TryParse(dpsDistanceValue,
                    System.Globalization.CultureInfo.InvariantCulture, out float parsedDpsDistance))
                {
                    dpsDistance = parsedDpsDistance;
                }
                Environment.ExitCode = Network.WeaponDps.Run(dpsTest, dpsHunter, dpsBeam, dpsSeconds, dpsDistance);
                return true;
            }
            // Pictures of the launcher's own screens, rendered without a
            // window. The one part of this program that could not be looked at
            // from a headless box.
            string? uiShot = ValueAfter(args, "uishot");
            if (uiShot != null)
            {
                Environment.ExitCode = RunUiCapture(uiShot);
                return true;
            }

            if (HasFlag(args, "frametimingcheck"))
            {
                Environment.ExitCode = Render.FrameTimingCheck.Run();
                return true;
            }

            string? mapTest = ValueAfter(args, "maptest");
            if (mapTest != null)
            {
                int players = 8;
                string? playerValue = ValueAfter(args, "players");
                if (playerValue != null && Int32.TryParse(playerValue, out int parsed))
                {
                    players = parsed;
                }
                double seconds = 10;
                string? secondsValue = ValueAfter(args, "seconds");
                if (secondsValue != null && Double.TryParse(secondsValue,
                    System.Globalization.CultureInfo.InvariantCulture, out double parsedSeconds))
                {
                    seconds = parsedSeconds;
                }
                GameMode mapMode = GameMode.Battle;
                string? modeValue = ValueAfter(args, "mode");
                if (modeValue != null && Enum.TryParse(modeValue, ignoreCase: true, out GameMode parsedMode))
                {
                    mapMode = parsedMode;
                }
                // The HUD is drawn to the window, not to the offscreen
                // target every other capture reads, so seeing it needs a real
                // window and a read from its buffer.
                Network.MapAudit.ShowWindow = HasFlag(args, "hudshots");
                // -drawrate N draws each simulation step N times, which is
                // what a 144 Hz screen does to a 60 Hz game. It is how the
                // decoupled loop is checked from a box with no display.
                string? drawRate = ValueAfter(args, "drawrate");
                if (drawRate != null && Int32.TryParse(drawRate, out int parsedDrawRate)
                    && parsedDrawRate > 0)
                {
                    Network.MapAudit.DrawRate = parsedDrawRate;
                }
                // -size WxH, so a HUD capture can be taken at a window shape
                // other than the one this happens to default to.
                string? sizeValue = ValueAfter(args, "size");
                if (sizeValue != null)
                {
                    string[] parts = sizeValue.ToLowerInvariant().Split('x');
                    if (parts.Length == 2 && Int32.TryParse(parts[0], out int sizeWidth)
                        && Int32.TryParse(parts[1], out int sizeHeight)
                        && sizeWidth > 0 && sizeHeight > 0)
                    {
                        Network.MapAudit.WindowSize = new OpenTK.Mathematics.Vector2i(sizeWidth, sizeHeight);
                    }
                }
                Environment.ExitCode = Network.MapAudit.Run(mapTest, players, seconds, mapMode,
                    bots: HasFlag(args, "bots"), shotDirectory: ValueAfter(args, "shots"),
                    renderProbe: HasFlag(args, "renderprobe"),
                    allNodes: HasFlag(args, "allnodes"));
                return true;
            }

            // Ask the directory to run a match and join it. The launcher's
            // "Online, no setup" in one command -- and the only way to host
            // from a machine with no launcher, which is every machine that is
            // not Windows.
            string? hostGame = ValueAfter(args, "hostgame");
            if (hostGame != null)
            {
                string masterHost = ValueAfter(args, "master") ?? NetMasterConfig.DefaultHost;
                int masterPort = NetMasterConfig.DefaultPort;
                string? masterPortValue = ValueAfter(args, "masterport");
                if (masterPortValue != null && Int32.TryParse(masterPortValue, out int parsedMaster))
                {
                    masterPort = parsedMaster;
                }
                GameMode hostMode = GameMode.Battle;
                string? hostModeValue = ValueAfter(args, "mode");
                if (hostModeValue != null
                    && Enum.TryParse(hostModeValue, ignoreCase: true, out GameMode parsedHostMode))
                {
                    hostMode = parsedHostMode;
                }
                string hostName = ParseName(args);
                Console.WriteLine($"[net] asking {masterHost}:{masterPort} to run {hostGame}");
                HostedGame game = NetMasterClient.RequestGame(masterHost, masterPort,
                    hostGame, hostMode, timeLimit: 7 * 60, pointGoal: 7,
                    maxPlayers: PlayerEntity.SlotCapacity, serverName: $"{hostName}'s game");
                if (!game.Started)
                {
                    Console.WriteLine($"[net] it would not: {game.Reason}");
                    Environment.ExitCode = 1;
                    return true;
                }
                Console.WriteLine($"[net] running on {game.Host}:{game.Port}; joining it");
                Network.NetConnectCommand.Run(game.Host, game.Port, hostName,
                    ParseHunter(args), ParseRecolor(args));
                return true;
            }

            // Join a server from the command line, with no launcher dialog.
            // The only way to start a client on a platform without WinForms,
            // and the only practical way to start two of them side by side --
            // which is the arrangement every bug in this feature has needed.
            string? connect = ValueAfter(args, "connect");
            if (connect != null)
            {
                Network.NetConnectCommand.Run(connect, ParsePort(args), ParseName(args),
                    ParseHunter(args), ParseRecolor(args));
                return true;
            }

            // The same client, running to a script and reporting what it saw.
            string? check = ValueAfter(args, "netcheck");
            if (check != null)
            {
                string? shots = ValueAfter(args, "shots");
                double seconds = 30;
                string? secondsValue = ValueAfter(args, "seconds");
                if (secondsValue != null && Double.TryParse(secondsValue,
                    System.Globalization.CultureInfo.InvariantCulture, out double parsedSeconds))
                {
                    seconds = parsedSeconds;
                }
                // Watching instead of playing. -spectate on its own means
                // "from the moment the map is up"; with a number it is the
                // second to stop playing at, and -rejoin the second to come
                // back. Spectating is the one player state the scripted tour
                // cannot reach on its own -- the tour exists to drive a
                // hunter, and this is a player who has stopped driving one.
                double spectateAt = -1;
                double rejoinAt = -1;
                if (HasFlag(args, "spectate"))
                {
                    spectateAt = 0;
                    string? spectateValue = ValueAfter(args, "spectate");
                    if (spectateValue != null && Double.TryParse(spectateValue,
                        System.Globalization.CultureInfo.InvariantCulture, out double parsedSpectate))
                    {
                        spectateAt = parsedSpectate;
                    }
                }
                string? rejoinValue = ValueAfter(args, "rejoin");
                if (rejoinValue != null && Double.TryParse(rejoinValue,
                    System.Globalization.CultureInfo.InvariantCulture, out double parsedRejoin))
                {
                    rejoinAt = parsedRejoin;
                }
                Environment.ExitCode = Network.NetCheckClient.Run(check, ParsePort(args),
                    ParseName(args), ParseHunter(args), seconds, shots, width, height,
                    recordDemo: HasFlag(args, "recorddemo"),
                    spectateAt: spectateAt, rejoinAt: rejoinAt);
                return true;
            }

            string? demoCheck = ValueAfter(args, "democheck");
            if (demoCheck != null)
            {
                double seconds = Double.TryParse(ValueAfter(args, "seconds"),
                    System.Globalization.CultureInfo.InvariantCulture, out double duration) ? duration : 60;
                Environment.ExitCode = DemoPlaybackCheck.Run(demoCheck, seconds);
                return true;
            }

            // What a recorded match actually contains. Reads the file and
            // nothing else -- no room, no window, no game files.
            string? demoInfo = ValueAfter(args, "demoinfo");
            if (demoInfo != null)
            {
                Environment.ExitCode = Network.DemoInfo.Print(demoInfo,
                    replay: HasFlag(args, "replay"));
                return true;
            }

            // Worker invocation: capture the rooms this process was given and
            // exit. This is what ThumbnailBatch spawns -- a share of the
            // batch rather than one room, so the runtime that starts and the
            // code that JITs are paid for once across several pictures -- and
            // a single -thumbnail still works by hand to re-shoot one map.
            List<string> share = ValuesAfter(args, "thumbnail");
            if (share.Count > 0)
            {
                int captured = ThumbnailCapture.CaptureRooms(share, width, height);
                Console.WriteLine($"[thumbnails] captured {captured}/{share.Count}");
                return true;
            }

            if (HasFlag(args, "thumbnails"))
            {
                GenerateThumbnails(args, width, height);
                return true;
            }
            return false;
        }

        private static void GenerateThumbnails(string[] args, int width, int height)
        {
            bool force = HasFlag(args, "force");
            IReadOnlyList<string> rooms = force
                ? ThumbnailGenerator.MultiplayerRooms()
                : ThumbnailGenerator.MissingThumbnails();
            if (rooms.Count == 0)
            {
                Console.WriteLine("[thumbnails] all previews already present in "
                    + ThumbnailGenerator.CacheDirectory);
                Console.WriteLine("[thumbnails] pass -force to re-render them");
                return;
            }
            int jobs = ThumbnailBatch.DefaultParallelism;
            string? jobsValue = ValueAfter(args, "jobs");
            if (jobsValue != null && Int32.TryParse(jobsValue, out int parsedJobs))
            {
                jobs = parsedJobs;
            }
            Console.WriteLine($"[thumbnails] rendering {rooms.Count} preview(s) at "
                + $"{width}x{height}, {jobs} at a time");
            Console.WriteLine($"[thumbnails] output: {ThumbnailGenerator.CacheDirectory}");
            int written = ThumbnailBatch.Run(rooms, jobs, width, height);
            Console.WriteLine($"[thumbnails] done -- {written}/{rooms.Count} written");
        }

        private static (int Width, int Height) ParseSize(string[] args)
        {
            string? value = ValueAfter(args, "size");
            if (value != null)
            {
                string[] parts = value.Split('x', 'X');
                if (parts.Length == 2
                    && Int32.TryParse(parts[0], out int w)
                    && Int32.TryParse(parts[1], out int h)
                    && w > 0 && h > 0)
                {
                    return (w, h);
                }
                Console.WriteLine($"[thumbnails] ignoring -size {value} (expected e.g. 1920x1440)");
            }
            return (ThumbnailGenerator.ThumbnailWidth, ThumbnailGenerator.ThumbnailHeight);
        }

        private static int ParsePort(string[] args)
        {
            string? value = ValueAfter(args, "port");
            return value != null && Int32.TryParse(value, out int port) ? port : NetConfig.DefaultPort;
        }

        private static string ParseName(string[] args)
        {
            return ValueAfter(args, "name") ?? Environment.MachineName;
        }

        private static Hunter ParseHunter(string[] args)
        {
            string? value = ValueAfter(args, "hunter");
            return value != null && Enum.TryParse(value, ignoreCase: true, out Hunter hunter)
                ? hunter
                : Hunter.Samus;
        }

        private static int ParseRecolor(string[] args)
        {
            string? value = ValueAfter(args, "recolor");
            return value != null && Int32.TryParse(value, out int recolor) ? recolor : 0;
        }

        /// <summary>
        /// Render-option overrides from the command line, applied for every
        /// invocation before anything draws.
        ///
        /// The settings file is the launcher's, and the paths that never open
        /// one -- <c>-thumbnail</c>, <c>-maptest</c>, <c>-connect</c> -- had no
        /// way to ask for cel shading at all. That made the one mode whose
        /// whole point is what the picture looks like the one mode no
        /// screenshot command could turn on.
        /// </summary>
        private static void ApplyRenderOverrides(string[] args)
        {
            string? cel = ValueAfter(args, "cel");
            if (cel != null && !cel.StartsWith('-'))
            {
                RenderOptions.CelShading = RenderOptions.ParseOnOff(cel, RenderOptions.CelShading);
            }
            else if (HasFlag(args, "cel"))
            {
                // a bare -cel, with the next word belonging to another option
                RenderOptions.CelShading = true;
            }
            string? fog = ValueAfter(args, "fog");
            if (fog != null && !fog.StartsWith('-'))
            {
                RenderOptions.Fog = RenderOptions.ParseOnOff(fog, RenderOptions.Fog);
            }
            string? fps = ValueAfter(args, "fps");
            if (fps != null && !fps.StartsWith('-'))
            {
                RenderOptions.ShowFps = RenderOptions.ParseOnOff(fps, RenderOptions.ShowFps);
            }
            else if (HasFlag(args, "fps"))
            {
                RenderOptions.ShowFps = true;
            }
            // The frame rate, for the paths that never open a launcher --
            // which is every screenshot command and every scripted run. The
            // simulation is not affected by either of these: it is pinned at
            // 60 Hz in Mods/Render/FrameTiming.cs and these only decide how
            // often, and how smoothly, it is drawn.
            string? fpsCap = ValueAfter(args, "fpscap");
            if (fpsCap != null && !fpsCap.StartsWith('-'))
            {
                Render.FrameTiming.FrameRateCap = Render.FrameTiming.ParseCap(fpsCap,
                    Render.FrameTiming.FrameRateCap);
            }
            string? bands = ValueAfter(args, "celbands");
            if (bands != null && Int32.TryParse(bands, out int bandCount))
            {
                RenderOptions.CelBands = bandCount;
            }
            string? edge = ValueAfter(args, "celedge");
            if (edge != null && Int32.TryParse(edge.TrimEnd('%'), out int edgePercent))
            {
                RenderOptions.CelEdge = edgePercent / 100f;
            }
            // The whole competitive HUD, for the same paths and the same
            // reason: it is a mode whose point is what the picture looks like,
            // and every command that can photograph one opens no launcher.
            string? proHud = ValueAfter(args, "prohud");
            if (proHud != null && !proHud.StartsWith('-'))
            {
                Features.ProHud = RenderOptions.ParseOnOff(proHud, Features.ProHud);
            }
            else if (HasFlag(args, "prohud"))
            {
                Features.ProHud = true;
            }
            // Which crosshair that HUD draws, and how big. Same reason again:
            // a screenshot command opens no launcher, and the crosshair is the
            // one thing in the middle of every one of those pictures.
            string? crosshair = ValueAfter(args, "crosshair");
            if (crosshair != null && !crosshair.StartsWith('-'))
            {
                Render.Crosshair.Style = Render.Crosshair.ParseStyle(crosshair,
                    Render.Crosshair.Style);
            }
            string? crosshairSize = ValueAfter(args, "crosshairsize");
            if (crosshairSize != null && !crosshairSize.StartsWith('-'))
            {
                Render.Crosshair.Size = Render.Crosshair.ParseSize(crosshairSize,
                    Render.Crosshair.Size);
            }
        }

        private static bool HasFlag(string[] args, string name)
        {
            return args.Any(a => a.TrimStart('-').Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Kept out of <see cref="TryHandle"/> and told not to inline.
        ///
        /// The runtime loads the assemblies a method needs when it first
        /// *enters* that method, not when it reaches the call -- so naming
        /// UiCapture directly in TryHandle made every command load Avalonia,
        /// including `-server`. On a machine without it that is not a missing
        /// feature, it is the dedicated server aborting at startup with a
        /// FileNotFoundException, which is exactly what the netcheck clients
        /// did against a bin/ that had not been refreshed.
        /// </summary>
        [System.Runtime.CompilerServices.MethodImpl(
            System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static int RunUiCapture(string directory)
        {
            try
            {
                return Launcher.Gui.UiCapture.Run(directory);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[uishot] no launcher toolkit here: {ex.Message}");
                return 1;
            }
        }

        /// <summary>Where an option appears, or -1. For the ones read by position.</summary>
        private static int IndexOfFlag(string[] args, string name)
        {
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i].TrimStart('-').Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }
            return -1;
        }

        private static string? ValueAfter(string[] args, string name)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i].TrimStart('-').Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    return args[i + 1];
                }
            }
            return null;
        }

        /// <summary>
        /// Every value given for a repeated option, in order. A thumbnail
        /// worker is handed a whole share of rooms this way rather than one.
        /// </summary>
        private static List<string> ValuesAfter(string[] args, string name)
        {
            var values = new List<string>();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i].TrimStart('-').Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    values.Add(args[i + 1]);
                }
            }
            return values;
        }
    }
}
