using System.Reflection;
using System.Text.Json;
var assembly = typeof(MphRead.Shaders).Assembly;
var result = new Dictionary<string, object>();
foreach (var (name, type, vertex, fragment) in new[] {
    ("World", "MphRead.Shaders", "VertexShader", "FragmentShader"),
    ("DeferredPbr", "MphRead.Mods.Render.DeferredPbrShader", "VertexSource", "FragmentSource"),
    ("PostProcess", "MphRead.Mods.Render.GraphicsPipelineShader", "VertexSource", "FragmentSource"),
    ("LauncherChamber", "MphRead.Mods.Render.LauncherChamberShader", "VertexSource", "FragmentSource")
}) {
    var t = assembly.GetType(type) ?? assembly.GetTypes().First(x => x.Name == type.Split('.').Last());
    result[name] = new { vertex = (string)t.GetProperty(vertex)!.GetValue(null)!, fragment = (string)t.GetProperty(fragment)!.GetValue(null)! };
}
File.WriteAllText(args[0], JsonSerializer.Serialize(result));
