using System;
using MphRead.Entities;

namespace MphRead.Mods.Network;

public static partial class NetSession
{
    private static bool _clientSuspended;
    public static bool ClientSuspended => _clientSuspended;

    private static PlayerEntity? ClearOwnerInput(Scene scene)
    {
        int slot = scene.Services.PlayerReplication.LocalSlot;
        if (slot >= scene.Players.Items.Count) return null;
        var player = slot < 0 ? scene.Players.Main : scene.Players.Items[slot];
        player.Controls.ClearAll(); player.ModForgetInputDeltas();
        player.ModCancelClientCharge();
        scene.ModSetLateAim(0, 0);
        scene.PlayerReplication.DiscardLocalInput();
        return player;
    }

    /// <summary>Called on the scene owner before a focus/surface pause.</summary>
    public static void SuspendClient(Scene scene)
    {
        var player = ClearOwnerInput(scene);
        if (!IsClient || _playback || _clientSuspended) return;
        NetFireEvents.DiscardLocalPending(LocalSlot);
        if (player != null && !FreezeGameplay)
        {
            // The last gameplay frame may already have been sent. Author a new
            // neutral carrier so the server can stop held input immediately.
            NetFrame++;
            var neutral = scene.PlayerReplication.CaptureIntent(player);
            neutral.Buttons &= IntentButtons.InPlayState | IntentButtons.AltFormState | IntentButtons.ZoomedState;
            neutral.Presses = default; neutral.MoveX = neutral.MoveY = 0;
            neutral.ShotFlags &= unchecked((byte)~IntentPacket.FlagBoosting);
            neutral.ChargeLevel = 0; neutral.ContinuousFireTick = 0;
            SendIntent(neutral);
        }
        _clientSuspended = true;
    }

    /// <summary>Discard the stale socket inbox and require a fresh fenced world
    /// baseline before this owner can author input after resuming.</summary>
    public static void ResumeClient(Scene scene)
    {
        var player = ClearOwnerInput(scene);
        if (!IsClient || _playback || !_clientSuspended) return;
        _clientSuspended = false;
        NetFireEvents.DiscardLocalPending(LocalSlot);
        NetHitClaims.ForgetPending();
        NetHitPrediction.ForgetPending();
        NetFireEvents.RetireClientAttacks(scene);
        player?.ResetNetworkResources();
        Array.Clear(RemoteIntentValid);
        NetSmoothing.Reset();
        _appliedBootstrap = _receivingBootstrap = null; _bootstrapMask = 0;
        _bootstrapObjectives.Reset();
        NetObjectiveSync.Reset();
        _loadedStart = null;
        _lastServerPacket = Clock;
        try
        {
            // Keep the connection identity, sequence windows and endpoint.
            // A new port cannot claim a still-live admission using ClientId.
            _transport?.DiscardIncoming();
            SendHello();
            SendIdentify();
            MarkMatchLoaded(refreshBootstrap: true);
        }
        catch (Exception ex)
        {
            Stop(); LastError = "Could not resume the network session: " + ex.Message;
        }
    }
}
