using System.Text.Json.Nodes;
using MphRead.Mods.Launcher.Core;

string directory = Path.Combine(Path.GetTempPath(), "prime-ui-policy-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
int checks = 0;
void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); checks++; }
void Reject(Action action, string message)
{
    try { action(); }
    catch (Exception error) when (error is ArgumentException or InvalidOperationException) { checks++; return; }
    throw new InvalidOperationException(message);
}
LauncherUiSelectionPolicy Policy(string name, bool native = true, bool legacy = true, params string[] accepted)
    => new(native, legacy, "osx-arm64", accepted, Path.Combine(directory, name + ".json"), "check-build");
try
{
    var policy = Policy("default");
    Check(policy.Resolve([]).Selected == LauncherUiMode.Legacy, "Unaccepted ordinary build preserves legacy default");
    Check(policy.Resolve(["-ui=rmlui"]).Selected == LauncherUiMode.RmlUi, "Explicit native selection is an opt-in trial");
    Check(policy.Resolve(["--UI", "RMLUI"]).Selected == LauncherUiMode.RmlUi, "Separated case-insensitive selection works");
    Check(policy.Resolve(["ui=auto"]).Selected == LauncherUiMode.Legacy, "Explicit auto retains target acceptance gate");
    Check(policy.Resolve(["-rmluipocshot", "somewhere"]).Selected == LauncherUiMode.RmlUi, "Existing capture switch retains native choice");
    Check(policy.Resolve(["-ui=legacy", "-rmlui"]).Selected == LauncherUiMode.Legacy, "Explicit legacy overrides development aliases");
    Check(Policy("eligible", accepted: ["osx-arm64"]).Resolve([]).Selected == LauncherUiMode.RmlUi, "Only accepted current RID can auto-select native");
    Check(Policy("other-rid", accepted: ["win-x64"]).Resolve([]).Selected == LauncherUiMode.Legacy, "Another accepted platform does not enable this RID");
    Check(Policy("native-only", legacy: false).Resolve([]).Selected == LauncherUiMode.RmlUi, "Explicit native-only compile can run before default cutover");
    Check(Policy("legacy-only", native: false).Resolve([]).Selected == LauncherUiMode.Legacy, "Legacy-only builds retain their available UI");
    Reject(() => Policy("legacy-only", native: false).Resolve(["-ui=rmlui"]), "Missing native UI fails explicitly");
    Reject(() => Policy("native-only", legacy: false).Resolve(["-ui=legacy"]), "Missing legacy UI fails explicitly");
    Reject(() => Policy("no-ui", native: false, legacy: false).Resolve([]), "No available UI is rejected");
    Reject(() => policy.Resolve(["-ui"]), "Missing UI value is rejected");
    Reject(() => policy.Resolve(["-ui=secret-input"]), "Unknown UI is rejected without returning arbitrary input");
    Reject(() => policy.Resolve(["-ui=legacy", "-ui=rmlui"]), "Conflicting choices are rejected");
    Check(LauncherUiSelectionPolicy.HasSelectionOption(["-ui=auto"]) && !LauncherUiSelectionPolicy.HasSelectionOption(["-room", "ui-value"]), "Only real UI options imply launcher startup");

    policy = Policy("interrupted", accepted: ["osx-arm64"]);
    policy.ObservePresented(LauncherUiMode.Legacy, "OpenGL"); policy.BeginNativeAttempt();
    var restarted = Policy("interrupted", accepted: ["osx-arm64"]);
    var rollback = restarted.Resolve([]);
    Check(restarted.Failure == LauncherUiFailure.InterruptedStartup && rollback.NativeBlocked, "A durable unfinished native startup is detected");
    Check(rollback.Selected == LauncherUiMode.Legacy && rollback.RendererRollback == "opengl", "Auto rollback keeps the recorded usable UI/renderer pair");
    Check(restarted.Resolve(["-ui=rmlui"]).Selected == LauncherUiMode.RmlUi, "Explicit native choice can retry a blocked rollout");
    restarted.BeginNativeAttempt();
    Check(restarted.NativeBlocked, "Beginning a retry does not erase its failure witness");
    restarted.ObservePresented(LauncherUiMode.RmlUi, "Metal");
    var recovered = Policy("interrupted", accepted: ["osx-arm64"]);
    Check(!recovered.NativeBlocked && recovered.Failure == LauncherUiFailure.None && recovered.Resolve([]).Selected == LauncherUiMode.RmlUi, "Successful physical presentation restores auto eligibility");
    recovered.RecordNativeFailure(LauncherUiFailure.Runtime);
    recovered.ObservePresented(LauncherUiMode.Legacy, "opengl");
    var fallback = Policy("interrupted", accepted: ["osx-arm64"]);
    Check(fallback.NativeBlocked && fallback.Failure == LauncherUiFailure.Runtime, "Successful fallback preserves native failure gating");
    Check(fallback.Resolve([]).RendererRollback == "opengl", "Fallback actual renderer becomes the durable usable renderer");
    Check(Policy("interrupted", legacy: false).Resolve([]) is { Selected: LauncherUiMode.RmlUi, NativeBlocked: true, RendererRollback: "opengl" }, "Native-only build retains available UI with recorded renderer rollback");

    policy = Policy("clean", accepted: ["osx-arm64"]); policy.BeginNativeAttempt(); policy.CompleteCleanShutdown();
    Check(!Policy("clean", accepted: ["osx-arm64"]).NativeBlocked, "Clean shutdown before a first UI frame is not treated as a crash");
    policy = Policy("diagnostic"); policy.ObservePresented(LauncherUiMode.Legacy,"opengl");policy.BeginNativeAttempt();
    string diagnosticPath=Path.Combine(directory,"diagnostic.json"), diagnosticBefore=File.ReadAllText(diagnosticPath);
    var diagnostic=new LauncherUiSelectionPolicy(true,true,"osx-arm64",["osx-arm64"],diagnosticPath,"check-build",persistenceEnabled:false);
    Check(diagnostic.Failure==LauncherUiFailure.InterruptedStartup&&diagnostic.Resolve(["-ui=rmlui"]).Selected==LauncherUiMode.RmlUi,
        "Diagnostic runs retain the real parser and recovery decision");
    diagnostic.BeginNativeAttempt();diagnostic.ObservePresented(LauncherUiMode.RmlUi,"metal");diagnostic.RecordNativeFailure(LauncherUiFailure.Runtime);diagnostic.CompleteCleanShutdown();
    Check(File.ReadAllText(diagnosticPath)==diagnosticBefore,"Diagnostic load recovery and all lifecycle mutations leave user recovery state unchanged");
    string corrupt = Path.Combine(directory, "corrupt.json");
    File.WriteAllText(corrupt, "private-field-sentinel");
    policy = Policy("corrupt", accepted: ["osx-arm64"]);
    Check(policy.NativeBlocked && policy.Failure == LauncherUiFailure.InvalidState && policy.Resolve([]).Selected == LauncherUiMode.Legacy, "Malformed state fails closed for automatic rollout");
    policy.ObservePresented(LauncherUiMode.Legacy, "vulkan");
    Check(!File.ReadAllText(corrupt).Contains("private-field-sentinel"), "Repaired diagnostics contain fixed labels, not old arbitrary input");
    File.WriteAllText(Path.Combine(directory, "large.json"), new string('x', 8193));
    Check(Policy("large", accepted: ["osx-arm64"]).NativeBlocked, "Oversized startup record is rejected within its byte bound");
    var future = JsonNode.Parse(File.ReadAllText(corrupt))!; future["Format"] = 2;
    File.WriteAllText(Path.Combine(directory, "future.json"), future.ToJsonString());
    Check(Policy("future", accepted: ["osx-arm64"]).Failure == LauncherUiFailure.InvalidState, "Unknown record format cannot enable auto rollout");
    Reject(() => policy.ObservePresented(LauncherUiMode.RmlUi, "arbitrary-renderer"), "Only approved renderer labels enter persistence");
    Reject(() => policy.RecordNativeFailure(LauncherUiFailure.None), "Failure mutation requires a concrete bounded failure code");
    Check(Task.Run(() => { try { policy.BeginNativeAttempt(); return false; } catch (InvalidOperationException) { return true; } }).GetAwaiter().GetResult(), "Workers cannot change startup authority");
    Check(!Directory.EnumerateFiles(directory, "*.tmp").Any(), "Atomic writes retire all temporary records");
    if (!OperatingSystem.IsWindows())
        Check(File.GetUnixFileMode(corrupt) == (UnixFileMode.UserRead | UnixFileMode.UserWrite), "Durable startup record is private to its user");
    Console.WriteLine($"UI SELECTION POLICY PASS {checks} contracts");
}
finally { Directory.Delete(directory, recursive: true); }
