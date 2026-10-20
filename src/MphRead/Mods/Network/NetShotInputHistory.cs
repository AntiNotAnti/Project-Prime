using System;
using MphRead.Entities;

namespace MphRead.Mods.Network;

/// <summary>Live-only physical controls, newest source frame first. No derived charge or scope state.</summary>
public struct ShotInputHistory
{
    public const int Capacity = 32;
    public const int WireSize = 1 + Capacity;
    public const byte ShootBit = 1 << 4, ZoomBit = 1 << 5, EligibleBit = 1 << 6;
    private ulong _a, _b, _c, _d;

    public byte this[int index]
    {
        readonly get
        {
            if ((uint)index >= Capacity) throw new ArgumentOutOfRangeException(nameof(index));
            ulong word = (index / 8) switch { 0 => _a, 1 => _b, 2 => _c, _ => _d };
            return (byte)(word >> ((index % 8) * 8));
        }
        set
        {
            if ((uint)index >= Capacity) throw new ArgumentOutOfRangeException(nameof(index));
            int shift = (index % 8) * 8;
            ulong mask = ~(255UL << shift), bits = (ulong)value << shift;
            switch (index / 8)
            {
                case 0: _a = (_a & mask) | bits; break;
                case 1: _b = (_b & mask) | bits; break;
                case 2: _c = (_c & mask) | bits; break;
                default: _d = (_d & mask) | bits; break;
            }
        }
    }

    public static byte Sample(byte weapon, bool shoot, bool physicalZoom, bool eligible)
    {
        if (weapon > 8) throw new ArgumentOutOfRangeException(nameof(weapon));
        return (byte)(weapon | (shoot ? ShootBit : 0) | (physicalZoom ? ZoomBit : 0)
            | (eligible ? EligibleBit : 0));
    }

    public static bool ValidSample(byte sample) => (sample & 0x80) == 0 && (sample & 0x0f) <= 8;
    public readonly bool Valid(byte count)
    {
        if (count > Capacity) return false;
        for (int i = 0; i < Capacity; i++)
            if (i < count ? !ValidSample(this[i]) : this[i] != 0) return false;
        return true;
    }
}

/// <summary>Bounded contiguous sender history. A missed capture cannot become invented held input.</summary>
public sealed class NetShotInputSender
{
    private ShotKey _life;
    private uint _frame;
    private bool _seen;
    private byte _count;
    private ShotInputHistory _samples;

    public void Record(in ShotKey life, uint frame, byte sample)
    {
        if (!ShotInputHistory.ValidSample(sample)) throw new ArgumentOutOfRangeException(nameof(sample));
        ShotKey identity = life with { ShotId = 0 };
        if (!_seen || identity != _life)
        {
            _life = identity; _frame = frame; _seen = true;
            _samples = default; _count = 0;
        }
        else
        {
            int delta = unchecked((int)(frame - _frame));
            if (delta < 0) return;
            if (delta == 0)
            {
                if (_count != 0 && _samples[0] != sample) { _samples = default; _count = 0; }
                return;
            }
            if (delta != 1) { _samples = default; _count = 0; }
            _frame = frame;
        }
        if (identity.Generation == 0 || identity.LifeId == 0) return;
        int count = Math.Min(_count + 1, ShotInputHistory.Capacity);
        for (int i = count - 1; i > 0; i--) _samples[i] = _samples[i - 1];
        _samples[0] = sample; _count = (byte)count;
    }

    public void Fill(uint frame, in ShotKey life, ref IntentPacket intent)
    {
        intent.ShotInputCount = 0; intent.ShotInputs = default;
        if (_seen && _frame == frame && _life == (life with { ShotId = 0 }))
        {
            intent.ShotInputCount = _count; intent.ShotInputs = _samples;
        }
    }
}

public static class NetShotInputHistory
{
    private static readonly NetShotInputSender Sender = new();

    public static void Capture(PlayerEntity player, bool eligible)
    {
        if (!NetSession.Active || player.SceneServices.IsReplica || NetSession.LocalSlot < 0
            || player.SlotIndex != NetSession.LocalSlot || (uint)player.CurrentWeapon > 8) return;
        Sender.Record(ShotKey.For(player.SlotIndex, 0), NetSession.NetFrame,
            ShotInputHistory.Sample((byte)player.CurrentWeapon, player.Controls.Shoot.IsDown,
                player.Controls.Zoom.IsDown, eligible&&!player.ModCustomOwnsBipedControls));
    }

    public static void Fill(uint frame, in ShotKey life, ref IntentPacket intent)
        => Sender.Fill(frame, life, ref intent);
}
