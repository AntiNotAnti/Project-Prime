using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace MphRead.Mods.MapGen;

public sealed record MapIdentity
{
    private static readonly Regex StableIdPattern = new(
        "^[a-z0-9][a-z0-9._-]{2,63}$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    public string StableId { get; init; }
    public MapVersion Version { get; init; }

    [JsonConstructor]
    public MapIdentity(string stableId, MapVersion version)
    {
        StableId = ValidateStableId(stableId);
        Version = version;
    }

    public static string ValidateStableId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (!StableIdPattern.IsMatch(value))
        {
            throw new ArgumentException(
                "Map stable IDs must match [a-z0-9][a-z0-9._-]{2,63}.", nameof(value));
        }
        return value;
    }

    public static string FromLegacyName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Span<char> buffer = stackalloc char[Math.Min(name.Length, 54)];
        int written = 0;
        bool separator = false;
        foreach (char source in name)
        {
            char value = char.ToLowerInvariant(source);
            if (value is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                if (separator && written > 0 && written < buffer.Length) buffer[written++] = '-';
                separator = false;
                if (written < buffer.Length) buffer[written++] = value;
            }
            else if (written > 0)
            {
                separator = true;
            }
        }
        string suffix = new string(buffer[..written]).TrimEnd('-');
        if (suffix.Length < 3) suffix = suffix.PadRight(3, '0');
        return "legacy." + suffix;
    }
}

[JsonConverter(typeof(MapVersionJsonConverter))]
public readonly record struct MapVersion : IComparable<MapVersion>
{
    public int Major { get; }
    public int Minor { get; }
    public int Patch { get; }
    public string? PreRelease { get; }

    public MapVersion(int major, int minor, int patch, string? preRelease = null)
    {
        if (major < 0 || minor < 0 || patch < 0)
            throw new ArgumentOutOfRangeException(nameof(major), "Map version components cannot be negative.");
        if (preRelease != null && !Regex.IsMatch(preRelease,
            "^[0-9A-Za-z-]+(?:\\.[0-9A-Za-z-]+)*$", RegexOptions.CultureInvariant))
            throw new ArgumentException("Invalid semantic-version prerelease value.", nameof(preRelease));
        Major = major;
        Minor = minor;
        Patch = patch;
        PreRelease = preRelease;
    }

    public static MapVersion Parse(string value)
    {
        if (!TryParse(value, out MapVersion result))
            throw new FormatException($"'{value}' is not a supported semantic map version.");
        return result;
    }

    public static bool TryParse(string? value, out MapVersion result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(value)) return false;
        string withoutBuild = value.Split('+', 2)[0];
        string[] releaseAndPre = withoutBuild.Split('-', 2);
        string[] parts = releaseAndPre[0].Split('.');
        if (parts.Length != 3
            || !int.TryParse(parts[0], out int major)
            || !int.TryParse(parts[1], out int minor)
            || !int.TryParse(parts[2], out int patch)
            || major < 0 || minor < 0 || patch < 0)
            return false;
        try
        {
            result = new MapVersion(major, minor, patch,
                releaseAndPre.Length == 2 ? releaseAndPre[1] : null);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    public int CompareTo(MapVersion other)
    {
        int result = Major.CompareTo(other.Major);
        if (result != 0) return result;
        result = Minor.CompareTo(other.Minor);
        if (result != 0) return result;
        result = Patch.CompareTo(other.Patch);
        if (result != 0) return result;
        if (PreRelease == other.PreRelease) return 0;
        if (PreRelease == null) return 1;
        if (other.PreRelease == null) return -1;
        return StringComparer.Ordinal.Compare(PreRelease, other.PreRelease);
    }

    public override string ToString()
        => $"{Major}.{Minor}.{Patch}" + (PreRelease == null ? "" : "-" + PreRelease);
}

public sealed record MapContentIdentity(MapIdentity Identity, string ContentHash)
{
    public string ContentHash { get; init; } = MapHash.Validate(ContentHash, nameof(ContentHash));
}

public static class MapHash
{
    public static string Validate(string value, string? parameterName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length != 64)
            throw new ArgumentException("A SHA-256 hash must contain 64 hexadecimal characters.", parameterName);
        foreach (char character in value)
        {
            if (!Uri.IsHexDigit(character))
                throw new ArgumentException("A SHA-256 hash must contain only hexadecimal characters.", parameterName);
        }
        return value.ToLowerInvariant();
    }
}

public sealed class MapVersionJsonConverter : JsonConverter<MapVersion>
{
    public override MapVersion Read(ref Utf8JsonReader reader, Type typeToConvert,
        JsonSerializerOptions options)
        => MapVersion.Parse(reader.GetString() ?? "");

    public override void Write(Utf8JsonWriter writer, MapVersion value,
        JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToString());
}
