using System;
using MphRead.Entities;
using MphRead.Mods.Diagnostics;

namespace MphRead;

public partial class Scene
{
    // Local diagnostics only: never a wire/replay identity or a binding change.
    internal long LifetimeGeneration { get; } = SceneLifetimeDiagnostics.NextGeneration();
    internal void ObserveLifetime(SceneLifetimeStage stage)
    {
        if (!SceneLifetimeDiagnostics.Enabled) return;
        SceneFacadeRoots roots = SceneFacadeRoots.None;
        if (ReferenceEquals(global::MphRead.GameState.Current, GameState)) roots |= SceneFacadeRoots.GameState;
        if (ReferenceEquals(PlayerEntity.LegacyRegistry, Players)) roots |= SceneFacadeRoots.Players;
        if (ReferenceEquals(Rng.Current, Random)) roots |= SceneFacadeRoots.Random;
        SceneLifetimeRole role = IsReplayLab ? SceneLifetimeRole.ReplayLab
            : SideScene ? SceneLifetimeRole.SideScene
            : Services.IsReplica ? SceneLifetimeRole.Replica
            : Mods.Headless.Active ? SceneLifetimeRole.HeadlessLive : SceneLifetimeRole.Live;
        int programs = (_shaderProgramId != 0 ? 1 : 0) + (_rttShaderProgramId != 0 ? 1 : 0)
            + (_shiftShaderProgramId != 0 ? 1 : 0) + (_celShaderProgramId != 0 ? 1 : 0);
        SceneLifetimeDiagnostics.Record(new(LifetimeGeneration, role, stage, roots,
            Players.PlayerCount, _entities.Count, _ownedTextures.Count, programs,
            _modelLeases.Count, _streamingTextureDecodes.Count,
            _decoderCts?.IsCancellationRequested == true, null, null));
    }
}
