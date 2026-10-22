using System;
using System.Buffers.Binary;
using MphRead.Mods.CustomWeapons;
using MphRead.Entities;

namespace MphRead.Mods.Network;

public static partial class NetSession
{
    private static CustomClientCombat? _customCombatClient;
    private static CustomReadinessWire? _customCombatWire;
    internal static WeaponDigest CustomWeaponPresentationManifest=>CustomWeaponReplicaReady?_customReadiness!.Wire.Manifest.Digest:default;
    internal static WeaponMechanics? CustomWeaponPresentationMechanics=>CustomWeaponReplicaReady?_customReadiness?.Wire.Definition.Mechanics:null;
    internal static CustomStateMessage? CustomWeaponPresentationState => CustomWeaponReadinessConfirmed ? _customCombatClient?.State:null;
    internal static CustomReceiptMessage? LastCustomWeaponReceipt => CustomWeaponReadinessConfirmed ? _customCombatClient?.LastReceipt:null;
    internal static CustomContactMessage? LastCustomWeaponContact => CustomWeaponReadinessConfirmed ? _customCombatClient?.LastContact:null;
    internal static CustomControlDenied? LastCustomControlDenied => CustomWeaponReadinessConfirmed ? _customCombatClient?.LastControlDenied:null;
    internal static CustomInputProjection? CustomWeaponInputProjection=>CustomWeaponReplicaReady
        &&CurrentCustomCombat() is {} client&&client.InputProjection is {} projection
        &&projection.Owner.Slot==LocalSlot&&NetPlayerLifecycle.Matches(LocalSlot,projection.Owner.Generation,projection.Owner.Life)
        ?projection:null;
    internal static bool TakeCustomPresentationOverflowed()=>CurrentCustomCombat()?.TakePresentationOverflowed()==true;
    internal static bool TryTakeCustomPresentationResult(out CustomPresentationResult result)
    {result=default;return CurrentCustomCombat()?.TryTakePresentationResult(out result)==true;}
    private static CustomClientCombat? CurrentCustomCombat()
    {
        if (!CustomWeaponReadinessConfirmed || _customReadiness is not { } ready)
        { _customCombatClient=null; _customCombatWire=null; return null; }
        if (!ReferenceEquals(_customCombatWire,ready.Wire))
        { _customCombatWire=ready.Wire; _customCombatClient=new(ready.Wire); }
        return _customCombatClient;
    }
    internal static bool SubmitCustomWeaponAction(in CustomWeaponAction action)
    {
        if (!CustomWeaponReplicaReady || !action.IsValid || action.Slot!=LocalSlot || !NetPlayerLifecycle.Matches(LocalSlot,action.Owner.Generation,action.Life)) return false;
        return CurrentCustomCombat()?.Submit(action,Clock)==true;
    }
    internal static bool SubmitCustomWeaponControl(in CustomControlRequest request)
    {
        if (!CustomWeaponReplicaReady || !request.IsValid || request.Slot!=LocalSlot || !NetPlayerLifecycle.Matches(LocalSlot,request.Owner.Generation,request.Life)) return false;
        return CurrentCustomCombat()?.Submit(request,Clock)==true;
    }
    internal static bool TrySubmitCustomWeaponControl(CustomWeaponActionKind kind,byte optic,out CustomControlRequest request)
    {
        request=default;
        if (!CustomWeaponReplicaReady || CurrentCustomCombat() is not { State: {HasWeapon:true} state } client
            || state.State.Owner.Slot!=LocalSlot
            || !NetPlayerLifecycle.Matches(LocalSlot,state.State.Owner.Generation,state.State.Owner.Life)) return false;
        return client.TrySubmitControl(kind,optic,Clock,out request);
    }
    private static void HandleCustomCombatResult(ReceivedPacket packet)
    {
        var client=CurrentCustomCombat(); if (client==null) return;
        // Initial/new-life state can beat its native lifecycle snapshot. The
        // periodic authority state recovers after that snapshot is accepted.
        if (packet.Type==PacketType.CustomWeaponState && (packet.Payload.Length!=CustomStateMessage.Size
            || BinaryPrimitives.ReadUInt16LittleEndian(packet.Payload[66..])!=NetPlayerLifecycle.Get(LocalSlot))) return;
        if (client.Receive(packet.Type,packet.Payload,out var ack) && ack.IsValid && _transport!=null && _hostEndPoint!=null)
        {
            System.Span<byte> bytes=stackalloc byte[CustomResultAck.Size]; ack.Write(bytes);
            _transport.TrySendSemantic(_hostEndPoint,PacketType.CustomWeaponResultAck,bytes);
        }
        if (client.Invalidated) _customReadiness?.Invalidate();
    }
    private static void PumpCustomCombat(double now)
    {
        var client=CurrentCustomCombat(); if (client==null || _transport==null || _hostEndPoint==null) return;
        System.Span<byte> bytes=stackalloc byte[CustomPrivateAction.Size];
        for (int n=0;n<4 && client.TryNext(now,out var action);n++)
        {
            if(!client.TryActionKnowledge(action,out var knowledge))break;
            new CustomPrivateAction(action,knowledge).Write(bytes);
            if (!_transport.TrySendSemantic(_hostEndPoint,PacketType.CustomWeaponAction,bytes)) break;
            client.Sent(action,now);
        }
        for (int n=0;n<4 && client.TryNextControl(now,out var request);n++)
        {
            request.Write(bytes);
            if (!_transport.TrySendSemantic(_hostEndPoint,PacketType.CustomWeaponControl,bytes[..CustomControlRequest.Size])) break;
            client.Sent(request,now);
        }
        if (client.Invalidated) _customReadiness?.Invalidate();
    }
    private static bool SendCustomInputCarrier(in IntentPacket intent,System.ReadOnlySpan<byte> native,CustomCapturedKnowledge captured)
    {
        if (_transport==null || _hostEndPoint==null || !TryEncodeCustomWeaponInput(intent,native,out var bytes,captured)) return false;
        _transport.Send(_hostEndPoint,PacketType.CustomWeaponInput,bytes.Span); return true;
    }
    internal static CustomCapturedKnowledge CaptureCustomWeaponInputKnowledge(PlayerEntity player)
    {
        if(!CustomWeaponReplicaReady||player.SlotIndex!=LocalSlot
            ||CurrentCustomCombat() is not {State:{HasWeapon:true} state}||!state.Knowledge.IsPresent
            ||state.State.Owner.Slot!=LocalSlot||state.State.Owner.MatchId!=CurrentMatchId||state.State.Owner.AuthorityEpoch!=AuthorityEpoch
            ||!NetPlayerLifecycle.Matches(LocalSlot,state.State.Owner.Generation,state.State.Owner.Life))return default;
        return new(state.State.Owner,state.Bootstrap,state.Manifest,state.Knowledge,NetFrame);
    }
    // The returned memory is borrowed until the next client input encoding.
    // Transport copies it synchronously; callers must not retain this buffer.
    internal static bool TryEncodeCustomWeaponInput(in IntentPacket intent,System.ReadOnlySpan<byte> native,out ReadOnlyMemory<byte> bytes,CustomCapturedKnowledge captured=default)
    {
        bytes=default;
        if (!captured.IsValid || !CustomWeaponReplicaReady || _transport==null || _hostEndPoint==null || CurrentCustomCombat() is not { } client
            || LocalSlot is <0 or >=8 || intent.MatchId==0 || intent.AuthorityEpoch==0 || intent.SlotGeneration==0 || intent.LifeId==0) return false;
        var owner=new WeaponOwnerContext(intent.MatchId,intent.AuthorityEpoch,(byte)LocalSlot,intent.SlotGeneration,intent.LifeId);
        var wire=_customCombatWire!;
        if(!captured.IsEmpty&&(captured.SourceFrame!=intent.Frame||captured.Owner!=owner||captured.Bootstrap!=wire.Bootstrap||captured.Manifest!=wire.Manifest.Digest))return false;
        // The caller's source and independently encoded native bytes must name
        // the same input even when MTU pressure omits the repeated control.
        if(!CompactIntentCodec.TryRead(native,out var decoded)||decoded.Frame!=intent.Frame
            ||decoded.MatchId!=intent.MatchId||decoded.AuthorityEpoch!=intent.AuthorityEpoch
            ||decoded.SlotGeneration!=intent.SlotGeneration||decoded.LifeId!=intent.LifeId)return false;
        CustomControlRequest? control=client.TryInputControl(owner,Clock,out var pending)?pending:null;
        if(client.Invalidated){_customReadiness?.Invalidate();return false;}
        if(control==null&&captured.IsEmpty)return false;
        // MTU pressure may omit only the repeated control: reliable91 retains
        // that exact request. Keep original input knowledge and native bytes.
        if(control!=null&&!captured.IsEmpty&&native.Length>CustomInputCarrier.MaxPayload-CustomInputCarrier.PrefixSize
            &&native.Length<=CustomInputCarrier.MaxPayload-CustomInputCarrier.EmptyPrefixSize)control=null;
        if (!CustomInputCarrier.TryWrite(wire.Bootstrap,wire.Manifest.Digest,captured.Knowledge,control,native,client.InputBytes,out int length)) return false;
        bytes=client.InputBytes.AsMemory(0,length); return true;
    }
}
