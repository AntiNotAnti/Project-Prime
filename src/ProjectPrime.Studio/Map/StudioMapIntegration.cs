using MphRead.Mods.MapEditor;
using MphRead.Mods.MapGen;

namespace ProjectPrime.Studio.Map;

/// <summary>Game-owned operations consumed by the map desktop host. Credentials are limited to a narrow Community ticket.</summary>
public interface IStudioMapIntegration
{
    Task<MapDefinition> InstallAsync(string package, MapContentIdentity identity, CancellationToken cancellation);
    Task PlaytestAsync(string package, MapContentIdentity identity, CancellationToken cancellation);
    Task HostAsync(string package, MapContentIdentity identity, string communityAddress, CancellationToken cancellation);
    Task<string> CommunityTicketAsync(bool refresh, CancellationToken cancellation);
    Task<MapAuditResult> AuditAsync(MapProject project, CancellationToken cancellation);
}

internal sealed class UnavailableStudioMapIntegration : IStudioMapIntegration
{
    private static IOException Unavailable() => new("Project Prime is unavailable. Open the game to install or playtest a package; sign in through Project Prime to publish Community maps.");
    public Task<MapDefinition> InstallAsync(string package, MapContentIdentity identity, CancellationToken cancellation) => Task.FromException<MapDefinition>(Unavailable());
    public Task PlaytestAsync(string package, MapContentIdentity identity, CancellationToken cancellation) => Task.FromException(Unavailable());
    public Task HostAsync(string package, MapContentIdentity identity, string communityAddress, CancellationToken cancellation) => Task.FromException(Unavailable());
    public Task<string> CommunityTicketAsync(bool refresh, CancellationToken cancellation) => Task.FromException<string>(Unavailable());
    public Task<MapAuditResult> AuditAsync(MapProject project, CancellationToken cancellation) => Task.FromException<MapAuditResult>(Unavailable());
}
