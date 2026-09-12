using System.Collections.Immutable;

namespace ProjectPrime.Server.Shared;

/// <summary>
/// Shared bounds and validation rules for the ephemeral online-presence
/// contract. These limits are intentionally independent from any account or
/// gameplay protocol so a public presence response remains small and safe to
/// buffer.
/// </summary>
public static class PresenceContract
{
    public const int MaximumDisplayNameLength = 16;
    public const int MaximumRegionLength = 32;
    public const int MaximumNodeEntries = 10_000;
    public const int PageSize = 50;
    public const int MaximumPages = 64;
    public const int MaximumEntries = PageSize * MaximumPages;

    public static bool IsValidDisplayName(string? value)
        => value is { Length: >= 1 and <= MaximumDisplayNameLength }
            && value == value.Trim()
            && value.All(c => c is >= ' ' and <= '~');

    public static bool IsValidRegion(string? value)
        => value is { Length: >= 1 and <= MaximumRegionLength }
            && value.All(c => char.IsAscii(c) && !char.IsControl(c));
}

public enum PlayerPresenceActivity : byte
{
    Online,
    InLobby,
    InMatch
}

/// <summary>Sanitized player data sent by a registered Node. It deliberately
/// contains no account, session, lobby, match, or device identity.</summary>
public sealed record NodePresenceEntry(string DisplayName, PlayerPresenceActivity Activity)
{
    public void Validate()
    {
        if (!PresenceContract.IsValidDisplayName(DisplayName)
            || !Enum.IsDefined(Activity))
            throw new ArgumentException("Invalid Node presence entry.");
    }
}

/// <summary>One monotonic presence projection from one Node incarnation.</summary>
public sealed record NodePresenceReport(Guid Incarnation, long Revision,
    ImmutableArray<NodePresenceEntry> Players)
{
    public void Validate()
    {
        if (Incarnation == Guid.Empty || Revision < 0 || Players.IsDefault
            || Players.Length > PresenceContract.MaximumNodeEntries)
            throw new ArgumentException("Invalid Node presence report.");
        foreach (NodePresenceEntry? player in Players)
        {
            if (player == null) throw new ArgumentException("Invalid Node presence entry.");
            player.Validate();
        }
    }
}

/// <summary>Public presence data. Only display name, activity, and region are
/// exposed; all ownership and session identifiers stay server-side.</summary>
public sealed record PublicPresenceEntry(string DisplayName,
    PlayerPresenceActivity Activity, string Region)
{
    public void Validate()
    {
        if (!PresenceContract.IsValidDisplayName(DisplayName)
            || !Enum.IsDefined(Activity)
            || !PresenceContract.IsValidRegion(Region))
            throw new ArgumentException("Invalid public presence entry.");
    }
}

/// <summary>One immutable, revision-pinned public presence page.</summary>
public sealed record PresenceDirectoryPage(long Revision, int TotalOnline,
    int VisibleOnline, int Page, int PageCount,
    ImmutableArray<PublicPresenceEntry> Entries, DateTimeOffset GeneratedAt)
{
    public void Validate()
    {
        if (Revision < 0 || TotalOnline < 0 || VisibleOnline < 0 || Page < 0
            || PageCount is < 1 or > PresenceContract.MaximumPages
            || Page >= PageCount || Entries.IsDefault
            || Entries.Length > PresenceContract.PageSize)
            throw new ArgumentException("Invalid presence directory page.");
        foreach (PublicPresenceEntry entry in Entries) entry.Validate();
    }
}
