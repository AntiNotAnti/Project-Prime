using System;
namespace MphRead.Mods.Network;
internal static class ServerTicketConfiguration
{
    // Keep operator secrets out of process command lines and diagnostic argument dumps.
    internal static ServerTicketOptions? FromEnvironment()
    {
        string? backend = Environment.GetEnvironmentVariable("PRIME_TICKET_BACKEND");
        string? issuer = Environment.GetEnvironmentVariable("PRIME_TICKET_ISSUER");
        string? serverId = Environment.GetEnvironmentVariable("PRIME_SERVER_ID");
        string? secret = Environment.GetEnvironmentVariable("PRIME_SERVER_SECRET");
        string? required = Environment.GetEnvironmentVariable("PRIME_REQUIRE_TICKETS");
        if (backend == null && issuer == null && secret == null && required == null) return null;
        if (!Uri.TryCreate(backend, UriKind.Absolute, out Uri? uri) || !Guid.TryParseExact(serverId, "D", out Guid id)
            || issuer == null || secret == null || (required != null && !bool.TryParse(required, out _)))
            throw new ProgramException("Incomplete ticket configuration: set PRIME_TICKET_BACKEND, PRIME_TICKET_ISSUER, PRIME_SERVER_ID and PRIME_SERVER_SECRET; PRIME_REQUIRE_TICKETS is optional true/false.");
        var options = new ServerTicketOptions(uri, issuer, id, secret) { RequireTickets = required != null && bool.Parse(required) };
        options.Validate();
        return options;
    }
}
