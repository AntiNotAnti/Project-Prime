using System.Reflection;
using MphRead.Mods;
using MphRead.Mods.MapGen;

int checks = 0;
void Check(bool pass, string name)
{
    if (!pass) throw new InvalidOperationException(name);
    checks++;
    Console.WriteLine("MAINTENANCE PASS " + name);
}
string Root()
    => (string)(typeof(MapBuildScheduler).GetProperty("CacheDirectory",
        BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(MapBuildScheduler.Shared)
        ?? throw new InvalidOperationException("The actual scheduler did not expose its captured cache root."));

if (args is ["--default"])
{
    Check(string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PROJECT_PRIME_USER_DATA")),
        "default-path process has no user-data override");
    string expected = Path.GetFullPath(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ProjectPrime", "map-cache"));
    Check(Root() == expected, "ordinary installs retain the existing cache path");
    Console.WriteLine($"PASS {checks}; default path observation only, no files read or removed.");
    return 0;
}
if (args is not ["--isolated", var requested])
    throw new ArgumentException("Pass --default or --isolated EMPTY_PRIVATE_ROOT; set the override before launching the isolated process.");

string privateRoot = Path.GetFullPath(requested);
Check(Path.IsPathFullyQualified(requested)
    && Directory.Exists(privateRoot) && !Directory.EnumerateFileSystemEntries(privateRoot).Any(),
    "fixture root is an existing, empty, absolute private directory");
Check(Environment.GetEnvironmentVariable("PROJECT_PRIME_USER_DATA") == privateRoot,
    "process user-data override was supplied before owner initialization");
string expectedRoot = Path.Combine(privateRoot, "map-cache");
Check(Root() == expectedRoot, "actual shared scheduler captures the isolated cache path");
object scheduler = MapBuildScheduler.Shared;
string cacheEntry = Path.Combine(expectedRoot, "test-build", "nested");
Directory.CreateDirectory(cacheEntry);
File.WriteAllText(Path.Combine(cacheEntry, "reproducible.bin"), "private cache bytes");
string retained = Path.Combine(privateRoot, "retained-save.dat");
File.WriteAllText(retained, "retained private save sentinel");
string alternate = Path.Combine(privateRoot, "later-override");
Directory.CreateDirectory(Path.Combine(alternate, "map-cache"));
string alternateSentinel = Path.Combine(alternate, "map-cache", "must-remain.bin");
File.WriteAllText(alternateSentinel, "another owner sentinel");
Environment.SetEnvironmentVariable("PROJECT_PRIME_USER_DATA", alternate);
Check(ReferenceEquals(scheduler, MapBuildScheduler.Shared) && Root() == expectedRoot,
    "environment changes cannot retarget the constructed scheduler");
MaintenanceReport report = Maintenance.ClearMapBuildCache();
Check(!Directory.Exists(expectedRoot), "actual maintenance clears the constructed owner's cache");
Check(report.FilesDeleted == 1 && report.DirectoriesDeleted == 1 && report.BytesFreed > 0,
    "actual maintenance reports the removed private cache");
Check(File.ReadAllText(alternateSentinel) == "another owner sentinel",
    "cleanup cannot follow a later override into another cache");
Check(File.ReadAllText(retained) == "retained private save sentinel",
    "non-cache user data is preserved");
report = Maintenance.ClearMapBuildCache();
Check(report.FilesDeleted == 0 && report.DirectoriesDeleted == 0 && report.BytesFreed == 0,
    "repeated cleanup is harmless");
Check(File.Exists(alternateSentinel) && File.Exists(retained),
    "repeated cleanup preserves both unrelated files");
Console.WriteLine($"PASS {checks}; actual scheduler/maintenance, private cache only.");
return 0;
