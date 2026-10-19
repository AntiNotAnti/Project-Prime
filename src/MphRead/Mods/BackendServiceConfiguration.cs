using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;

namespace MphRead.Mods;

// Public routing metadata only. Credentials remain in the existing auth/report owners.
internal sealed class BackendServiceConfiguration
{
    internal const string DefaultUrl = "https://hwcjaygoistufktorbmf.supabase.co/";
    private const string DefaultPublicApiKey = "sb_publishable_EVT45OPl638kA_j8vZ0ebg_sw3aWaVz";
    internal Uri BaseUri { get; }
    internal string PublicApiKey { get; }
    internal Uri CareerReportUri { get; }
    private BackendServiceConfiguration(Uri root, string key, Uri report)
        => (BaseUri, PublicApiKey, CareerReportUri) = (root, key, report);
    public override string ToString() => "Project Prime backend " + BaseUri.GetLeftPart(UriPartial.Authority);

    internal static BackendServiceConfiguration Capture(Func<string, string?>? read = null)
    {
        read ??= Environment.GetEnvironmentVariable;
        string? configuredUrl = read("PROJECT_PRIME_SUPABASE_URL");
        string? configuredKey = read("PROJECT_PRIME_SUPABASE_KEY");
        string? configuredReport = read("PROJECT_PRIME_CAREER_REPORT_URL");
        var root = Parse(configuredUrl ?? DefaultUrl, root: true);
        bool defaultOrigin = SameOrigin(root, new Uri(DefaultUrl));
        if (!defaultOrigin && configuredKey == null)
            throw new InvalidOperationException("A custom backend requires its matching public API key.");
        string key = configuredKey ?? DefaultPublicApiKey;
        if (!PublicKey(key, root)) throw new InvalidOperationException("Configure a publishable or legacy anon API key; server secrets are not accepted here.");
        var report = configuredReport == null ? new Uri(root, "functions/v1/career-report") : Parse(configuredReport, root: false);
        if (report.Host.EndsWith(".supabase.co", StringComparison.Ordinal) && !SameOrigin(root, report))
            throw new InvalidOperationException("A career report project must match the configured backend and public API key.");
        return new(root, key, report);
    }
    private static Uri Parse(string text, bool root)
    {
        if (text != text.Trim() || !Uri.TryCreate(text, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps || uri.UserInfo.Length != 0
            || uri.Query.Length != 0 || uri.Fragment.Length != 0
            || (root && uri.AbsolutePath != "/"))
            throw new InvalidOperationException("Configure an HTTPS backend origin without credentials, path, query or fragment.");
        return uri;
    }
    private static bool PublicKey(string key, Uri root)
    {
        if (key.Length > 4096 || key.Length == 0 || key.Any(c => c < '!' || c > '~')) return false;
        if (key.StartsWith("sb_publishable_", StringComparison.Ordinal)) return key.Length > 15;
        var parts = key.Split('.');
        if (parts.Length != 3) return false;
        try
        {
            string payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight((payload.Length + 3) / 4 * 4, '=');
            using var json = JsonDocument.Parse(Convert.FromBase64String(payload));
            if (!json.RootElement.TryGetProperty("role", out var role) || role.GetString() != "anon") return false;
            // Legacy anon JWT metadata can expose a project mismatch. Opaque
            // publishable-key ownership still requires service-side verification.
            if (root.Host.EndsWith(".supabase.co", StringComparison.Ordinal)
                && json.RootElement.TryGetProperty("ref", out var project))
                return project.ValueKind == JsonValueKind.String
                    && root.Host == project.GetString() + ".supabase.co";
            return true;
        }
        catch (Exception error) when (error is FormatException or JsonException or InvalidOperationException) { return false; }
    }
    private static bool SameOrigin(Uri a, Uri b) => a.Scheme == b.Scheme && a.Host == b.Host && a.Port == b.Port;
    internal bool Owns(Uri? uri) => uri != null && uri.IsAbsoluteUri && uri.UserInfo.Length == 0
        && uri.Fragment.Length == 0 && SameOrigin(BaseUri, uri);
    internal Uri Function(string name)
    {
        if (name.Length == 0 || name.Length > 64 || name.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_')))
            throw new ArgumentException("Invalid backend function name.", nameof(name));
        return new Uri(BaseUri, "functions/v1/" + name);
    }
    internal void AddReportApiKey(HttpRequestMessage request)
    {
        if (request.RequestUri != CareerReportUri) throw new InvalidOperationException("Refusing an API key for an unreviewed reporter endpoint.");
        request.Headers.Remove("apikey");
        request.Headers.Add("apikey", PublicApiKey);
    }
    internal void AddApiKey(HttpRequestMessage request)
    {
        if (!Owns(request.RequestUri)) throw new InvalidOperationException("Refusing an API key for a different backend origin.");
        request.Headers.Remove("apikey");
        request.Headers.Add("apikey", PublicApiKey);
    }
}
