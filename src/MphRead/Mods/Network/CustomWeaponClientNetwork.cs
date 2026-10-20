using System.Buffers.Binary;
using MphRead.Mods.CustomWeapons;

namespace MphRead.Mods.Network;

public static partial class NetSession
{
    private static CustomClientCombat? _customCombatClient;
    private static CustomReadinessWire? _customCombatWire;
    internal static CustomStateMessage? CustomWeaponPresentationState => CustomWeaponReadinessConfirmed ? _customCombatClient?.State:null;
    internal static CustomReceiptMessage? LastCustomWeaponReceipt => CustomWeaponReadinessConfirmed ? _customCombatClient?.LastReceipt:null;
    internal static CustomContactMessage? LastCustomWeaponContact => CustomWeaponReadinessConfirmed ? _customCombatClient?.LastContact:null;
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
        if (!action.IsValid || action.Slot!=LocalSlot || !NetPlayerLifecycle.Matches(LocalSlot,action.Owner.Generation,action.Life)) return false;
        return CurrentCustomCombat()?.Submit(action,Clock)==true;
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
        System.Span<byte> bytes=stackalloc byte[CustomWeaponAction.Size];
        for (int n=0;n<4 && client.TryNext(now,out var action);n++)
        {
            action.Write(bytes);
            if (!_transport.TrySendSemantic(_hostEndPoint,PacketType.CustomWeaponAction,bytes)) break;
            client.Sent(action,now);
        }
        if (client.Invalidated) _customReadiness?.Invalidate();
    }
}
