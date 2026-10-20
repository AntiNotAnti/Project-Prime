using System;
using System.IO;
using System.Reflection;
using System.Threading;
using MphRead.Mods.CustomWeapons;
using OpenTK.Mathematics;

namespace MphRead.Mods.Network;

public static partial class NetLobbyTest
{
    private sealed record SourcePhaseSnapshot(uint World,Vector3 Position,WeaponOwnerContext Owner,
        uint InputFrame,bool HasState,WeaponStateSnapshot State,bool HasBoundary,CustomSourceReadyContext Boundary,
        bool HasSource,CustomEngineObservation Source);

    // These are real dedicated engine actors and UDP input/ready handlers.
    // Manual clients acknowledge native scene loading; they do not run a renderer.
    public static int RunCustomSourceBoundary(string data)
    {
        string directory = Directory.GetCurrentDirectory(); _checks = 0;
        try
        {
            Directory.SetCurrentDirectory(Path.GetFullPath(data)); Paths.UpdatePaths(); Paths.ChooseMphPath();
            Headless.Enter();
            using var rig = new Rig(simulate:true,room:"MP3 PROVING GROUND",maxPlayers:2,
                customReadiness:true,customCombat:true);
            var first = rig.Add(9120); var second = rig.Add(9121);
            using var a = new CustomReadinessProbe(first); using var b = new CustomReadinessProbe(second);
            rig.ReadyAll(); rig.Expect(first,first.Command(LobbyCommandType.StartMatch),LobbyResultCode.Ok);
            first.Loaded(); second.Loaded();
            rig.Wait(()=>first.State?.Phase == SessionPhase.InMatch && a.Ready.HasValue && b.Ready.HasValue,
                "real dedicated scene/native UDP barrier supplies both custom manifests",15000);
            const BindingFlags hidden = BindingFlags.Instance|BindingFlags.NonPublic;
            var sim = (ServerSim)typeof(DedicatedServer).GetField("_sim",hidden)!.GetValue(rig.Server)!;
            var scene = sim.EvidenceScene!;
            Check(scene.CustomCombat is { IsEligible:true },"separate explicit combat opt-in installed real authority engine adapter");
            var readiness = (ICustomSourceReadiness)typeof(CustomAuthorityCombatAdapter).GetField("_sourceBoundary",hidden)!.GetValue(scene.CustomCombat)!;
            SourcePhaseSnapshot? latest = null;
            var sinkField = typeof(NetSession).GetField("_snapshotSink",BindingFlags.Static|BindingFlags.NonPublic)!;
            var original = (SnapshotSink)sinkField.GetValue(null)!;
            // Read simulation-owned mutable state only on its snapshot thread;
            // publish one immutable observation to the coordinating test thread.
            SnapshotSink observe = payload =>
            {
                original(payload);
                if (scene.CustomCombat is not { IsEligible:true } root) return;
                ushort generation = NetPlayerLifecycle.Generation(first.Slot), life = NetPlayerLifecycle.Get(first.Slot);
                if (generation == 0 || life == 0) return; // Native disconnect/teardown can run before the rig exits.
                var player = scene.Players.Items[first.Slot];
                var owner = new WeaponOwnerContext(NetSession.CurrentMatchId,NetSession.AuthorityEpoch,
                    (byte)first.Slot,generation,life);
                bool hasState = root.TryState(player,out var state);
                bool hasBoundary = readiness.TryGet(owner,out var boundary);
                uint input = NetSession.RemoteIntents[first.Slot].Frame;
                bool hasSource = root.TryGetSourceObservation(owner,input,out var source);
                Volatile.Write(ref latest,new(NetSession.NetFrame,player.Position,owner,input,
                    hasState,state,hasBoundary,boundary,hasSource,source));
            };
            sinkField.SetValue(null,observe);
            rig.Wait(()=>Volatile.Read(ref latest) is { Owner.IsValid:true },"real snapshot thread publishes spawned current-life identity");
            var initial = Volatile.Read(ref latest)!;
            Check(!initial.HasState && !initial.HasBoundary,"manifest publication alone created neither source boundary nor inventory");
            void Send(uint frame,Vector3 position,WeaponOwnerContext owner)
            {
                var input = new IntentPacket { MatchId=owner.MatchId,AuthorityEpoch=owner.AuthorityEpoch,
                    SlotGeneration=owner.Generation,LifeId=owner.Life,Frame=frame,Position=position,Aim=Vector3.UnitZ,
                    Buttons=IntentButtons.InPlayState,WeaponSelect=(byte)BeamType.PowerBeam,HasState=true,
                    HasFireEvents=true,HasAnalogMove=true,HasContinuousFireTick=true };
                byte[] bytes = new byte[CompactIntentCodec.Size(input)]; CompactIntentCodec.Write(input,bytes);
                first.Send(PacketType.Intent,bytes);
            }
            uint beforeFrame = initial.World+1;
            Send(beforeFrame,initial.Position,initial.Owner);
            rig.Wait(()=>Volatile.Read(ref latest)?.InputFrame == beforeFrame,
                "actual authenticated UDP intent reached native current-movement write before custom acknowledgement");
            Check(!Volatile.Read(ref latest)!.HasState,"accepted input before readiness grants no custom resources");
            a.Send();
            rig.Wait(()=>a.Confirmations > 0 && rig.Server.TryGetCustomWeaponReady(first.Slot,out _),
                "actual endpoint acknowledges capability after pre-ready input ingress");
            uint confirmedAt = Volatile.Read(ref latest)!.World;
            rig.Wait(()=>Volatile.Read(ref latest)!.World > confirmedAt+2,"held pre-ready input is consumed in later real engine phases");
            var held = Volatile.Read(ref latest)!;
            Check(!held.HasBoundary && !held.HasState,"later engine consumption cannot promote a pre-ready carrier or refill inventory");
            Send(beforeFrame,held.Position,held.Owner);
            uint duplicateAt = held.World;
            rig.Wait(()=>Volatile.Read(ref latest)!.World > duplicateAt+2,"duplicate UDP native carrier traverses production recovery path");
            Check(!Volatile.Read(ref latest)!.HasState,"native duplicate true-result does not count as fresh custom ingress");
            var current = Volatile.Read(ref latest)!; uint firstSource = beforeFrame+1;
            Send(firstSource,current.Position,current.Owner);
            rig.Wait(()=>Volatile.Read(ref latest) is { HasState:true,HasBoundary:true,HasSource:true } sample
                && sample.InputFrame == firstSource,
                "fresh post-ready UDP input reaches actual placement/controls and automatically binds one life",6000);
            var opened = Volatile.Read(ref latest)!;
            Check(opened.State.Owner == current.Owner && opened.State.Magazine == 4 && opened.State.Reserve == 16
                && opened.State.Phase == CustomWeaponPhase.Unequipped,
                "real source boundary grants canonical unequipped four-round magazine and sixteen reserve");
            Check(opened.Boundary.FirstSourceFrame == firstSource && opened.Source.Input.Frame == firstSource
                && opened.Source.Input.Owner == current.Owner && opened.Source.ObservedWorldFrame >= opened.Boundary.OpenedWorld,
                "boundary uses exact accepted source frame and immutable engine observation rather than authority clock");
            Check(opened.Boundary.Ready.Bootstrap == a.Ready!.Value.Bootstrap
                && opened.Boundary.PeerIncarnation == first.Transport.ConnectionStats(first.Server)!.Value.ConnectionId,
                "live source bridge retains exact bootstrap and endpoint transport incarnation");
            a.Send(); Send(firstSource,current.Position,current.Owner);
            uint openedAt = opened.World;
            rig.Wait(()=>Volatile.Read(ref latest)!.World > openedAt+2,"duplicate ready/input traverse already-bound live source owner");
            var duplicate = Volatile.Read(ref latest)!;
            Check(duplicate.HasState && duplicate.Boundary == opened.Boundary
                && duplicate.State.Magazine == 4 && duplicate.State.Reserve == 16,
                "duplicate packets preserve boundary and once-per-life inventory");
            Check(sim.StepFailures == 0,"actual dedicated source phases complete without simulation exceptions");
            Console.WriteLine($"PASS {_checks} real UDP/engine custom source-boundary checks.");
            Console.WriteLine("SCOPE: actual dedicated simulation and endpoint-owned ready/input handlers, post-ready ingress fence, exact engine-consumed source and automatic once-per-life loadout. Native remote scene acknowledgements are manual. No custom action/outcome transport, latency fairness, local/bot parity or rendered client is tested.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine("CUSTOM SOURCE UDP FAIL " + ex); return 1; }
        finally { NetSession.Stop(); Directory.SetCurrentDirectory(directory); }
    }
}
