using MphRead.Entities;

namespace MphRead.Mods.Network;

/// <summary>Apply the server's complete objective facts without changing player movement.
/// The existing world stream fences match, authority and tick before publishing a frame.</summary>
internal static class NetObjectiveSync
{
    internal static bool IsClient(Scene scene) => !scene.Services.IsReplica
        && NetSession.IsClient && !NetSession.IsAuthority;

    internal static PlayerEntity? Resolve(Scene scene, ReplayActorRef actor) => actor.Slot < PlayerEntity.SlotCapacity
        && NetPlayerLifecycle.Matches(actor.Slot, actor.Generation, actor.Life)
        && scene.Players.Items[actor.Slot].LoadFlags.TestFlag(LoadFlags.Active)
        ? scene.Players.Items[actor.Slot] : null;

    internal static void Apply(Scene scene, ReplayAuthorityWorld world, bool bootstrap = false)
    {
        if (!IsClient(scene) || (!bootstrap && !NetRoomChange.GameplayReady)
            || (!bootstrap && !NetSession.ObjectiveTickIsCurrent(world.Tick))
            || world.MatchId != NetSession.CurrentMatchId || world.Epoch != NetSession.AuthorityEpoch
            || scene.Room?.Meta.Name != NetSession.ServerMatch?.RoomKey) return;
        world.ApplyTokens(scene);
        foreach (var ammo in world.Chamber)
            Resolve(scene, ammo.Actor)?.ApplyChamberAmmo(ammo.Ammo, ammo.AcknowledgedFrame);
        scene.GameState.ActiveHardpointId = world.ActiveHardpointId;
        scene.GameState.HardpointTicksRemaining = world.HardpointTicksRemaining;
        scene.GameState.PrimeHunter = Resolve(scene, world.Prime)?.SlotIndex ?? -1;
        world.FlagScores.CopyTo(scene.GameState.OctolithScores, 0);
        world.NodesCaptured.CopyTo(scene.GameState.NodesCaptured, 0);
        foreach (var flag in world.Flags)
            if (scene.TryGetEntity(flag.Id, out var entity) && entity is OctolithFlagEntity target)
                target.ApplyLiveAuthority(flag);
        foreach (var node in world.Nodes)
            if (scene.TryGetEntity(node.Id, out var entity) && entity is NodeDefenseEntity target)
                target.ApplyLiveAuthority(node);
    }
}
