using System;
using System.Linq;
namespace MphRead.Mods.Network;
public static partial class NetLobbyTest
{
    public static int RunUnifiedSwitches()
    {
        _checks=0;
        try
        {
            using (var capabilityRig = new Rig(rotation: MapRotation.SingleMatch("CAPTURE THE ZONE V1", GameMode.CaptureTheZone, 0, 2)))
            {
            // Older unified clients reused IDs now assigned to pickup messages.
            // Refuse them before allocating a slot in a custom-mode lobby.
            using (var legacy = new Client(capabilityRig.Server.BoundPort, 97000, ctf: false))
            {
                capabilityRig.Wait(() => { legacy.Drain(); return legacy.Refusal.HasValue; }, "Missing unified capability refused");
                Check(legacy.Slot < 0 && capabilityRig.Server.PeerCount == 0
                    && legacy.Refusal!.Value.Reason == RefusedPacket.ReasonCtfRuleset,
                    "Missing capability allocates no peer");
                legacy.Refusal = null;
                byte[] hello = new byte[27]; hello[0] = NetConfig.ProtocolVersion; hello[1] = 255;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(hello.AsSpan(2), legacy.Id);
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(hello.AsSpan(23), 0x32444d55);
                legacy.Send(PacketType.Hello, hello);
                capabilityRig.Wait(() => { legacy.Drain(); return legacy.Refusal.HasValue; }, "Previous unified capability refused");
                Check(legacy.Slot < 0 && capabilityRig.Server.PeerCount == 0
                    && legacy.Refusal!.Value.Reason == RefusedPacket.ReasonCtfRuleset,
                    "Previous packet layout allocates no peer");
                foreach (uint oldCapability in new[] { 0x33444d55u, 0x34444d55u, 0x35444d55u, 0x36444d55u, 0x37444d55u })
                {
                legacy.Refusal = null;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(hello.AsSpan(23), oldCapability);
                legacy.Send(PacketType.Hello, hello);
                capabilityRig.Wait(() => { legacy.Drain(); return legacy.Refusal.HasValue; }, "Previous traversal capability refused");
                Check(legacy.Slot < 0 && capabilityRig.Server.PeerCount == 0
                    && legacy.Refusal!.Value.Reason == RefusedPacket.ReasonCtfRuleset,
                    "Previous trigger semantics allocate no peer");
                }
            }
            }
            using var rig=new Rig();
            var owner=CtzAdd(rig,97001);
            for(int i=1;i<8;i++)CtzAdd(rig,(uint)(97001+i));
            string ordinary=owner.State!.Value.Match.RoomKey;
            var modes=new[]{GameMode.BattleTeams,GameMode.SurvivalTeams,GameMode.CaptureTheZone,GameMode.PushThePayload,
                GameMode.CaptureTheZone,GameMode.PushThePayload,GameMode.Oddball,GameMode.CaptureTheFlag,GameMode.BattleTeams};
            for(int cycle=0;cycle<3;cycle++)foreach(var mode in modes)
            {
                string room=mode==GameMode.CaptureTheZone?"CAPTURE THE ZONE V1":mode==GameMode.PushThePayload?"PAYLOAD DISTRICT V1":ordinary;
                NetworkMapIdentity.StageRoom(room);
                var config=owner.State!.Value;
                config.Match=config.Match with{Mode=mode,RoomKey=room,MapIdentity=NetworkMapIdentity.ForRoom(room),Format=MatchFormat.FourVsFour};
                rig.Expect(owner,owner.Command(LobbyCommandType.UpdateMatch,config:config),LobbyResultCode.Ok);
                rig.Stable();
                Check(rig.Clients.All(c=>c.State!.Value.Match.Mode==mode),$"Eight peers retain {mode} after refresh cycle {cycle}");
            }
            Console.WriteLine($"[unified-lobby] PASS {_checks} socket/control-plane checks; no gameplay simulation.");return 0;
        }
        catch(Exception e){Console.Error.WriteLine(e);return 1;}
        finally{NetSession.Stop();}
    }
}
