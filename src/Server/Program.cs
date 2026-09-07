using System;
using System.IO;
using System.Linq;
using MphRead.Mods;
using MphRead.Mods.Network;
using Update = MphRead.Mods.Update;

namespace MphRead;

internal static class ServerProgram
{
    public static int Main(string[] args)
    {
        ConsoleSetup.Run();
        try
        {
            if (!Run(args))
            {
                Console.WriteLine("Prime Hunters dedicated server\n-server [ROOM] -data DIRECTORY [-port 27888] [-rotation FILE] [-players 8] [-friendlyfire true] [-spawnpolicy classic|enhanced|duel] [-cancelspawnprotection true|false]\n-masterserver [-port 27889] [-data DIRECTORY] [-hostports 27900-27919] [-public HOST]");
                return args.Length == 0 || HasFlag(args, "help") ? 0 : 2;
            }
            return Environment.ExitCode;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("[server] " + ex);
            return 1;
        }
    }

    private static bool Run(string[] args)
    {
            if (HasFlag(args, "server-apply-update"))
            {
                string? plan = ValueAfter(args, "server-apply-update");
                Environment.ExitCode = plan == null ? 1 : Update.ServerUpdateInstall.ApplyHandoff(plan);
                return true;
            }
            if (HasFlag(args, "authoritative-server-validate"))
            {
                Environment.ExitCode = Update.ServerUpdateRuntime.RunValidation(args);
                return true;
            }
        string? mapDir = ValueAfter(args, "mapdir");
        if (mapDir != null) Mods.MapGen.CustomRooms.MapDirectory = Path.GetFullPath(Path.Combine(ConsoleSetup.LaunchDirectory, mapDir));
            string? netLag = ValueAfter(args, "netlag");
            if (netLag != null && !NetLag.Configure(netLag))
            {
                Console.WriteLine($"[net] -netlag {netLag} is not a number of "
                    + "milliseconds (try -netlag 200 or -netlag 200:40)");
                return true;
            }
            string? netLoss = ValueAfter(args, "netloss");
            if (netLoss != null && !NetLag.ConfigureLoss(netLoss))
            {
                Console.WriteLine($"[net] -netloss {netLoss} is not a percentage");
                return true;
            }
            if (NetLag.Active)
            {
                Console.WriteLine($"[net] simulating a bad line: {NetLag.Describe()}");
            }

            if (HasFlag(args, "server") || HasFlag(args, "dedicated") || HasFlag(args, "authoritative-server"))
            {
                string? data = ValueAfter(args, "data");
                if (data == null)
                {
                    Console.Error.WriteLine("-server requires -data DIRECTORY (extracted game data or a baked server package). -dataversion defaults to AMHE1.");
                    Environment.ExitCode = 2;
                    return true;
                }
                try
                {
                    string? requestedRoom = ValueAfter(args, "server") ?? ValueAfter(args, "authoritative-server");
                    var entry = new RotationEntry
                    {
                        RoomKey = requestedRoom != null && !requestedRoom.StartsWith('-')
                            ? requestedRoom : "MP1 SANCTORUS",
                        Mode = Enum.TryParse(ValueAfter(args, "mode"), true, out GameMode mode) ? mode : GameMode.Battle,
                        TimeLimit = 600,
                        PointGoal = 0
                    };
                    string? cycle = ValueAfter(args, "rotation");
                    MapRotation? simulationRotation = cycle == null ? null : MapRotation.Load(cycle);
                    MasterReporter? reporter = null;
                    string? listing = ValueAfter(args, "master");
                    if (listing != null && !HasFlag(args, "nomaster"))
                    {
                        if (!Uri.TryCreate("udp://" + listing, UriKind.Absolute, out Uri? endpoint))
                        {
                            throw new ProgramException("Invalid directory address.");
                        }
                        int listingPort = endpoint.Port > 0 ? endpoint.Port : NetMasterConfig.DefaultPort;
                        if (Int32.TryParse(ValueAfter(args, "masterport"), out int configuredListingPort))
                        {
                            listingPort = configuredListingPort;
                        }
                        reporter = new MasterReporter(endpoint.Host, listingPort);
                    }
                    using var simulationUpdates = Update.ServerUpdateRuntime.Create(args);
                    var simulationServer = new AuthoritativeServer(ParsePort(args), data,
                        ValueAfter(args, "dataversion") ?? "AMHE1", simulationRotation?.Current ?? entry)
                    {
                        Rotation = simulationRotation,
                        SpawnPolicy = ServerSpawnOptions.ParsePolicy(ValueAfter(args, "spawnpolicy"), HasFlag(args, "spawnpolicy")),
                        CancelSpawnProtectionOnOffensiveAction = ServerSpawnOptions.ParseCancellation(
                            ValueAfter(args, "cancelspawnprotection"), HasFlag(args, "cancelspawnprotection")),
                        LagCompEnabled = !HasFlag(args, "nolagcomp"),
                        ProjectileCatchUpEnabled = !HasFlag(args, "noprojectilecatchup"),
                        MaxPlayers = Int32.TryParse(ValueAfter(args, "players"), out int capacity) ? capacity : 8,
                        FriendlyFire = HasFlag(args, "friendlyfire")
                            && (!Boolean.TryParse(ValueAfter(args, "friendlyfire"), out bool friendly) || friendly),
                        ServerName = ValueAfter(args, "servername") ?? ValueAfter(args, "name") ?? "Prime Hunters",
                        Reporter = reporter,
                        Updates = simulationUpdates
                    };
                    using var simulationSignals = new ShutdownSignals();
                    simulationSignals.OnShutdown(simulationServer.Stop);
                    if (HasFlag(args, "parent-stdin"))
                    {
                        new System.Threading.Thread(() =>
                        {
                            string? line;
                            do { line = Console.ReadLine(); } while (line != null && line != "stop");
                            simulationServer.Stop();
                        }) { IsBackground = true, Name = "Server parent lifetime" }.Start();
                    }
                    simulationServer.Run();
                    simulationUpdates?.RestartAfterShutdown();
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("[server] " + ex);
                    Environment.ExitCode = 1;
                }
                return true;
            }

            if (HasFlag(args, "masterserver"))
            {
                int masterPort = NetMasterConfig.DefaultPort;
                string? masterPortValue = ValueAfter(args, "port")
                    ?? ValueAfter(args, "masterport");
                if (masterPortValue != null && Int32.TryParse(masterPortValue, out int parsedMasterPort))
                {
                    masterPort = parsedMasterPort;
                }
                using var masterUpdates = Update.ServerUpdateRuntime.Create(args);
                var master = new MasterServer(masterPort) { Updates = masterUpdates };
                string? hostData = ValueAfter(args, "data");
                if (hostData != null)
                {
                    master.SetHostContent(hostData, ValueAfter(args, "dataversion") ?? "AMHE1");
                }
                using var masterSignals = new ShutdownSignals();
                // The ports it may start games on, for players whose routers
                // will not forward one. A range by default, because the whole
                // point of the feature is that it works without anybody being
                // asked to configure it; -hostports none turns it off.
                string hostPorts = ValueAfter(args, "hostports") ?? "27900-27919";
                if (!hostPorts.Equals("none", StringComparison.OrdinalIgnoreCase))
                {
                    string[] parts = hostPorts.Split('-', 2);
                    if (parts.Length == 2 && Int32.TryParse(parts[0], out int first)
                        && Int32.TryParse(parts[1], out int last) && first > 0 && last >= first)
                    {
                        master.SetHostPorts(first, last);
                    }
                    else
                    {
                        Console.WriteLine($"[master] ignoring -hostports {hostPorts} "
                            + "(expected e.g. 27900-27919, or none)");
                    }
                }
                // The address to hand out for servers running on this same
                // machine, which is the usual arrangement: the directory and
                // one game server on one small box. Their heartbeats arrive
                // over the loopback, and a list of loopback addresses is a
                // list of servers nobody can reach.
                string? publicHost = ValueAfter(args, "public")
                    ?? ValueAfter(args, "publicaddress");
                if (publicHost != null)
                {
                    master.SetPublicAddress(publicHost);
                }
                using var masterCancel = new System.Threading.CancellationTokenSource();
                masterSignals.OnShutdown(() =>
                {
                    masterCancel.Cancel();
                    master.Stop();
                });
                master.Run(masterCancel.Token);
                masterUpdates?.RestartAfterShutdown();
                return true;
            }
        return false;
    }

    private static bool HasFlag(string[] args, string name)
        => args.Any(a => a.TrimStart('-').Equals(name, StringComparison.OrdinalIgnoreCase));
    private static string? ValueAfter(string[] args, string name)
    {
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i].TrimStart('-').Equals(name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
        return null;
    }
    private static int ParsePort(string[] args)
        => Int32.TryParse(ValueAfter(args, "port"), out int port) ? port : NetConfig.DefaultPort;
}
