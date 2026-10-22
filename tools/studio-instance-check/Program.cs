using MphRead.Mods.StudioIntegration;
using ProjectPrime.Studio.IPC;
using ProjectPrime.Studio.Protocol;

static void Check(bool pass, string message)
{
    if (!pass) throw new InvalidOperationException(message);
    Console.WriteLine("STUDIO INSTANCE PASS " + message);
}
string temporary = Path.Combine(Path.GetTempPath(), "prime-studio-instance-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temporary);
try
{
    string installation = Path.Combine(temporary, "installation");
    Directory.CreateDirectory(installation);
    string data = Path.Combine(temporary, "data");
    Directory.CreateDirectory(data);
    string replay = Path.Combine(temporary, "Unicode replay Ω.ppdemo");
    string clip = Path.Combine(temporary, "clip.ppclip");
    foreach (var invalid in new[] {
        new StudioOpenRequest(Guid.Empty, StudioOpenKind.Home),
        new StudioOpenRequest(Guid.NewGuid(), StudioOpenKind.Home, replay),
        new StudioOpenRequest(Guid.NewGuid(), StudioOpenKind.Replay, "relative.ppdemo"),
        new StudioOpenRequest(Guid.NewGuid(), StudioOpenKind.Replay, clip),
        new StudioOpenRequest(Guid.NewGuid(), StudioOpenKind.Map, Path.Combine(temporary,"map.ppmap")) })
        Check(invalid.Validate() != null, "invalid or unsupported document rejected");
    string bundles = Path.Combine(temporary, "bundles");
    string gameMacOs = Path.Combine(bundles, "Project Prime.app", "Contents", "MacOS");
    string studioMacOs = Path.Combine(bundles, "Project Prime Studio.app", "Contents", "MacOS");
    Directory.CreateDirectory(gameMacOs);
    Directory.CreateDirectory(studioMacOs);
    string sibling = Path.Combine(studioMacOs, "ProjectPrimeStudio");
    File.WriteAllText(sibling, "fixture only; never executed");
    Check(StudioApplicationLauncher.FindExecutable(gameMacOs) == sibling,
        "game bundle resolves sibling Studio bundle executable");
    Check(LocalIpcEndpointStore.GetDirectory(gameMacOs, data)
        == LocalIpcEndpointStore.GetDirectory(studioMacOs, data),
        "paired app bundles share installation IPC identity");
    Check(LocalIpcEndpointStore.GetDirectory(gameMacOs, data)
        != LocalIpcEndpointStore.GetDirectory(gameMacOs, Path.Combine(temporary, "other-data")),
        "explicit user data roots isolate Studio instances");
    Check((int)StudioOpenKind.Home == 0 && (int)StudioOpenKind.Map == 1
        && (int)StudioOpenKind.Replay == 2 && (int)StudioOpenKind.Clip == 3
        && (int)StudioOpenKind.ReplayLibrary == 4, "workspace kind values preserve protocol indices");
    Check(StudioApplicationLauncher.CreateStartInfoWorkspace(Path.Combine(installation, "ProjectPrimeStudio"), true)
        .ArgumentList.SequenceEqual(new[] { "--replay-library" })
        && StudioApplicationLauncher.CreateStartInfoWorkspace(Path.Combine(installation, "ProjectPrimeStudio"), false)
        .ArgumentList.Count == 0, "typed workspace launch emits library or Home arguments");
    foreach (var invalid in new[] {
        new StudioOpenRequest(Guid.NewGuid(), StudioOpenKind.ReplayLibrary, replay),
        new StudioOpenRequest(Guid.NewGuid(), StudioOpenKind.ReplayLibrary, Recover: true),
        new StudioOpenRequest(Guid.NewGuid(), StudioOpenKind.ReplayLibrary, SafeMode: true) })
        Check(invalid.Validate() != null, "library protocol rejects document and recovery flags");
    foreach (var arguments in new[] {
        new[] { "--replay-library", "--replay", replay },
        new[] { "--clip", clip, "--replay-library" },
        new[] { "--replay-library", "--replay-library" },
        new[] { "--replay-library", "--recover" },
        new[] { "--safe-mode", "--replay-library" },
        new[] { "--recover", "--recover" },
        new[] { "--safe-mode", "--safe-mode" },
        new[] { "--studio", "--studio" } })
    {
        using var rejected = StudioApplicationInstance.AcquireStudioInstance(arguments, installation, data);
        Check(rejected.Error != null && !rejected.IsPrimary && !rejected.Forwarded,
            "conflicting or duplicate startup switches rejected before acquiring instance");
    }
    var launch = StudioApplicationLauncher.CreateStartInfo(Path.Combine(installation, "ProjectPrimeStudio"), replay, true);
    Check(launch.ArgumentList.SequenceEqual(new[] { "--replay", replay, "--recover" }) && !launch.UseShellExecute,
        "logical Unicode path uses separate process arguments");
    Check(!StudioApplicationLauncher.TryOpen("relative.ppdemo", false, out var invalidError) && invalidError != null,
        "launch rejects relative paths before executable discovery");
    var primary = StudioApplicationInstance.AcquireStudioInstance(Array.Empty<string>(), installation, data);
    using (primary)
    {
        Check(primary.IsPrimary && primary.Error == null, "first Studio owns authenticated endpoint");
        var received = new List<StudioOpenRequest>();
        Check(primary.Pump(received.Add) == 1 && received[0].Kind == StudioOpenKind.Home, "initial Home queued to owner");
        using var secondary = StudioApplicationInstance.AcquireStudioInstance(new[] { "--clip", clip, "--recover" }, installation, data);
        Check(secondary.Forwarded && !secondary.IsPrimary && secondary.Error == null, "second launch forwards instead of opening another owner");
        Check(primary.Pump(received.Add) == 1 && received[1].Path == clip && received[1].Recover,
            "authenticated logical request reaches owner exactly once");
        using var library = StudioApplicationInstance.AcquireStudioInstance(
            new[] { "--studio", "--replay-library" }, installation, data);
        Check(library.Forwarded && primary.Pump(received.Add) == 1
            && received[^1].Kind == StudioOpenKind.ReplayLibrary && received[^1].Path == null,
            "authenticated replay workspace forwards without a document path");
        using var home = StudioApplicationInstance.AcquireStudioInstance(
            StudioApplicationLauncher.CreateWorkspaceRequest(false), installation, data);
        Check(home.Forwarded && primary.Pump(received.Add) == 1
            && received[^1].Kind == StudioOpenKind.Home && received[^1].Path == null,
            "typed map placeholder workspace forwards Home without a document");
        var endpoint = StudioEndpointStore.Read(installation, data);
        var wrong = endpoint with { Secret = StudioIpcAuthentication.NewSecret() };
        var denied = StudioIpcClient.ForwardAsync(wrong, StudioApplicationInstance.CreateRequest(replay)).GetAwaiter().GetResult();
        Check(!denied.Accepted && primary.Pump(received.Add) == 0, "wrong capability cannot queue document");
        var request = StudioApplicationInstance.CreateRequest(replay);
        Check(StudioIpcClient.ForwardAsync(endpoint, request).GetAwaiter().GetResult().Accepted
            && StudioIpcClient.ForwardAsync(endpoint, request).GetAwaiter().GetResult().Accepted
            && primary.Pump(received.Add) == 1, "retries of same request ID queue once");
        bool wrongThread = Task.Run(() => { try { primary.Pump(received.Add); return false; }
            catch (InvalidOperationException) { return true; } }).GetAwaiter().GetResult();
        Check(wrongThread, "document dispatch remains owner-thread only");
    }
    using var replacement = StudioApplicationInstance.AcquireStudioInstance(Array.Empty<string>(), installation, data);
    Check(replacement.IsPrimary, "shutdown retires endpoint and releases durable instance lease");
}
finally { Directory.Delete(temporary, true); }
