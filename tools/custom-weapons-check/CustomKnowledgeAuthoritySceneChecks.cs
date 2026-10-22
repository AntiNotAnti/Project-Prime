using System.Reflection;
using MphRead;
using MphRead.Mods;
using MphRead.Mods.CustomWeapons;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

// Actual dedicated UDP -> accepted carrier -> engine observation. Existing
// fixture native load ACKs, developer 4/16 loadout, actor lane/health placement,
// authored native source/ACK/body/aim and synthetic tokens are explicit seams.
// No pickup funding, knowledge admission, custom action or damage is exercised.
internal static class CustomKnowledgeAuthoritySceneChecks
{
    private const BindingFlags All=BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
    private sealed record Watch(uint First,uint Second);
    private sealed record Observation(uint World,WeaponOwnerContext Owner,uint CurrentSource,uint SourceFrontier,uint ClockWorld,
        bool HasFirst,CustomEngineObservation First,bool HasSecond,CustomEngineObservation Second,
        bool HasState,WeaponStateSnapshot State,uint Settled,uint Committed,
        int Health,int OtherHealth,ulong Emissions,ulong Payments,ulong Contacts,long Failures);
    private static object Get(object instance,string name)
        =>instance.GetType().GetField(name,All)?.GetValue(instance)
            ??instance.GetType().GetProperty(name,All)?.GetValue(instance)
            ??throw new InvalidOperationException("Missing knowledge fixture member "+name);
    private static object? Call(object instance,string name,params object?[] args)
    {
        var method=instance.GetType().GetMethods(All).Single(value=>value.Name==name);
        var parameters=method.GetParameters();var values=new object?[parameters.Length];
        for(int index=0;index<values.Length;index++)values[index]=index<args.Length?args[index]:parameters[index].DefaultValue;
        try{return method.Invoke(instance,values);}
        catch(TargetInvocationException error) when(error.InnerException!=null)
        {System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw();throw;}
    }
    internal static int Run(string data)
    {
        string cwd=Directory.GetCurrentDirectory();bool force=MapAudit.ForceEveryone,developer=NetSession.DeveloperCustomWeaponReadiness;
        object? fixture=null,secondClient=null;FieldInfo? sinkField=null,packetField=null;
        SnapshotSink? previousSink=null;Action<ReceivedPacket>? previousPacket=null;
        int retiring=0,checks=0;Observation? observation=null;Watch? watch=null;byte[]? relayFirst=null,relaySecond=null;
        try
        {
            Directory.SetCurrentDirectory(Path.GetFullPath(data));Paths.UpdatePaths();Paths.ChooseMphPath();
            Headless.Enter();MapAudit.ForceEveryone=true;
            void Check(bool condition,string label)
            {if(!condition)throw new InvalidOperationException(label);checks++;Console.WriteLine("KNOWLEDGE AUTHORITY PASS "+label);}
            var type=typeof(NetLobbyTest).GetNestedType("ActionTransportFixture",BindingFlags.NonPublic)
                ??throw new InvalidOperationException("Dedicated action fixture is missing.");
            var constructor=type.GetConstructors(All).Single();var parameters=constructor.GetParameters();
            var arguments=parameters.Select(value=>value.DefaultValue).ToArray();
            for(int index=0;index<parameters.Length;index++)
            {
                if(parameters[index].Name=="completeRoom")arguments[index]=true;
                if(parameters[index].Name=="publicAvailability")arguments[index]=false;
            }
            fixture=constructor.Invoke(arguments);
            var rig=Get(fixture,"Rig");var server=(DedicatedServer)Get(rig,"Server");
            var first=Get(fixture,"First");secondClient=Get(fixture,"Second");
            int firstSlot=(int)Get(first,"Slot"),secondSlot=(int)Get(secondClient,"Slot");
            var scene=(Scene)Get(fixture,"_scene");var sim=(ServerSim)Get(fixture,"_sim");
            var ready=(CustomWeaponReady)Get(Get(fixture,"FirstReady"),"Ready");
            var serverTransport=(NetTransport)typeof(DedicatedServer).GetField("_transport",All)!.GetValue(server)!;
            long ReceivedInputs()=>serverTransport.Wire.Capture().Categories.Single(value=>value.Kind==NetTrafficKind.Intent).RxPackets;
            void Wait(Func<bool> condition,string label,int milliseconds=6000)=>Call(rig,"Wait",condition,label,milliseconds);
            sinkField=typeof(NetSession).GetField("_snapshotSink",All)!;previousSink=(SnapshotSink)sinkField.GetValue(null)!;
            SnapshotSink observer=payload=>
            {
                previousSink(payload);
                if(Volatile.Read(ref retiring)!=0||scene.CustomCombat is not {IsEligible:true} root)return;
                var player=scene.Players.Items[firstSlot];
                var owner=new WeaponOwnerContext(NetSession.CurrentMatchId,NetSession.AuthorityEpoch,(byte)firstSlot,
                    NetPlayerLifecycle.Generation(firstSlot),NetPlayerLifecycle.Get(firstSlot));
                var current=Volatile.Read(ref watch);CustomEngineObservation a=default,b=default;
                bool hasA=current is {First:>0}&&root.TryGetSourceObservation(owner,current.First,out a);
                bool hasB=current is {Second:>0}&&root.TryGetSourceObservation(owner,current.Second,out b);
                bool state=root.TryState(player,out var resources);root.TryStateMetadata(player,out _,out uint settled,out uint committed);
                var life=((Array)Get(root,"_current")).GetValue(firstSlot);
                if(life==null||Get(life,"Journal") is not CustomSourceActionJournal journal)return;
                uint clock=(uint)Get(Get(journal,"_clock"),"_worldFrontier");
                Volatile.Write(ref observation,new(NetSession.NetFrame,owner,NetSession.RemoteIntents[firstSlot].Frame,
                    journal.ObservedSourceFrame,clock,hasA,a,hasB,b,state,resources,settled,committed,
                    player.Health,scene.Players.Items[secondSlot].Health,root.AcceptedEmissions,root.PositivePayments,
                    root.TerminalContacts,sim.StepFailures));
            };
            sinkField.SetValue(null,observer);
            packetField=secondClient.GetType().GetField("PacketReceived",All)!;
            previousPacket=(Action<ReceivedPacket>?)packetField.GetValue(secondClient);
            packetField.SetValue(secondClient,(Action<ReceivedPacket>)(packet=>
            {
                previousPacket?.Invoke(packet);
                if(packet.Type!=PacketType.SlotIntent||packet.Payload.Length<=1||packet.Payload[0]!=firstSlot
                    ||!CompactIntentCodec.TryRead(packet.Payload[1..],out var native))return;
                var current=Volatile.Read(ref watch);
                if(current?.First==native.Frame)Volatile.Write(ref relayFirst,packet.Payload[1..].ToArray());
                if(current?.Second==native.Frame)Volatile.Write(ref relaySecond,packet.Payload[1..].ToArray());
            }));
            Wait(()=>Volatile.Read(ref observation) is {HasState:true},"owner-thread snapshot observer starts after developer life binding");
            var baseline=Volatile.Read(ref observation)!;
            Check(server.DeveloperCustomWeaponReadiness&&server.DeveloperCustomWeaponCombat
                &&baseline.State.Magazine==4&&baseline.State.Reserve==16&&baseline.State.Phase==CustomWeaponPhase.Unequipped,
                "declared default developer fixture supplies existing four/sixteen unequipped inventory");
            var stampA=new CustomResourceKnowledge(7,0x1111222233334444UL,0x5555666677778888UL);
            var stampB=new CustomResourceKnowledge(8,0x9999aaaabbbbccccUL,0xddddeeeeffff1234UL);
            uint sourceA=Math.Max(baseline.World+1,baseline.CurrentSource+1);
            Volatile.Write(ref watch,new(sourceA,0));
            byte[] Native(uint source)
            {
                var world=Get(fixture,"Latest");var actor=Get(world,"First");var target=Get(world,"Second");
                var owner=(WeaponOwnerContext)Get(actor,"Owner");var body=(Vector3)Get(actor,"Position");
                var aim=((Vector3)Get(target,"Position")+Vector3.UnitY*.2f-(body+Vector3.UnitY*.4f)).Normalized();
                uint ack=(uint)Get(Get(fixture,"A"),"SnapshotWorld");
                if(ack==0)throw new InvalidOperationException("Native snapshot ACK is unavailable.");
                var intent=new IntentPacket{MatchId=owner.MatchId,AuthorityEpoch=owner.AuthorityEpoch,
                    SlotGeneration=owner.Generation,LifeId=owner.Life,Frame=source,AckFrame=ack,Position=body,Aim=aim,
                    Buttons=IntentButtons.InPlayState,WeaponSelect=(byte)BeamType.PowerBeam,HasState=true,HasFireEvents=true,
                    HasAnalogMove=true,HasContinuousFireTick=true};
                var bytes=new byte[CompactIntentCodec.Size(intent)];CompactIntentCodec.Write(intent,bytes);return bytes;
            }
            void Send(byte[] native,CustomResourceKnowledge stamp)
            {
                var bytes=new byte[CustomInputCarrier.EmptyPrefixSize+native.Length];
                if(!CustomInputCarrier.TryWrite(ready.Bootstrap,ready.Manifest,stamp,null,native,bytes,out int size)||size!=bytes.Length)
                    throw new InvalidOperationException("Knowledge-only native envelope fixture cannot encode.");
                Call(first,"Send",PacketType.CustomWeaponInput,bytes);
            }
            var nativeA=Native(sourceA);Send(nativeA,stampA);
            Wait(()=>Volatile.Read(ref observation) is {HasFirst:true}&&Volatile.Read(ref relayFirst) is { } bytes&&bytes.SequenceEqual(nativeA),
                "actual UDP knowledge-only carrier reaches engine observation and unchanged native relay");
            var firstObserved=Volatile.Read(ref observation)!;
            Check(firstObserved.First.Input.Knowledge==stampA&&firstObserved.First.Input.IngressWorld!=0
                &&firstObserved.First.Input.Owner==baseline.Owner&&firstObserved.First.Input.Frame==sourceA
                &&unchecked((int)(firstObserved.First.ObservedWorldFrame-firstObserved.First.Input.IngressWorld))>=0,
                "engine observation restores genuine accepted-carrier stampA and ingress world from metadata-free engine candidate");
            long duplicateRx=ReceivedInputs();uint duplicateWorld=firstObserved.World;
            Send(nativeA,stampB);
            Wait(()=>ReceivedInputs()>duplicateRx&&Volatile.Read(ref observation) is {} value&&value.World>=duplicateWorld+3,
                "duplicate changed-stamp datagram reaches server ingress and subsequent owner snapshots");
            var duplicate=Volatile.Read(ref observation)!;
            Check(duplicate.HasFirst&&duplicate.First==firstObserved.First&&duplicate.CurrentSource==sourceA
                &&duplicate.SourceFrontier==firstObserved.SourceFrontier&&duplicate.ClockWorld==firstObserved.ClockWorld,
                "same source with stampB cannot replace first stamp/time, source frontier or source-clock world");
            uint sourceB=Math.Max(duplicate.World+1,sourceA+1);Volatile.Write(ref watch,new(sourceA,sourceB));
            var nativeB=Native(sourceB);Send(nativeB,stampB);
            Wait(()=>Volatile.Read(ref observation) is {HasFirst:true,HasSecond:true}
                &&Volatile.Read(ref relaySecond) is {} bytes&&bytes.SequenceEqual(nativeB),
                "next genuine native source captures stampB and relays its native payload intact");
            var final=Volatile.Read(ref observation)!;
            Check(final.First==firstObserved.First&&final.Second.Input.Knowledge==stampB
                &&final.Second.Input.IngressWorld>=final.First.Input.IngressWorld
                &&final.CurrentSource==sourceB&&final.SourceFrontier==sourceB,
                "fresh sourceB has independent accepted stamp/time while retained sourceA stays immutable");
            Check(Volatile.Read(ref relayFirst)!.SequenceEqual(nativeA)&&Volatile.Read(ref relaySecond)!.SequenceEqual(nativeB)
                &&CompactIntentCodec.TryRead(Volatile.Read(ref relayFirst)!,out var relayedA)&&relayedA.FireEventCount==0
                &&CompactIntentCodec.TryRead(Volatile.Read(ref relaySecond)!,out var relayedB)&&relayedB.FireEventCount==0,
                "second endpoint receives only original compact native bytes, with no private knowledge extension or invented fire");
            Check(final.State.Magazine==4&&final.State.Reserve==16&&final.State.Phase==CustomWeaponPhase.Unequipped
                &&final.State.LastAcceptedShotId==0&&final.Settled==baseline.Settled&&final.Committed==baseline.Committed
                &&final.Health==baseline.Health&&final.OtherHealth==baseline.OtherHealth
                &&final.Emissions==baseline.Emissions&&final.Payments==baseline.Payments&&final.Contacts==baseline.Contacts
                &&final.Failures==0,"input-only knowledge carriage cannot select/pay/emit custom fire or change either actor's health");
            Console.WriteLine($"KNOWLEDGE AUTHORITY SCENE PASS: {checks} assertions; sourceA={sourceA}/ingress{final.First.Input.IngressWorld}, sourceB={sourceB}/ingress{final.Second.Input.IngressWorld}.");
            Console.WriteLine("SCOPE: real authenticated UDP NewCurrentCarrier and owner-thread engine metadata restoration; developer loadout/native ACK/body/aim setup and tokens synthetic. No pickup funding, knowledge admission, custom action, device or damage qualification.");
            return 0;
        }
        catch(Exception error){Console.Error.WriteLine("KNOWLEDGE AUTHORITY SCENE FAIL "+error);return 1;}
        finally
        {
            Volatile.Write(ref retiring,1);
            if(packetField!=null&&secondClient!=null)packetField.SetValue(secondClient,previousPacket);
            if(sinkField!=null&&previousSink!=null)sinkField.SetValue(null,previousSink);
            (fixture as IDisposable)?.Dispose();NetSession.Stop();
            MapAudit.ForceEveryone=force;NetSession.DeveloperCustomWeaponReadiness=developer;Directory.SetCurrentDirectory(cwd);
        }
    }
}
