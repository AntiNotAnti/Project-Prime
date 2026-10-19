using System.Reflection;
using MphRead.Mods.Network;

var describe=typeof(NetStatus).GetMethod("Describe",BindingFlags.Static|BindingFlags.NonPublic)!;
var packet=new ServerStatusPacket {Match=new(){AuthorityEpoch=ulong.MaxValue,MatchId=17,RoomKey=""},
 Protocol=NetConfig.ProtocolVersion,Phase=SessionPhase.Lobby,LobbyEnabled=true,ServerName="Synthetic",MaxPlayers=8};
byte[] bytes=new byte[ServerStatusPacket.SizeWithWaitlist];packet.Write(bytes);
var parsed=ServerStatusPacket.Read(bytes);
var status=(ServerStatus)describe.Invoke(null,new object[]{parsed,false,7})!;
if(status.AuthorityEpoch!=ulong.MaxValue||status.Protocol!=NetConfig.ProtocolVersion||status.Phase!=SessionPhase.Lobby||!status.LobbyEnabled)
 throw new Exception("Existing status codec/projection lost authority facts");
PartyProtocolAcceptance.Run();
Console.WriteLine("PASS actual source owner+browser compile and actual status codec epoch/lobby/protocol projection; no socket/assets/auth.");
