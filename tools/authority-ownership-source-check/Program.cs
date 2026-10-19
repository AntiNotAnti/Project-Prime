using MphRead.Mods.Network;

int checks=0;
void Check(bool value,string name){if(!value)throw new Exception(name);++checks;}
NetSession.Replace(NetRole.Client,new());var client=new NetClientJoinOwnership();client.Capture();
Check(client.IsCurrent&&!client.IsCurrentAuthority,"existing client capture remains client-only");
Check(client.RunCurrent(()=>{}),"existing exact client action remains permitted");
Check(client.TryStopCurrent()&&!NetSession.Active&&!client.TryStopCurrent(),"existing client cleanup retires once");

NetSession.Replace(NetRole.Server,null);var accidental=new NetClientJoinOwnership();accidental.Capture();
Check(!accidental.IsCurrent&&!accidental.IsCurrentAuthority&&!accidental.TryStopCurrent()&&NetSession.IsAuthority,"ordinary client capture never gains null-transport authority permission");
var authority=new NetClientJoinOwnership();authority.CaptureAuthority();
Check(authority.IsCurrentAuthority&&!authority.IsCurrent&&!authority.TryStopCurrent()&&NetSession.IsAuthority,"explicit authority mode cannot use public client cleanup");
int actions=0;Check(authority.RunCurrentAuthority(()=>++actions)&&actions==1,"explicit current authority action executes once");
NetSession.Replace(NetRole.Server,null);var next=new NetClientJoinOwnership();next.CaptureAuthority();
Check(!authority.IsCurrentAuthority&&!authority.RunCurrentAuthority(()=>++actions)&&actions==1&&next.IsCurrentAuthority,"same-role null-reference replacement fences exact old authority incarnation");
Check(next.RunCurrentAuthority(()=>NetSession.Replace(NetRole.Server,null)),"owned action may publish replacement reentrantly");
Check(!next.IsCurrentAuthority&&!next.RunCurrentAuthority(()=>++actions)&&actions==1,"callback replacement cannot be reset by following old-owner work");

NetSession.Replace(NetRole.Client,new());var replacementClient=new NetClientJoinOwnership();replacementClient.Capture();
Check(!authority.RunCurrentAuthority(NetSession.Stop)&&replacementClient.IsCurrent,"old authority cleanup preserves replacement client");
NetSession.Replace(NetRole.Server,null);var current=new NetClientJoinOwnership();current.CaptureAuthority();
Check(!replacementClient.TryStopCurrent()&&current.IsCurrentAuthority,"old client cleanup preserves replacement authority");
Check(current.RunCurrentAuthority(NetSession.Stop)&&!NetSession.Active&&!current.IsCurrentAuthority,"current authority action can retire through existing lifecycle owner");
bool refused=false;try{current.CaptureAuthority();}catch(InvalidOperationException){refused=true;}
Check(refused,"authority capture refuses offline state");

for(int round=0;round<100;++round)
{
    NetSession.Replace(NetRole.Server,null);var old=new NetClientJoinOwnership();old.CaptureAuthority();
    Parallel.Invoke(()=>NetSession.Replace(NetRole.Client,new()),()=>NetSession.Replace(NetRole.Server,null));
    Check(!old.IsCurrentAuthority&&!old.RunCurrentAuthority(()=>throw new Exception("stale action")),"competing lifecycle publications retire old authority "+round);
}
Console.WriteLine($"PASS {checks} actual ownership-helper source assertions; closed lifecycle facade, no main build/socket/assets");

namespace MphRead.Mods.Network
{
    internal enum NetRole{Offline,Host,Client,Server}
    internal sealed class NetTransport{}
    internal static class NetSession
    {
        internal static readonly object ClientLifecycleGate=new();
        internal static NetTransport? EvidenceTransport;internal static NetRole Role;
        internal static bool IsClient=>Role==NetRole.Client;
        internal static bool IsServer=>Role==NetRole.Server;
        internal static bool IsAuthority=>IsServer;
        internal static bool Active=>Role!=NetRole.Offline;
        internal static void Replace(NetRole role,NetTransport? transport)
        {lock(ClientLifecycleGate){NetClientJoinOwnership.RetireIncarnation();Role=role;EvidenceTransport=transport;}}
        internal static void Stop()=>Replace(NetRole.Offline,null);
    }
}
