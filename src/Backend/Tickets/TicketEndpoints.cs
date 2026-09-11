namespace MphRead.Backend.Tickets;

public static class TicketEndpoints
{
    // Direct game-server session registration and account game-ticket issuance were
    // retired with the standalone server path. Node registration/admission is mapped
    // by NodeEndpoints; this endpoint only exposes the retained trust status.
    public static void MapGameTickets(this WebApplication app)
    {
        app.MapGet("/v1/ranked-availability", (GameServerRegistry registry) =>
            Results.Ok(registry.RankedAvailability)).RequireRateLimiting(BackendRoutePolicy.Api);
    }
}
