using System;

namespace ProjectPrime.Server.Shared;

/// <summary>
/// The one wire-level contract for a public Node control endpoint. Discovery,
/// admission, the Node reporter and map acquisition must all agree on this
/// exact path; accepting a bare <c>wss://host</c> URL only moves a typo to the
/// first WebSocket connection.
/// </summary>
public static class NodeEndpointContract
{
    public const string ControlPath = "/v1/control";
    public const int MaximumControlUriCharacters = 256;

    public static bool TryValidatePublicControlUri(string? value, out Uri uri)
    {
        uri = null!;
        if (value is not { Length: > 0 and <= MaximumControlUriCharacters }
            || !Uri.TryCreate(value, UriKind.Absolute, out Uri? parsed)
            || parsed.Scheme != Uri.UriSchemeWss
            || string.IsNullOrEmpty(parsed.Host)
            || parsed.UserInfo.Length != 0
            || parsed.Query.Length != 0
            || parsed.Fragment.Length != 0
            || parsed.Port is < 1 or > 65535
            || parsed.AbsolutePath != ControlPath
            // UriComponents.Path deliberately omits the leading slash. Compare
            // like-for-like so the canonical endpoint is not rejected while
            // still excluding escaped spellings of the control path.
            || !parsed.GetComponents(UriComponents.Path, UriFormat.UriEscaped)
                .AsSpan().SequenceEqual(ControlPath.AsSpan(1)))
            return false;

        // A canonical endpoint has no trailing slash and no escaped spelling
        // of the path. Preserve the caller's host/port representation for the
        // request, but never admit a semantically different path.
        uri = parsed;
        return true;
    }

    public static Uri RequirePublicControlUri(string value, string parameterName = "publicControlUri")
        => TryValidatePublicControlUri(value, out Uri uri)
            ? uri
            : throw new ArgumentException(
                $"The public Node control URI must be an exact wss://host[:port]{ControlPath} endpoint.",
                parameterName);
}

/// <summary>Shared lifetime and clock-skew bounds for Node admission JWTs.</summary>
public static class NodeAdmissionContract
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(120);
    public static readonly TimeSpan MaximumClockSkew = TimeSpan.FromSeconds(30);
    public const int LifetimeSeconds = 120;
    public const int MaximumClockSkewSeconds = 30;
}
