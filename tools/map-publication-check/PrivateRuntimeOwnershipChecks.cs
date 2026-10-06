using MphRead;
using MphRead.AvaloniaShared;
using MphRead.Mods.MapGen;

internal static class PrivateRuntimeOwnershipChecks
{
    internal static void Run(string root, Action<bool, string> check)
    {
        string priorGame = Paths.FileSystem;
        string fixtures = Path.Combine(root, "private-ownership");
        Directory.CreateDirectory(fixtures);
        try
        {
            string game = Path.Combine(fixtures, "game");
            Directory.CreateDirectory(game);
            Paths.FileSystem = game;
            Rejected(() => new StudioPrivateMapRuntime("relative"), typeof(ArgumentException),
                "private runtime rejects relative owner roots");
            string studio = Path.Combine(fixtures, "studio");
            var owner = new StudioPrivateMapRuntime(studio);
            Publish(owner);
            string[] actual = MapBuildScheduler.Publications.Single();
            string runtime = Path.Combine(studio, "runtime");
            check(actual.SequenceEqual(new[] { Path.Combine(runtime, "_archives", "private_room"),
                Path.Combine(runtime, "levels", "entities"), Path.Combine(runtime, "levels", "nodes") }),
                "real private runtime reaches the canonical build boundary only with its explicit owned destinations");
            foreach (string overlappingGame in new[] { runtime, studio,
                Path.Combine(runtime, "nested-game"), Path.Combine(runtime, "_archives", "private_room"),
                Path.Combine(runtime, "levels", "entities") })
            {
                Paths.FileSystem = overlappingGame;
                Rejected(() => new StudioPrivateMapRuntime(studio), typeof(IOException),
                    "private runtime rejects equal, ancestor or descendant extracted game roots: " + Path.GetRelativePath(studio, overlappingGame));
            }
            Paths.FileSystem = game;
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            Rejected(() => owner.PublishPrivate(new(), new(), cancelled.Token), typeof(OperationCanceledException),
                "private publication cancellation is observed before the build boundary");

            string outside = Path.Combine(fixtures, "outside"); Directory.CreateDirectory(outside);
            string probe = Path.Combine(fixtures, "link-probe");
            if (CanLink(probe, outside))
            {
                Directory.Delete(probe);
                string swapped = Path.Combine(fixtures, "swapped");
                var stale = new StudioPrivateMapRuntime(swapped);
                Directory.CreateDirectory(swapped);
                Directory.CreateSymbolicLink(Path.Combine(swapped, "runtime"), outside);
                Rejected(() => Publish(stale), typeof(IOException),
                    "a private runtime root swapped to an outside alias cannot reach publication");

                foreach (string relative in new[] { "_archives", Path.Combine("levels", "entities"), Path.Combine("levels", "nodes") })
                {
                    string source = Path.Combine(fixtures, "destination-" + Guid.NewGuid().ToString("N"));
                    var destinationOwner = new StudioPrivateMapRuntime(source);
                    string leaf = Path.Combine(source, "runtime", relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(leaf)!);
                    Directory.CreateSymbolicLink(leaf, outside);
                    Rejected(() => Publish(destinationOwner), typeof(IOException),
                        "a private destination alias outside the captured physical owner is rejected: " + relative);
                }

                Directory.CreateDirectory(runtime);
                string alias = Path.Combine(fixtures, "game-alias"); Directory.CreateSymbolicLink(alias, runtime);
                Paths.FileSystem = alias;
                Rejected(() => new StudioPrivateMapRuntime(studio), typeof(IOException),
                    "an extracted game root reached through a physical alias is excluded");
                Paths.FileSystem = "";
                string rootAlias = Path.Combine(fixtures, "root-alias"); Directory.CreateDirectory(rootAlias);
                Directory.CreateSymbolicLink(Path.Combine(rootAlias, "runtime"), Path.GetPathRoot(fixtures)!);
                Rejected(() => new StudioPrivateMapRuntime(rootAlias), typeof(IOException),
                    "an initial private runtime alias to the filesystem root is rejected even without game data configured");

                string upper = Path.Combine(fixtures, "OwnedCase"), lower = Path.Combine(fixtures, "ownedcase");
                Directory.CreateDirectory(Path.Combine(upper, "runtime"));
                Directory.CreateDirectory(Path.Combine(lower, "runtime"));
                File.WriteAllText(Path.Combine(upper, "case.marker"), "owner");
                if (!File.Exists(Path.Combine(lower, "case.marker")))
                {
                    var caseOwner = new StudioPrivateMapRuntime(upper);
                    Directory.CreateSymbolicLink(Path.Combine(upper, "runtime", "_archives"), Path.Combine(lower, "runtime"));
                    Rejected(() => Publish(caseOwner), typeof(IOException),
                        "a distinct case-sensitive sibling alias cannot satisfy positive private containment");
                }
                else Console.WriteLine("SKIP: private runtime case-sensitive sibling proof requires a case-sensitive filesystem.");
            }
            Paths.FileSystem = Path.GetPathRoot(fixtures)!;
            Rejected(() => new StudioPrivateMapRuntime(Path.Combine(fixtures, "game-root-is-filesystem-root")), typeof(IOException),
                "filesystem-root game data excludes all private descendant publication roots");
        }
        finally { Paths.FileSystem = priorGame; MapBuildScheduler.Publications.Clear(); }

        void Publish(StudioPrivateMapRuntime runtime)
        { MapBuildScheduler.Publications.Clear(); runtime.PublishPrivate(new(), new(), default); }
        void Rejected(Action action, Type expected, string message)
        {
            MapBuildScheduler.Publications.Clear();
            bool rejected = false;
            try { action(); } catch (Exception ex) when (ex.GetType() == expected) { rejected = true; }
            check(rejected && MapBuildScheduler.Publications.Count == 0, message);
        }
    }
    private static bool CanLink(string link, string target)
    {
        try { Directory.CreateSymbolicLink(link, target); return true; }
        catch (UnauthorizedAccessException) when (OperatingSystem.IsWindows()) { }
        catch (IOException ex) when (OperatingSystem.IsWindows() && (ex.HResult & 0xffff) == 1314) { }
        Console.WriteLine("SKIP: private runtime alias proof requires Windows symbolic-link privilege."); return false;
    }
}
