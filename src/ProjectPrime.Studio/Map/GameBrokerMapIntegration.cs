using MphRead.Mods.MapEditor;
using MphRead.Mods.MapGen;
using ProjectPrime.Studio.IPC;
using ProjectPrime.Studio.Protocol;
using ProjectPrime.Studio.Settings;

namespace ProjectPrime.Studio.Map;

public sealed class GameBrokerMapIntegration : IStudioMapIntegration, IDisposable
{
    private readonly StudioGameBrokerClient _client;
    private readonly StudioPaths _paths;
    private readonly SemaphoreSlim _playtestGate=new(1);
    public Guid ActivePlaytestId { get; private set; }
    public GameBrokerMapIntegration(StudioPaths paths) { _paths=paths; _client = new(paths.InstallationDirectory,paths.UserDataDirectory); }
    private static StudioMapIdentity Identity(MapContentIdentity identity) => new(identity.MapId,identity.RoomKey,identity.ContentHash.ToString(),identity.PackageHash.ToString());
    private static void RequireAccepted(StudioGameResult result) { if (!result.Accepted) throw new IOException(result.Error ?? "Project Prime rejected the creator request."); }
    public async Task<MapDefinition> InstallAsync(string package, MapContentIdentity identity, CancellationToken cancellation)
    {
        var result = await _client.InstallMapPackageAsync(package,Identity(identity),cancellation);
        RequireAccepted(result);
        cancellation.ThrowIfCancellationRequested();
        return MapDefinition.Load(package);
    }
    public async Task PlaytestAsync(string package, MapContentIdentity identity, CancellationToken cancellation)
    {
        await _playtestGate.WaitAsync(cancellation);
        try
        {
            bool restarting=false;
            if (ActivePlaytestId != Guid.Empty)
            {
                var status=await _client.GetPlaytestStatusAsync(ActivePlaytestId,cancellation);
                if(status.Accepted && status.PlaytestState is StudioPlaytestState.Accepted or StudioPlaytestState.Started)
                { RequireAccepted(await _client.StopPlaytestAsync(ActivePlaytestId,cancellation)); restarting=true; }
                ActivePlaytestId=Guid.Empty;
            }
            var result = await _client.RequestPlaytestAsync(package,Identity(identity),cancellationToken:cancellation);
            // A stop response precedes the game's next owner-thread disposal tick. Retry only this owned restart.
            for(int attempt=0;restarting && !result.Accepted && result.Deferred && attempt<40;attempt++)
            { await Task.Delay(250,cancellation); result=await _client.RequestPlaytestAsync(package,Identity(identity),cancellationToken:cancellation); }
            RequireAccepted(result); ActivePlaytestId = result.PlaytestId;
        }
        finally { _playtestGate.Release(); }
    }
    public async Task HostAsync(string package, MapContentIdentity identity, string communityAddress, CancellationToken cancellation)
    {
        var result = await _client.RequestHostAsync(package,Identity(identity),new StudioPlaytestOptions(CommunityAddress:communityAddress),cancellation);
        RequireAccepted(result);
    }
    public async Task<string> CommunityTicketAsync(bool refresh, CancellationToken cancellation)
    {
        var result = await _client.RequestCommunityTicketAsync(refresh,cancellation);
        RequireAccepted(result);
        return result.CommunityTicket ?? throw new IOException("Project Prime returned no Community ticket.");
    }
    public Task<MapAuditResult> AuditAsync(MapProject project, CancellationToken cancellation)
        => _client.GameExecutable is { } game
            ? MapAuditRunner.Run(project,cancellation,new MapAuditLaunchOptions(game,Path.Combine(_paths.StagingDirectory,"audit")))
            : Task.FromException<MapAuditResult>(new IOException("Install a compatible Project Prime beside Studio, or configure PROJECT_PRIME_GAME_PATH, to run an isolated map audit."));
    public Task<StudioGameResult> PlaytestStatusAsync(CancellationToken cancellation = default) => _client.GetPlaytestStatusAsync(ActivePlaytestId,cancellation);
    public Task<StudioGameResult> StopPlaytestAsync(CancellationToken cancellation = default) => _client.StopPlaytestAsync(ActivePlaytestId,cancellation);
    public void Dispose() { _client.Dispose(); _playtestGate.Dispose(); }
}
