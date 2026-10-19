using System;
using System.Globalization;

namespace MphRead.Mods.CustomWeapons;

public enum WeaponIdentityKind : byte { Invalid, Native, Custom }

/// <summary>Persistent identity. Custom revisions never alias native beam values.</summary>
public readonly record struct WeaponIdentity
{
    public WeaponIdentityKind Kind { get; }
    private readonly BeamType _native;
    public Guid ContentId { get; }
    public string? DefinitionHash { get; }
    public bool IsValid => Kind is WeaponIdentityKind.Native or WeaponIdentityKind.Custom;
    public BeamType NativeBeam => Kind == WeaponIdentityKind.Native ? _native
        : throw new InvalidOperationException("This identity is not a native weapon.");

    private WeaponIdentity(WeaponIdentityKind kind, BeamType native, Guid content, string? hash)
    { Kind = kind; _native = native; ContentId = content; DefinitionHash = hash; }

    public static WeaponIdentity Native(BeamType beam)
    {
        if ((int)beam < 0 || (int)beam > 8) throw new ArgumentOutOfRangeException(nameof(beam));
        return new(WeaponIdentityKind.Native, beam, Guid.Empty, null);
    }

    public static WeaponIdentity Custom(Guid contentId, string definitionHash)
    {
        if (contentId == Guid.Empty) throw new ArgumentException("Content ID must be nonempty.", nameof(contentId));
        if (!IsDigest(definitionHash)) throw new ArgumentException("Expected lowercase SHA-256.", nameof(definitionHash));
        return new(WeaponIdentityKind.Custom, BeamType.None, contentId, definitionHash);
    }

    public static bool IsDigest(string? hash)
    {
        if (hash == null || hash.Length != 64) return false;
        foreach (char c in hash) if (!(c is >= '0' and <= '9' or >= 'a' and <= 'f')) return false;
        return true;
    }

    // A canonical text codec for local metadata only. No legacy serializer uses it.
    public override string ToString() => Kind switch
    {
        WeaponIdentityKind.Native => "native/" + ((int)_native).ToString(CultureInfo.InvariantCulture),
        WeaponIdentityKind.Custom => "custom/" + ContentId.ToString("N") + "/" + DefinitionHash,
        _ => "invalid"
    };

    public static bool TryParse(string? value, out WeaponIdentity identity)
    {
        identity = default;
        if (value is { Length: 8 } && value.StartsWith("native/", StringComparison.Ordinal)
            && value[7] is >= '0' and <= '8')
        { identity = Native((BeamType)(value[7] - '0')); return true; }
        if (value is not { Length: 104 } || !value.StartsWith("custom/", StringComparison.Ordinal)
            || value[39] != '/' || !Guid.TryParseExact(value.AsSpan(7, 32), "N", out var content)
            || content == Guid.Empty || !IsDigest(value[40..])) return false;
        var parsed = Custom(content, value[40..]);
        if (!StringComparer.Ordinal.Equals(value, parsed.ToString())) return false;
        identity = parsed;
        return true;
    }

    // Stable across processes, unlike the randomized string hash. Persistence
    // still uses the full identity, never this noncryptographic dictionary hash.
    public override int GetHashCode()
    {
        uint hash = 2166136261;
        void Add(byte value) => hash = unchecked((hash ^ value) * 16777619);
        Add((byte)Kind); Add(unchecked((byte)_native));
        Span<byte> bytes = stackalloc byte[16]; ContentId.TryWriteBytes(bytes);
        foreach (byte value in bytes) Add(value);
        if (DefinitionHash != null) foreach (char value in DefinitionHash) Add((byte)value);
        return unchecked((int)hash);
    }
}

/// <summary>Match-local only; invalid without its owning frozen registry.</summary>
public readonly record struct WeaponRuntimeId
{
    public ushort Value { get; }
    public bool IsValid => Value != 0;
    public WeaponRuntimeId(ushort value)
    {
        if (value == 0) throw new ArgumentOutOfRangeException(nameof(value));
        Value = value;
    }
}
