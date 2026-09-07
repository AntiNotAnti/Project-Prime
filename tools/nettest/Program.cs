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
                    case "--lagcomp-script": return LagCompScriptCheck.Run(args);
                    case "--mixed-soak-server": return MixedCombatSoak.RunServer(args);
                    case "--mixed-soak-clients": return MixedCombatClients.Run(args);
                    case "--mixed-backpressure-self-test": return MixedCombatBackpressureCheck.Run();
                    case "--simulation": return SimulationCheck.Run(args);
                    case "--match-lifecycle": return MatchLifecycleCheck.Run(args);
                    case "--match-phases": return MatchPhaseCheck.Run(args);
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

        private static void PrintUsage()
        {
            Console.WriteLine("nettest [HOST [PORT]]: authoritative join, ready, roster, clock, input and snapshot checks");
            Console.WriteLine("--simulation SECONDS PORT,... | --authority-check PORT,... | --baseline SECONDS PORT,...");
            Console.WriteLine("--world-check DATA MODE | --match-lifecycle DATA | --connection-server PORT");
            Console.WriteLine("--match-baseline DATA: current multiplayer scoring and objective behavior");
            Console.WriteLine("--match-phases DATA: authoritative waiting, countdown reset, phase timing and input epochs");
            Console.WriteLine("--audit-multiplayer DATA OUTPUT_JSON [FH_DATA|-] [MAP_DIRECTORY|-]: read-only multiplayer entity inventory");
            Console.WriteLine("--audit-multiplayer-self-test: malformed and edge-case content audit checks");
            Console.WriteLine("--history-boundary DATA [VERSION]: completed simulation history and snapshot invariants");
            Console.WriteLine("--bomb-pool DATA [VERSION]: headless bomb creation, expiry and pool reuse");
            Console.WriteLine("--catch-up DATA [VERSION]: completed-boundary projectile catch-up and collision invariants");
            Console.WriteLine("--weapon-policy DATA [VERSION]: actual multiplayer weapon timing variants");
            Console.WriteLine("--lagcomp-script DATA CONFIG_JSON OUTPUT_JSON: deterministic comparison fixture");
            Console.WriteLine("--mixed-soak-server DATA PORT SECONDS REPORT_JSON MODE SEED: MODE on, trace-only or off");
            Console.WriteLine("--mixed-soak-clients SECONDS PORT,... REPORT_JSON SERVER_COMPLETION_JSON: eight UDP clients");
            Console.WriteLine("--mixed-backpressure-self-test: reliable admission failure follows dedicated-server policy");
            Console.WriteLine("The connection-server is a data-free test fixture; it does not simulate gameplay.");
        }
    }
}
