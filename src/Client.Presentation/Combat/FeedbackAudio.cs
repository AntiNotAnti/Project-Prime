using System;
using MphRead.Sound;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.Combat;

public enum FeedbackCue { Hit, Headshot, Kill, CriticalHealth, PickupRespawned, ObjectiveTaken, ObjectiveDropped, ObjectiveScored, PrimeChanged, Overtime, MatchPoint, HeadshotKill }

/// <summary>Bounded presentation cues with a separate gain and no gameplay callbacks.</summary>
public sealed class FeedbackAudio : IDisposable
{
    public static float Volume { get; set; } = .7f;
    public static HeadshotKillSoundStyle HeadshotKillSound { get; set; }
        = HeadshotKillSoundStyle.Warzone;
    private readonly SoundSource _ui;
    private readonly SoundSource _world;
    private readonly HeadshotKillAudioPlayer _headshotKillAudio = new();
    private readonly uint[] _last = new uint[(int)FeedbackCue.HeadshotKill + 1];
    private readonly bool[] _played = new bool[(int)FeedbackCue.HeadshotKill + 1];
    private CombatActor _identity = CombatActor.None;
    private bool _lowHealth;

    public FeedbackAudio(Scene scene)
    {
        _ui = new SoundSource(scene) { Self = true };
        _world = new SoundSource(scene) { ReferenceDistance = 4, MaxDistance = 40, RolloffFactor = 1 };
    }

    public static SfxId Sound(FeedbackCue cue) => cue switch
    {
        // Purpose-specific combat cues: do not reuse launcher/menu navigation
        // sounds for an in-match authoritative confirmation.
        FeedbackCue.Hit => SfxId.OPPONENT_DAMAGE,
        FeedbackCue.Headshot => SfxId.SNIPER_HIT,
        FeedbackCue.Kill => SfxId.SUCCESS,
        FeedbackCue.CriticalHealth => SfxId.ENERGY_ALARM,
        FeedbackCue.PickupRespawned => SfxId.ITEM_SPAWN1,
        FeedbackCue.ObjectiveTaken => SfxId.POWER_UP1,
        FeedbackCue.ObjectiveDropped => SfxId.MENU_CANCEL,
        FeedbackCue.ObjectiveScored => SfxId.CAPTURE_RING_5,
        FeedbackCue.PrimeChanged => SfxId.POWER_UP2,
        FeedbackCue.Overtime => SfxId.ALARM,
        FeedbackCue.MatchPoint => SfxId.WEAPON_ALARM,
        // Used only if the selected embedded clip cannot be opened or decoded.
        FeedbackCue.HeadshotKill => SfxId.SUCCESS,
        _ => throw new ArgumentOutOfRangeException(nameof(cue))
    };

    public static FeedbackCue MarkerCue(HitMarkerKind marker,
        CombatEventFlags flags, bool headshotCueEnabled)
        => marker == HitMarkerKind.Kill
            && headshotCueEnabled
            && (flags & CombatEventFlags.Headshot) != 0
                ? FeedbackCue.HeadshotKill
                : marker == HitMarkerKind.Kill ? FeedbackCue.Kill
                : marker == HitMarkerKind.Headshot ? FeedbackCue.Headshot
                : FeedbackCue.Hit;

    public static bool TryGetPickupSound(ItemType itemType, out SfxId sound)
    {
        switch (itemType)
        {
            case ItemType.HealthSmall:
                sound = SfxId.POWER_UP1;
                return true;
            case ItemType.HealthMedium:
            case ItemType.HealthBig:
                sound = SfxId.POWER_UP2;
                return true;
            case ItemType.UASmall:
            case ItemType.MissileSmall:
                sound = SfxId.AMMO_POWER_UP1;
                return true;
            case ItemType.UABig:
            case ItemType.MissileBig:
                sound = SfxId.AMMO_POWER_UP2;
                return true;
            case ItemType.DoubleDamage:
            case ItemType.Deathalt:
                sound = SfxId.DOUBLE_DAMAGE_POWER_UP;
                return true;
            case ItemType.Cloak:
                sound = SfxId.CLOAK_POWER_UP;
                return true;
            case ItemType.ArtifactKey:
                sound = SfxId.KEY_PICKUP;
                return true;
            case ItemType.VoltDriver:
            case ItemType.Battlehammer:
            case ItemType.Imperialist:
            case ItemType.Judicator:
            case ItemType.Magmaul:
            case ItemType.ShockCoil:
            case ItemType.OmegaCannon:
            case ItemType.AffinityWeapon:
                // The authority does not distinguish a new weapon from a
                // duplicate weapon converted to ammo in this event.
                sound = SfxId.WEAPON_POWER_UP;
                return true;
            default:
                sound = default;
                return false;
        }
    }

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
        if (cue == FeedbackCue.HeadshotKill && !position.HasValue
            && _headshotKillAudio.TryPlay(HeadshotKillSound, gain)) return true;
        SoundSource source = position.HasValue ? _world : _ui;
        source.Volume = gain;
        if (position.HasValue) source.Position = position.Value;
        source.PlaySfx(sound, noUpdate: position.HasValue);
        return true;
    }

    /// <summary>Play the local confirmation for an authoritative pickup without a shared cue throttle.</summary>
    public bool PlayPickupAcquired(ItemType itemType, uint tick)
    {
        _ = tick;
        if (!TryGetPickupSound(itemType, out SfxId sound)) return false;
        float gain = float.IsFinite(Volume) ? Math.Clamp(Volume, 0, 1) : 0;
        if (gain == 0 || Sfx.TimedSfxMute > 0) return false;
        _ui.Volume = gain;
        _ui.PlaySfx(sound);
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

    public void Dispose() => _headshotKillAudio.Dispose();
}
