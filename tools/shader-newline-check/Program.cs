using System.Reflection;
using MphRead;

int checks = 0;
void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); checks++; }
var specialize = typeof(Shaders).GetMethod("WithoutDepthVertexSpecialization", BindingFlags.Static | BindingFlags.NonPublic)!;
string Apply(string source) => (string)specialize.Invoke(null, new object[] { source })!;
foreach (var type in new[] { typeof(Shaders) })
{
    string actual = (string)type.GetProperty("VertexShader", BindingFlags.Static | BindingFlags.Public)!.GetValue(null)!;
    string lf = actual.Replace("\r\n", "\n"), crlf = lf.Replace("\n", "\r\n");
    string expected = Apply(lf);
    Check(Apply(crlf) == expected, type.Name + " LF/CRLF specialization differs");
    Check(Apply(actual) == expected, type.Name + " checked-out literal differs");
    Check(!expected.Contains("depth_vertex_fast", StringComparison.Ordinal), type.Name + " fast-path declarations/body remain");
    Check(expected.Contains("vec4 vtx_color =", StringComparison.Ordinal), type.Name + " original body missing");
    Check(expected.Count(c => c == '{') == expected.Count(c => c == '}'), type.Name + " legacy scope is unbalanced");
    Check((string)type.GetProperty("LegacyVertexShader", BindingFlags.Static | BindingFlags.Public)!.GetValue(null)! == expected,
        type.Name + " published shader differs");
}
try { Apply("void main() {}\r\n"); throw new Exception("Malformed specialization accepted"); }
catch (TargetInvocationException e) when (e.InnerException is InvalidOperationException) { checks++; }
Console.WriteLine($"PASS {checks} actual desktop shader newline contracts; CPU-only, Android deferred.");
