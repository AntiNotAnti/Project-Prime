using System.Net;
using System.Reflection;
using MphRead;
using MphRead.Entities;
using MphRead.Mods;
using MphRead.Mods.Network;

internal static partial class CustomAuthoritySceneChecks
{
    // The authority uses the existing explicitly trusted source fixture. Its
    // actual native BroadcastSnapshot bytes are then admitted by the real client
    // decoder/lifecycle/bridge on retained asset-backed actors. The role handoff
    // is a declared test seam; this is not a second authenticated client socket.
    internal static int RunClientProjection(string data)
    {
        _checks=0;
        string directory=Directory.GetCurrentDirectory();
        bool enabled=NetUnlagged.Enabled,pressAge=NetUnlagged.PressAgeEnabled;
        int maximum=NetUnlagged.MaxRewindFrames,players=PlayerEntity.MaxPlayers;
        bool geometry=NetDynamicGeometryHistory.ProductionEnabled,shadow=NetDynamicGeometryHistory.ShadowEnabled;
        bool force=MapAudit.ForceEveryone,script=NetTestScript.Enabled,claims=NetHitClaims.Enabled;
        bool acceptedPlayback=NetFireEvents.AcceptedPlaybackEnabled;
        var proof=NetShotProof.Mode;var timed=NetTimedShotProof.Mode;
        try
        {
            Directory.SetCurrentDirectory(Path.GetFullPath(data));Paths.UpdatePaths();Paths.ChooseMphPath();
            Headless.Enter();MapAudit.ForceEveryone=true;NetTestScript.Enabled=false;
            NetShotProof.Mode=ShotProofMode.Off;NetTimedShotProof.Mode=TimedGrantProofMode.Off;
            NetDynamicGeometryHistory.ProductionEnabled=true;NetDynamicGeometryHistory.ShadowEnabled=false;
            Check(ServerSim.Available(out string reason),"client projection has real assets: "+reason);
            foreach(bool local in new[]{false,true})
                foreach(bool lethal in new[]{false,true}) ClientProjection(local,lethal);
            Console.WriteLine($"PASS {_checks} actual custom authority/native client projection checks.");
            Console.WriteLine("SCOPE: trusted source fixture emits real M7 and pays actual authority HP; actual BroadcastSnapshot bytes enter native HandleSnapshot, lifecycle and live PlayerReplicationBridge on real actors. Local/puppet role handoff is explicit fixture policy, not another authenticated UDP socket; no GL/audio, fast/slow arrival-order or custom replay reconstruction claim.");
            return 0;
        }
        catch(Exception error) {Console.Error.WriteLine("CUSTOM CLIENT PROJECTION FAIL "+error);return 1;}
        finally
        {
            try {NetUnlagged.AbortShot();}
            finally
            {
                NetSession.Stop();NetUnlagged.Enabled=enabled;NetUnlagged.PressAgeEnabled=pressAge;
                NetUnlagged.MaxRewindFrames=maximum;PlayerEntity.MaxPlayers=players;
                NetDynamicGeometryHistory.ProductionEnabled=geometry;NetDynamicGeometryHistory.ShadowEnabled=shadow;
                NetTestScript.Enabled=script;NetShotProof.Mode=proof;NetTimedShotProof.Mode=timed;
                NetHitClaims.Enabled=claims;NetFireEvents.AcceptedPlaybackEnabled=acceptedPlayback;
                MapAudit.ForceEveryone=force;Directory.SetCurrentDirectory(directory);
            }
        }
    }

    private static byte[] CaptureNativeSnapshot()
    {
        var field=typeof(NetSession).GetField("_snapshotSink",StaticHidden)!;
        object? prior=field.GetValue(null);byte[]? bytes=null;
        SnapshotSink capture=payload=>bytes=payload.ToArray();
        try {field.SetValue(null,capture);NetSession.BroadcastSnapshot();}
        finally {field.SetValue(null,prior);}
        Check(bytes is {Length: > SnapshotHeader.Size},"real authority BroadcastSnapshot invokes canonical snapshot sink");
        return bytes!;
    }

    private static PlayerState SnapshotPlayer(byte[] bytes,int slot)
    {
        var header=SnapshotHeader.Read(bytes);
        for(int row=0;row<header.PlayerCount;row++)
        {
            var state=PlayerState.Read(bytes.AsSpan(SnapshotHeader.Size+row*PlayerState.Size));
            if(state.SlotIndex==slot)return state;
        }
        throw new InvalidOperationException("Actual snapshot omitted projected victim.");
    }

    private static void ReceiveNativeSnapshot(byte[] canonical)
    {
        var bytes=new byte[canonical.Length+1];bytes[0]=(byte)PacketType.Snapshot;canonical.CopyTo(bytes,1);
        var packet=new ReceivedPacket(new IPEndPoint(IPAddress.Loopback,31874),bytes,bytes.Length);
        typeof(NetSession).GetMethod("HandleSnapshot",StaticHidden)!.Invoke(null,new object[]{packet,false});
        NetHooks.ApplyRemoteStates();
    }

    private static void ClientProjection(bool local,bool lethal)
    {
        using var f=new Fixture();int slot=f.Victim.SlotIndex;
        int initial=lethal?150:200,expected=lethal?0:135;
        string label=(local?"local":"puppet")+" "+(lethal?"lethal150":"body65");
        Fixture.Clean(f.Victim,initial);
        byte[] baseline=CaptureNativeSnapshot();var before=SnapshotPlayer(baseline,slot);
        Frame(NetSession.NetFrame+1);
        f.Fire(head:lethal,victimHealth:initial);
        Check(f.Victim.Health==expected&&f.Root.PositivePayments==1&&f.Root.LastOutcome is { } paid
            &&paid.Health.BodyRemoved==(lethal?150:65)&&paid.Health.DamageEventId==1,
            label+" actual trusted-source M7 collision pays exact body delta once");
        byte[] result=CaptureNativeSnapshot();var after=SnapshotPlayer(result,slot);
        Check(before.Health==initial&&before.DamageEventId==0&&after.Health==expected&&after.DamageEventId==1
            &&after.SlotGeneration==before.SlotGeneration&&after.LifeId==before.LifeId,
            label+" actual native capture carries canonical health and custom shared frontier");
        Check(after.AttackerSlot==NetDamage.NoSlot&&after.DamageBeam==NetDamage.NoBeam&&after.DamageFlags==0
            &&Enumerable.Range(0,PlayerState.DamageHistory).All(i=>after.EventAt(i).EventId==0),
            label+" actual custom snapshot has neutral native attribution and no native history");
        Check(after.Form.BodyHealth==expected&&after.Form.DamageEventId==1&&after.Form.Phase==FormPhase.Biped,
            label+" actual Weavel form sample carries the same canonical health/frontier without split");

        // The authority half is complete. Reset only receive/lifecycle state and
        // reuse its loaded native actors as a client projection target; no engine
        // simulation or custom emission runs under this declared role handoff.
        f.Root.Dispose();
        typeof(NetSession).GetProperty(nameof(NetSession.Role))!.SetValue(null,NetRole.Client);
        typeof(NetSession).GetProperty(nameof(NetSession.IsAuthority))!.SetValue(null,false);
        typeof(NetSession).GetProperty(nameof(NetSession.LocalSlot))!.SetValue(null,local?slot:f.Shooter.SlotIndex);
        f.Scene.Players.MainPlayerIndex=NetSession.LocalSlot;
        NetSession.ResetMatchState();
        Check(NetSession.IsClient&&!NetSession.IsAuthority&&!NetSession.IsHost,
            label+" projection explicitly uses ordinary client role and real live bridge");
        ReceiveNativeSnapshot(baseline);
        Check(NetSession.RemoteStateValid[slot]&&f.Victim.Health==initial
            &&NetPlayerLifecycle.Matches(slot,before.SlotGeneration,before.LifeId),
            label+" real native decoder/lifecycle/bridge materializes captured alive baseline");
        Field(f.Victim,"_timeSinceDead",(ushort)7);
        int replayed=NetDamage.Replayed[slot];
        ReceiveNativeSnapshot(result);
        Check(NetSession.RemoteStateValid[slot]&&f.Victim.Health==expected
            &&NetSession.RemoteStates[slot].DamageEventId==1&&NetDamage.Replayed[slot]==replayed,
            label+" empty-history custom frontier converges actual client HP without native event replay");
        Check(DamageField<ushort[]>("_lastSeen")[slot]==1,
            label+" canonical snapshot alone closes the native shared-counter hole");
        ushort time=(ushort)typeof(PlayerEntity).GetField("_timeSinceDead",Hidden)!.GetValue(f.Victim)!;
        Check(time==(lethal?0:7)&&(!lethal||!f.Victim.ModIsInPlay),
            label+" lethal snapshot runs real native death path while body snapshot does not");
        Check(f.Scene.GameState.Points[slot]==after.Points&&f.Scene.GameState.Kills[slot]==after.Kills
            &&f.Scene.GameState.Deaths[slot]==after.Deaths,
            label+" client replay preserves captured authority victim scores");

        Field(f.Victim,"_timeSinceDead",(ushort)11);
        int[] points=(int[])f.Scene.GameState.Points.Clone(),kills=(int[])f.Scene.GameState.Kills.Clone(),deaths=(int[])f.Scene.GameState.Deaths.Clone();
        long outOfOrder=NetSession.SnapshotsOutOfOrder;
        ReceiveNativeSnapshot(result);ReceiveNativeSnapshot(baseline);
        Check(f.Victim.Health==expected&&NetDamage.Replayed[slot]==replayed
            &&(ushort)typeof(PlayerEntity).GetField("_timeSinceDead",Hidden)!.GetValue(f.Victim)! ==11,
            label+" duplicate and reordered snapshots cannot reenter death or debit client HP twice");
        Check(NetSession.SnapshotsOutOfOrder==outOfOrder+2&&points.SequenceEqual(f.Scene.GameState.Points)
            &&kills.SequenceEqual(f.Scene.GameState.Kills)&&deaths.SequenceEqual(f.Scene.GameState.Deaths),
            label+" native ordering rejects both stale snapshots without score changes");
        Check(f.Root.PositivePayments==1,"client snapshot projection cannot repay authority custom contact");
        Console.WriteLine($"CLIENT PROJECTION {label}: before={before.Health}, after={after.Health}, event={after.DamageEventId}, nativeHistory=0, nativeReplays={NetDamage.Replayed[slot]-replayed}.");
    }
}
