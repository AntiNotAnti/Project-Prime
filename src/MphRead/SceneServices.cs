namespace MphRead
{
    /// <summary>Host policy is explicit for each scene, including headless replicas.</summary>
    public interface ISceneServices
    {
        bool IsReplica { get; }
        bool MayEndOnScore { get; }
        bool ShouldLeaveAfterMatch { get; }
        bool SuppressDamage { get; }
        bool AllowsPresentationSideEffects { get; }
        Mods.Network.IPlayerReplicationHost PlayerReplication { get; }
        bool DisablePowerups { get; }
        Mods.Multiplayer.MatchWorldProfile? NetworkWorldProfile { get; }
        bool ReplicatesHealthSpawns { get; }
        bool TryGetHealthSpawn(short id, out Mods.Network.HealthSpawnState state);
    }
    internal sealed class LiveSceneServices : ISceneServices
    {
        internal static LiveSceneServices Instance { get; } = new();
        public bool IsReplica => false;
        public bool MayEndOnScore => true;
        public bool ShouldLeaveAfterMatch => true;
        public bool SuppressDamage => false;
        public bool AllowsPresentationSideEffects => !Mods.Headless.Active;
        public Mods.Network.IPlayerReplicationHost PlayerReplication => Mods.Network.LivePlayerReplicationHost.Instance;
        public bool DisablePowerups => Mods.Network.NetSession.ActiveMatchDefinition?.DisablePowerups == true;
        public Mods.Multiplayer.MatchWorldProfile? NetworkWorldProfile => Mods.Network.NetSession.Active ? Mods.Network.NetLaunch.WorldProfile : null;
        public bool ReplicatesHealthSpawns => Mods.Network.NetHealthSync.IsReplica;
        public bool TryGetHealthSpawn(short id, out Mods.Network.HealthSpawnState state) => Mods.Network.NetHealthSync.TryGet(id, out state) || Mods.Network.NetObjectiveSync.TryGetPickup(id, out state);
    }

    /// <summary>The authority owns gameplay without presentation or audio devices.</summary>
    internal sealed class DedicatedSceneServices : ISceneServices
    {
        internal static DedicatedSceneServices Instance { get; } = new();
        public bool IsReplica => false;
        public bool MayEndOnScore => true;
        public bool ShouldLeaveAfterMatch => true;
        public bool SuppressDamage => false;
        public bool AllowsPresentationSideEffects => false;
        public Mods.Network.IPlayerReplicationHost PlayerReplication => Mods.Network.LivePlayerReplicationHost.Instance;
        public bool DisablePowerups => LiveSceneServices.Instance.DisablePowerups;
        public Mods.Multiplayer.MatchWorldProfile? NetworkWorldProfile => LiveSceneServices.Instance.NetworkWorldProfile;
        public bool ReplicatesHealthSpawns => LiveSceneServices.Instance.ReplicatesHealthSpawns;
        public bool TryGetHealthSpawn(short id, out Mods.Network.HealthSpawnState state) => LiveSceneServices.Instance.TryGetHealthSpawn(id, out state);
    }
}
