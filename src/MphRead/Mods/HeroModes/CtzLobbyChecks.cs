using System;
using System.Linq;
using MphRead.Mods.HeroModes;

namespace MphRead.Mods.Network;

public static partial class NetLobbyTest
{
    public static int RunCtz(string room,GameMode mode=GameMode.CaptureTheZone)
    {
        _checks = 0;
        try
        {
            NetworkMapIdentity.StageRoom(room);
            using var rig=new Rig();
            Client owner=CtzAdd(rig,94001);
            var config=owner.State!.Value;
            config.Match=config.Match with {RoomKey=room,MapIdentity=NetworkMapIdentity.ForRoom(room),Mode=mode,Format=MatchFormat.FourVsFour,
                TimeLimitSeconds=0,PointGoal=MatchGoalRules.DefaultValue(mode)};
            rig.Expect(owner,owner.Command(LobbyCommandType.UpdateMatch,config:config),LobbyResultCode.Ok);
            var old=new Client(rig.Server.BoundPort,95000,ctf:false);rig.Clients.Add(old);
            rig.Wait(()=>old.Refusal.HasValue,"CTZ refuses old client over real UDP");
            Check(old.Refusal!.Value.Reason==RefusedPacket.ReasonCtzRuleset && old.Slot==-1,"Capability refusal allocates no CTZ slot");
            rig.Clients.Remove(old);old.Dispose();
            for(int i=1;i<8;i++) CtzAdd(rig,(uint)(94001+i));
            Check(owner.Roster.Count==8,"Eight CTZ clients converge on real wire roster");
            Check(Enumerable.Range(0,8).GroupBy(i=>(owner.Roster.Teams[i],owner.Roster.Hunters[i])).All(g=>g.Count()==1),
                "Initial CTZ admission reserves distinct hunters per team");
            int team=owner.Roster.Teams[Array.IndexOf(owner.Roster.Slots,(byte)owner.Slot,0,owner.Roster.Count)];
            var same=rig.Clients.Where(c=>owner.Roster.Teams[Array.IndexOf(owner.Roster.Slots,(byte)c.Slot,0,8)]==team).ToArray();
            var opponent=rig.Clients.First(c=>owner.Roster.Teams[Array.IndexOf(owner.Roster.Slots,(byte)c.Slot,0,8)]!=team);
            // Two actual socket senders ask for the same currently free hunter.
            same[0].Identify((byte)Hunter.Weavel); same[1].Identify((byte)Hunter.Weavel);
            rig.Wait(()=>same.Take(2).Any(c=>c.Chats.Any(x=>x.Text.Contains("Already selected"))),"Duplicate hunter gets authoritative rejection");
            rig.Stable();
            Check(Enumerable.Range(0,8).Count(i=>owner.Roster.Teams[i]==team && owner.Roster.Hunters[i]==(byte)Hunter.Weavel)==1,
                "Concurrent wire requests cannot reserve two Weavels on one team");
            opponent.Identify((byte)Hunter.Weavel);
            rig.Wait(()=>owner.Roster.Hunters[Array.IndexOf(owner.Roster.Slots,(byte)opponent.Slot,0,8)]==(byte)Hunter.Weavel,
                "Opposing team can mirror Weavel");
            rig.Expect(owner,owner.Command(LobbyCommandType.SetHandicap,target:(byte)owner.Slot,reduction:50),LobbyResultCode.InvalidConfiguration);
            rig.ReadyAll();
            var installed=NetworkMapIdentity.ForRoom(room);
            foreach(var client in rig.Clients)
            {
                var state=client.State!.Value;
                byte[] ready=new byte[MapAvailabilityPacket.Size];
                new MapAvailabilityPacket(state.MatchId,state.AuthorityEpoch,installed,
                    MapAvailabilityState.Ready,state.MapGeneration,1).Write(ready);
                client.Send(PacketType.MapAvailability,ready);
            }
            rig.Wait(()=>rig.Clients.All(c=>c.State?.MapAvailability?[c.Slot]==MapAvailabilityState.Ready),
                "All peers acknowledge the exact immutable custom map identity");
            rig.Expect(owner,owner.Command(LobbyCommandType.StartMatch),LobbyResultCode.Ok);
            foreach(var client in rig.Clients)client.Loaded();
            rig.Wait(()=>owner.State!.Value.Phase==SessionPhase.InMatch,"CTZ load barrier enters InMatch (control-plane fixture only)");
            byte oldHunter=owner.Roster.Hunters[Array.IndexOf(owner.Roster.Slots,(byte)owner.Slot,0,8)];
            int prior=owner.Chats.Count;
            owner.Identify(oldHunter==(byte)Hunter.Trace?(byte)Hunter.Sylux:(byte)Hunter.Trace);
            rig.Wait(()=>owner.Chats.Skip(prior).Any(c=>c.Text.Contains("respawn")||c.Text.Contains("Respawn")),"Midmatch request without trusted death denied");
            Check(owner.Roster.Hunters[Array.IndexOf(owner.Roster.Slots,(byte)owner.Slot,0,8)]==oldHunter,"Rejected combat change preserves hunter");
            Console.WriteLine($"[herolobby] PASS {_checks} real UDP/control-plane checks. No simulated world, native gameplay or rendering test.");
            return 0;
        }
        catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
        finally{NetSession.Stop();}
    }
    private static Client CtzAdd(Rig rig,uint id)
    {
        var client=new Client(rig.Server.BoundPort,id,ctz:true);rig.Clients.Add(client);
        rig.Wait(()=>client.Slot>=0 && client.State!=null,"CTZ-capable client admitted");
        // Keep the server's initial free selection; only set the name in Identify.
        rig.Wait(()=>client.Roster.Count>0 && Array.IndexOf(client.Roster.Slots,(byte)client.Slot,0,client.Roster.Count)>=0,"Own roster selection received");
        int at=Array.IndexOf(client.Roster.Slots,(byte)client.Slot,0,client.Roster.Count);
        client.Identify(client.Roster.Hunters[at]);rig.Stable();return client;
    }
}
