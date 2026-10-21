using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Reflection;
using System.Text.Json;
using MphRead;
using MphRead.Mods.CustomWeapons;
using MphRead.Mods.Movement;
using MphRead.Mods.Network;

// Pure configuration/wire contracts only. Protocol70 availability does not
// prove engine grants, client readiness, public controls or canonical damage.
internal static class CustomMatchPolicyChecks
{
    private static MatchDefinition Native=>new()
    {
        RoomKey="MP1 SANCTORUS",Mode=GameMode.Battle,TimeLimitSeconds=300,PointGoal=7,
        FriendlyFire=true,SpawnProtection=true
    };
    private static SessionStatePacket Session(MatchDefinition match)=>new()
    {
        Phase=SessionPhase.Lobby,Policy=ServerSessionPolicy.Lobby,Revision=11,MatchId=7,
        AuthorityEpoch=9,OwnerSlot=255,MaxPlayers=8,
        RuleFlags=LobbyRuleFlags.RequireReady|LobbyRuleFlags.AllowJoinInProgress,Match=match
    };
    private static byte[] Bytes(MatchDefinition match)
    {var bytes=new byte[SessionStatePacket.Size];Session(match).Write(bytes);return bytes;}
    internal static void Run(Action<bool,string> check)
    {
        DefaultsAndDto(check);NativeWire(check);AvailabilityBit(check);ReplayRefusal(check);FrozenAvailability(check);RecordingAdmission(check);Policy(check);
    }
    private static void DefaultsAndDto(Action<bool,string> check)
    {
        check(!default(MatchDefinition).RiftlineM7Available&&!new MatchDefinition().RiftlineM7Available
            &&!Native.RiftlineM7Available&&(Native.Rules&MatchModifierFlags.RiftlineM7Available)==0,
            "native/default match definitions never opt into M7 availability");
        check((uint)MatchModifierFlags.RiftlineM7Available==262144
            &&MovementModelRules.KnownModifierMask==2097151&&NetConfig.ProtocolVersion==81,
            "protocol81 retains availability bit18 while preserving every earlier modifier ID");
        var requested=Native with{RiftlineM7Available=true};
        check(requested.Rules==(Native.Rules|MatchModifierFlags.RiftlineM7Available)
            &&requested.ModifierSummary.Contains("Riftline M7",StringComparison.Ordinal),
            "explicit M7 availability projects its reviewed configuration bit and an availability label");
        var copied=requested with{PointGoal=20};
        check(copied.RiftlineM7Available&&copied.NormalizeLegacy().RiftlineM7Available,
            "ordinary match record projections preserve explicit availability without activation");
        string json=JsonSerializer.Serialize(requested);
        var restored=JsonSerializer.Deserialize<MatchDefinition>(json);
        check(restored==requested&&restored.RiftlineM7Available,
            "typed match-definition JSON DTO roundtrip preserves explicit availability independently of wire acceptance");
        check(!JsonSerializer.Deserialize<MatchDefinition>("{\"RoomKey\":\"MP1 SANCTORUS\",\"Mode\":3}").RiftlineM7Available,
            "older match-definition JSON without availability retains native default false");
        check(LobbyRules.ValidateDefinition(requested,out string reason)==LobbyResultCode.Ok,
            "direct Battle availability configuration is valid independently of engine readiness or grants");
        var parkour=requested with{MovementModel=MovementModel.Parkour};
        check(LobbyRules.ValidateDefinition(parkour,out _)==LobbyResultCode.Ok
            &&MovementModelRules.FromRules(parkour.Rules)==MovementModel.Parkour,
            "Parkour movement and explicit M7 availability coexist under native Hunter abilities");
        foreach(var invalid in new[]{requested with{Mode=GameMode.Survival},requested with{Mode=GameMode.BattleTeams},
            requested with{InstaGib=true},requested with{OneInTheChamber=true},requested with{NoImperialist=true},
            requested with{BalancedMode=true},requested with{EnhancedHunters=true}})
            check(LobbyRules.ValidateDefinition(invalid,out reason)!=LobbyResultCode.Ok&&reason.Length>0,
                "live lobby refuses unsupported requested custom mode/loadout: "+invalid.Mode+"/"+invalid.Rules);
        check(LobbyRules.ValidateDefinition(Native,out _)==LobbyResultCode.Ok,
            "the same native-only match remains valid without the reserved availability bit");
    }
    private static void NativeWire(Action<bool,string> check)
    {
        byte[] bytes=Bytes(Native);
        // Frozen protocol69 native fixture: fixed Lobby/identity/default world,
        // MP1 SANCTORUS Battle300/7 with native FriendlyFire+SpawnProtection17.
        check(bytes.Length==368&&Convert.ToHexStringLower(SHA256.HashData(bytes))==
            "ce09f2f15d6102dbab813f03acc8a36bbb8f27f2058f03b7bc1b6a52d562ce14",
            "native session368 golden bytes remain unchanged after reserving custom availability");
        check(bytes.AsSpan().SequenceEqual(Bytes(Native with{RiftlineM7Available=false}))
            &&BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(SessionStatePacket.Protocol28Size))==17,
            "explicit false emits exactly original native modifier bytes");
        check(SessionStatePacket.TryRead(bytes,out var live)&&live.Match==Native&&!live.Match.RiftlineM7Available
            &&SessionStatePacket.TryRead(bytes,out var structural,validateDefinition:false)&&structural.Match==Native
            &&SessionStatePacket.TryReadArchived(bytes,out var archive)&&archive.Match==Native&&!archive.Match.RiftlineM7Available,
            "native live, structural request and archived decoders roundtrip without inventing availability");
        var retired=Native with{EnhancedHunters=true};
        check(SessionStatePacket.TryReadArchived(Bytes(retired),out var historical)&&historical.Match==retired
            &&!historical.Match.RiftlineM7Available,
            "existing retired Enhanced archive decoding stays native-only and does not synthesize M7 bit");
    }
    private static void AvailabilityBit(Action<bool,string> check)
    {
        var requested=Native with{RiftlineM7Available=true};byte[] bytes=Bytes(requested);
        check(BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(SessionStatePacket.Protocol28Size))==(17u|262144u),
            "requested configuration writer preserves explicit availability bit18");
        check(SessionStatePacket.TryRead(bytes,out var live)&&live.Match==requested
            &&SessionStatePacket.TryRead(bytes,out var structural,validateDefinition:false)&&structural.Match==requested
            &&!SessionStatePacket.TryReadArchived(bytes,out _),
            "protocol70 live and structural availability is accepted while every custom archive configuration is refused");
        var historic=new byte[bytes.Length+1];historic[0]=(byte)PacketType.SessionState;bytes.CopyTo(historic,1);
        for(int protocol=31;protocol<70;protocol++)
        {
            bool refused=false;
            try{ReplayIdentityCompatibility.Convert(historic,protocol);}catch(InvalidDataException){refused=true;}
            check(refused,"pre70 replay session cannot acquire previously reserved availability: "+protocol);
        }
        var command=new LobbyCommandPacket{CommandId=1,ExpectedRevision=11,Type=LobbyCommandType.UpdateMatch,
            Configuration=Session(requested)};
        var commandBytes=new byte[LobbyCommandPacket.Size];command.Write(commandBytes);
        check(LobbyCommandPacket.TryRead(commandBytes,out var parsed)&&parsed.Configuration.Match==requested,
            "protocol70 UpdateMatch carries availability for subsequent lobby/readiness admission");
        var packaged=requested with{PackagedWeaponAvailable=true};
        check(SessionStatePacket.TryRead(Bytes(packaged),out var packageLive)&&packageLive.Match==packaged
            &&packaged.Rules==(requested.Rules|MatchModifierFlags.PackagedWeaponAvailable)
            &&!SessionStatePacket.TryReadArchived(Bytes(packaged),out _),
            "protocol81 package bit20 preserves exact live configuration and refuses unsupported archive playback");
        byte[] orphan=Bytes(Native with{PackagedWeaponAvailable=true});
        check(!SessionStatePacket.TryRead(orphan,out _)&&SessionStatePacket.TryRead(orphan,out var orphanRequest,validateDefinition:false)
            &&LobbyRules.ValidateDefinition(orphanRequest.Match,out _)!=LobbyResultCode.Ok
            &&!SessionStatePacket.TryReadArchived(orphan,out _),
            "structural package request still requires pickup availability before live admission and cannot enter archives");
        var packageArchive=new byte[orphan.Length+1];packageArchive[0]=(byte)PacketType.SessionState;orphan.CopyTo(packageArchive,1);
        for(int protocol=31;protocol<=NetConfig.ProtocolVersion;protocol++)
            check(Refused(()=>{_ = ReplayIdentityCompatibility.Convert(packageArchive,protocol).Length;}),
                "every historical/current native archive refuses standalone package rule bit20: "+protocol);
        foreach(int bit in Enumerable.Range(21,11))
        {
            byte[] invalid=Bytes(Native);
            BinaryPrimitives.WriteUInt32LittleEndian(invalid.AsSpan(SessionStatePacket.Protocol28Size),17u|(1u<<bit));
            check(!SessionStatePacket.TryRead(invalid,out _)
                &&!SessionStatePacket.TryRead(invalid,out _,validateDefinition:false)
                &&!SessionStatePacket.TryReadArchived(invalid,out _),
                "unknown session modifier bit is rejected in every decode path: "+bit);
        }
    }
    private static bool Refused(Action action)
    {try{action();return false;}catch(InvalidDataException){return true;}}
    private static void ReplayRefusal(Action<bool,string> check)
    {
        byte[] nativeSession=new byte[1+SessionStatePacket.Size];nativeSession[0]=(byte)PacketType.SessionState;
        Bytes(Native).CopyTo(nativeSession,1);
        check(ReplayIdentityCompatibility.Convert(nativeSession,69).SequenceEqual(nativeSession)
            &&ReplayIdentityCompatibility.Convert(nativeSession,70).SequenceEqual(nativeSession),
            "native session70 body is byte-identical to reviewed69 without inventing custom availability");
        var match=new MatchStatePacket{MatchId=7,AuthorityEpoch=9,RoomKey=Native.RoomKey,
            NextRoomKey="",Mode=(byte)GameMode.Battle,RuleBits=(uint)Native.Rules};
        byte[] nativeMatch=new byte[1+MatchStatePacket.Size];nativeMatch[0]=(byte)PacketType.MatchState;match.Write(nativeMatch.AsSpan(1));
        check(ReplayIdentityCompatibility.Convert(nativeMatch,69).SequenceEqual(nativeMatch)
            &&ReplayIdentityCompatibility.Convert(nativeMatch,70).SequenceEqual(nativeMatch),
            "native MatchState70 body retains exact reviewed69 bytes");
        check(CustomReplayBoundary.FirstFrozenAvailabilityProtocol==70,
            "custom replay bootstrap boundary follows frozen availability protocol70 independently of the current live version");
        foreach(byte nativeProtocol in new byte[]{59,69,70,71,72,73,74,75,76})
        {
            byte[] recordedMatch=nativeProtocol<60?nativeMatch.AsSpan(0,1+MatchStatePacket.Protocol59Size).ToArray():nativeMatch;
            var nativeMetadata=new ReplayMetadata{ProtocolVersion=nativeProtocol,FormatVersion=6,TimedShotCertificates=true,
                TickRate=60,RoomKey=Native.RoomKey,Mode=GameMode.Battle,BuildVersion="fixture",BuildId="fixture",
                Bootstrap=new ReplayBootstrap{Packets=new[]{nativeSession,recordedMatch}}};
            var encoded=ReplayFormatV3.EncodeMetadata(nativeMetadata);
            var decoded=ReplayFormatV3.DecodeMetadata(nativeProtocol,encoded,6);
            check(decoded.Bootstrap.Packets.Count==2&&decoded.Bootstrap.Packets[0].AsSpan().SequenceEqual(nativeSession)
                &&decoded.Bootstrap.Packets[1].AsSpan().SequenceEqual(recordedMatch)
                &&ReplayFormatV3.EncodeMetadata(decoded).AsSpan().SequenceEqual(encoded),
                "native metadata retains exact reviewed bootstrap bodies across the activation boundary: "+nativeProtocol);
        }
        byte[] customSession=nativeSession.ToArray();Bytes(Native with{RiftlineM7Available=true}).CopyTo(customSession,1);
        byte[] customMatch=nativeMatch.ToArray();match.RuleBits|=262144u;match.Write(customMatch.AsSpan(1));
        foreach(byte[] packet in new[]{customMatch,customSession})
        {
            check(Refused(()=>{ReplayIdentityCompatibility.Convert(packet,70);}),
                "current70 replay conversion refuses custom availability instead of silently playing native: "+(PacketType)packet[0]);
            check(Refused(()=>{ReplayIdentityCompatibility.Convert(packet,71);}),
                "protocol71 replay conversion preserves custom availability refusal: "+(PacketType)packet[0]);
            foreach(int protocol in new[]{72,73,74,75,76})
                check(Refused(()=>{ReplayIdentityCompatibility.Convert(packet,protocol);}),
                    "reviewed native archive support does not admit custom availability: "+protocol+"/"+(PacketType)packet[0]);
            var replica=new ReplayReplicaState();replica.Accept(nativeMatch,0);var before=replica.CaptureCheckpoint();
            check(Refused(()=>replica.Accept(packet,1))&&replica.CaptureCheckpoint().Bytes.SequenceEqual(before.Bytes),
                "direct custom replay packet refusal is transactional: "+(PacketType)packet[0]);
            var metadata=new ReplayMetadata{ProtocolVersion=70,FormatVersion=6,TimedShotCertificates=true,
                TickRate=60,RoomKey=Native.RoomKey,Mode=GameMode.Battle,BuildVersion="fixture",BuildId="fixture",
                Bootstrap=new ReplayBootstrap{Packets=new[]{packet}}};
            check(metadata.RecordedMovementModel==null,
                "unsupported custom replay facts cannot label themselves as native movement: "+(PacketType)packet[0]);
            check(Refused(()=>{var encoded=ReplayFormatV3.EncodeMetadata(metadata);ReplayFormatV3.DecodeMetadata(70,encoded,6);}),
                "metadata archive bootstrap refuses custom configuration: "+(PacketType)packet[0]);
            var currentMetadata=new ReplayMetadata{ProtocolVersion=71,FormatVersion=6,TimedShotCertificates=true,
                TickRate=60,RoomKey=Native.RoomKey,Mode=GameMode.Battle,BuildVersion="fixture",BuildId="fixture",
                Bootstrap=new ReplayBootstrap{Packets=new[]{packet}}};
            check(currentMetadata.RecordedMovementModel==null
                &&Refused(()=>{var encoded=ReplayFormatV3.EncodeMetadata(currentMetadata);ReplayFormatV3.DecodeMetadata(71,encoded,6);}),
                "protocol71 metadata retains the custom archive boundary: "+(PacketType)packet[0]);
            foreach(byte nativeProtocol in new byte[]{72,73,74,75,76})
            {
                check(Refused(()=>{ReplayIdentityCompatibility.Convert(packet,nativeProtocol);}),
                    "reviewed native protocol retains custom replay conversion refusal: "+nativeProtocol);
                var reviewedMetadata=new ReplayMetadata{ProtocolVersion=nativeProtocol,FormatVersion=6,TimedShotCertificates=true,
                    TickRate=60,RoomKey=Native.RoomKey,Mode=GameMode.Battle,BuildVersion="fixture",BuildId="fixture",
                    Bootstrap=new ReplayBootstrap{Packets=new[]{packet}}};
                check(reviewedMetadata.RecordedMovementModel==null
                    &&Refused(()=>{var encoded=ReplayFormatV3.EncodeMetadata(reviewedMetadata);ReplayFormatV3.DecodeMetadata(nativeProtocol,encoded,6);}),
                    "reviewed native protocol retains the stable custom archive boundary: "+nativeProtocol);
            }
        }
        var destination=new ReplayReplicaState();destination.Accept(nativeMatch,0);var original=destination.CaptureCheckpoint();
        byte[] forged=original.Bytes.ToArray();
        // v12 has8-byte header+36-byte clocks/counters+3 presence flags.
        const int matchAt=47;BinaryPrimitives.WriteUInt32LittleEndian(forged.AsSpan(matchAt+103),17u|262144u);
        check(Refused(()=>destination.RestoreCheckpoint(new(forged)))
            &&destination.CaptureCheckpoint().Bytes.SequenceEqual(original.Bytes),
            "forged current70 custom MatchState checkpoint refuses before changing native replica state");
    }
    private static void FrozenAvailability(Action<bool,string> check)
    {
        check(!NetSession.Active&&NetSession.ServerSession==null&&NetSession.ServerMatch==null,
            "availability adoption fixture owns an inactive empty session; no peer authentication claim");
        const BindingFlags hidden=BindingFlags.Static|BindingFlags.NonPublic;
        var current=typeof(GameState).GetProperty("Current",hidden)!;
        var error=typeof(NetSession).GetProperty(nameof(NetSession.LastError))!;
        object? prior=current.GetValue(null);string? priorError=NetSession.LastError;
        var applied=typeof(NetSession).GetField("_appliedBootstrap",hidden)!;
        var confirmed=typeof(NetSession).GetField("_confirmedBootstrap",hidden)!;
        var local=typeof(NetSession).GetProperty(nameof(NetSession.LocalSlot))!;
        try
        {
            current.SetValue(null,new SceneGameState(new ScenePlayerRegistry()));
            MatchStatePacket Control(SessionStatePacket state)=>new(){MatchId=state.MatchId,AuthorityEpoch=state.AuthorityEpoch,
                RoomKey=state.Match.RoomKey,NextRoomKey="",Mode=(byte)state.Match.Mode,Flags=MatchStatePacket.FlagInProgress,
                RuleBits=(uint)state.Match.Rules};
            WorldBootstrapIdentity Ready(SessionStatePacket state)
            {
                local.SetValue(null,0);NetPlayerLifecycle.SetOccupant(0,3);
                var baseline=new WorldBootstrapIdentity(new(state.MatchId,state.AuthorityEpoch,state.StartGeneration),1,3,100);
                applied.SetValue(null,baseline);confirmed.SetValue(null,baseline);return baseline;
            }
            foreach(bool selected in new[]{false,true})
            foreach(bool controlFirst in new[]{false,true})
            {
                NetSession.Stop();var state=Session(Native with{RiftlineM7Available=selected});
                state.Policy=ServerSessionPolicy.Continuous;state.Phase=SessionPhase.Starting;
                state.StartGeneration=1;state.StartStage=StartStage.Loading;
                if(controlFirst)NetSession.ApplyMatchState(Control(state),false);else NetSession.ApplySessionState(state);
                var baseline=Ready(state);var conflicting=state;conflicting.Revision++;
                conflicting.Match=state.Match with{RiftlineM7Available=!selected};
                NetSession.ApplyMatchState(Control(conflicting),false);NetSession.ApplySessionState(conflicting);
                check(NetSession.ServerMatch?.RuleBits==(uint)state.Match.Rules
                    &&(controlFirst?NetSession.ServerSession==null:NetSession.ServerSession?.Revision==state.Revision)
                    &&Equals(applied.GetValue(null),baseline)&&Equals(confirmed.GetValue(null),baseline)
                    &&NetSession.WorldIsReady&&NetSession.WorldAdmissionConfirmed,
                    "first frozen availability and readiness survive opposite flag in either arrival order: selected="+selected+" controlFirst="+controlFirst);
                if(controlFirst)NetSession.ApplySessionState(state);
                check(NetSession.ServerSession?.Match.RiftlineM7Available==selected,
                    "matching late session adopts first frozen availability: "+selected);
            }
            foreach(bool selected in new[]{false,true})
            {
                NetSession.Stop();var oldLobby=Session(Native with{RiftlineM7Available=!selected});
                oldLobby.Policy=ServerSessionPolicy.Continuous;oldLobby.MatchId=41;oldLobby.AuthorityEpoch=7;
                NetSession.ApplySessionState(oldLobby);
                var next=oldLobby;next.MatchId=42;next.Revision++;next.Phase=SessionPhase.Starting;
                next.StartStage=StartStage.Loading;next.StartGeneration=1;next.Match=Native with{RiftlineM7Available=selected};
                NetSession.ApplyMatchState(Control(next),false);
                check(NetSession.ServerSession?.MatchId==41&&NetSession.ServerMatch?.MatchId==42
                    &&NetSession.ServerMatch?.RuleBits==(uint)next.Match.Rules,
                    "actual next-stream control can precede its session while old mutable lobby remains: "+selected);
                var baseline=Ready(oldLobby);var conflict=next;conflict.Match=next.Match with{RiftlineM7Available=!selected};
                NetSession.ApplyMatchState(Control(conflict),false);NetSession.ApplySessionState(conflict);
                check(NetSession.ServerSession?.MatchId==41&&NetSession.ServerMatch?.MatchId==42
                    &&NetSession.ServerMatch?.RuleBits==(uint)next.Match.Rules
                    &&Equals(applied.GetValue(null),baseline)&&Equals(confirmed.GetValue(null),baseline),
                    "old lobby cannot make actual next-stream frozen control mutable or admit conflicting session: "+selected);
                NetSession.ApplySessionState(next);
                check(NetSession.ServerSession?.MatchId==42&&NetSession.ServerSession?.Match.RiftlineM7Available==selected,
                    "matching next-stream session adopts actual control after conflicting reorder was refused: "+selected);
                NetSession.Stop();NetSession.ApplySessionState(oldLobby);NetSession.ApplySessionState(next);
                check(NetSession.ServerSession?.MatchId==42&&NetSession.ServerSession?.Match.RiftlineM7Available==selected,
                    "without actual next-stream control, old provisional lobby does not freeze next chosen availability: "+selected);
            }
            NetSession.Stop();var lobby=Session(Native);lobby.Policy=ServerSessionPolicy.Continuous;
            NetSession.ApplySessionState(lobby);lobby.Revision++;lobby.Match=lobby.Match with{RiftlineM7Available=true};
            NetSession.ApplySessionState(lobby);
            check(NetSession.ServerSession?.Phase==SessionPhase.Lobby&&NetSession.ServerSession?.Match.RiftlineM7Available==true,
                "pre-start lobby configuration can change availability before the round freezes");
        }
        finally{NetSession.Stop();current.SetValue(null,prior);error.SetValue(null,priorError);}
        check(ReferenceEquals(current.GetValue(null),prior)&&!NetSession.Active,
            "availability adoption restores prior scene bootstrap and leaves transport inactive");
    }
    private static void RecordingAdmission(Action<bool,string> check)
    {
        const BindingFlags hidden=BindingFlags.Static|BindingFlags.NonPublic;
        var current=typeof(GameState).GetProperty("Current",hidden)!;
        var role=typeof(NetSession).GetProperty(nameof(NetSession.Role))!;
        var error=typeof(NetSession).GetProperty(nameof(NetSession.LastError))!;
        var fallback=typeof(CustomReplayBoundary).GetField("_ownerlessSelected",hidden)!;
        object? previous=current.GetValue(null),previousFallback=fallback.GetValue(null);
        string? previousError=NetSession.LastError;
        try
        {
            check(!NetSession.Active&&!ReplayCapture.LiveRecordingActive,
                "public availability recording fixture starts without transport or writers");
            current.SetValue(null,new SceneGameState(new ScenePlayerRegistry()));fallback.SetValue(null,false);
            foreach(bool available in new[]{false,true})
            {
                NetSession.Stop();var frozen=Session(Native with{RiftlineM7Available=available});
                frozen.Policy=ServerSessionPolicy.Continuous;frozen.Phase=SessionPhase.Starting;
                frozen.StartGeneration=1;frozen.StartStage=StartStage.Loading;
                NetSession.ApplySessionState(frozen);
                // Explicit role-only fixture: no socket, native world admission,
                // readiness evidence or replica page is manufactured here.
                role.SetValue(null,NetRole.Client);
                check(NetSession.PublicCustomWeaponAvailability==available
                    &&!NetSession.CustomWeaponReadinessConfirmed&&!NetSession.CustomWeaponReplicaReady,
                    "accepted frozen client availability precedes custom readiness and first public page: "+available);
                bool canRecord=ReplayCapture.CanStartLiveRecording(out var reason);
                check(CustomReplayBoundary.IsBlocked()==available&&canRecord==!available
                    &&(!available||reason==CustomReplayBoundary.Error)&&!ReplayCapture.LiveRecordingActive,
                    "frozen public M7 availability alone refuses recording while native availability remains recordable: "+available);
            }
        }
        finally
        {
            NetSession.Stop();current.SetValue(null,previous);fallback.SetValue(null,previousFallback);
            error.SetValue(null,previousError);
        }
    }
    private static void Policy(Action<bool,string> check)
    {
        foreach(bool online in new[]{false,true})
        {
            check(CustomWeaponMatchPolicy.TryAdmit(GameMode.Battle,0,online,0,out string reason)&&reason.Length==0,
                "shared requested-loadout policy admits Battle humans without activating availability: online="+online);
            foreach(var mode in Enum.GetValues<GameMode>().Where(value=>value!=GameMode.Battle))
                check(!CustomWeaponMatchPolicy.TryAdmit(mode,0,online,0,out reason)&&reason.Length>0,
                    "M7 shared policy refuses unsupported mode with reason: "+mode+" online="+online);
            foreach(var modifier in new[]{MatchModifierFlags.InstaGib,MatchModifierFlags.OneInTheChamber,
                MatchModifierFlags.NoImperialist,MatchModifierFlags.BalancedMode,MatchModifierFlags.EnhancedHunters})
                check(!CustomWeaponMatchPolicy.TryAdmit(GameMode.Battle,modifier,online,0,out reason)&&reason.Length>0,
                    "M7 shared policy refuses incompatible loadout modifier: "+modifier+" online="+online);
            foreach(var modifier in new[]{MatchModifierFlags.Fiesta,MatchModifierFlags.FriendlyFire,MatchModifierFlags.AffinityWeapons,
                MatchModifierFlags.ShadowFreeze,MatchModifierFlags.SpawnProtection,MatchModifierFlags.DisablePowerups,
                MatchModifierFlags.LowTier,MatchModifierFlags.ThirdPersonCamera})
                check(CustomWeaponMatchPolicy.TryAdmit(GameMode.Battle,modifier,online,0,out reason)&&reason.Length==0,
                    "M7 policy retains otherwise-supported native modifier: "+modifier+" online="+online);
            foreach(var model in Enum.GetValues<MovementModel>())
                check(CustomWeaponMatchPolicy.TryAdmit(GameMode.Battle,MovementModelRules.Modifier(model),online,0,out reason),
                    "shared custom policy retains each native-validated movement model: "+model+" online="+online);
            check(!CustomWeaponMatchPolicy.TryAdmit(GameMode.Battle,0,online,-1,out reason)&&reason.Length>0,
                "negative bot counts fail closed: online="+online);
            foreach(int bit in Enumerable.Range(21,11))
                check(!CustomWeaponMatchPolicy.TryAdmit(GameMode.Battle,(MatchModifierFlags)(1u<<bit),online,0,out reason),
                    "shared policy refuses unknown modifier bit: "+bit+" online="+online);
            check(!CustomWeaponMatchPolicy.TryAdmit(GameMode.Battle,
                MatchModifierFlags.MomentumMovement|MatchModifierFlags.TraversalMovement,online,0,out reason),
                "shared policy refuses conflicting movement flags: online="+online);
        }
        check(CustomWeaponMatchPolicy.TryAdmit(GameMode.Battle,MatchModifierFlags.RiftlineM7Available,true,0,out _),
            "shared policy accepts recognized availability without implying runtime readiness");
        var roster=RosterPacket.Create();roster.Count=2;roster.Slots[0]=0;roster.Slots[1]=1;
        check(LobbyRules.Validate(Native with{RiftlineM7Available=true},roster,false,out _)==LobbyResultCode.Ok,
            "human-only roster can validate availability before readiness admission");
        roster.Flags[1]=1;roster.BotLevels[1]=1;roster.ContainsBots=true;
        check(LobbyRules.Validate(Native with{RiftlineM7Available=true},roster,false,out _)!=LobbyResultCode.Ok,
            "availability lobby refuses actual bot roster before custom bot integration");
        check(LobbyRules.Validate(Native,roster,false,out _)==LobbyResultCode.Ok,
            "native-only lobby keeps ordinary bot admission");
        foreach(int bots in Enumerable.Range(1,8))
        {
            check(CustomWeaponMatchPolicy.TryAdmit(GameMode.Battle,0,false,bots,out _),
                "offline native bot targets remain supported: "+bots);
            check(!CustomWeaponMatchPolicy.TryAdmit(GameMode.Battle,0,true,bots,out string reason)&&reason.Length>0,
                "online M7 policy refuses bot participants before bot support exists: "+bots);
        }
    }
}
