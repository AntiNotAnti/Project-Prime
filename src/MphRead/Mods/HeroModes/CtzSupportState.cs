using System;
using System.IO;
namespace MphRead.Mods.HeroModes;

/// <summary>Authority-only, lifecycle-fenced support presentation. Never contains owner transforms.</summary>
public readonly record struct CtzSupportState(byte Slot,ushort Generation,ushort Life,ushort FreezeCooldown,
    ushort AuraRemaining,byte AuraPulses,sbyte HealTarget,ushort TargetGeneration,ushort TargetLife,ushort TargetTicks)
{
    public bool Valid=>Slot<8 && FreezeCooldown<=CtzRules.NoxusFreezeCooldownTicks && AuraRemaining<=CtzRules.HealingAuraTicks
        && AuraPulses<=CtzRules.SpireAuraPulses && HealTarget is >=-1 and <8 && TargetTicks<=CtzRules.MaximumHealedAllyRevealTicks;
    public void Write(BinaryWriter w)
    {
        if(!Valid) throw new InvalidDataException("Invalid CTZ support state.");
        w.Write(Slot);w.Write(Generation);w.Write(Life);w.Write(FreezeCooldown);w.Write(AuraRemaining);w.Write(AuraPulses);
        w.Write(HealTarget);w.Write(TargetGeneration);w.Write(TargetLife);w.Write(TargetTicks);
    }
    public static CtzSupportState Read(BinaryReader r)
    {
        var s=new CtzSupportState(r.ReadByte(),r.ReadUInt16(),r.ReadUInt16(),r.ReadUInt16(),r.ReadUInt16(),r.ReadByte(),
            r.ReadSByte(),r.ReadUInt16(),r.ReadUInt16(),r.ReadUInt16());
        if(!s.Valid) throw new InvalidDataException("Invalid CTZ support state."); return s;
    }
}
