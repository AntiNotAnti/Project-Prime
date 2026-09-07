using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using MphRead.Export;
using MphRead.Formats.Sound;
using MphRead.Mods.Network;
using MapGen = MphRead.Mods.MapGen;
using Network = MphRead.Mods.Network;
namespace MphRead
{
    internal static class ToolsProgram
    {
        public static int Main(string[] args)
        {
            ConsoleSetup.Run();
            try
            {
                if (args.Length > 0 && args[0] == "balance") return BalanceCommand.Run(args);
                if (args.Length > 0 && args[0] == "telemetry") return TelemetryCommand.Run(args);
                MapGen.MapImageDecoding.Decoder = MphRead.Imaging.StbImageDecoder.Decode;
                if (args.Length == 0 || HasFlag(args, "help"))
                { Console.WriteLine("FruityPrimeTools: -extract ARCHIVE, -export TARGET, -setup, -servercontent OUTPUT -data DIRECTORY, -mapbundle, -mapgen, -q3maps, -q3convert, -q3shaders, -mapmaterials, -mechanics"); return 0; }
                string? mapDir = ValueAfter(args, "mapdir");
                if (mapDir != null) MapGen.CustomRooms.MapDirectory = Path.GetFullPath(Path.Combine(ConsoleSetup.LaunchDirectory, mapDir));
                if (HandleEarly(args) || CheckSetup(args) || HandleAssets(args)) return Environment.ExitCode;
                IReadOnlyList<Argument> arguments = ParseArguments(args);
            if (arguments.Any(a => a.Name == "setup"))
            {
                foreach (string path in Directory.EnumerateFiles(Paths.Combine(Paths.FileSystem, "archives")))
                {
                    Extract.ExtractArchive(Path.GetFileNameWithoutExtension(path));
                }
            }
            else if (TryGetString(arguments, "export", "e", out string? exportValue))
            {
                if (exportValue.ToLower() == "layer2d")
                {
                    Images.ExportHudLayers();
                }
                else if (exportValue.ToLower() == "object2d")
                {
                    Images.ExportHudObjects();
                }
                else if (exportValue.ToLower() == "sfx")
                {
                    MphRead.Export.SoundExport.ExportSamples();
                }
                else if (exportValue.ToLower() == "wfs")
                {
                    MphRead.Export.SoundExport.ExportWfsSamples();
                }
                else if (exportValue.ToLower() == "strm")
                {
                    MphRead.Export.SoundExport.ExportStreams();
                }
                else if (exportValue.ToLower() == "fhsfx")
                {
                    MphRead.Export.SoundExport.ExportAllFh();
                }
                else if (exportValue.ToLower() == "movie")
                {
                    TryGetArgument(arguments, "export", "e", out Argument? exportArgument);
                    if (exportArgument!.Value.ValueTwo != null)
                    {
                        Formats.VxDecoder.Instance1.Export(exportArgument!.Value.ValueTwo).GetAwaiter().GetResult();
                    }
                    else
                    {
                        Formats.VxDecoder.Instance1.ExportAll().GetAwaiter().GetResult();
                    }
                }
                else
                {
                    bool firstHunt = arguments.Any(a => a.Name == "fh");
                    AssetTools.ReadAndExport(exportValue, firstHunt);
                }
            }
            else if (TryGetString(arguments, "extract", "x", out string? extractValue))
            {
                Extract.ExtractArchive(extractValue);
            }
                else { Console.WriteLine("FruityPrimeTools: -extract ARCHIVE, -export TARGET, -setup, -servercontent OUTPUT -data DIRECTORY, -mapbundle, -mapgen, -q3maps, -q3convert, -q3shaders, -mapmaterials, -mechanics"); return args.Length == 0 ? 0 : 2; }
                return Environment.ExitCode;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }
        }
        private static bool HandleEarly(string[] args)
        {
            if (HasFlag(args, "servercontent"))
            {
                string? source = ValueAfter(args, "data");
                string? output = ValueAfter(args, "servercontent");
                if (source == null || output == null)
                {
                    Console.Error.WriteLine("-servercontent OUTPUT -data EXTRACTED_DIRECTORY [-room ROOM ...] [-dataversion AMHE1]");
                    Environment.ExitCode = 2;
                    return true;
                }
                try
                {
                    List<string> rooms = ValuesAfter(args, "room");
                    if (HasFlag(args, "allrooms")) { rooms.AddRange(ServerContentPack.RetailRooms); }
                    if (rooms.Count == 0) { rooms.Add("MP1 SANCTORUS"); }
                    ServerContentPack.Bake(source, output, ValueAfter(args, "dataversion") ?? "AMHE1", rooms);
                    Console.WriteLine($"Server content written to {System.IO.Path.GetFullPath(output)}");
                }
                catch (Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode = 1; }
                return true;
            }
            if (HasFlag(args, "mapbundle"))
            {
                string? which = ValueAfter(args, "mapbundle");
                string? outPath = ValueAfter(args, "out");
                int cooked = 0;
                int failed = 0;
                foreach (MapGen.MapDefinition def in MapGen.CustomRooms.Definitions)
                {
                    if (which != null && !which.Equals(def.Name, StringComparison.OrdinalIgnoreCase)
                        && !which.Equals("all", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    if (def.SourcePath == null || def.BundlePath != null || def.Import == null)
                    {
                        // Already a bundle, or a map that builds from its own
                        // description and has no level to carry.
                        continue;
                    }
                    try
                    {
                        MapGen.MapBundleTools.Cook(def, def.SourcePath, outPath);
                        cooked++;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"{def.Name}: {ex.Message}");
                        failed++;
                    }
                }
                if (cooked == 0 && failed == 0)
                {
                    Console.WriteLine("No map to bundle. A bundle is cooked from a recipe and the "
                        + $"level it converts; put both in {MapGen.CustomRooms.MapDirectory}.");
                }
                Environment.ExitCode = failed == 0 ? 0 : 1;
                return true;
            }
            return false;
        }
        private static bool HandleAssets(string[] args)
        {
            // Generate the binaries for the custom maps in `maps/`. The
            // textures come out of the player's own extracted files, so this
            // has to run here rather than at build time, and what ships in the
            // repository is the JSON, never the .bin.


            // What levels are in a .pk3, so a conversion knows what to ask
            // for. Reads the archive's index only -- nothing is extracted.
            string? q3Maps = ValueAfter(args, "q3maps");
            if (q3Maps != null)
            {
                try
                {
                    foreach (string name in MapGen.Q3Bsp.ListMaps(q3Maps))
                    {
                        Console.WriteLine(name);
                    }
                    Environment.ExitCode = 0;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Could not read {q3Maps}: {ex.Message}");
                    Environment.ExitCode = 1;
                }
                return true;
            }

            // A .pk3 to a room, in one command: the textures baked from the
            // level's own art, the scale and the extents picked from its
            // geometry, the spawns from its entities, and a map file written
            // out. Weapons and powerups are left for a person to place.
            string? q3Convert = ValueAfter(args, "q3convert");
            if (q3Convert != null)
            {
                float? scale = null;
                if (Single.TryParse(ValueAfter(args, "scale"), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float parsedScale) && parsedScale > 0)
                {
                    scale = parsedScale;
                }
                int textureSize = Int32.TryParse(ValueAfter(args, "texsize"), out int parsedSize)
                    && parsedSize >= 8 && parsedSize <= 256
                        ? parsedSize
                        : MapGen.MapTextureBake.DefaultSize;
                try
                {
                    Environment.ExitCode = MapGen.Q3Convert.Run(q3Convert, ValueAfter(args, "map"),
                        ValueAfter(args, "name"), ValueAfter(args, "out"), HasFlag(args, "noclip"),
                        scale, textureSize);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Could not convert {q3Convert}: {ex.Message}");
                    Environment.ExitCode = 1;
                }
                return true;
            }

            // What a level draws with, commonest first, so a conversion knows
            // which shaders are worth mapping to a borrowed texture.
            string? q3Shaders = ValueAfter(args, "q3shaders");
            if (q3Shaders != null)
            {
                Environment.ExitCode = MapGen.MapReport.ListShaders(q3Shaders, ValueAfter(args, "map"));
                return true;
            }

            // List a room's materials, with the texture each one uses, so a
            // map can say which of them it wants to borrow.
            string? mapMaterials = ValueAfter(args, "mapmaterials");
            if (mapMaterials != null)
            {
                Environment.ExitCode = MapGen.MapReport.ListMaterials(mapMaterials);
                return true;
            }

            if (HasFlag(args, "mechanics"))
            {
                Network.MechanicsDump.Run();
                return true;
            }
            if (HasFlag(args, "mapgen"))
            {
                string? only = ValueAfter(args, "mapgen");
                bool force = HasFlag(args, "force");
                int count = 0;
                int failed = 0;
                foreach (MapGen.MapDefinition def in MapGen.CustomRooms.Definitions)
                {
                    if (only != null && !only.Equals(def.Name, StringComparison.OrdinalIgnoreCase)
                        && !only.Equals("all", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    // one map at a time: a map that cannot be built -- most
                    // often one whose source level is not where it says --
                    // must not stop the others from being generated
                    try
                    {
                        MapGen.MapPacker.Generate(def, MapGen.CustomRooms.ArchiveDirectory(def),
                            MapGen.CustomRooms.EntityDirectory(), MapGen.CustomRooms.NodeDirectory(),
                            verbose: true);
                        count++;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"{def.Name}: {ex.Message}");
                        failed++;
                    }
                }
                if (count == 0 && failed == 0)
                {
                    Console.WriteLine($"No maps to generate. Put a map JSON in {MapGen.CustomRooms.MapDirectory}.");
                }
                Environment.ExitCode = failed == 0 ? 0 : 1;
                return true;
            }
            return false;
        }
        private static bool CheckSetup(string[] args)
        {
            if (args.Length == 1 && !args[0].StartsWith('-') && File.Exists(args[0]))
            {
                Extract.Setup(args[0]);
                return true;
            }
            string? data = ValueAfter(args, "data");
            if (data != null) { ContentEnvironment.Open(data, ValueAfter(args, "dataversion") ?? "AMHE1"); return false; }
            if (!File.Exists("paths.txt")) throw new ProgramException("Supply -data DIRECTORY or run FruityPrimeTools with a ROM path first.");
            Paths.UpdatePaths();
            Paths.ChooseMphPath();
            Paths.ChooseFhPath();
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

        private static bool HasFlag(string[] args, string name) => args.Any(a => a.TrimStart('-').Equals(name, StringComparison.OrdinalIgnoreCase));
        private static string? ValueAfter(string[] args, string name)
        {
            for (int i=0;i<args.Length-1;i++) if (args[i].TrimStart('-').Equals(name,StringComparison.OrdinalIgnoreCase)) return args[i+1];
            return null;
        }
        private static List<string> ValuesAfter(string[] args, string name)
        {
            var values = new List<string>();
            for (int i=0;i<args.Length-1;i++) if (args[i].TrimStart('-').Equals(name,StringComparison.OrdinalIgnoreCase)) values.Add(args[i+1]);
            return values;
        }
    }
}
