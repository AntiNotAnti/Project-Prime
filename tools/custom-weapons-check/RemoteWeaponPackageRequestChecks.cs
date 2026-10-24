using System.Buffers.Binary;
using System.Net;
using System.Text;
using MphRead;
using MphRead.Mods.Network;

// Metadata-only wire checks: no package IO, allocator, fake UDP or child process.
internal static class RemoteWeaponPackageRequestChecks
{
    internal static int Run()
    {
        int checks=0;
        void Check(bool value,string label){if(!value)throw new InvalidOperationException(label);checks++;}
        void Reject(HostRequestPacket request,string label)
        {
            try{request.Write(new byte[request.Length]);}
            catch(ArgumentException){Check(true,label);return;}
            throw new InvalidOperationException("Expected argument rejection: "+label);
        }
        try
        {
            const string archive="0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
            HostRequestPacket Request()=>new(){Protocol=83,MaxPlayers=8,Mode=(byte)GameMode.Battle,TimeLimit=0x1234,PointGoal=0x5678,
                RoomKey="MP3 PROVING GROUND",ServerName="Wire package",HostNonce=0x0102030405060708,HostCookie=0x1112131415161718};
            byte[] Bytes(HostRequestPacket request){var bytes=new byte[request.Length];request.Write(bytes);return bytes;}
            // Independent native request writer pins old field offsets and the appended tail.
            byte[] Golden(HostRequestPacket request,int count,string? hash)
            {
                int tail=79+1+count*73,proof=tail+4+81;
                byte[] bytes=new byte[proof+16+32];bytes[0]=83;bytes[1]=8;bytes[2]=(byte)GameMode.Battle;
                BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(3),0x1234);BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(5),0x5678);
                Encoding.UTF8.GetBytes("MP3 PROVING GROUND").CopyTo(bytes,7);Encoding.UTF8.GetBytes("Wire package").CopyTo(bytes,47);bytes[79]=(byte)count;
                for(int i=0;i<count;i++){Encoding.UTF8.GetBytes(request.Rotation![i].RoomKey).CopyTo(bytes,80+i*73);bytes[120+i*73]=(byte)GameMode.Battle;}
                bytes[tail+1]=1;BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(proof),request.HostNonce);BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(proof+8),request.HostCookie);
                if(!string.IsNullOrEmpty(hash))Convert.FromHexString(hash).CopyTo(bytes,proof+16);
                return bytes;
            }
            Check(NetConfig.ProtocolVersion == 86,"deliberate live protocol 85 contract");
            var native=Request();byte[] absent=Bytes(native);int oldLength=79+1+4+81+16;
            Check(native.Length==oldLength+32&&absent.SequenceEqual(Golden(native,0,null)),"native independent vector preserves prior fields and appends exactly 32 zero bytes");
            native.WeaponArchiveHash="";Check(Bytes(native).SequenceEqual(absent),"null and empty native selection have identical bytes");
            Check(!native.RequiresMapPreparation&&!native.RequiresWeaponPreparation&&!native.RequiresPreparation,"native request requires neither preparation lane");
            var packaged=Request();packaged.WeaponArchiveHash=archive;byte[] wire=Bytes(packaged);
            Check(wire.SequenceEqual(Golden(packaged,0,archive)),"independent archive vector pins raw digest tail and prior nonce/cookie offsets");
            Check(wire.AsSpan(oldLength,32).SequenceEqual(Convert.FromHexString(archive)),"archive is raw 32-byte digest, without local path metadata");
            var decoded=HostRequestPacket.Read(wire);Check(decoded.Protocol==83&&decoded.WeaponArchiveHash==archive&&decoded.HostNonce==packaged.HostNonce&&decoded.HostCookie==packaged.HostCookie,"package request canonical lowercase digest and challenge proof round trip");
            Check(!decoded.RequiresMapPreparation&&decoded.RequiresWeaponPreparation&&decoded.RequiresPreparation,"weapon-only request enters preparation without map download");
            Check(string.IsNullOrEmpty(HostRequestPacket.Read(absent).WeaponArchiveHash),"zero wire tail decodes absent native selection");
            foreach(string bad in new[]{archive.ToUpperInvariant(),new string('a',63),new string('g',64),new string('0',64),"C:\\private\\weapon.ppweapon"}){var invalid=packaged;invalid.WeaponArchiveHash=bad;Reject(invalid,"write rejects invalid hash: "+bad);}
            for(int length=0;length<wire.Length;length++)Check(HostRequestPacket.Read(wire.AsSpan(0,length)).Protocol==0,"truncated request rejected at "+length);
            for(int extra=1;extra<=32;extra++){byte[] trailing=new byte[wire.Length+extra];wire.CopyTo(trailing,0);Check(HostRequestPacket.Read(trailing).Protocol==0,"trailing request bytes rejected: "+extra);}
            foreach((int offset,byte value) in new[]{(79,(byte)17),(80,(byte)2),(81,(byte)2),(82,(byte)2),(83,(byte)255),(164,(byte)128)})
            {var malformed=(byte[])wire.Clone();malformed[offset]=value;Check(HostRequestPacket.Read(malformed).Protocol==0,"malformed count/policy/map field rejected at "+offset);}
            var maximum=packaged;maximum.Rotation=Enumerable.Range(0,16).Select(i=>new HostRotationEntry("ROOM"+i,GameMode.Battle,default)).ToArray();byte[] full=Bytes(maximum);
            Check(full.SequenceEqual(Golden(maximum,16,archive)),"sixteen-entry independent rotation vector has exact digest tail");
            Check(full.Length+NetHeader.Size<=1472&&NetConfig.MaxPacketSize==1472,"maximum rotation plus actual transport header obeys 1472-byte datagram ceiling");
            Check(HostRequestPacket.Read(full).Rotation?.Count==16,"maximum rotation round trips all sixteen entries");
            var endpoint=new IPEndPoint(IPAddress.Parse("127.64.12.5"),31283);const double now=390;
            var proof=HostRequestGuard.Challenge(endpoint,new HostChallengePacket(83,packaged.HostNonce),now);packaged.HostCookie=proof.Cookie;
            Check(HostRequestGuard.Validate(endpoint,packaged,now+1,out _),"actual challenge API authorizes selected archive");
            Check(HostRequestGuard.Validate(endpoint,packaged,now+90,out _),"exact selected-archive retry retains accepted authorization beyond cookie window");
            var changed=packaged;changed.WeaponArchiveHash=new string('b',64);
            Check(!HostRequestGuard.Validate(endpoint,changed,now+91,out var reason)&&reason.Contains("different request",StringComparison.Ordinal),"same nonce/cookie cannot substitute another archive");
            changed.WeaponArchiveHash=null;Check(!HostRequestGuard.Validate(endpoint,changed,now+92,out _),"same authorization cannot remove archive requirement");
            Check(HostRequestGuard.Validate(endpoint,packaged,now+93,out _),"rejected substitutions do not poison original exact retry");
            Console.WriteLine($"PASS {checks} remote weapon package host-request wire/guard checks. No allocator, UDP handshake or older-server rejection claim.");return 0;
        }
        catch(Exception error){Console.Error.WriteLine("REMOTE PACKAGE REQUEST FAIL "+error);return 1;}
    }
}
