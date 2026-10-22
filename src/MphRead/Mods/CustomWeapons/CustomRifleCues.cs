using System;
using System.Buffers.Binary;

namespace MphRead.Mods.CustomWeapons;

internal enum CustomRifleCue : byte { Shot,Bolt,Reload,Empty }

// Original CC0 signal designs and generated PCM; see the M7 asset README.
// Integer oscillators, envelopes and sample-index hashing make the bytes stable
// across platforms. No gameplay RNG, audio device, native sample bank or clock.
internal static class CustomRifleCues
{
    private const int Rate=22050;
    private readonly record struct Shape(int Frames,int Active,int Attack,int StartHz,int EndHz,
        int Body,int Overtone,int Noise,int Gain);

    internal static byte[] BuildWave(CustomRifleCue cue)
    {
        var shape=cue switch
        {
            CustomRifleCue.Shot=>new Shape(4410,3969,33,1550,115,70,17,40,22000),
            CustomRifleCue.Bolt=>new Shape(2646,1984,22,1850,700,35,34,58,17000),
            CustomRifleCue.Reload=>new Shape(8820,2646,22,980,380,48,23,56,18000),
            CustomRifleCue.Empty=>new Shape(1764,1323,11,2200,1400,24,48,55,11000),
            _=>throw new ArgumentOutOfRangeException(nameof(cue))
        };
        var wave=new byte[44+shape.Frames*2];
        "RIFF"u8.CopyTo(wave);BinaryPrimitives.WriteInt32LittleEndian(wave.AsSpan(4),wave.Length-8);
        "WAVEfmt "u8.CopyTo(wave.AsSpan(8));BinaryPrimitives.WriteInt32LittleEndian(wave.AsSpan(16),16);
        BinaryPrimitives.WriteInt16LittleEndian(wave.AsSpan(20),1); // PCM, mono,16-bit.
        BinaryPrimitives.WriteInt16LittleEndian(wave.AsSpan(22),1);
        BinaryPrimitives.WriteInt32LittleEndian(wave.AsSpan(24),Rate);
        BinaryPrimitives.WriteInt32LittleEndian(wave.AsSpan(28),Rate*2);
        BinaryPrimitives.WriteInt16LittleEndian(wave.AsSpan(32),2);
        BinaryPrimitives.WriteInt16LittleEndian(wave.AsSpan(34),16);
        "data"u8.CopyTo(wave.AsSpan(36));BinaryPrimitives.WriteInt32LittleEndian(wave.AsSpan(40),shape.Frames*2);
        uint phase=0;
        for(int i=0;i<shape.Frames;i++)
        {
            int local=i,active=shape.Active,start=shape.StartHz,end=shape.EndHz;
            // Two independently shaped mechanical transients, separated by
            // silence. This short cue never claims to time gameplay reload.
            if(cue==CustomRifleCue.Reload&&i>=4851)
            {local=i-4851;active=3528;start=1480;end=520;if(local==0)phase=0;}
            int sample=0;
            if(local<active)
            {
                int frequency=start+(end-start)*local/(active-1);
                phase=unchecked(phase+(uint)(((long)frequency<<32)/Rate));
                int attack=Math.Min(32767,local*32767/shape.Attack);
                int release=(active-1-local)*32767/(active-1);
                long envelope=(long)attack*release/32767*release/32767;
                int mix=(Triangle(phase)*shape.Body+Triangle(unchecked(phase*3))*shape.Overtone
                    +Noise(i,(uint)cue)*shape.Noise)/127;
                sample=(int)((long)mix*envelope*shape.Gain/(32767L*32768));
            }
            BinaryPrimitives.WriteInt16LittleEndian(wave.AsSpan(44+i*2),(short)sample);
        }
        return wave;
    }
    private static int Triangle(uint phase)
    {int cycle=(int)(phase>>16);return cycle<32768?cycle*2-32768:98303-cycle*2;}
    private static int Noise(int sample,uint cue)
    {
        uint value=unchecked((uint)sample*0x9e3779b9u)^unchecked(0x6d375ac1u+cue*0x85ebca6bu);
        value^=value>>16;value=unchecked(value*0x7feb352du);
        value^=value>>15;value=unchecked(value*0x846ca68bu);value^=value>>16;
        return (int)(value>>16)-32768;
    }
}
