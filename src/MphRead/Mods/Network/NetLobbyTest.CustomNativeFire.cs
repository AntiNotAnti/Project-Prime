using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using MphRead.Entities;
using MphRead.Mods.CustomWeapons;
using OpenTK.Mathematics;

namespace MphRead.Mods.Network;

public static partial class NetLobbyTest
{
    private sealed record NativeFireWatch(ShotKey Key,FireEvent Fire);
    private sealed record NativeFireRow(NativeFireWatch Watch,bool InLedger,ShotDisposition Disposition,
        bool TimedProof,bool PhysicalBeam,bool IncomingPending,CustomNativeFireDecision Decision,CustomNativeFireDecision Admitted);
    private sealed record NativeFireSnapshot(uint World,uint InputFrame,IntentButtons Buttons,Vector3 Position,
        int Ammo0,int Ammo1,int Health,ushort DamageId,long ClaimsReceived,long ClaimsApplied,
        long Staged,int Pending,NativeFireRow[] Rows);
    private sealed record NativeFireMutation(int Serial,Action<Scene> Apply);
    // Real UDP, real source phases and native accepted-event playback. Manually
    // authored native FireEvents/claims are explicit protocol facts, not input UI.
    private sealed class NativeFireFixture : IDisposable
    {
        internal readonly ControlTransportFixture F=new();
        private readonly Scene _scene;
        private readonly FieldInfo _sink;
        private readonly SnapshotSink _previous;
        private NativeFireWatch[] _watch=Array.Empty<NativeFireWatch>();
        private NativeFireSnapshot? _snapshot;
        private NativeFireMutation? _mutation;
        private int _serial,_done;
        private uint _frame;
        internal readonly Dictionary<ushort,CombatAckEntry> Acks=new();
        internal readonly List<IntentPacket> Relays=new();
        internal NativeFireSnapshot Snapshot=>Volatile.Read(ref _snapshot)??throw new InvalidOperationException("No native-fire snapshot.");
        internal NativeFireFixture()
        {
            var sim=(ServerSim)typeof(DedicatedServer).GetField("_sim",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(F.Engine.Rig.Server)!;
            _scene=sim.EvidenceScene!;_frame=F.Engine.Latest.First.InputFrame;
            _sink=typeof(NetSession).GetField("_snapshotSink",BindingFlags.Static|BindingFlags.NonPublic)!;
            _previous=(SnapshotSink)_sink.GetValue(null)!;
            SnapshotSink observer=payload=>
            {
                _previous(payload);
                if(Interlocked.Exchange(ref _mutation,null) is {} mutation)
                {mutation.Apply(_scene);Volatile.Write(ref _done,mutation.Serial);}
                if(_scene.CustomCombat is not {IsEligible:true} root)return;
                var player=_scene.Players.Items[F.Engine.First.Slot];
                var input=NetSession.RemoteIntents[player.SlotIndex];
                var ammo=(int[])typeof(PlayerEntity).GetField("_ammo",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(player)!;
                var gates=(CustomNativeFireGate?[])typeof(CustomAuthorityCombatAdapter).GetField("_nativeFireGates",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(root)!;
                var gate=gates[player.SlotIndex];
                var nativeSlots=(Array)typeof(NetFireEvents).GetField("_slots",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null)!;
                var nativeSlot=nativeSlots.GetValue(player.SlotIndex)!;
                var incoming=(NetIncomingFireHistory)nativeSlot.GetType().GetField("Incoming",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)!.GetValue(nativeSlot)!;
                var rows=Volatile.Read(ref _watch).Select(w=>
                {
                    bool found=NetFireEvents.CaptureLedger(player.SlotIndex).TryGet(w.Key,out _,out var disposition,out _);
                    bool proof=NetTimedShotProof.TryGet(w.Key,w.Fire.SourceFrame,out _);
                    bool physical=player.EquipInfo.Beams.Any(b=>b.ModLaunchKey==w.Key&&b.ModComponentId!=0);
                    return new NativeFireRow(w,found,disposition,proof,physical,incoming.TryGetPending(w.Key.ShotId,out _),gate?.Read(w.Key,out _)??CustomNativeFireDecision.Pending,
                        gate?.ReadAdmitted(w.Key,out _)??CustomNativeFireDecision.Pending);
                }).ToArray();
                Volatile.Write(ref _snapshot,new(NetSession.NetFrame,input.Frame,input.Buttons,player.Position,
                    ammo[0],ammo[1],_scene.Players.Items[F.Engine.Second.Slot].Health,NetDamage.Sequence(F.Engine.Second.Slot),
                    NetHitClaims.Received,NetHitClaims.AppliedHere,gate?.Staged??0,gate?.PendingCount??0,rows));
            };
            _sink.SetValue(null,observer);
            var first=F.Engine.First.PacketReceived;
            F.Engine.First.PacketReceived=packet=>
            {
                first?.Invoke(packet);
                if(packet.Type==PacketType.CombatAck&&packet.Payload.Length>=HitVerdictPacket.HeaderSize)
                    for(int i=0;i<packet.Payload[0]&&HitVerdictPacket.HeaderSize+(i+1)*CombatAckEntry.Size<=packet.Payload.Length;i++)
                    {var ack=CombatAckEntry.Read(packet.Payload.Slice(HitVerdictPacket.HeaderSize+i*CombatAckEntry.Size));Acks[ack.ClaimId]=ack;}
            };
            var second=F.Engine.Second.PacketReceived;
            F.Engine.Second.PacketReceived=packet=>
            {
                second?.Invoke(packet);
                if(packet.Type==PacketType.SlotIntent&&packet.Payload.Length>1&&packet.Payload[0]==F.Engine.First.Slot
                    &&CompactIntentCodec.TryRead(packet.Payload.Slice(1),out var intent))Relays.Add(intent);
            };
            OnOwner(scene=>
            {
                // Explicit resource/proof configuration; no source/certificate,
                // accepted native event or native payment is injected.
                var player=scene.Players.Items[F.Engine.First.Slot];
                NetShotProof.Mode=ShotProofMode.Shadow;NetTimedShotProof.Mode=TimedGrantProofMode.Shadow;
                NetFireEvents.AcceptedPlaybackEnabled=true;NetHitClaims.Enabled=true;
                NetShotProof.Seed(player,new ShotProofSource(0x1ff,0x1ff,10000,10000,false,player.Hunter,false,false));
            },"native accepted playback and Shadow claims enabled on actual simulation owner thread");
        }
        internal void OnOwner(Action<Scene> apply,string label)
        {
            int serial=++_serial;
            if(Interlocked.CompareExchange(ref _mutation,new(serial,apply),null)!=null)throw new InvalidOperationException("Overlapping native fixture mutation.");
            F.Engine.Rig.Wait(()=>Volatile.Read(ref _done)==serial,label);
        }
        internal uint Next()=>_frame=Math.Max(_frame+1,Math.Max(F.Engine.Latest.World+1,F.Engine.Latest.First.InputFrame+1));
        internal FireEvent Event(uint id,uint source,BeamType weapon=BeamType.PowerBeam)
        {
            var world=F.Engine.Latest;var aim=(world.Second.Position+Vector3.UnitY*.2f-(world.First.Position+Vector3.UnitY*.5f)).Normalized();
            return new(id,source,F.A.NativeWorld,0,FireEventKind.PressFire,(byte)weapon,0,0,
                FireEvent.FlagPose,world.First.Position+Vector3.UnitY*.5f,aim,aim,aim,SourceLife:world.First.Owner.Life);
        }
        internal NativeFireWatch Track(FireEvent fire)
        {
            var owner=F.Engine.Latest.First.Owner;
            var watch=new NativeFireWatch(new(owner.AuthorityEpoch,owner.MatchId,owner.Slot,owner.Generation,owner.Life,fire.ShotId),fire);
            Volatile.Write(ref _watch,Volatile.Read(ref _watch).Append(watch).ToArray());return watch;
        }
        internal NativeFireRow Row(NativeFireWatch watch)=>Snapshot.Rows.Single(r=>r.Watch==watch);
        internal IntentPacket Carrier(uint frame,FireEvent fire)
        {
            var world=F.Engine.Latest;var owner=world.First.Owner;
            var input=new IntentPacket {MatchId=owner.MatchId,AuthorityEpoch=owner.AuthorityEpoch,SlotGeneration=owner.Generation,LifeId=owner.Life,
                Frame=frame,AckFrame=F.A.NativeWorld,Position=world.First.Position,Aim=fire.Aim,Buttons=IntentButtons.InPlayState|IntentButtons.Shoot,
                WeaponSelect=fire.Weapon,HasState=true,HasFireEvents=true,HasAnalogMove=true,HasContinuousFireTick=true,
                FireEventCount=1,ShotInputCount=2};
            input.FireEvents[0]=fire;
            input.ShotInputs[0]=ShotInputHistory.Sample(fire.Weapon,true,false,true);
            input.ShotInputs[1]=ShotInputHistory.Sample(fire.Weapon,false,false,true);
            return input;
        }
        internal void Send(IntentPacket input,CustomControlRequest? request=null)
        {
            var native=new byte[CompactIntentCodec.Size(input)];CompactIntentCodec.Write(input,native);
            if(request is {} control)
            {
                var bytes=new byte[CustomInputCarrier.PrefixSize+native.Length];
                Check(CustomInputCarrier.TryWrite(control,native,bytes,out int written)&&written==bytes.Length,"manual native event and genuine custom control encode one atomic93 carrier");
                F.Engine.First.Send(PacketType.CustomWeaponInput,bytes);
            }
            else F.Engine.First.Send(PacketType.Intent,native);
        }
        internal void Consumed(uint frame)
            =>F.Engine.Rig.Wait(()=>Snapshot.InputFrame==frame&&F.Engine.Latest.First.HasSource&&F.Engine.Latest.First.Source.Input.Frame==frame,
                "actual UDP native carrier reaches real source placement/control capture");
        internal bool Relayed(uint id)=>Relays.Any(p=>Enumerable.Range(0,p.FireEventCount).Any(i=>p.FireEvents[i].ShotId==id));
        internal HitClaimPacket Claim(NativeFireWatch watch,ushort id,BeamType beam,bool head=false)
        {
            var world=F.Engine.Latest;var owner=world.First.Owner;var victim=world.Second.Owner;
            return new() {MatchId=owner.MatchId,AuthorityEpoch=owner.AuthorityEpoch,ShooterGeneration=owner.Generation,ShooterLifeId=owner.Life,
                VictimSlot=victim.Slot,VictimGeneration=victim.Generation,VictimLifeId=victim.Life,ClaimId=id,ShotId=watch.Key.ShotId,ComponentId=1,
                Frame=watch.Fire.SourceFrame,AckFrame=watch.Fire.AckFrame,LaunchFrame=watch.Fire.AckFrame,Beam=(byte)beam,
                Flags=(byte)(HitClaimPacket.FlagDirect|(head?HitClaimPacket.FlagHeadshot:0)),Damage=(ushort)(beam==BeamType.Imperialist?(head?150:72):6),
                // Protocol Direction is an impact impulse, not the aiming ray.
                // This explicit damage-only claim asks for no restored knockback.
                HitPoint=world.Second.Position,Direction=Vector3.Zero};
        }
        internal void SendClaim(HitClaimPacket claim)
        {var bytes=new byte[1+HitClaimPacket.Size];bytes[0]=1;claim.Write(bytes.AsSpan(1));F.Engine.First.Send(PacketType.HitClaim,bytes);}
        internal void WaitAck(ushort id)=>WaitForActionInputs(F.Engine,()=>Acks.ContainsKey(id),"real native claim receives authoritative UDP combat verdict",8000);
        public void Dispose(){_sink.SetValue(null,_previous);F.Dispose();}
    }
    public static int RunCustomNativeFire(string data)
    {
        string directory=Directory.GetCurrentDirectory();_checks=0;
        var proof=NetShotProof.Mode;var timed=NetTimedShotProof.Mode;bool playback=NetFireEvents.AcceptedPlaybackEnabled,claims=NetHitClaims.Enabled;
        try
        {
            Directory.SetCurrentDirectory(Path.GetFullPath(data));Paths.UpdatePaths();Paths.ChooseMphPath();Headless.Enter();
            CustomNativeFireSelected();CustomNativeFireHistorical();CustomNativeFireClaimBeforeEvent();CustomNativeFireFutureLife();CustomNativeFireIncomingCollision();
            Console.WriteLine($"PASS {_checks} actual UDP/engine custom native-fire isolation checks.");
            Console.WriteLine("SCOPE: actual endpoint-owned UDP carriers93/native intents, real source phases, ordered Equip/Unequip, native accepted ledger/playback, recipient relay and Shadow claim verdicts. Native FireEvents/claims, loading acknowledgements, actor placement and proof resources are explicit fixtures; no source certificates or canonical payment are injected. Connection-retirement, autonomous turret/NoBeam positives, UI input and rendered clients are not claimed.");return 0;
        }
        catch(Exception ex){Console.Error.WriteLine("CUSTOM NATIVE FIRE FAIL "+ex);return 1;}
        finally{NetSession.Stop();NetShotProof.Mode=proof;NetTimedShotProof.Mode=timed;NetFireEvents.AcceptedPlaybackEnabled=playback;NetHitClaims.Enabled=claims;Directory.SetCurrentDirectory(directory);}
    }
    private static void CustomNativeFireSelected()
    {
        using var f=new NativeFireFixture();var engine=f.F.Engine;
        uint frame=f.Next();var offered=f.Track(f.Event(3,frame));var input=f.Carrier(frame,offered.Fire);
        var equip=f.F.Request(1,CustomWeaponActionKind.Equip);var before=f.Snapshot;
        f.Send(input,equip);f.Consumed(frame);
        Check(f.F.Settle(equip).Receipt.Result==WeaponActionResult.Accepted,"same93 Equip settles on the actually consumed native event source");
        engine.Rig.Wait(()=>f.Row(offered).Decision==CustomNativeFireDecision.Rejected,"pending native event is terminal rejected after same-source custom selection settles");
        var filtered=f.Snapshot;
        Check(filtered.Staged>before.Staged&&!f.Row(offered).InLedger&&!f.Row(offered).TimedProof&&!f.Row(offered).PhysicalBeam
            &&filtered.Ammo0==before.Ammo0&&filtered.Ammo1==before.Ammo1&&filtered.Health==before.Health&&filtered.DamageId==before.DamageId,
            "same-carrier pending event cannot charge native resources, enter ledger/proof or damage before custom Equip owns source");
        Check(filtered.InputFrame==frame&&(filtered.Buttons&IntentButtons.Shoot)!=0&&filtered.Position==input.Position,
            "filter preserves physical Shoot, current movement frame and actual placed body");
        Check(!f.Relayed(offered.Key.ShotId)&&f.Relays.Any(p=>p.Frame==frame&&(p.Buttons&IntentButtons.Shoot)!=0&&p.FireEventCount==0),
            "recipient native SlotIntent preserves physical Shoot but omits custom-owned FireEvent");
        f.F.WaitPending(0,"Equip application ACK leaves no private result obligation");
        var optic=f.F.Request(2,CustomWeaponActionKind.Optic,1);f.F.Reserve(optic);f.F.Fresh();
        Check(f.F.Settle(optic).Receipt.Result==WeaponActionResult.Accepted,"actual later source selects custom optic");f.F.WaitPending(0,"Optic result acknowledged");
        var fire=f.F.Request(3,CustomWeaponActionKind.Fire,1);f.F.Reserve(fire);f.F.Fresh();
        Check(f.F.Settle(fire,false).Receipt.Emission==CustomEmissionDisposition.Spawned,"independent custom sequence3 emits without aliasing rejected native shot3");
        WaitForActionInputs(engine,()=>f.F.A.HasContact(fire)&&engine.Latest.Second.Health==135,"actual private custom flight pays canonical65 once",8000);
        f.F.A.Ack(f.F.A.Receipt(fire).Ack);f.F.A.Ack(f.F.A.Contact(fire).Ack);f.F.WaitPending(0,"custom receipt/contact acknowledged");
        var paid=f.Snapshot;
        foreach(var pair in new[]{(BeamType.PowerBeam,false),(BeamType.Imperialist,true)})
        {
            ushort id=(ushort)(pair.Item1==BeamType.PowerBeam?101:102);f.SendClaim(f.Claim(offered,id,pair.Item1,pair.Item2));f.WaitAck(id);
            Check(f.Acks[id].Result==HitVerdictPacket.ResultInvalidLaunch&&f.Snapshot.Health==135
                &&f.Snapshot.DamageId==paid.DamageId&&f.Snapshot.ClaimsApplied==paid.ClaimsApplied,
                "shaped Shadow native claim under custom-owned key cannot rescue or head-correct custom paid65: "+pair.Item1);
        }
        Check(!f.Row(offered).InLedger&&!f.Row(offered).TimedProof&&!f.Relayed(offered.Key.ShotId)
            &&engine.Latest.Payments==1&&engine.Latest.Contacts==1,"rejected native key remains absent across custom payment and claim retries");
        var unequip=f.F.Request(4,CustomWeaponActionKind.Unequip);f.F.Reserve(unequip);f.F.Fresh();
        Check(f.F.Settle(unequip).Receipt.Result==WeaponActionResult.Accepted,"proper later-source Unequip returns ownership to native");f.F.WaitPending(0,"Unequip result acknowledged");
        uint nativeFrame=f.Next();var native=f.Track(f.Event(4,nativeFrame));var carrier=f.Carrier(nativeFrame,native.Fire);f.Send(carrier);f.Consumed(nativeFrame);
        WaitForActionInputs(engine,()=>f.Row(native).InLedger&&f.Row(native).Disposition==ShotDisposition.Fired&&f.Relayed(4),"fresh native event released via retained old carrier emits after accepted Unequip",8000);
        Check(f.Row(native).TimedProof&&f.Row(native).PhysicalBeam&&f.Snapshot.Staged>filtered.Staged
            &&f.Snapshot.InputFrame>=nativeFrame&&engine.Latest.Payments==1&&engine.Latest.Contacts==1,
            "native release authors actual native proof/projectile while old carrier cannot roll movement back or repay custom contact");
        WaitForActionInputs(engine,()=>f.Snapshot.Health<135,"released native projectile actually reaches canonical victim health",8000);
        var nativePaid=f.Snapshot;
        var wrongBeam=f.Claim(native,103,BeamType.Imperialist,true);
        var wrongWorld=f.Claim(native,104,BeamType.PowerBeam);wrongWorld.LaunchFrame=native.Fire.AckFrame-1;
        foreach(var claim in new[]{wrongBeam,wrongWorld})
        {
            f.SendClaim(claim);f.WaitAck(claim.ClaimId);
            Check(f.Acks[claim.ClaimId].Result==HitVerdictPacket.ResultInvalidLaunch
                &&f.Snapshot.Health==nativePaid.Health&&f.Snapshot.DamageId==nativePaid.DamageId
                &&f.Snapshot.ClaimsApplied==nativePaid.ClaimsApplied,
                "known-admitted native source rejects mismatched beam or launch-world even in Shadow: "+claim.ClaimId);
        }
        Check(engine.Latest.StepFailures==0,"native/custom isolation has no simulation step failures");
    }
    private static void CustomNativeFireHistorical()
    {
        using var f=new NativeFireFixture();uint old=f.F.Engine.Latest.First.InputFrame;var historical=f.Track(f.Event(9,old));
        f.F.Equip();uint carrierFrame=f.Next();var input=f.Carrier(carrierFrame,historical.Fire);f.Send(input);f.Consumed(carrierFrame);
        WaitForActionInputs(f.F.Engine,()=>f.Row(historical).InLedger&&f.Row(historical).Disposition==ShotDisposition.Fired&&f.Relayed(9),
            "late pre-Equip native source still emits while current source selection is custom",8000);
        Check(f.Row(historical).Decision==CustomNativeFireDecision.Released&&f.Row(historical).TimedProof&&f.Row(historical).PhysicalBeam
            &&f.F.Engine.Latest.First.State.Magazine==4&&f.F.Engine.Latest.Payments==0&&f.F.Engine.Latest.Contacts==0,
            "event source history rather than current selection preserves genuine historical native emission without custom ammo payment");
    }
    private static void CustomNativeFireIncomingCollision()
    {
        using var f=new NativeFireFixture();var engine=f.F.Engine;
        // Explicitly hold native playback so the first genuine incoming offer
        // owns its bounded bucket while the colliding datagram is admitted.
        f.OnOwner(_=>NetFireEvents.AcceptedPlaybackEnabled=false,"fixture holds actual native incoming work before turret bucket collision");
        uint frame=f.Next();var target=engine.Latest.Second.Owner;
        var turret=f.Event(1,frame,BeamType.Battlehammer) with {Kind=FireEventKind.TurretFire,
            Target=new((byte)(NetTargetIdentity.Valid|target.Slot+1),target.Generation,target.Life)};
        var first=f.Track(turret);f.Send(f.Carrier(frame,turret));f.Consumed(frame);
        engine.Rig.Wait(()=>f.Row(first).IncomingPending&&f.Row(first).Admitted==CustomNativeFireDecision.Released,
            "real UDP turret offer1 occupies native incoming bucket before any emission");
        uint secondFrame=f.Next();var second=f.Track(turret with {ShotId=129,SourceFrame=secondFrame});
        f.Send(f.Carrier(secondFrame,second.Fire));
        engine.Rig.Wait(()=>f.Snapshot.Rows.Any(row=>row.Watch==second&&row.Decision==CustomNativeFireDecision.Released),
            "later separate UDP carrier129 receives only selection permission before actual native intake refusal");
        var collision=f.Row(second);
        Check(collision.Decision==CustomNativeFireDecision.Released&&collision.Admitted==CustomNativeFireDecision.Pending
            &&!collision.IncomingPending&&!collision.InLedger&&!collision.TimedProof&&!collision.PhysicalBeam&&!f.Relayed(129),
            "permission-released turret129 cannot advertise or gain native source admission when actual incoming bucket1 refuses it");
        var before=f.Snapshot;var claim=f.Claim(second,401,BeamType.Battlehammer);f.SendClaim(claim);
        engine.Rig.Wait(()=>f.Snapshot.ClaimsReceived>before.ClaimsReceived,"shaped Shadow claim names real refused native incoming identity129");
        MaintainActionInputs(engine,150,"refused colliding native offer cannot authorize Shadow claim payment");
        Check(f.Row(first).IncomingPending&&f.Row(second).Admitted!=CustomNativeFireDecision.Released
            &&f.Snapshot.Health==before.Health&&f.Snapshot.DamageId==before.DamageId&&f.Snapshot.ClaimsApplied==before.ClaimsApplied
            &&(!f.Acks.TryGetValue(401,out var ack)||ack.Result!=HitVerdictPacket.ResultApplied),
            "native incoming refusal remains fail-closed for payment and canonical health while original bucket owner stays pending");
        f.OnOwner(_=>NetFireEvents.AcceptedPlaybackEnabled=true,"fixture restores native accepted playback after bounded collision assertions");
    }
    private static void CustomNativeFireFutureLife()
    {
        using var f=new NativeFireFixture();var engine=f.F.Engine;var old=engine.Latest.First.Owner;
        engine.RespawnFirst();engine.Refresh(engine.First);
        engine.Rig.Wait(()=>engine.Latest.First.Owner.Life!=old.Life&&engine.Latest.First.HasState,
            "real native death/respawn and fresh actual source bind the new life before malformed event test");
        uint frame=f.Next();var victim=engine.Latest.Second.Owner;
        var future=f.Event(44,frame,BeamType.Battlehammer) with {Kind=FireEventKind.TurretFire,
            Target=new((byte)(NetTargetIdentity.Valid|victim.Slot+1),victim.Generation,victim.Life)};
        var watch=f.Track(future);var carrier=f.Carrier(frame,watch.Fire);
        carrier.LifeId=old.Life;var before=f.Snapshot;f.Send(carrier);
        MaintainActionInputs(engine,150,"old carrier cannot authenticate a future-life native event");
        Check(!f.Row(watch).InLedger&&!f.Row(watch).TimedProof&&!f.Row(watch).PhysicalBeam
            &&f.Row(watch).Admitted!=CustomNativeFireDecision.Released&&!f.Relayed(44),
            "old carrier life cannot authorize future-life source event, ledger, proof or recipient relay");
        f.SendClaim(f.Claim(watch,301,BeamType.Battlehammer));
        engine.Rig.Wait(()=>f.Snapshot.ClaimsReceived>before.ClaimsReceived,"future-life native claim is received over the genuine current endpoint");
        MaintainActionInputs(engine,150,"future-life forged event cannot enable Shadow claim rescue");
        Check(f.Snapshot.Health==before.Health&&f.Snapshot.ClaimsApplied==before.ClaimsApplied
            &&(!f.Acks.TryGetValue(301,out var ack)||ack.Result!=HitVerdictPacket.ResultApplied),
            "rejected future-life event cannot grant claim authority or change canonical health");
    }
    private static void CustomNativeFireClaimBeforeEvent()
    {
        using var f=new NativeFireFixture();var engine=f.F.Engine;uint source=engine.Latest.First.InputFrame;var watch=f.Track(f.Event(12,source));
        var before=f.Snapshot;f.SendClaim(f.Claim(watch,201,BeamType.PowerBeam));
        engine.Rig.Wait(()=>f.Snapshot.ClaimsReceived>before.ClaimsReceived,"actual UDP claim reaches authority before its source event exists");
        MaintainActionInputs(engine,100,"native claim waits for exact source event without Shadow rescue");
        Console.WriteLine($"CUSTOM NATIVE CLAIM WAIT DIAG before={before} after={f.Snapshot} row={f.Row(watch)} ackPresent={f.Acks.TryGetValue(201,out var diagnosticAck)} ack={diagnosticAck} ackDescription={HitVerdictPacket.Describe(diagnosticAck.Result)} fire={watch.Fire}");
        Check(!f.Acks.ContainsKey(201)&&f.Snapshot.Health==before.Health&&f.Snapshot.ClaimsApplied==before.ClaimsApplied
            &&f.Row(watch).Decision==CustomNativeFireDecision.Pending&&!f.Row(watch).InLedger,
            "claim-before-event remains pending without proof, native payment, or health effects");
        uint frame=f.Next();f.Send(f.Carrier(frame,watch.Fire));f.Consumed(frame);
        WaitForActionInputs(engine,()=>f.Row(watch).Disposition==ShotDisposition.Fired&&f.Row(watch).InLedger,
            "later actual native event releases original pending claim source",8000);f.WaitAck(201);
        Check(f.Row(watch).Decision==CustomNativeFireDecision.Released&&f.Acks[201].Result!=HitVerdictPacket.ResultInvalidLaunch
            &&f.Snapshot.Health>=before.Health-6&&engine.Latest.Payments==0&&engine.Latest.Contacts==0,
            "pending claim proceeds through native resolution only after native source release and cannot double-pay");
    }
}
