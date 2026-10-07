using MphRead.Mods.Render;

int checks = 0;
void Check(bool value, string name)
{
    if (!value) throw new InvalidOperationException(name);
    checks++;
}
void Rejected(Action action, string name)
{
    try { action(); } catch (InvalidOperationException) { checks++; return; }
    throw new InvalidOperationException(name);
}

Check(ShaderDiagnosticPolicy.Enabled("1", "1", "1"), "exact triple opt-in");
foreach (string? invalid in new string?[] { null, "", "0", "true", "01", " 1", "1 " })
{
    Check(!ShaderDiagnosticPolicy.Enabled(invalid, "1", "1"), "nonexact diagnostic opt-in");
    Check(!ShaderDiagnosticPolicy.Enabled("1", invalid, "1"), "nonexact native validation");
    Check(!ShaderDiagnosticPolicy.Enabled("1", "1", invalid), "nonexact GPU validation");
}
var layout = GeneratedShaderLayouts.Get(ModernProgramKind.PostProcess);
Check(layout.Size == ShaderDiagnosticPolicy.PostProcessBytes && layout.Size / 16 == 85, "actual PostProcess 85-row layout");
ShaderDiagnosticPolicy.ValidateBinding(0, layout.Size, layout.Size / 4); checks++;
ShaderDiagnosticPolicy.ValidateBinding(1536, layout.Size, layout.Size / 4); checks++;
Rejected(() => ShaderDiagnosticPolicy.ValidateBinding(1, layout.Size, 340), "misaligned actual offset");
Rejected(() => ShaderDiagnosticPolicy.ValidateBinding(0, layout.Size - 16, 336), "changed layout size");
Rejected(() => ShaderDiagnosticPolicy.ValidateBinding(0, layout.Size, 339), "truncated packed words");
foreach (var field in layout.Uniforms)
    Check(field.Offset >= 0 && field.Offset % 4 == 0 && field.Offset + field.Stride * field.Count <= layout.Size,
        "actual generated field is inside captured backing storage");
Check(layout.Uniforms.Single(f => f.Name == "pbr_enabled").Offset == 880, "actual pbr offset");
Check(layout.Uniforms.Single(f => f.Name == "depth_available").Offset == 48, "actual depth offset");

var lease = new ShaderDiagnosticMapLease();
nint token = ShaderDiagnosticMapRegistry.Register(lease);
Check(lease.Status == -1 && ShaderDiagnosticMapRegistry.PendingCount == 1, "map remains pending until callback");
Check(ShaderDiagnosticMapRegistry.Complete(token, 0) && lease.Status == 0, "real callback registry completes once");
Check(!ShaderDiagnosticMapRegistry.Complete(token, 1) && lease.Status == 0, "duplicate callback cannot replace result");
var cancelled = new ShaderDiagnosticMapLease();
nint cancelledToken = ShaderDiagnosticMapRegistry.Register(cancelled);
ShaderDiagnosticMapRegistry.Cancel(cancelledToken);
Check(!ShaderDiagnosticMapRegistry.Complete(cancelledToken, 0) && cancelled.Status == -1, "late callback after cancel cannot use released userdata");
Check(!ShaderDiagnosticMapRegistry.Complete(0, 0) && ShaderDiagnosticMapRegistry.PendingCount == 0, "unknown token is harmless and no registry retention");
for (int i = 0; i < 1024; i++)
{
    var next = new ShaderDiagnosticMapLease();
    nint nextToken = ShaderDiagnosticMapRegistry.Register(next);
    ShaderDiagnosticMapRegistry.Cancel(nextToken);
    Check(!ShaderDiagnosticMapRegistry.Complete(nextToken, i), "cancelled token is never reused");
}
Check(ShaderDiagnosticMapRegistry.PendingCount == 0, "all callback leases released");
Check(!ShaderDiagnosticPolicy.Write(new ThrowingWriter(), "diagnostic"), "throwing diagnostic writer is contained");
var writer = new StringWriter();
Check(ShaderDiagnosticPolicy.Write(writer, "actual diagnostic") && writer.ToString().Contains("actual diagnostic"), "working writer preserves complete record");
checks += ObservationControls.Run();
Console.WriteLine($"Shader diagnostic CPU policy/layout/callback controls PASS {checks}; no native device or GPU observation inferred.");

sealed class ThrowingWriter : StringWriter
{
    public override void WriteLine(string? value) => throw new IOException("injected diagnostic writer failure");
}
