using System.Net;
using System.Reflection;
using System.Text.Json;
using MphRead;
using MphRead.Mods.Network;

internal static class PartyProtocolAcceptance
{
    internal static int Run()
    {
        int checks=0;void Check(bool value,string name){if(!value)throw new Exception(name);++checks;}
        using(var input=Assembly.GetExecutingAssembly().GetManifestResourceStream("packet-registry-baseline.json")!)
        using(var baseline=JsonDocument.Parse(input))
        {
            Check(NetConfig.ProtocolVersion==75,"actual DLL uses protocol 75");
            foreach(var old in baseline.RootElement.GetProperty("packet_types").EnumerateObject())
                Check(Enum.TryParse<PacketType>(old.Name,out var type)&&(byte)type==old.Value.GetInt32(),"old packet registry "+old.Name);
            var approved = new (string Name, byte Id)[]
            {
                ("PartyReservationRequest",80), ("PartyReservationState",81), ("PartyReservationAction",82),
                ("CustomWeaponManifest",83), ("CustomWeaponReady",84), ("CustomWeaponReadyConfirmed",85),
                ("CustomWeaponAction",86), ("CustomWeaponReceipt",87), ("CustomWeaponContact",88),
                ("CustomWeaponResultAck",89), ("CustomWeaponState",90),
                ("CustomWeaponControl",91), ("CustomWeaponControlDenied",92), ("CustomWeaponInput",93),
                ("CustomWeaponReplicaPage",94), ("CustomWeaponReplicaEvent",95), ("CustomWeaponReplicaAck",96),
                ("CustomWeaponPickupState",97), ("CustomWeaponPickupAck",98)
            };
            foreach (var added in approved)
                Check(Enum.TryParse<PacketType>(added.Name,out var type) && (byte)type == added.Id,
                    "explicitly approved packet identity " + added.Name);
            var expected = baseline.RootElement.GetProperty("packet_types").EnumerateObject().Select(p => p.Name)
                .Concat(approved.Select(p => p.Name)).ToHashSet(StringComparer.Ordinal);
            Check(Enum.GetNames<PacketType>().ToHashSet(StringComparer.Ordinal).SetEquals(expected),
                "only explicitly approved registry names added");
            Check((byte)PacketType.PartyReservationRequest==80&&(byte)PacketType.PartyReservationState==81&&(byte)PacketType.PartyReservationAction==82,"actual DLL exact new IDs");
            Check((byte)PacketType.CustomWeaponManifest==83&&(byte)PacketType.CustomWeaponReady==84&&(byte)PacketType.CustomWeaponReadyConfirmed==85,"upstream custom readiness IDs retained");
        }
        Check(MatchStatePacket.Size==107&&ServerStatusPacket.Size==141&&ServerStatusPacket.SizeWithFlags==150&&ServerStatusPacket.SizeWithWaitlist==153,"protocol 60 status offsets follow widened match rules");
        var packet=new ServerStatusPacket{Match=new(){MatchId=42,AuthorityEpoch=ulong.MaxValue,RoomKey=""},Protocol=57,
            MaxPlayers=8,Flags=ServerStatusPacket.FlagCanHost,Phase=SessionPhase.Lobby,LobbyEnabled=true,WaitlistSupported=true};
        var oldBytes=new byte[ServerStatusPacket.SizeWithWaitlist];packet.Write(oldBytes);
        packet.Flags|=ServerStatusPacket.FlagPartyReservationsV1;var newBytes=new byte[oldBytes.Length];packet.Write(newBytes);
        Check(Enumerable.Range(0,newBytes.Length).Where(i=>newBytes[i]!=oldBytes[i]).SequenceEqual(new[]{ServerStatusPacket.Size}),"capability changes only existing optional flag byte");
        Check(ServerStatusPacket.Read(newBytes).Flags==3&&ServerStatusPacket.Read(oldBytes.AsSpan(0,ServerStatusPacket.Size)).Flags==0,"new and old optional status decoding");
        var legacyHello=Convert.FromHexString("3939040302010807060504030201");
        Check(!QueueHelloPacket.TryRead(legacyHello,out _),"literal protocol57 QueueHello refused by current live admission");
        var previousHello=Convert.FromHexString("393A040302010807060504030201");
        Check(!QueueHelloPacket.TryRead(previousHello,out _),"literal protocol58 QueueHello refused by current live admission");
        var movementV1Hello=Convert.FromHexString("393B040302010807060504030201");
        Check(!QueueHelloPacket.TryRead(movementV1Hello,out _),"literal protocol59 QueueHello refused by current live admission");
        foreach(string priorHex in new[]{"393C040302010807060504030201","393D040302010807060504030201"})
        {
            var priorBytes=Convert.FromHexString(priorHex);
            Check(!QueueHelloPacket.TryRead(priorBytes,out _),"prior Quake III QueueHello refused by exact live protocol");
        }
        var protocol62Hello=Convert.FromHexString("393E040302010807060504030201");
        Check(!QueueHelloPacket.TryRead(protocol62Hello,out _),"literal protocol62 QueueHello refused by current live admission");
        Check(!QueueHelloPacket.TryRead(Convert.FromHexString("393F040302010807060504030201"),out _),"literal protocol63 QueueHello refused by current live admission");
        Check(!QueueHelloPacket.TryRead(Convert.FromHexString("3947040302010807060504030201"),out _),"literal protocol71 QueueHello refused by protocol75 live admission");
        Check(!QueueHelloPacket.TryRead(Convert.FromHexString("3948040302010807060504030201"),out _),"literal protocol72 QueueHello refused by protocol75 live admission");
        Check(!QueueHelloPacket.TryRead(Convert.FromHexString("3949040302010807060504030201"),out _),"literal protocol73 QueueHello refused by protocol75 live admission");
        Check(!QueueHelloPacket.TryRead(Convert.FromHexString("394C040302010807060504030201"),out _),"literal future76 QueueHello refused by protocol75 live admission");
        Check(!QueueHelloPacket.TryRead(Convert.FromHexString("394A040302010807060504030201"),out _),"literal protocol74 QueueHello refused by protocol75 live admission");
        var hello=new QueueHelloPacket(NetConfig.ProtocolVersion,0x01020304,0x0102030405060708);var h=new byte[QueueHelloPacket.Size];hello.Write(h);
        Check(Convert.ToHexString(h)=="39"+NetConfig.ProtocolVersion.ToString("X2")+"040302010807060504030201"&&h.AsSpan(2).SequenceEqual(legacyHello.AsSpan(2))&&QueueHelloPacket.TryRead(h,out _),"current QueueHello preserves historical payload bytes and changes only its version byte");
        var join=new QueueJoinPacket(0x0102030405060708);var j=new byte[QueueJoinPacket.Size];join.Write(j);
        Check(Convert.ToHexString(j)=="3B0807060504030201","legacy QueueJoin literal bytes unchanged");
        var request=new PartyReservationRequestPacket(1,PartyReservationPurpose.MemberAttach,1,uint.MaxValue,2,
            Guid.NewGuid().ToString("D"),Guid.NewGuid().ToString("D"),9007199254740991,Guid.NewGuid().ToString("D"),1000000,
            ushort.MaxValue,ulong.MaxValue,"pp1."+new string('x',764));
        var bytes=new byte[request.EncodedSize];request.Write(bytes);
        Check(bytes.Length==929&&NetHeader.Size+4+bytes.Length==957&&PartyReservationRequestPacket.TryRead(bytes,out var actual)&&actual==request,"actual DLL maximum codec and MTU");
        Check(!PartyReservationRequestPacket.TryRead(bytes.AsSpan(0,bytes.Length-1),out _)
            &&!PartyReservationRequestPacket.TryRead(bytes.Concat(new byte[]{0}).ToArray(),out _),"actual DLL truncated/trailing refusal");
        var queueAllowed=typeof(NetTransport).GetMethod("QueueInboundAllowed",BindingFlags.Static|BindingFlags.NonPublic)!;
        var unsequenced=typeof(NetTransport).GetMethod("Unsequenced",BindingFlags.Static|BindingFlags.NonPublic)!;
        var serverSide=new NetConnection(new IPEndPoint(IPAddress.Loopback,1234),1,1){QueueOnly=true,QueueServerSide=true};
        var clientSide=new NetConnection(new IPEndPoint(IPAddress.Loopback,1235),2,2){QueueOnly=true,QueueServerSide=false};
        foreach(var type in new[]{PacketType.PartyReservationRequest,PacketType.PartyReservationState,PacketType.PartyReservationAction})
        {
            Check(NetReliableChannel.IsReliable(type)&&NetReliableChannel.IsCritical(type)&&NetPacketQueue.Priority(type)==NetPacketPriority.Critical,"actual reliable critical registration "+type);
            Check(!(bool)unsequenced.Invoke(null,new object[]{type})!,"no new bootstrap lane "+type);
            Check((bool)queueAllowed.Invoke(null,new object[]{serverSide,type})! == (type!=PacketType.PartyReservationState),"actual server queue direction "+type);
            Check((bool)queueAllowed.Invoke(null,new object[]{clientSide,type})! == (type==PacketType.PartyReservationState),"actual client queue direction "+type);
        }
        try
        {
            NetSession.StartPlayback();var previous=NetSession.EvidenceTransport;int fences=0;
            bool connected=NetLaunch.Connect("127.0.0.1",27015,"SYNTHETIC",Hunter.Samus,
                initializeFence:initialize=>{++fences;return false;});
            Check(!connected&&fences==1&&NetSession.IsClient&&ReferenceEquals(NetSession.EvidenceTransport,previous),"actual denied initialization fence preserves existing session without socket or pump");
            // Populate every other leader prerequisite while retaining the
            // socket-free transport, so the two refusal guards are independent.
            typeof(NetSession).GetProperty(nameof(NetSession.ServerSession))!.SetValue(null,
                new SessionStatePacket {Policy=ServerSessionPolicy.Lobby,Phase=SessionPhase.Lobby,
                    MatchId=42,AuthorityEpoch=100});
            typeof(NetSession).GetField("_hostEndPoint",BindingFlags.Static|BindingFlags.NonPublic)!
                .SetValue(null,new IPEndPoint(IPAddress.Loopback,27015));
            typeof(NetSession).GetProperty(nameof(NetSession.LocalSlot))!.SetValue(null,0);
            var leader=request with {Purpose=PartyReservationPurpose.LeaderCreate,QueueNonce=0,ClientId=NetSession.ClientId,
                MatchId=42,AuthorityEpoch=100};
            object witness=new();
            Check(leader.Validate(),"synthetic accepted leader request satisfies actual schema before negative role tests");
            Check(!NetSession.BeginNativePartyReservation(leader,witness),"actual replay role cannot send a leader reservation");
            typeof(NetSession).GetField("_playback",BindingFlags.Static|BindingFlags.NonPublic)!.SetValue(null,false);
            typeof(NetSession).GetProperty(nameof(NetSession.LocalSlot))!.SetValue(null,-1);
            Check(!NetSession.BeginNativePartyReservation(leader,witness),"actual unadmitted client cannot send a leader reservation");
            typeof(NetSession).GetProperty(nameof(NetSession.LocalSlot))!.SetValue(null,0);
            Check(NetSession.BeginNativePartyReservation(leader,witness),"synthetic accepted client reaches existing leader reservation owner");
        }
        finally{NetSession.Stop();}
        Console.WriteLine($"PASS {checks} actual-DLL registry/codec/status/transport-registration/initialization-fence checks; no UDP/backend/assets");
        return checks;
    }
}
