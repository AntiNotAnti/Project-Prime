using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;

namespace MphRead
{
    internal static class Program
    {
        public static Version Version => Mods.Branding.EngineVersion;
        private static readonly Version _minExtractVersion = new Version(0, 19, 0, 0);

        private static void Main(string[] args)
        {
            ConsoleSetup.Run();
            // A console only if this run is going to use one: the Windows
            // build is a GUI binary, so double-clicking it opens the launcher
            // with no terminal behind it.
            if (OperatingSystem.IsWindows())
            {
                Mods.ConsoleWindow.Prepare(args);
            }
            // Select the requested renderer before headless commands dispatch.
            // Thumbnail/map/deterministic utilities return from TryHandleHeadless
            // and therefore never reach the normal setup path below.
            if (!RenderBackendSelection.ApplyArguments(args, out string? rendererError))
            {
                Console.Error.WriteLine(rendererError);
                Exit();
            }
            // Route launcher commands and report moved commands before game-file setup.
            if (Mods.ModEntry.TryHandleHeadless(args))
            {
                return;
            }
            if (CheckSetup(args))
            {
                return;
            }
            IReadOnlyList<Argument> arguments = ParseArguments(args);
            if (Mods.ModEntry.TryHandle(args))
            {
                return;
            }
            if (arguments.Count == 0 || arguments.Any(a => a.Name == "menu"))
            {
                Mods.Launcher.TextLauncher.Run();
            }
            else
            {
                var rooms = new List<string>();
                var models = new List<(string, int)>();
                int nodeLayerMask = 0;
                int entityLayerId = -1;
                if (TryGetInt(arguments, "room", "r", out int roomId))
                {
                    RoomMetadata? meta = Metadata.GetRoomById(roomId);
                    if (meta == null)
                    {
                        Exit();
                    }
                    rooms.Add(meta.Name);
                }
                else if (TryGetString(arguments, "room", "r", out string? roomName))
                {
                    rooms.Add(roomName);
                }
                if (arguments.Any(a => a.Name is "mode" or "g" or "players" or "p"))
                {
                    Console.Error.WriteLine("Local gameplay options -mode and -players are no longer supported. Use -launcher to host or join a server.");
                    Exit();
                }
                if (TryGetInt(arguments, "node", "n", out int nodeValue))
                {
                    nodeLayerMask = nodeValue;
                }
                if (TryGetInt(arguments, "entity", "l", out int entityValue))
                {
                    entityLayerId = entityValue;
                }
                foreach ((string, int) pair in GetPairs(arguments, "model", "m"))
                {
                    models.Add(pair);
                }
                if (rooms.Count > 1 || (rooms.Count == 0 && models.Count == 0))
                {
                    Exit();
                }
                foreach (string room in rooms)
                {
                    (RoomMetadata? metadata, _) = Metadata.GetRoomByName(room);
                    if (metadata == null || !metadata.Multiplayer)
                    {
                        Console.Error.WriteLine("Room inspection supports multiplayer rooms only. Use -export for campaign room assets.");
                        Exit();
                    }
                }
                bool firstHunt = arguments.Any(a => a.Name == "fh");
                var scene = new Scene(features: ClientMatchFeatures.Capture());
                using var sdlHost = new SdlGameHost();
                sdlHost.RunScene(scene, presentation =>
                {
                    foreach (string room in rooms)
                    {
                        // No player is created: this is the existing free-camera asset viewer.
                        presentation.AddRoom(room, GameMode.Battle, 0, nodeLayerMask, entityLayerId);
                    }
                    foreach ((string model, int recolor) in models)
                    {
                        presentation.AddModel(model, recolor, firstHunt);
                    }
                });
            }
        }

        private static bool CheckSetup(string[] args)
        {
            // The graphical launcher starts a child copy of this executable
            // with the selected ROM as its only argument. The extraction code
            // is already part of the client, so handle that request here
            // instead of routing it to the separate tools executable.
            if (args.Length == 1 && !args[0].StartsWith('-') && File.Exists(args[0]))
            {
                Extract.Setup(args[0], replaceConfiguredPaths: true);
                return true;
            }
            if (File.Exists("paths.txt") && !CheckVersion())
            {
                Console.WriteLine($"Your paths.txt file is not compatible with this version of {Mods.Branding.Name} and needs to be recreated.");
                Console.WriteLine("It is recommended that you delete the file as well as any extracted game files, " +
                    "then perform setup again.");
                Console.WriteLine();
                Console.WriteLine("Press any key to exit...");
                Console.ReadKey();
                return true;
            }
            if (!File.Exists("paths.txt"))
            {
                Console.WriteLine("Could not find the paths.txt file.");
                Console.WriteLine($"Perform first-time setup by passing a ROM path to FruityPrimeTools.");
                Console.WriteLine();
                Console.WriteLine("Press any key to exit...");
                Console.ReadKey();
                return true;
            }
            Paths.UpdatePaths();
            Paths.ChooseMphPath();
            Paths.ChooseFhPath();
            return false;
        }

        private static bool CheckVersion()
        {
            string text = File.ReadAllText("paths.txt").Split('\n')[0].Trim();
            if (Version.TryParse(text, out Version? extractVersion))
            {
                return extractVersion >= _minExtractVersion;
            }
            return false;
        }

        private readonly struct Argument
        {
            public readonly string Name;
            public readonly string? ValueOne;
            public readonly string? ValueTwo;

            public Argument(string name, string? valueOne, string? valueTwo = null)
            {
                Name = name;
                ValueOne = valueOne;
                ValueTwo = valueTwo;
            }
        }

        private static IEnumerable<(string, int)> GetPairs(IEnumerable<Argument> arguments, string fullName, string shortName)
        {
            foreach (Argument argument in arguments.Where(a => a.Name == fullName || a.Name == shortName))
            {
                if (argument.ValueOne != null)
                {
                    Int32.TryParse(argument.ValueTwo, out int valueTwo);
                    yield return (argument.ValueOne, valueTwo);
                }
            }
        }

        private static bool TryGetArgument(IEnumerable<Argument> arguments, string fullName, string shortName,
            [NotNullWhen(true)] out Argument? argument)
        {
            IEnumerable<Argument> matches = arguments.Where(a => a.Name == fullName || a.Name == shortName);
            if (matches.Any())
            {
                argument = matches.First();
                return true;
            }
            argument = null;
            return false;
        }

        private static bool TryGetString(IEnumerable<Argument> arguments, string fullName, string shortName,
            [NotNullWhen(true)] out string? value)
        {
            if (TryGetArgument(arguments, fullName, shortName, out Argument? argument) && argument.Value.ValueOne != null)
            {
                value = argument.Value.ValueOne;
                return true;
            }
            value = null;
            return false;
        }

        private static bool TryGetInt(IEnumerable<Argument> arguments, string fullName, string shortName,
            out int value)
        {
            if (TryGetString(arguments, fullName, shortName, out string? stringValue))
            {
                if (Int32.TryParse(stringValue, out int intValue))
                {
                    value = intValue;
                    return true;
                }
            }
            value = 0;
            return false;
        }

        private static IReadOnlyList<Argument> ParseArguments(string[] args)
        {
            var arguments = new List<Argument>();
            if (args != null)
            {
                for (int i = 0; i < args.Length; i++)
                {
                    string arg = args[i];
                    if (arg.StartsWith('-') && arg.Length > 1)
                    {
                        arg = arg[1..];
                        if (i == args.Length - 1)
                        {
                            arguments.Add(new Argument(arg, null));
                        }
                        else
                        {
                            string valueOne = args[i + 1];
                            if (valueOne.StartsWith('-'))
                            {
                                arguments.Add(new Argument(arg, null));
                            }
                            else
                            {
                                string? valueTwo = null;
                                if (i < args.Length - 2 && !args[i + 2].StartsWith('-'))
                                {
                                    valueTwo = args[i + 2];
                                    i++;
                                }
                                arguments.Add(new Argument(arg, valueOne, valueTwo));
                                i++;
                            }
                        }
                    }
                }
            }
            return arguments;
        }

        [DoesNotReturn]
        private static void Exit()
        {
            Nop();
            Console.WriteLine($"{Mods.Branding.Executable} usage:");
            Console.WriteLine("    -room <multiplayer_room_name -or- room_id> (asset inspection)");
            Console.WriteLine("    -model <model_name> [recolor_index]");
            Console.WriteLine("At most one room may be specified. Any number of models may be specified.");
            Console.WriteLine("To load First Hunt models, include -fh in the argument list.");
            Console.WriteLine("Available room inspection options: -node, -entity");
            Console.WriteLine("Asset extraction/export commands are available in FruityPrimeTools.");
            Environment.Exit(1);
        }

        private static void Nop() { }
    }

}
