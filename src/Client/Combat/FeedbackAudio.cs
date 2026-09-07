using System;
using MphRead.Sound;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.Combat;

public enum FeedbackCue { Hit, Headshot, Kill, CriticalHealth, Pickup, ObjectiveTaken, ObjectiveDropped, ObjectiveScored, PrimeChanged, Overtime, MatchPoint }

/// <summary>Bounded presentation cues with a separate gain and no gameplay callbacks.</summary>
public sealed class FeedbackAudio
{
    public static float Volume { get; set; } = .7f;
    private readonly SoundSource _ui;
    private readonly SoundSource _world;
    private readonly uint[] _last = new uint[11];
    private readonly bool[] _played = new bool[11];
    private CombatActor _identity = CombatActor.None;
    private bool _lowHealth;

    public FeedbackAudio(Scene scene)
    {
        _ui = new SoundSource(scene) { Self = true };
        _world = new SoundSource(scene) { ReferenceDistance = 4, MaxDistance = 40, RolloffFactor = 1 };
    }

    public static SfxId Sound(FeedbackCue cue) => cue switch
    {
        FeedbackCue.Hit => SfxId.LETTER_BLIP,
        FeedbackCue.Headshot => SfxId.MENU_CURSOR,
        FeedbackCue.Kill => SfxId.MENU_CONFIRM,
        FeedbackCue.CriticalHealth => SfxId.ENERGY_ALARM,
        FeedbackCue.Pickup => SfxId.ITEM_SPAWN1,
        FeedbackCue.ObjectiveTaken => SfxId.POWER_UP1,
        FeedbackCue.ObjectiveDropped => SfxId.MENU_CANCEL,
        FeedbackCue.ObjectiveScored => SfxId.CAPTURE_RING_5,
        FeedbackCue.PrimeChanged => SfxId.POWER_UP2,
        FeedbackCue.Overtime => SfxId.ALARM,
        FeedbackCue.MatchPoint => SfxId.WEAPON_ALARM,
        _ => throw new ArgumentOutOfRangeException(nameof(cue))
    };

    public bool Play(FeedbackCue cue, uint tick, Vector3? position = null)
    {
        SfxId sound = Sound(cue);
        int index = (int)cue;
        uint spacing = cue is FeedbackCue.Hit or FeedbackCue.Headshot ? 4u : 30u;
        if (_played[index] && CombatFeedback.Age(tick, _last[index]) < spacing) return false;
        _played[index] = true;
        _last[index] = tick;
        float gain = float.IsFinite(Volume) ? Math.Clamp(Volume, 0, 1) : 0;
        if (gain == 0 || Sfx.TimedSfxMute > 0) return false;
        SoundSource source = position.HasValue ? _world : _ui;
        source.Volume = gain;
        if (position.HasValue) source.Position = position.Value;
        source.PlaySfx(sound, noUpdate: position.HasValue);
        return true;
    }

    internal void RestoreReplayBaseline(CombatActor identity, ushort health)
    {
        Array.Clear(_played); Array.Clear(_last);
        _identity = identity;
        _lowHealth = identity.IsValid && health is > 0 and < 25;
    }

    public void ObserveHealth(CombatActor identity, ushort health, uint tick)
    {
        if (identity != _identity)
        {
            _identity = identity;
            _lowHealth = false;
            Array.Clear(_played);
        }
        if (!identity.IsValid || health == 0) { _lowHealth = false; return; }
        bool low = health < 25;
        if (low && !_lowHealth) Play(FeedbackCue.CriticalHealth, tick);
        // Hysteresis prevents small recovery/damage oscillations from producing a tone each tick.
        if (low || health >= 35) _lowHealth = low;
    }
}
