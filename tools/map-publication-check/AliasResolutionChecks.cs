using MphRead.Mods.MapGen;
using ProjectPrime.DesktopShared;

internal static class AliasResolutionChecks
{
    internal static void Run(string root, Action<bool, string> check)
    {
        string physicalRoot = MapPublicationLease.ResolveRuntimeDirectoryAliases(root);
        string mixed = Path.Combine(physicalRoot, "MixedCase-Assets");
        Directory.CreateDirectory(mixed);
        check(MapPublicationLease.ResolveRuntimeDirectoryAliases(mixed) == mixed,
            "physical containment preserves mixed-case directory names");
        check(MapPublicationLease.ResolveRuntimeDirectoryAliases(mixed + Path.DirectorySeparatorChar) == mixed,
            "physical containment trims a directory separator without folding casing");
        check(MapPublicationLease.ResolveRuntimeDirectoryAliases(Path.Combine(mixed, "absent", "..", "FutureChild"))
            == Path.Combine(mixed, "FutureChild"), "physical containment resolves lexical parents and missing children");
        check(MapPublicationLease.ResolveRuntimeDirectoryAliases(Path.GetPathRoot(mixed)!) == Path.GetPathRoot(mixed),
            "filesystem roots keep their required separator");
        bool blankRejected = false;
        try { MapPublicationLease.ResolveRuntimeDirectoryAliases(" "); } catch (ArgumentException) { blankRejected = true; }
        check(blankRejected, "raw resolver rejects an empty directory");

        string paired = Path.Combine(physicalRoot, "PairedRelease");
        string game = Path.Combine(paired, "Project Prime.app", "Contents", "MacOS");
        string studio = Path.Combine(paired, "Project Prime Studio.app", "Contents", "MacOS");
        Directory.CreateDirectory(game); Directory.CreateDirectory(studio);
        check(MapPublicationLease.ResolveRuntimeDirectoryAliases(game) == game
            && MapPublicationLease.ResolveRuntimeDirectoryAliases(studio) == studio,
            "physical containment preserves the separate game and Studio bundle directories");
        string foldedPair = Fold(paired);
        check(DesktopInstallationIdentity.CanonicalInstallation(game) == foldedPair
            && DesktopInstallationIdentity.CanonicalInstallation(studio) == foldedPair,
            "existing installation identity still collapses paired app bundles");
        check(MapPublicationLease.CanonicalizeRuntimeDirectory(mixed) == Fold(mixed),
            "existing publication identity still applies its platform casing rule");

        string ancestor = Path.Combine(physicalRoot, "AncestorAlias");
        try { Directory.CreateSymbolicLink(ancestor, mixed); }
        catch (UnauthorizedAccessException) when (OperatingSystem.IsWindows())
        { Console.WriteLine("SKIP: real directory alias checks require Windows symbolic-link privilege."); return; }
        catch (IOException ex) when (OperatingSystem.IsWindows() && (ex.HResult & 0xffff) == 1314)
        { Console.WriteLine("SKIP: real directory alias checks require Windows symbolic-link privilege."); return; }
        string child = Path.Combine(mixed, "ChildCase"); Directory.CreateDirectory(child);
        check(MapPublicationLease.ResolveRuntimeDirectoryAliases(ancestor) == mixed,
            "raw resolver follows a real directory alias without changing target casing");
        check(MapPublicationLease.ResolveRuntimeDirectoryAliases(Path.Combine(ancestor, "ChildCase")) == child,
            "raw resolver follows an ancestor alias before testing a child");
        check(MapPublicationLease.ResolveRuntimeDirectoryAliases(Path.Combine(ancestor, "NewChild"))
            == Path.Combine(mixed, "NewChild"), "raw resolver follows ancestor aliases for a not-yet-created destination");
        check(DesktopInstallationIdentity.CanonicalInstallation(ancestor)
            == DesktopInstallationIdentity.CanonicalInstallation(mixed),
            "existing installation identity still unifies a real alias and its target");
        check(MapPublicationLease.CanonicalizeRuntimeDirectory(ancestor)
            == MapPublicationLease.CanonicalizeRuntimeDirectory(mixed),
            "existing publication identity still unifies a real alias and its target");

        string outside = Path.Combine(physicalRoot, "OutsideCase"); Directory.CreateDirectory(outside);
        string escape = Path.Combine(mixed, "Escape"); Directory.CreateSymbolicLink(escape, outside);
        string escaped = MapPublicationLease.ResolveRuntimeDirectoryAliases(Path.Combine(escape, "NewFileParent"));
        check(escaped == Path.Combine(outside, "NewFileParent") && !Contained(mixed, escaped),
            "a real descendant alias resolves outside its owner root for containment rejection");

        string upper = Path.Combine(physicalRoot, "AssetsCase"), lower = Path.Combine(physicalRoot, "assetscase");
        Directory.CreateDirectory(upper); Directory.CreateDirectory(lower);
        File.WriteAllText(Path.Combine(upper, "upper.marker"), "upper");
        if (!File.Exists(Path.Combine(lower, "upper.marker")))
        {
            string caseAlias = Path.Combine(upper, "SiblingAlias"); Directory.CreateSymbolicLink(caseAlias, lower);
            string actual = MapPublicationLease.ResolveRuntimeDirectoryAliases(caseAlias);
            check(actual == lower && !Contained(upper, actual),
                "case-sensitive sibling alias cannot pass physical containment by case folding");
            check(OperatingSystem.IsLinux() || MapPublicationLease.CanonicalizeRuntimeDirectory(upper)
                == MapPublicationLease.CanonicalizeRuntimeDirectory(lower),
                "conservative publication case folding remains separate from physical containment");
        }
        else Console.WriteLine("SKIP: this filesystem has no distinct case-variant sibling directories.");

        string cycleA = Path.Combine(physicalRoot, "CycleA"), cycleB = Path.Combine(physicalRoot, "CycleB");
        Directory.CreateSymbolicLink(cycleA, cycleB); Directory.CreateSymbolicLink(cycleB, cycleA);
        bool cycleRejected = false;
        try { MapPublicationLease.ResolveRuntimeDirectoryAliases(cycleA); } catch (IOException) { cycleRejected = true; }
        check(cycleRejected, "directory alias cycles fail closed");
    }

    private static string Fold(string path) => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
        ? path.ToUpperInvariant() : path;
    private static bool Contained(string root, string path)
    {
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return path.Equals(root, comparison) || path.StartsWith(root + Path.DirectorySeparatorChar, comparison);
    }
}
