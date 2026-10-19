using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using MphRead;

// Tool-only reflection snapshots every stored native tuning field, including
// ricochet indices and array entries; it never calls computed asset/name getters.
static string Digest(IReadOnlyList<WeaponInfo> table)
{
    using var stream = new MemoryStream();
    using (var writer = new Utf8JsonWriter(stream))
    {
        writer.WriteStartArray();
        foreach (var row in table)
        {
            writer.WriteStartObject();
            foreach (var field in typeof(WeaponInfo).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .OrderBy(x => x.Name, StringComparer.Ordinal))
            {
                writer.WritePropertyName(field.Name);
                JsonSerializer.Serialize(writer, field.GetValue(row), field.FieldType);
            }
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }
    return Convert.ToHexStringLower(SHA256.HashData(stream.ToArray()));
}
var snapshot = new
{
    schema = 1,
    source = "d726c766a10affaa0d8f95bc25bce9d178528215",
    enumType = Enum.GetUnderlyingType(typeof(BeamType)).Name,
    nativeIds = Enum.GetValues<BeamType>().ToDictionary(x => x.ToString(), x => (int)x),
    multiplayerCount = Weapons.WeaponsMP.Count,
    singleplayerCount = Weapons.Weapons1P.Count,
    multiplayerSha256 = Digest(Weapons.WeaponsMP),
    singleplayerSha256 = Digest(Weapons.Weapons1P)
};
string json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true }) + "\n";
if (args.Length == 2 && args[0] == "--record")
{
    File.WriteAllText(args[1], json);
    Console.WriteLine("Recorded native source baseline; review before accepting: " + args[1]);
    return 0;
}
if (args.Length != 1) { Console.Error.WriteLine("Usage: snapshot <fixture.json> | --record <fixture.json>"); return 1; }
bool equal = JsonElement.DeepEquals(JsonDocument.Parse(json).RootElement,
    JsonDocument.Parse(File.ReadAllText(args[0])).RootElement);
Console.WriteLine(equal ? "PASS all native IDs and stored MP/1P weapon tuning fields" : "FAIL native weapon fixture changed");
return equal ? 0 : 1;
