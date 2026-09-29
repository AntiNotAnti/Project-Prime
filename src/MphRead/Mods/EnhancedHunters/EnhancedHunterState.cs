using System;

namespace MphRead.Mods.EnhancedHunters;

/// <summary>Owned by a single player/scene. Timers are simulation frames at 60 Hz.</summary>
public sealed class EnhancedHunterState
{
    public byte TargetSlot = byte.MaxValue;
    public ushort TargetLifeId, TargetGeneration;
    public byte ValueA, ValueB, Flags;
    public int TimerA, TimerB, ContactFrames, LastContactFrame = -1, MovementCooldown;
    public int GhostFrames, DecayFrames, CrossfireFrame = -1000, CrossfireCooldown;
    public byte CrossfireTarget = byte.MaxValue;
    public ushort CrossfireLife;
    public bool WasAlt, WasAttacking, WasFiring, StationaryShot;
    public EnhancedMovementEvent Impulse0, Impulse1;
    public ushort AppliedImpulse;
    public OpenTK.Mathematics.Vector3 PredictedImpulse;
    public int PredictedUntil;
    public byte[] FrostStacks = new byte[8], BrittleTiers = new byte[8];
    public int[] FrostExpiry = new int[8], CrossfireExpiry = new int[8];
    public ushort[] FrostLives = new ushort[8], FrostGenerations = new ushort[8];
    public bool[] Brittle = new bool[8];
    public ushort LocalLife;
    public Hunter Hunter;

    public void ClearTarget()
    {
        TargetSlot = byte.MaxValue; TargetLifeId = TargetGeneration = 0;
        ValueA = ValueB = Flags = 0; TimerA = TimerB = ContactFrames = 0;
        LastContactFrame = -1;
    }

    public void Reset()
    {
        PredictedImpulse = default; PredictedUntil = 0;
        Array.Clear(FrostStacks); Array.Clear(BrittleTiers); Array.Clear(FrostExpiry);
        Array.Clear(FrostLives); Array.Clear(FrostGenerations); Array.Clear(Brittle); Array.Clear(CrossfireExpiry);
        Impulse0 = Impulse1 = default; AppliedImpulse = 0;
        ClearTarget(); MovementCooldown = GhostFrames = DecayFrames = CrossfireCooldown = 0;
        CrossfireFrame = -1000; CrossfireTarget = byte.MaxValue; CrossfireLife = 0;
        WasAlt = WasAttacking = WasFiring = StationaryShot = false;
    }
}
