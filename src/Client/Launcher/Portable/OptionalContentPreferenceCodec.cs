using System;
using System.Text;
using MphRead.Runtime.Content;

namespace MphRead.Mods.Launcher;

/// <summary>
/// Lossless launcher.txt representation of an exact optional-pack identity.
/// The value is local UI state only; it is never sent to a Node or folded into
/// gameplay compatibility.
/// </summary>
internal static class OptionalContentPreferenceCodec
{
    // Covers maximum-length UTF-8 stable/version tokens plus the 64-byte hex
    // hash while retaining a small, fixed launcher-line bound.
    internal const int MaximumEncodedLength = 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static string Encode(ContentPackIdentity? identity)
    {
        if (!identity.HasValue) return "";
        identity.Value.Validate();
        string plain = String.Join('\0', identity.Value.StableId, identity.Value.Version,
            identity.Value.ContentHash.ToLowerInvariant());
        return Convert.ToBase64String(StrictUtf8.GetBytes(plain))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static bool TryDecode(string value, out ContentPackIdentity? identity)
    {
        identity = null;
        if (String.IsNullOrEmpty(value)) return true;
        if (value.Length > MaximumEncodedLength || value.IndexOfAny(['+', '/', '=']) >= 0)
            return false;
        try
        {
            string padded = value.Replace('-', '+').Replace('_', '/');
            padded += new string('=', (4 - padded.Length % 4) % 4);
            string[] parts = StrictUtf8.GetString(Convert.FromBase64String(padded)).Split('\0');
            if (parts.Length != 3) return false;
            var parsed = new ContentPackIdentity(parts[0], parts[1], parts[2]);
            parsed.Validate();
            identity = parsed;
            return true;
        }
        catch (Exception error) when (error is FormatException
            or DecoderFallbackException or ContentManifestValidationException)
        {
            return false;
        }
    }
}
