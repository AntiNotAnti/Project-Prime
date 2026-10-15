using MphRead.Mods.Launcher;
using MphRead.Mods.MapEditor;
using MphRead.Mods.MapGen;
using MphRead.Mods.StudioIntegration;
using ProjectPrime.Studio.IPC;
using ProjectPrime.Studio.Protocol;
using ProjectPrime.Studio.Settings;

namespace ProjectPrime.Studio.Map;

/// <summary>Narrow runtime handoff. Studio never installs into the game's live runtime directories.</summary>
internal sealed class GameBrokerMapIntegration : IStudioMapIntegration,IDisposable
{
    private readonly StudioGameBrokerClient _broker;
    private readonly StudioPaths _paths;
    internal GameBrokerMapIntegration(StudioPaths paths,StudioGameBrokerClient? broker=null)
    { _paths=paths;_broker=broker??new(paths.InstallationDirectory,paths.UserDataDirectory); }
    private static void Require(StudioGameResult result,CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if(!result.Accepted)throw new IOException(result.Error??"Project Prime rejected the exact map request.");
    }
    private static StudioMapIdentity Verify(string path,MapContentIdentity identity)
    {
        if(!MapContentIdentity.FromPackage(path).Matches(identity))throw new InvalidDataException("The package changed before its game handoff. Build it again.");
        return GameStudioBroker.ToWireIdentity(identity);
    }
    public async Task<MapDefinition> InstallAsync(string package,MapContentIdentity identity,CancellationToken cancellation)
    {
        using var staged=await FreezeAsync(package,identity,cancellation);
        Require(await _broker.InstallMapPackageAsync(staged.Path,Verify(staged.Path,identity),cancellation),cancellation);
        // Source inspection is safe here; runtime publication has already been
        // acknowledged by the game's owner-thread broker with the exact identity.
        return MapProjectSerializer.Load(staged.Path).Definition;
    }
    public async Task PlaytestAsync(string package,MapContentIdentity identity,CancellationToken cancellation)
    {
        using var staged=await FreezeAsync(package,identity,cancellation);
        Require(await _broker.RequestPlaytestAsync(staged.Path,Verify(staged.Path,identity),cancellationToken:cancellation),cancellation);
    }
    public async Task HostAsync(string package,MapContentIdentity identity,string communityAddress,CancellationToken cancellation)
    {
        using var staged=await FreezeAsync(package,identity,cancellation);
        Require(await _broker.RequestHostAsync(staged.Path,Verify(staged.Path,identity),new StudioPlaytestOptions(CommunityAddress:communityAddress),cancellation),cancellation);
    }
    private async Task<FrozenPackage> FreezeAsync(string package,MapContentIdentity identity,CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        LocalIpcEndpointStore.EnsurePrivateDirectory(_paths.StagingDirectory);
        string directory=Path.Combine(_paths.StagingDirectory,Guid.NewGuid().ToString("N"));
        LocalIpcEndpointStore.EnsurePrivateDirectory(directory);
        FrozenPackage staged=new(Path.Combine(directory,"map.ppmap"));
        try
        {
            await using(var source=new FileStream(package,FileMode.Open,FileAccess.Read,FileShare.Read,81920,FileOptions.Asynchronous))
            await using(var target=new FileStream(staged.Path,FileMode.CreateNew,FileAccess.Write,FileShare.None,81920,FileOptions.Asynchronous))
                await source.CopyToAsync(target,cancellation);
            Verify(staged.Path,identity);
            cancellation.ThrowIfCancellationRequested();
            return staged;
        }
        catch { staged.Dispose();throw; }
    }
    private sealed class FrozenPackage(string path):IDisposable
    {
        public string Path {get;}=path;
        public void Dispose()
        {
            try { Directory.Delete(System.IO.Path.GetDirectoryName(Path)!,true); }
            catch(Exception ex)when(ex is IOException or UnauthorizedAccessException)
            { Console.Error.WriteLine("[studio] A staged package is still in use; cleanup deferred: "+ex.Message); }
        }
    }
    public async Task<string> CommunityTicketAsync(bool refresh,CancellationToken cancellation)
    {
        var result=await _broker.RequestCommunityTicketAsync(refresh,cancellation);Require(result,cancellation);
        return result.CommunityTicket??throw new IOException("Sign in through Project Prime to publish Community maps.");
    }
    public Task<MapAuditResult> AuditAsync(MapProject project,CancellationToken cancellation)
    {
        string executable=_broker.GameExecutable??throw new IOException("Install Project Prime beside Studio before running an engine map audit.");
        return MapAuditRunner.Run(project,cancellation,executable,GameFiles.Root);
    }
    public void Dispose()=>_broker.Dispose();
}
