using System;
using System.Buffers.Binary;
using System.Security.Cryptography;
using MphRead.Mods.Network;

namespace MphRead.Mods.CustomWeapons;

// Public item facts contain no owner resources, grant nonce or client claim.
internal readonly record struct CustomPickupReplicaState(WorldBootstrapIdentity Bootstrap,CustomPickupWorld World,
    byte Index,byte Count,uint AuthorityFrame,ulong Serial,CustomPickupItemState Item);
internal readonly record struct CustomPickupReplicaAck(WorldBootstrapIdentity Bootstrap,CustomPickupWorld World,
    byte Index,ulong Serial,WeaponDigest Digest);
internal static class CustomPickupReplicaCodec
{
    internal const int StateSize=134,AckSize=138;
    private static bool Before(uint a,uint b)=>unchecked((int)(a-b))<0;
    internal static bool Valid(in CustomPickupReplicaState value)
        =>CustomWeaponProtocol.ValidBootstrap(value.Bootstrap)&&value.World.IsValid&&value.Bootstrap.Start==value.World.Start
            &&value.Count is >0 and <=CustomPickupLayout.Capacity&&value.Index<value.Count&&value.Serial!=0
            &&!Before(value.AuthorityFrame,value.Bootstrap.AuthorityFrame)&&value.Item.Key.IsValid&&value.Item.Key.World==value.World
            &&(value.Item.Revision==0)==(value.Item.Key.Incarnation==1)
            &&value.Item.Available==!Before(value.AuthorityFrame,value.Item.ReadyAt);
    internal static bool Matches(in CustomPickupReplicaState value,CustomPickupLayout layout)
        =>Valid(value)&&value.World.Layout==layout.Digest&&value.World.Manifest==layout.Manifest&&value.Count==layout.Count
            &&value.Item.Key.SpawnId==layout.Placements[value.Index].SpawnId;
    internal static void Write(in CustomPickupReplicaState value,Span<byte> bytes)
    {
        if(bytes.Length<StateSize||!Valid(value))throw new ArgumentException("Valid public pickup state required.");
        CustomWeaponProtocol.WriteHeader(bytes,value.Bootstrap,value.World.Manifest);value.World.Layout.Write(bytes[65..]);
        bytes[97]=value.Index;bytes[98]=value.Count;BinaryPrimitives.WriteUInt32LittleEndian(bytes[99..],value.AuthorityFrame);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes[103..],value.Serial);BinaryPrimitives.WriteUInt16LittleEndian(bytes[111..],value.Item.Key.SpawnId);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes[113..],value.Item.Key.Incarnation);BinaryPrimitives.WriteUInt32LittleEndian(bytes[121..],value.Item.ReadyAt);
        bytes[125]=value.Item.Available?(byte)1:(byte)0;BinaryPrimitives.WriteUInt64LittleEndian(bytes[126..],value.Item.Revision);
    }
    internal static bool TryReadState(ReadOnlySpan<byte> bytes,out CustomPickupReplicaState value)
    {
        value=default;if(bytes.Length!=StateSize||!CustomWeaponProtocol.ReadHeader(bytes,out var bootstrap,out var manifest)||bytes[125]>1)return false;
        var world=new CustomPickupWorld(bootstrap.Start,manifest,WeaponDigest.Read(bytes[65..97]));
        var item=new CustomPickupItemState(new(world,BinaryPrimitives.ReadUInt16LittleEndian(bytes[111..]),BinaryPrimitives.ReadUInt64LittleEndian(bytes[113..])),
            BinaryPrimitives.ReadUInt32LittleEndian(bytes[121..]),bytes[125]!=0,BinaryPrimitives.ReadUInt64LittleEndian(bytes[126..]));
        var candidate=new CustomPickupReplicaState(bootstrap,world,bytes[97],bytes[98],BinaryPrimitives.ReadUInt32LittleEndian(bytes[99..]),
            BinaryPrimitives.ReadUInt64LittleEndian(bytes[103..]),item);
        if(!Valid(candidate))return false;value=candidate;return true;
    }
    internal static WeaponDigest Hash(ReadOnlySpan<byte> bytes)
    {Span<byte> digest=stackalloc byte[32];SHA256.HashData(bytes,digest);return WeaponDigest.Read(digest);}
    internal static CustomPickupReplicaAck Ack(in CustomPickupReplicaState value,ReadOnlySpan<byte> bytes)
        =>new(value.Bootstrap,value.World,value.Index,value.Serial,Hash(bytes));
    internal static void Write(in CustomPickupReplicaAck value,Span<byte> bytes)
    {
        if(bytes.Length<AckSize||!CustomWeaponProtocol.ValidBootstrap(value.Bootstrap)||!value.World.IsValid||value.Bootstrap.Start!=value.World.Start
            ||value.Index>=CustomPickupLayout.Capacity||value.Serial==0||!value.Digest.IsValid)throw new ArgumentException("Exact pickup acknowledgement required.");
        CustomWeaponProtocol.WriteHeader(bytes,value.Bootstrap,value.World.Manifest);value.World.Layout.Write(bytes[65..]);bytes[97]=value.Index;
        BinaryPrimitives.WriteUInt64LittleEndian(bytes[98..],value.Serial);value.Digest.Write(bytes[106..]);
    }
    internal static bool TryReadAck(ReadOnlySpan<byte> bytes,out CustomPickupReplicaAck value)
    {
        value=default;if(bytes.Length!=AckSize||!CustomWeaponProtocol.ReadHeader(bytes,out var bootstrap,out var manifest))return false;
        var world=new CustomPickupWorld(bootstrap.Start,manifest,WeaponDigest.Read(bytes[65..97]));
        var candidate=new CustomPickupReplicaAck(bootstrap,world,bytes[97],BinaryPrimitives.ReadUInt64LittleEndian(bytes[98..]),WeaponDigest.Read(bytes[106..138]));
        if(!world.IsValid||candidate.Index>=CustomPickupLayout.Capacity||candidate.Serial==0||!candidate.Digest.IsValid)return false;
        value=candidate;return true;
    }
}

// One bounded immutable outstanding fact per authored item per recipient. A
// funded incarnation cannot overwrite an unacknowledged public transition.
internal sealed class CustomPickupReplicaMailbox
{
    private readonly WorldBootstrapIdentity _bootstrap;
    private readonly CustomPickupWorld _world;
    private readonly CustomPickupLayout _layout;
    private readonly byte[][] _bytes;
    private readonly CustomPickupReplicaState?[] _states;
    private readonly WeaponDigest[] _digests;
    private readonly bool[] _acked,_sent;
    private readonly double[] _due,_created;
    private ulong _serial;
    internal bool Closed {get;private set;}
    internal bool IsReady
    {get{if(Closed)return false;for(int i=0;i<_layout.Count;i++)if(!_acked[i])return false;return true;}}
    internal CustomPickupReplicaMailbox(WorldBootstrapIdentity bootstrap,CustomPickupWorld world,CustomPickupLayout layout)
    {
        if(!CustomWeaponProtocol.ValidBootstrap(bootstrap)||bootstrap.Start!=world.Start||world.Layout!=layout.Digest||world.Manifest!=layout.Manifest)
            throw new ArgumentException("Exact recipient and frozen pickup world required.");
        _bootstrap=bootstrap;_world=world;_layout=layout;int n=layout.Count;
        _bytes=new byte[n][];_states=new CustomPickupReplicaState?[n];_digests=new WeaponDigest[n];
        _acked=new bool[n];_sent=new bool[n];_due=new double[n];_created=new double[n];
        for(int i=0;i<n;i++)_bytes[i]=new byte[CustomPickupReplicaCodec.StateSize];
    }
    internal bool CanChange(int index)=>!Closed&&_serial<ulong.MaxValue&&(uint)index<_states.Length&&(_states[index]==null||_acked[index]);
    internal bool Publish(int index,in CustomPickupItemState item,uint world,double now)
    {
        if(Closed||(uint)index>=_states.Length||!double.IsFinite(now)||now<0)return false;
        if(_states[index] is {} prior&&prior.Item==item)return true;
        if(!CanChange(index)||_serial==ulong.MaxValue)return false;
        var value=new CustomPickupReplicaState(_bootstrap,_world,(byte)index,(byte)_layout.Count,world,_serial+1,item);
        if(!CustomPickupReplicaCodec.Matches(value,_layout))return false;
        CustomPickupReplicaCodec.Write(value,_bytes[index]);_digests[index]=CustomPickupReplicaCodec.Hash(_bytes[index]);
        _states[index]=value;_serial++;_acked[index]=_sent[index]=false;_due[index]=now;_created[index]=now;return true;
    }
    internal bool Acknowledge(ReadOnlySpan<byte> bytes)
    {
        if(Closed||!CustomPickupReplicaCodec.TryReadAck(bytes,out var ack)||ack.Bootstrap!=_bootstrap||ack.World!=_world
            ||ack.Index>=_states.Length||!_sent[ack.Index]||_states[ack.Index] is not {} state||state.Serial!=ack.Serial
            ||_digests[ack.Index]!=ack.Digest)return false;
        _acked[ack.Index]=true;return true;
    }
    internal bool TryNext(double now,out int index,out ReadOnlyMemory<byte> bytes)
    {
        index=0;bytes=default;if(Closed||!double.IsFinite(now))return false;
        for(int i=0;i<_states.Length;i++)if(_states[i]!=null&&!_acked[i]&&now>=_due[i])
        {index=i;bytes=_bytes[i];return true;}return false;
    }
    internal void Sent(int index,double now){_sent[index]=true;_due[index]=now+.25;}
    internal bool HasExpired(double now)
    {for(int i=0;i<_states.Length;i++)if(_states[i]!=null&&!_acked[i]&&now-_created[i]>15)return true;return false;}
    internal void Close()=>Closed=true;
}

internal sealed class CustomPickupReplicaReceiver
{
    private readonly WorldBootstrapIdentity _bootstrap;
    internal CustomPickupLayout Layout {get;}
    private readonly CustomPickupReplicaState?[] _states;
    internal bool Closed {get;private set;}
    internal bool IsReady
    {get{if(Closed)return false;foreach(var state in _states)if(state==null)return false;return true;}}
    internal CustomPickupReplicaReceiver(WorldBootstrapIdentity bootstrap,CustomPickupLayout layout)
    {_bootstrap=bootstrap;Layout=layout;_states=new CustomPickupReplicaState?[layout.Count];}
    internal bool Receive(ReadOnlySpan<byte> bytes,out CustomPickupReplicaAck ack)
    {
        ack=default;if(Closed||!CustomPickupReplicaCodec.TryReadState(bytes,out var value)||value.Bootstrap!=_bootstrap
            ||!CustomPickupReplicaCodec.Matches(value,Layout))return false;
        if(_states[value.Index] is {} previous)
        {
            if(value.Serial<previous.Serial)return false;
            if(value.Serial==previous.Serial)
            {if(value!=previous){Closed=true;return false;}ack=CustomPickupReplicaCodec.Ack(value,bytes);return true;}
            if(unchecked((int)(value.AuthorityFrame-previous.AuthorityFrame))<0||value.Item.Key.Incarnation<previous.Item.Key.Incarnation
                ||value.Item.Revision<previous.Item.Revision)return false;
            if(value.Item.Key.Incarnation==previous.Item.Key.Incarnation
                ?value.Item.Revision!=previous.Item.Revision||value.Item.ReadyAt!=previous.Item.ReadyAt
                    ||previous.Item.Available&&!value.Item.Available
                :value.Item.Revision<=previous.Item.Revision)return false;
        }
        _states[value.Index]=value;ack=CustomPickupReplicaCodec.Ack(value,bytes);return true;
    }
    internal bool TryState(int index,out CustomPickupItemState item)
    {item=default;if(!IsReady||(uint)index>=_states.Length||_states[index] is not {} state)return false;item=state.Item;return true;}
    internal bool TryAcceptedState(int index,out CustomPickupReplicaState state)
    {state=default;if(Closed||(uint)index>=_states.Length||_states[index] is not {} value)return false;state=value;return true;}
}
