using System;
using System.Net;
using System.Net.Sockets;
using MphRead.Mods.Network;

namespace MphRead.NetTest
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            if (args.Length > 0 && args[0].TrimStart('-') is "headlesscheck" or "server-sim" or "combatcheck" or "spectatorcheck" or "combatduel")
                return RunContentDiagnostic(args);
            if (args.Length > 0)
            {
                switch (args[0])
                {
                    case "--authority-check": return AuthorityCheck.Run(args);
                    case "--audit-multiplayer": return MultiplayerContentAudit.Run(args);
                    case "--audit-multiplayer-self-test": return MultiplayerContentAudit.SelfTest();
                    case "--world-check": return WorldCheck.Run(args);
                    case "--history-boundary": return HistoryBoundaryCheck.Run(args);
                    case "--bomb-pool": return BombPoolCheck.Run(args);
                    case "--catch-up": return CatchUpCheck.Run(args);
                    case "--homing": return HomingCheck.Run(args);
                    case "--weapon-policy": return WeaponPolicyCheck.Run(args);
                    case "--shared-lock": return SharedLockCheck.Run(args);
                    case "--lagcomp-script": return LagCompScriptCheck.Run(args);
                    case "--mixed-soak-server": return MixedCombatSoak.RunServer(args);
                    case "--mixed-soak-clients": return MixedCombatClients.Run(args);
                    case "--mixed-backpressure-self-test": return MixedCombatBackpressureCheck.Run();
                    case "--simulation": return SimulationCheck.Run(args);
                    case "--match-lifecycle": return MatchLifecycleCheck.Run(args);
                    case "--match-phases": return MatchPhaseCheck.Run(args);
                    case "--simulation-order": return SimulationOrderingCheck.Run(args);
                    case "--match-baseline": return MatchBaselineCheck.Run(args);
                    case "--baseline": return ConnectionBaseline.Run(args);
                    case "--connection-server": return ConnectionBaseline.RunServer(args);
                    case "--help": PrintUsage(); return 0;
                }
            }
            if (args.Length > 2 || (args.Length > 0 && args[0].StartsWith("--", StringComparison.Ordinal)))
            {
                PrintUsage(); return 2;
            }
            string host = args.Length > 0 ? args[0] : "127.0.0.1";
            int port = NetConfig.DefaultPort;
            if (args.Length > 1 && (!Int32.TryParse(args[1], out port) || port < 1 || port > UInt16.MaxValue))
            {
                PrintUsage(); return 2;
            }
            try
            {
                IPAddress? address = IPAddress.TryParse(host, out IPAddress? literal) ? literal
                    : Array.Find(Dns.GetHostAddresses(host), ip => ip.AddressFamily == AddressFamily.InterNetwork);
                if (address?.AddressFamily != AddressFamily.InterNetwork)
                    throw new ArgumentException("The server must resolve to an IPv4 address.");
                Console.WriteLine($"Authoritative connection conformance against {host}:{port}; no rendered gameplay.");
                return ConnectionBaseline.Run(new[] { "--baseline", "10", $"{port},{port}" }, address);
            }
            catch (Exception ex) when (ex is SocketException or ArgumentException)
            {
                Console.Error.WriteLine(ex.Message); return 2;
            }
        }

        private static int RunContentDiagnostic(string[] args)
        {
            string command = args[0].TrimStart('-');
            string? Value(string name)
            {
                for (int i = 0; i < args.Length - 1; i++)
                    if (args[i].TrimStart('-').Equals(name, StringComparison.OrdinalIgnoreCase)
                        && (!args[i + 1].StartsWith('-') || Int32.TryParse(args[i + 1], out _))) return args[i + 1];
                return null;
            }
            bool Has(string name) => Array.Exists(args, value => value.TrimStart('-').Equals(name, StringComparison.OrdinalIgnoreCase));
            string? data = Value("data");
            if (data == null) { Console.Error.WriteLine("This diagnostic requires --data DIRECTORY."); return 2; }
            string version = Value("dataversion") ?? "AMHE1";
            string room = Value(command) ?? "MP1 SANCTORUS";
            try
            {
                if (Value("mapdir") is string maps) MphRead.Mods.MapGen.CustomRooms.MapDirectory = System.IO.Path.GetFullPath(maps);
                return command switch
                {
                    "combatcheck" => ServerCombatCheck.Run(data, version, room),
                    "spectatorcheck" => ServerSpectatorCheck.Run(data, version, room),
                    "combatduel" => ServerCombatDuelCheck.Run(data, version, room,
                        Int32.TryParse(Value("seconds"), out int duration) ? duration : 30,
                        Enum.TryParse(Value("weapon"), true, out BeamType weapon) ? weapon : BeamType.Imperialist),
                    _ => HeadlessCheck.Run(data, version, room,
                        Int32.TryParse(Value("frames"), out int frames) ? frames : command == "server-sim" ? -1 : 600,
                        Int32.TryParse(Value("players"), out int players) ? players : 8,
                        Enum.TryParse(Value("mode"), true, out GameMode mode) ? mode : GameMode.Battle,
                        realtime: Has("realtime") || command == "server-sim")
                };
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        }

        private static void PrintUsage()
        {
            Console.WriteLine("--headlesscheck [ROOM] --data DIRECTORY [--frames 600 --players 8 --mode Battle --realtime]");
            Console.WriteLine("--server-sim [ROOM] --data DIRECTORY: realtime headless simulation until stopped");
            Console.WriteLine("--combatcheck [ROOM] --data DIRECTORY: direct authoritative weapon probes");
            Console.WriteLine("--spectatorcheck [ROOM] --data DIRECTORY: participation and objective checks");
            Console.WriteLine("--combatduel [ROOM] --data DIRECTORY [--seconds 30 --weapon Imperialist]: real UDP combat fixture");
            Console.WriteLine("Content diagnostics accept --dataversion AMHE1 and --mapdir DIRECTORY; legacy single-dash spellings remain accepted.");
            Console.WriteLine("nettest [HOST [PORT]]: authoritative join, ready, roster, clock, input and snapshot checks");
            Console.WriteLine("--simulation SECONDS PORT,... | --authority-check PORT,... | --baseline SECONDS PORT,...");
            Console.WriteLine("--world-check DATA MODE | --match-lifecycle DATA | --connection-server PORT");
            Console.WriteLine("--simulation-order DATA: real-content simultaneous-event ordering and lifecycle boundaries");
            Console.WriteLine("--match-baseline DATA: current multiplayer scoring and objective behavior");
            Console.WriteLine("--match-phases DATA: authoritative waiting, countdown reset, phase timing and input epochs");
            Console.WriteLine("--audit-multiplayer DATA OUTPUT_JSON [FH_DATA|-] [MAP_DIRECTORY|-]: read-only multiplayer entity inventory");
            Console.WriteLine("--audit-multiplayer-self-test: malformed and edge-case content audit checks");
            Console.WriteLine("--history-boundary DATA [VERSION]: completed simulation history and snapshot invariants");
            Console.WriteLine("--bomb-pool DATA [VERSION]: headless bomb creation, expiry and pool reuse");
            Console.WriteLine("--catch-up DATA [VERSION]: completed-boundary projectile catch-up and collision invariants");
            Console.WriteLine("--weapon-policy DATA [VERSION]: actual multiplayer weapon timing variants");
            Console.WriteLine("--shared-lock DATA [VERSION]: actual force-field lock beam and bomb variants");
            Console.WriteLine("--lagcomp-script DATA CONFIG_JSON OUTPUT_JSON: deterministic comparison fixture");
            Console.WriteLine("--mixed-soak-server DATA PORT SECONDS REPORT_JSON MODE SEED: MODE on, trace-only or off");
            Console.WriteLine("--mixed-soak-clients SECONDS PORT,... REPORT_JSON SERVER_COMPLETION_JSON: eight UDP clients");
            Console.WriteLine("--mixed-backpressure-self-test: reliable admission failure follows dedicated-server policy");
            Console.WriteLine("The connection-server is a data-free test fixture; it does not simulate gameplay.");
        }
    }
}
