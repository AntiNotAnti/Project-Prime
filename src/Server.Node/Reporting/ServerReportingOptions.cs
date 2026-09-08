using System;

namespace MphRead.Reporting;

/// <summary>Null disables reporting. Credentials remain outside immutable reports/spool files.</summary>
public sealed record ServerReportingOptions(Guid ServerId, MatchReportOutboxOptions Outbox,
    Func<IMatchReportTransport> CreateTransport)
{
    public static ServerReportingOptions? FromEnvironment()
    {
        string? directory = Environment.GetEnvironmentVariable("PRIME_REPORT_DIRECTORY");
        if (string.IsNullOrWhiteSpace(directory)) return null;
        if (!Guid.TryParse(Environment.GetEnvironmentVariable("PRIME_SERVER_ID"), out Guid server) || server == Guid.Empty
            || !Uri.TryCreate(Environment.GetEnvironmentVariable("PRIME_REPORT_URL"), UriKind.Absolute, out Uri? endpoint)
            || endpoint.Scheme != "https" || !string.IsNullOrEmpty(endpoint.UserInfo))
            throw new ArgumentException("Reporting requires a nonempty server UUID and HTTPS report URL.");
        string credential = Environment.GetEnvironmentVariable("PRIME_REPORT_CREDENTIAL")
            ?? Environment.GetEnvironmentVariable("PRIME_SERVER_SECRET") ?? "";
        if (string.IsNullOrWhiteSpace(credential)) throw new ArgumentException("Reporting credential is missing.");
        return new(server, new(directory), () => new HttpMatchReportTransport(server, endpoint, credential));
    }
}
