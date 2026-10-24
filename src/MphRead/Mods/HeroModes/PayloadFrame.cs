using System;
using System.IO;
namespace MphRead.Mods.HeroModes;

/// <summary>One bounded payload fact, carried inside the existing match/epoch/tick-fenced world snapshot.</summary>
internal readonly record struct PayloadFrame(int Round,int Pair,int Attacker,byte Phase,int Setup,int Clock,
    long Tick,long Progress,long Best,long Length,int Checkpoints,int Hold,bool Overtime,int Fuse,bool Contested,int Escorts,
    long Target,long AlphaBest,long BravoBest,int AlphaPoints,int BravoPoints,int AlphaBank,int BravoBank,int Winner,bool Finished)
{
    internal bool Valid=>Round is >=0 and <=65535 && Pair is >=0 and <=32767 && Attacker is >=0 and <=1 && Phase<=2
        && Setup is >=0 and <=36000 && Clock is >=0 and <=5184000 && Tick>=0 && Tick<=51840000
        && Length is >0 and <=600000000 && Progress>=0 && Progress<=Length && Best>=Progress && Best<=Length
        && Checkpoints is >=0 and <=2 && Hold is >=0 and <=36000 && Fuse is >=0 and <=36000 && Escorts is >=0 and <=4
        && Target>=-1 && Target<=Length && AlphaBest>=0&&AlphaBest<=Length&&BravoBest>=0&&BravoBest<=Length
        && AlphaPoints is >=0 and <=100000 && BravoPoints is >=0 and <=100000
        && AlphaBank is >=0 and <=5184000 && BravoBank is >=0 and <=5184000 && Winner is >=-1 and <=1
        && (!Finished || Phase==2) && (Finished || Winner==-1);
    internal void Write(BinaryWriter w)
    {
        if(!Valid)throw new InvalidDataException("Invalid payload authority state.");
        w.Write(Round);w.Write(Pair);w.Write((byte)Attacker);w.Write(Phase);w.Write(Setup);w.Write(Clock);
        w.Write(Tick);w.Write(Progress);w.Write(Best);w.Write(Length);w.Write((byte)Checkpoints);w.Write(Hold);w.Write(Overtime);w.Write(Fuse);w.Write(Contested);w.Write((byte)Escorts);
        w.Write(Target);w.Write(AlphaBest);w.Write(BravoBest);w.Write(AlphaPoints);w.Write(BravoPoints);w.Write(AlphaBank);w.Write(BravoBank);w.Write((sbyte)Winner);w.Write(Finished);
    }
    internal static PayloadFrame Read(BinaryReader r)
    {
        var v=new PayloadFrame(r.ReadInt32(),r.ReadInt32(),r.ReadByte(),r.ReadByte(),r.ReadInt32(),r.ReadInt32(),r.ReadInt64(),r.ReadInt64(),r.ReadInt64(),r.ReadInt64(),
            r.ReadByte(),r.ReadInt32(),Bool(r),r.ReadInt32(),Bool(r),r.ReadByte(),r.ReadInt64(),r.ReadInt64(),r.ReadInt64(),r.ReadInt32(),r.ReadInt32(),r.ReadInt32(),r.ReadInt32(),r.ReadSByte(),Bool(r));
        if(!v.Valid)throw new InvalidDataException("Invalid payload authority state.");return v;
    }
    private static bool Bool(BinaryReader r)=>r.ReadByte() switch {0=>false,1=>true,_=>throw new InvalidDataException("Invalid payload boolean.")};
}
