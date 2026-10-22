using System;
using System.Collections.Generic;
using System.Net;
namespace MphRead.Mods.Network
{
internal static class NetConfig { public const int MaxPacketSize=1472, MaxPayloadSize=1448, ProtocolVersion=57; }
internal static class NetSession { public static uint ClientId=1234; }
internal static class NetConnection { private static long _id; public static ulong NewId()=>(ulong)System.Threading.Interlocked.Increment(ref _id); }
internal static class SequenceMath { public static bool Newer(uint a,uint b)=>unchecked((int)(a-b))>0; }
internal readonly struct RefusedPacket { internal static RefusedPacket Read(ReadOnlySpan<byte> source)=>new();internal string Describe(string server)=>"Refused"; }
internal readonly struct ReceivedPacket
{
 internal readonly IPEndPoint Sender;internal readonly byte[] Data;internal readonly int Length;internal readonly double ArrivedAt;internal readonly ulong ConnectionId;internal readonly uint Sequence;
 internal ReceivedPacket(IPEndPoint sender,byte[] data,int length,double arrivedAt=0,ulong connectionId=0,uint sequence=0)=>(Sender,Data,Length,ArrivedAt,ConnectionId,Sequence)=(sender,data,length,arrivedAt,connectionId,sequence);
 internal PacketType Type=>(PacketType)Data[0];internal ReadOnlySpan<byte> Payload=>Data.AsSpan(1,Length-1);
}
internal sealed class NetTransport(int port):IDisposable
{
 internal readonly List<(IPEndPoint Server,PacketType Type,byte[] Payload)> Sent=new();private readonly Queue<ReceivedPacket> _incoming=new();internal bool Disposed;
 internal void AnswerPingsImmediately(){}internal void Send(IPEndPoint server,PacketType type,ReadOnlySpan<byte> data){if(Disposed)throw new ObjectDisposedException("transport");Sent.Add((server,type,data.ToArray()));}
 internal void QueueIncoming(ReceivedPacket packet)=>_incoming.Enqueue(packet);
 internal IEnumerable<ReceivedPacket> Drain(){while(_incoming.TryDequeue(out var packet))yield return packet;}
 public void Dispose()=>Disposed=true;
}
}
namespace MphRead.Mods.Render { internal static class FrameTiming { internal const int SimulationHz=60; } }
namespace MphRead.Mods.Launcher
{
internal static partial class HunterLicenseClient
{
 private static readonly object CareerTicketLock=new();private static string _careerTicket="";private static uint _careerTicketClientId;private static DateTimeOffset _careerTicketExpiresAt,_careerTicketRetryAt;
 internal static object? TestLease;internal static int TestReads;
 internal static bool IsNativeAccountLeaseCurrent(object? lease)=>lease!=null&&ReferenceEquals(lease,TestLease);
 internal static bool TryGetCareerTicket(uint id,out string ticket){++TestReads;ticket=_careerTicket;return id==_careerTicketClientId&&ticket.Length>0;}
 internal static int TestGeneration=>_careerTicketGeneration;internal static string TestCached=>_careerTicket;
 internal static void TestInvalidate(){++_careerTicketGeneration;_careerTicketAccountLease=null;_careerTicket="";}
}
}
