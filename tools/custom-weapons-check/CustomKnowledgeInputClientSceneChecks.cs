using System.Collections;
using System.Reflection;
using MphRead;
using MphRead.Entities;
using MphRead.Mods;
using MphRead.Mods.CustomWeapons;
using MphRead.Mods.Input;
using MphRead.Mods.Network;

// Actual asset-backed client/NetHooks/UDP, with explicitly synthetic authenticated
// authority facts. No authority engine, pickup funding or damage is exercised.
internal static class CustomKnowledgeInputClientSceneChecks
{
    private const BindingFlags All=BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
    private static object Get(object target,string name)
        =>target.GetType().GetField(name,All)?.GetValue(target)
            ??target.GetType().GetProperty(name,All)?.GetValue(target)
            ??throw new InvalidOperationException("Missing fixture member "+name);
    private static object? Call(object target,string name,params object?[] args)
    {
        try{return target.GetType().GetMethod(name,All)!.Invoke(target,args);}
        catch(TargetInvocationException e)when(e.InnerException!=null)
        {System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException).Throw();throw;}
    }
    private sealed record Packet(PacketType Type,ulong Connection,byte[] Bytes);
    private static Packet[] Packets(object wire)
        =>((IEnumerable)Get(wire,"Sent")).Cast<object>().Select(p=>new Packet(
            (PacketType)Get(p,"Type"),(ulong)Get(p,"Connection"),(byte[])Get(p,"Payload"))).ToArray();

    internal static int Run(string data)
    {
        string cwd=Directory.GetCurrentDirectory();int checks=0;
        bool force=MapAudit.ForceEveryone,readiness=NetSession.DeveloperCustomWeaponReadiness;
        bool focus=GamepadContexts.Focused;var context=GamepadContexts.Current;
        IDisposable? fixture=null;
        try
        {
            void Check(bool value,string label)
            {if(!value)throw new InvalidOperationException(label);checks++;Console.WriteLine("KNOWLEDGE CLIENT PASS "+label);}
            NetSession.Stop();Directory.SetCurrentDirectory(Path.GetFullPath(data));
            Paths.UpdatePaths();Paths.ChooseMphPath();Headless.Enter();MapAudit.ForceEveryone=true;
            GamepadContexts.Focused=true;GamepadContexts.Current=GamepadContext.Gameplay;
            NetSession.DeveloperCustomWeaponReadiness=true;
            var type=typeof(CustomPublicInputClientSceneChecks).GetNestedType("Fixture",BindingFlags.NonPublic)!;
            fixture=(IDisposable)Activator.CreateInstance(type,All,null,Array.Empty<object>(),null)!;
            object f=fixture,wire=Get(f,"Wire");
            var scene=(Scene)Get(f,"Scene");var player=(PlayerEntity)Get(f,"Player");
            var manifest=(CustomReadinessWire)Get(f,"Manifest");var definition=(WeaponDefinition)Get(f,"Definition");
            var bootstrap=(WorldBootstrapIdentity)Get(f,"Bootstrap");
            var owner=(WeaponOwnerContext)Call(f,"Owner",(ushort)1)!;
            int initialHealth=player.Health;
            // Deterministic private nonce fixtures are authenticated authority
            // publications, never claimed evidence of an engine pickup.
            var knownA=new CustomResourceKnowledge(1,0x11223344,0x55667788);
            var knownB=new CustomResourceKnowledge(2,0x99aabbcc,0xddeeff00);
            CustomStateMessage State(uint revision,CustomResourceKnowledge knowledge)
                =>new(bootstrap,manifest.Manifest.Digest,new(10),bootstrap.AuthorityFrame+10,revision,0,
                    default,100,(WeaponStateSnapshot)Call(f,"Snapshot",(ushort)1,false,(byte)0)!,true,
                    knowledge,bootstrap.AuthorityFrame,100);
            void Until(Func<bool> condition,string label)=>Call(f,"Until",condition,label);
            void Publish(CustomStateMessage state)
            {
                var bytes=new byte[CustomStateMessage.Size];
                Check(CustomCombatResults.Valid(state,manifest.Manifest,definition),"synthetic owned knowledge state is codec-valid");
                CustomCombatResults.Write(state,bytes,manifest.Manifest,definition);
                Call(f,"Send",PacketType.CustomWeaponState,bytes);
                Until(()=>NetSession.CustomWeaponPresentationState==state,"authenticated synthetic knowledge state adopted");
            }
            void Frame(uint frame)=>typeof(NetSession).GetProperty(nameof(NetSession.NetFrame),All)!.SetValue(null,frame);
            CustomCapturedKnowledge Captured()=> (CustomCapturedKnowledge)typeof(NetHooks).GetField("_continuousKnowledge",All)!.GetValue(null)!;
            void Capture(uint frame,bool equip=false)
            {
                player.Controls.ClearAll();player.Controls.CustomWeapon.IsPressed=equip;
                Frame(frame);NetHooks.AfterInput(scene);
            }
            void Emit(uint frame){Frame(frame);NetHooks.AfterSimulation();NetSession.Pump();}
            (CustomInputEnvelope Envelope,Packet Packet) Input(uint frame)
            {
                Until(()=>Packets(wire).Any(p=>p.Type==PacketType.CustomWeaponInput
                    &&CustomInputCarrier.TryRead(p.Bytes,out var e)&&e.Intent.Frame==frame),"actual UDP input source "+frame);
                var packet=Packets(wire).First(p=>p.Type==PacketType.CustomWeaponInput
                    &&CustomInputCarrier.TryRead(p.Bytes,out var e)&&e.Intent.Frame==frame);
                Check(CustomInputCarrier.TryRead(packet.Bytes,out var envelope),"actual carrier decodes completely");
                return(envelope,packet);
            }

            Publish(State(2,knownA));Capture(302,equip:true);
            var capturedA=Captured();
            Check(capturedA==new CustomCapturedKnowledge(owner,bootstrap,manifest.Manifest.Digest,knownA,302),
                "AfterInput stamps full owner/bootstrap/manifest/source302 and knowledge A at actual capture");
            var requestA=(CustomControlRequest)Call(f,"Request",1u,(ushort)1)!;
            Check(requestA.Owner==owner&&requestA.Bootstrap==bootstrap&&requestA.Manifest==manifest.Manifest.Digest
                &&requestA.Weapon.Value==10&&requestA.Sequence==1&&requestA.Kind==CustomWeaponActionKind.Equip
                &&requestA.Knowledge==knownA,"real C edge creates canonical nonce1 carrying original knowledge A");

            // The real UDP pump runs between the two production hooks. Restore
            // the declared synthetic sample clock after Pump before emission.
            Publish(State(3,knownB));
            Check(NetSession.CustomWeaponPresentationState?.Knowledge==knownB&&Captured()==capturedA,
                "new authority knowledge B cannot rewrite an already captured native sample");
            Emit(302);var a=Input(302);
            Check(a.Envelope.Knowledge==knownA&&a.Envelope.Control==requestA
                &&a.Envelope.Bootstrap==bootstrap&&a.Envelope.Manifest==manifest.Manifest.Digest
                &&a.Envelope.Intent.MatchId==owner.MatchId&&a.Envelope.Intent.AuthorityEpoch==owner.AuthorityEpoch
                &&a.Envelope.Intent.SlotGeneration==owner.Generation&&a.Envelope.Intent.LifeId==owner.Life,
                "actual93 source302 emits captured A plus exact nonce1 despite current private knowledge B");

            Capture(304);var capturedB=Captured();Emit(304);var b=Input(304);
            Check(capturedB==new CustomCapturedKnowledge(owner,bootstrap,manifest.Manifest.Digest,knownB,304)
                &&b.Envelope.Knowledge==knownB&&b.Envelope.Control==requestA,
                "fresh source304 carries B independently while pending original control remains knowledge A");
            var client=(CustomClientCombat)typeof(NetSession).GetField("_customCombatClient",All)!.GetValue(null)!;
            Check(client.TryInputControl(owner,NetSession.Clock,out var retained)&&retained==requestA,
                "input carriage and newer state never remove or retag reliable pending control");
            Check(Packets(wire).Where(p=>p.Type==PacketType.CustomWeaponControl).All(p=>
                CustomControlRequest.TryRead(p.Bytes,out var r)&&r==requestA),
                "every actual reliable91 retry preserves the full immutable request including knowledge A");

            Call(f,"Deny",requestA,1ul);
            Check(NetSession.LastCustomControlDenied?.Request==requestA
                &&!client.TryInputControl(owner,NetSession.Clock,out _),
                "authenticated typed denial matches original knowledge A and clears its exact pending request");
            Capture(306);Emit(306);var c=Input(306);
            Check(c.Envelope.Knowledge==knownB&&c.Envelope.Control==null&&c.Packet.Bytes[65]==0
                &&c.Packet.Bytes.Length==CustomInputCarrier.EmptyPrefixSize+
                    System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(c.Packet.Bytes.AsSpan(90)),
                "source306 emits input-only92-byte-prefix knowledge B without a new control edge");

            var native=c.Packet.Bytes.AsSpan(CustomInputCarrier.EmptyPrefixSize).ToArray();
            var capture=new CustomCapturedKnowledge(owner,bootstrap,manifest.Manifest.Digest,knownB,306);
            Check(NetSession.TryEncodeCustomWeaponInput(c.Envelope.Intent,native,out var encoded,capture)
                &&encoded.Span.SequenceEqual(c.Packet.Bytes),"production encoder preserves exact captured input/native bytes");
            var changedOwner=new WeaponOwnerContext(owner.MatchId,owner.AuthorityEpoch,owner.Slot,owner.Generation,(ushort)(owner.Life+1));
            var wrongManifest=manifest.Manifest.Digest with{A=manifest.Manifest.Digest.A^1};
            foreach(var stale in new[]{capture with{Owner=changedOwner},capture with{Bootstrap=bootstrap with{Revision=bootstrap.Revision+1}},
                capture with{Manifest=wrongManifest},capture with{SourceFrame=308}})
                Check(!NetSession.TryEncodeCustomWeaponInput(c.Envelope.Intent,native,out var refused,stale)&&refused.IsEmpty,
                    "mismatched captured owner/bootstrap/manifest/source is refused without an encoded output");

            Check(NetSession.TrySubmitCustomWeaponControl(CustomWeaponActionKind.Equip,0,out var requestB)
                &&requestB.Owner==owner&&requestB.Sequence==2&&requestB.Knowledge==knownB,
                "fresh producer request2 captures B after exact request1 denial");
            byte[] NativeBytes(IntentPacket intent)
            {var bytes=new byte[CompactIntentCodec.Size(intent)];CompactIntentCodec.Write(intent,bytes);return bytes;}
            bool sawOmission=false,sawTooLarge=false;IntentPacket omittedIntent=default;byte[] omittedNative=Array.Empty<byte>();
            // Synthetic native ReleaseFire history is codec data only. It is
            // never sent, admitted, replayed or applied to an engine actor.
            for(int n=0;n<=NetFireEvents.LiveCapacity;n++)
            {
                var candidate=c.Envelope.Intent;candidate.NativeTouch=default;
                candidate.HasFireEvents=true;candidate.FireEvents=default;candidate.FireEventCount=(byte)n;
                for(int i=0;i<n;i++)candidate.FireEvents[i]=new((uint)i+1,candidate.Frame,candidate.AckFrame,0,
                    FireEventKind.ReleaseFire,(byte)BeamType.PowerBeam,0,0,SourceLife:owner.Life);
                var payload=NativeBytes(candidate);
                Check(CompactIntentCodec.TryRead(payload,out var decoded)&&decoded.FireEventCount==n,
                    "synthetic native event history remains independently codec-valid at count "+n);
                if(payload.Length>CustomInputCarrier.MaxPayload-CustomInputCarrier.PrefixSize
                    &&payload.Length<=CustomInputCarrier.MaxPayload-CustomInputCarrier.EmptyPrefixSize)
                {
                    Check(NetSession.TryEncodeCustomWeaponInput(candidate,payload,out var reduced,capture)
                        &&CustomInputCarrier.TryRead(reduced.Span,out var envelope)&&envelope.Control==null
                        &&envelope.Knowledge==knownB&&envelope.Intent.Frame==candidate.Frame
                        &&reduced.Span[CustomInputCarrier.EmptyPrefixSize..].SequenceEqual(payload),
                        "production MTU fallback omits only repeated control and preserves B plus every native byte");
                    Check(client.TryInputControl(owner,NetSession.Clock,out var pendingB)&&pendingB==requestB,
                        "MTU fallback leaves exact reliable B request pending with original nonce and identity");
                    sawOmission=true;omittedIntent=candidate;omittedNative=payload;
                }
                else if(payload.Length>CustomInputCarrier.MaxPayload-CustomInputCarrier.EmptyPrefixSize)
                {
                    Check(!NetSession.TryEncodeCustomWeaponInput(candidate,payload,out var tooLarge,capture)&&tooLarge.IsEmpty,
                        "native payload beyond input-only MTU returns false for intact ordinary native fallback");
                    sawTooLarge=true;
                }
            }
            Check(sawOmission&&sawTooLarge,"valid native histories exercise both production MTU boundaries");
            foreach(var malformed in new[]{omittedNative[..^1],omittedNative.Concat(new byte[1]).ToArray()})
                Check(!NetSession.TryEncodeCustomWeaponInput(omittedIntent,malformed,out var malformedOutput,capture)&&malformedOutput.IsEmpty,
                    "malformed native history cannot become accepted through the control-omission fallback");
            var wrongLife=omittedIntent;wrongLife.LifeId++;
            var wrongSource=omittedIntent;wrongSource.Frame++;
            foreach(var wrong in new[]{wrongLife,wrongSource})
                Check(!NetSession.TryEncodeCustomWeaponInput(omittedIntent,NativeBytes(wrong),out var mismatchedOutput,capture)&&mismatchedOutput.IsEmpty,
                    "independent native bytes cannot substitute another life or source during MTU fallback");
            Check(NetSession.CustomWeaponPresentationState==State(3,knownB)&&player.Health==initialHealth
                &&NetSession.CustomWeaponPresentationState?.State.Magazine==4&&NetSession.CustomWeaponPresentationState?.State.Reserve==16,
                "capture, transport, state replacement and no-source result cannot mutate canonical ammo or HP");
            Check(!(bool)Get(wire,"Overflow")&&Get(f,"Server") is DedicatedServer{Simulating:false},
                "bounded proxy completes with a control-plane authority and no remote combat engine");
            Console.WriteLine($"PASS {checks} knowledge input client boundary checks.");
            Console.WriteLine("SCOPE: actual asset actor, public C input router, NetHooks capture/send and authenticated91/93 UDP. Native snapshot, public baseline, private ownership/knowledge states and typed denial are explicit synthetic authority facts. Source frames302/304/306 are explicit client-clock seams. MTU tests call the production encoder with synthetic codec-only native event history; those events are never sent or fired. No pickup funding, authority admission, damage, physical input device or DedicatedServer combat E2E is claimed.");
            return 0;
        }
        catch(Exception e){Console.Error.WriteLine("KNOWLEDGE CLIENT FAIL "+e);return 1;}
        finally
        {
            fixture?.Dispose();NetSession.Stop();NetSession.DeveloperCustomWeaponReadiness=readiness;
            MapAudit.ForceEveryone=force;GamepadContexts.Focused=focus;GamepadContexts.Current=context;
            Directory.SetCurrentDirectory(cwd);
        }
    }
}
