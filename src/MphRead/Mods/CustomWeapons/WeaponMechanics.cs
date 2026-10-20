using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace MphRead.Mods.CustomWeapons;

public enum WeaponBehavior : byte { SemiAutoPrecision = 1, PirateUaBurst = 2 }

/// <summary>Validated, immutable schema-1 rules. Distances use 1/4096 world units;
/// time uses authoritative 60 Hz ticks, spread uses millidegrees, optics use thousandths.</summary>
public sealed class WeaponMechanics
{
    public const int SchemaVersion = 1, BehaviorVersion = 1, MaxJsonBytes = 16384;
    private readonly bool _pirateBurst;
    public WeaponBehavior Behavior => _pirateBurst ? WeaponBehavior.PirateUaBurst : WeaponBehavior.SemiAutoPrecision;
    public int BodyDamage { get; }
    public int HeadDamage { get; }
    public int MagazineCapacity { get; }
    public int InitialReserve { get; }
    public int MaximumReserve { get; }
    public int ShotCooldownTicks { get; }
    public int ReloadTicks { get; }
    public int ProjectileSpeed4096 { get; }
    public int ProjectileRadius4096 { get; }
    public int Range4096 { get; }
    public int LifetimeTicks { get; }
    public int HipSpreadMilliDegrees { get; }
    public int ScopedSpreadMilliDegrees { get; }
    public int MaxActiveProjectiles { get; }
    private readonly int[] _optics;
    public IReadOnlyList<int> OpticMagnificationsMilli { get; }
    public string DefinitionHash { get; }

    // Closed local profile; the public schema1 parser intentionally cannot
    // import or advertise it as a precision rifle/network behavior.
    internal static WeaponMechanics CreatePirateBurstV1() => new(pirateBurst: true);
    private WeaponMechanics(bool pirateBurst)
    {
        if (!pirateBurst) throw new ArgumentException("Private burst profile required.");
        _pirateBurst = true;
        BodyDamage = 12; HeadDamage = 16;
        ShotCooldownTicks = 40; ProjectileSpeed4096 = 8192; ProjectileRadius4096 = 409;
        Range4096 = 0; LifetimeTicks = 510; MaxActiveProjectiles = 40;
        // This profile has native UA, not a magazine, reload or optic state.
        _optics = Array.Empty<int>(); OpticMagnificationsMilli = Array.AsReadOnly(_optics);
        DefinitionHash = Convert.ToHexStringLower(SHA256.HashData(ToCanonicalBytes()));
    }

    private WeaponMechanics(JsonElement root)
    {
        Keys(root, "schema", "behaviorVersion", "behavior", "damage", "magazineCapacity", "initialReserve",
            "maximumReserve", "shotCooldownTicks", "reloadTicks", "projectileSpeed4096", "projectileRadius4096",
            "range4096", "lifetimeTicks", "hipSpreadMilliDegrees", "scopedSpreadMilliDegrees",
            "maxActiveProjectiles", "opticsMilli", "splash", "piercing");
        if (Integer(root,"schema",1,1) != SchemaVersion || Integer(root,"behaviorVersion",1,1) != BehaviorVersion
            || root.GetProperty("behavior").GetString() != "semi-auto-precision") throw new FormatException("Unsupported weapon behavior/schema.");
        var damage = root.GetProperty("damage"); Keys(damage,"body","head");
        BodyDamage = Integer(damage,"body",0,2000); HeadDamage = Integer(damage,"head",0,3000);
        MagazineCapacity = Integer(root,"magazineCapacity",1,32);
        MaximumReserve = Integer(root,"maximumReserve",0,512);
        InitialReserve = Integer(root,"initialReserve",0,MaximumReserve);
        ShotCooldownTicks = Integer(root,"shotCooldownTicks",6,3600);
        ReloadTicks = Integer(root,"reloadTicks",1,3600);
        ProjectileSpeed4096 = Integer(root,"projectileSpeed4096",1,4096*1000);
        ProjectileRadius4096 = Integer(root,"projectileRadius4096",0,4096);
        Range4096 = Integer(root,"range4096",1,4096*10000);
        LifetimeTicks = Integer(root,"lifetimeTicks",1,600);
        if ((long)ProjectileSpeed4096 * LifetimeTicks < Range4096) throw new FormatException("Projectile expires before configured range.");
        HipSpreadMilliDegrees = Integer(root,"hipSpreadMilliDegrees",0,10000);
        ScopedSpreadMilliDegrees = Integer(root,"scopedSpreadMilliDegrees",0,HipSpreadMilliDegrees);
        MaxActiveProjectiles = Integer(root,"maxActiveProjectiles",1,8);
        var optics = root.GetProperty("opticsMilli");
        if (optics.ValueKind != JsonValueKind.Array || optics.GetArrayLength() is < 1 or > 4)
            throw new FormatException("Expected one to four ordered optics.");
        _optics = new int[optics.GetArrayLength()];
        int index = 0, previous = 999;
        foreach (var optic in optics.EnumerateArray())
        {
            if (!optic.TryGetInt32(out int value) || value <= previous || value > 8000)
                throw new FormatException("Optics must be unique ascending integer magnifications, 1000–8000.");
            _optics[index++] = previous = value;
        }
        OpticMagnificationsMilli = Array.AsReadOnly(_optics);
        if (root.GetProperty("splash").ValueKind != JsonValueKind.False
            || root.GetProperty("piercing").ValueKind != JsonValueKind.False)
            throw new FormatException("Schema 1 precision supports no splash or piercing.");
        DefinitionHash = Convert.ToHexStringLower(SHA256.HashData(ToCanonicalBytes()));
    }

    private static void Keys(JsonElement element, params string[] expected)
    {
        if (element.ValueKind != JsonValueKind.Object) throw new FormatException("Expected mechanics object.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
            if (!seen.Add(property.Name) || !expected.Contains(property.Name, StringComparer.Ordinal))
                throw new FormatException("Duplicate or unsupported mechanics field.");
        if (seen.Count != expected.Length) throw new FormatException("Missing mechanics field.");
    }
    private static int Integer(JsonElement root, string name, int min, int max)
    {
        if (!root.GetProperty(name).TryGetInt32(out int value) || value < min || value > max)
            throw new FormatException("Mechanics field out of range: " + name);
        return value;
    }
    public static WeaponMechanics Parse(ReadOnlySpan<byte> utf8)
    {
        if (utf8.Length is 0 or > MaxJsonBytes) throw new FormatException("Mechanics JSON exceeds its byte limit.");
        try
        {
            using var document = JsonDocument.Parse(utf8.ToArray(), new JsonDocumentOptions { MaxDepth = 8 });
            return new(document.RootElement);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or OverflowException)
        { throw new FormatException("Invalid mechanics JSON.", ex); }
    }
    public byte[] ToCanonicalBytes()
    {
        if (_pirateBurst) return PirateBlasterDefinition.CanonicalBytes();
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream))
        {
            w.WriteStartObject(); w.WriteNumber("schema",SchemaVersion); w.WriteNumber("behaviorVersion",BehaviorVersion);
            w.WriteString("behavior","semi-auto-precision"); w.WriteStartObject("damage");
            w.WriteNumber("body",BodyDamage); w.WriteNumber("head",HeadDamage); w.WriteEndObject();
            w.WriteNumber("magazineCapacity",MagazineCapacity); w.WriteNumber("initialReserve",InitialReserve);
            w.WriteNumber("maximumReserve",MaximumReserve); w.WriteNumber("shotCooldownTicks",ShotCooldownTicks);
            w.WriteNumber("reloadTicks",ReloadTicks); w.WriteNumber("projectileSpeed4096",ProjectileSpeed4096);
            w.WriteNumber("projectileRadius4096",ProjectileRadius4096); w.WriteNumber("range4096",Range4096);
            w.WriteNumber("lifetimeTicks",LifetimeTicks); w.WriteNumber("hipSpreadMilliDegrees",HipSpreadMilliDegrees);
            w.WriteNumber("scopedSpreadMilliDegrees",ScopedSpreadMilliDegrees); w.WriteNumber("maxActiveProjectiles",MaxActiveProjectiles);
            w.WriteStartArray("opticsMilli"); foreach (int optic in _optics) w.WriteNumberValue(optic); w.WriteEndArray();
            w.WriteBoolean("splash",false); w.WriteBoolean("piercing",false); w.WriteEndObject();
        }
        return stream.ToArray(); // Detached copy; callers cannot mutate cached rules or hash.
    }
}
