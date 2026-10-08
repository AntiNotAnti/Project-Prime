using System.Collections.Immutable;
using MphRead;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Network;

sealed class FakeLobby : ILobbySessionBackend
{
    public LobbySnapshot State = new()
    {
        Active=true, Persistent=true, Phase=SessionPhase.Lobby, SessionRevision=1, RosterRevision=1,
        MaxPlayers=8, OwnerSlot=0, LocalSlot=0, LocalHunter=Hunter.Samus, PlayerName="Jarrett", RequiredMapReady=true,
        Match=new MatchDefinition { RoomKey="AD1 TRANSFER LOCK BT", Mode=GameMode.Battle, PointGoal=7,TimeLimitSeconds=420 },
        Players=ImmutableArray.Create(new LobbyPlayerSnapshot(0,"Jarrett",Hunter.Samus,0,-1,false,0,false,false,1,0,MapAvailabilityState.Ready,1))
    };
    public List<LobbyIntent> Commands = new();
    public double Clock=>0; public bool ShouldLoadMatch=>false; public bool Refused=>false; public bool TimedOut=>false;
    public string RefusedMessage=>"";
    public LobbySnapshot Capture()=>State;
    public void Pump(){} public void Stop(){}
    public bool SendCommand(LobbyIntent intent){Commands.Add(intent);return true;}
    public void Identify(Hunter hunter,byte color){State=State with {LocalHunter=hunter,LocalColor=color};}
    public void SetSpectator(bool spectator){} public void SendChat(string text){} public void RetryMap(){}
    public LobbyActionResult ValidateRules(MatchDefinition match)=>LobbyActionResult.Ok;
    public void RulesAccepted(MatchDefinition match){}
}
