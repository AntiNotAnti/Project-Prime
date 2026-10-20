using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using MphRead;
using MphRead.Mods.CustomWeapons;
using MphRead.Mods.Movement;
using MphRead.Mods.Network;

// Pure configuration contracts only. The reserved availability bit is never
// accepted by a protocol69 decoder; this does not activate a public M7 match.
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
        DefaultsAndDto(check);NativeWire(check);ReservedBit(check);Policy(check);
    }
    private static void DefaultsAndDto(Action<bool,string> check)
    {
        check(!default(MatchDefinition).RiftlineM7Available&&!new MatchDefinition().RiftlineM7Available
            &&!Native.RiftlineM7Available&&(Native.Rules&MatchModifierFlags.RiftlineM7Available)==0,
            "native/default match definitions never opt into M7 availability");
        check((uint)MatchModifierFlags.RiftlineM7Available==262144
            &&MovementModelRules.KnownModifierMask==262143&&NetConfig.ProtocolVersion==69,
            "M7 availability reserves bit18 while accepted protocol69 modifier mask remains unchanged");
        var requested=Native with{RiftlineM7Available=true};
        check(requested.Rules==(Native.Rules|MatchModifierFlags.RiftlineM7Available)
            &&requested.ModifierSummary.Contains("Riftline M7",StringComparison.Ordinal),
            "explicit M7 availability projects reserved configuration bit and an availability label");
        var copied=requested with{PointGoal=20};
        check(copied.RiftlineM7Available&&copied.NormalizeLegacy().RiftlineM7Available,
            "ordinary match record projections preserve explicit availability without activation");
        string json=JsonSerializer.Serialize(requested);
        var restored=JsonSerializer.Deserialize<MatchDefinition>(json);
        check(restored==requested&&restored.RiftlineM7Available,
            "typed match-definition JSON DTO roundtrip preserves explicit availability independently of wire acceptance");
        check(!JsonSerializer.Deserialize<MatchDefinition>("{\"RoomKey\":\"MP1 SANCTORUS\",\"Mode\":3}").RiftlineM7Available,
            "older match-definition JSON without availability retains native default false");
        check(LobbyRules.ValidateDefinition(requested,out string reason)!=LobbyResultCode.Ok
            &&reason.Contains("Riftline M7",StringComparison.Ordinal)&&reason.Contains("protocol",StringComparison.OrdinalIgnoreCase),
            "direct live lobby configuration refuses requested availability with explicit protocol activation reason");
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
    private static void ReservedBit(Action<bool,string> check)
    {
        var requested=Native with{RiftlineM7Available=true};byte[] bytes=Bytes(requested);
        check(BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(SessionStatePacket.Protocol28Size))==(17u|262144u),
            "requested configuration writer retains reserved bit rather than silently dropping availability intent");
        check(!SessionStatePacket.TryRead(bytes,out _)
            &&!SessionStatePacket.TryRead(bytes,out _,validateDefinition:false)
            &&!SessionStatePacket.TryReadArchived(bytes,out _),
            "all protocol69 session decoders reject M7 reserved bit including structural requests and archives");
        var command=new LobbyCommandPacket{CommandId=1,ExpectedRevision=11,Type=LobbyCommandType.UpdateMatch,
            Configuration=Session(requested)};
        var commandBytes=new byte[LobbyCommandPacket.Size];command.Write(commandBytes);
        check(!LobbyCommandPacket.TryRead(commandBytes,out _),
            "reserved M7 availability cannot enter live UpdateMatch through structural decode");
        foreach(int bit in Enumerable.Range(18,14))
        {
            byte[] invalid=Bytes(Native);
            BinaryPrimitives.WriteUInt32LittleEndian(invalid.AsSpan(SessionStatePacket.Protocol28Size),17u|(1u<<bit));
            check(!SessionStatePacket.TryRead(invalid,out _)
                &&!SessionStatePacket.TryRead(invalid,out _,validateDefinition:false)
                &&!SessionStatePacket.TryReadArchived(invalid,out _),
                "reserved/unknown session modifier bit is rejected in every decode path: "+bit);
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
            foreach(int bit in Enumerable.Range(18,14))
                check(!CustomWeaponMatchPolicy.TryAdmit(GameMode.Battle,(MatchModifierFlags)(1u<<bit),online,0,out reason),
                    "shared policy refuses reserved or unknown modifier bit: "+bit+" online="+online);
            check(!CustomWeaponMatchPolicy.TryAdmit(GameMode.Battle,
                MatchModifierFlags.MomentumMovement|MatchModifierFlags.TraversalMovement,online,0,out reason),
                "shared policy refuses conflicting movement flags: online="+online);
        }
        foreach(int bots in Enumerable.Range(1,8))
        {
            check(CustomWeaponMatchPolicy.TryAdmit(GameMode.Battle,0,false,bots,out _),
                "offline native bot targets remain supported: "+bots);
            check(!CustomWeaponMatchPolicy.TryAdmit(GameMode.Battle,0,true,bots,out string reason)&&reason.Length>0,
                "online M7 policy refuses bot participants before bot support exists: "+bots);
        }
    }
}
