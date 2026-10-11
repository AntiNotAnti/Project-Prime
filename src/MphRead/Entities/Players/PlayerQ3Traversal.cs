using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MphRead.Mods.MapGen;
using MphRead.Formats;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.Entities;

public partial class PlayerEntity
{
    private IReadOnlyList<MapQ3Trigger>? _q3Triggers;
    private int[] _q3Cooldowns = Array.Empty<int>();
    private float _q3LaunchGravity;
    private int _q3LaunchFrames;
    private string? _q3Room;
    private uint _q3ReportedFrame;

    private void ModResetQ3Traversal()
    {
        _q3LaunchGravity = 0; _q3LaunchFrames = 0; _q3ReportedFrame = 0;
        _q3Triggers = null; _q3Room = null; _q3Cooldowns = Array.Empty<int>();
    }
    private void ModLoadQ3Traversal()
    {
        string? room = _scene.Room?.Meta.Name;
        if (_q3Room == room && _q3Triggers != null) return;
        _q3Room = room;
        _q3LaunchGravity = 0; _q3LaunchFrames = 0; _q3ReportedFrame = 0;
        _q3Triggers = Q3Traversal.ForScene(_scene);
        _q3Cooldowns = new int[_q3Triggers.Count];
    }
    private Vector3 ModQ3Feet => IsAltForm
        ? Volume.SpherePosition.AddY(-Volume.SphereRadius) : Position;

    private void ModProcessQ3Triggers()
    {
        ModLoadQ3Traversal();
        if (Health == 0) { _q3LaunchGravity = 0; _q3LaunchFrames = 0; Array.Clear(_q3Cooldowns); return; }
        // A replica/remote human's owner supplies its pose using the existing movement contract.
        // The authority observes traversal proof for validation without authoring a competing pose.
        if (_scene.Services.IsReplica || NetHooks.IsPuppet(this)) return;
        for (int i = 0; i < _q3Cooldowns.Length; i++)
            if (_q3Cooldowns[i] > 0) _q3Cooldowns[i]--;
        if (_q3LaunchFrames > 0) _q3LaunchFrames--;
        if (_q3LaunchFrames == 0 || Flags2.TestFlag(PlayerFlags2.AltAttack) || _accelerationTimer > 0)
            _q3LaunchGravity = 0;
        Vector3 feet = ModQ3Feet;
        for (int i = 0; i < _q3Triggers!.Count; i++)
        {
            MapQ3Trigger trigger = _q3Triggers[i];
            if (_q3Cooldowns[i] != 0 || !Q3Traversal.Contact(trigger, PrevPosition + feet - Position, feet)) continue;
            _q3Cooldowns[i] = trigger.CooldownFrames;
            if (trigger.Kind == "push" && Q3Traversal.TryLaunch(trigger, out Vector3 velocity))
            {
                if (Flags2.TestFlag(PlayerFlags2.AltAttack)) EndAltAttack();
                _boostCharge = _boostDamage = _boostAimLock = 0;
                Speed = velocity; Acceleration = Vector3.Zero; _accelerationTimer = 0;
                Flags1 &= ~PlayerFlags1.Boosting;
                Flags2 &= ~(PlayerFlags2.AltAttack | PlayerFlags2.BipedStuck | PlayerFlags2.SpireClimbing | PlayerFlags2.GravityOverride);
                _q3LaunchGravity = -trigger.Gravity / 900;
                _q3LaunchFrames = 600;
                _jumpPadControlLock = _jumpPadControlLockMin = 0;
                _lastJumpPad = null;
                _timeSinceJumpPad = 0;
                Flags1 &= ~(PlayerFlags1.UsedJumpPad | PlayerFlags1.Standing | PlayerFlags1.UsedJump);
                ModNoteQ3PadAudio();
                _soundSource.PlaySfx(SfxId.JUMP_PAD);
                Mods.WorldEvents.NoteJumpPad(this, checked((short)trigger.SourceEntity));
            }
            else if (trigger.Kind == "teleport")
            {
                float yaw = MathHelper.DegreesToRadians(trigger.Yaw);
                Vector3 destination = Q3Traversal.ToVector(trigger.Destination);
                // Source teleport origin is Q3's center. Imported starts use origin-24 for feet.
                // Destination metadata is already expressed as Prime feet by the extractor.
                Vector3 bodyDestination = destination + Position - feet;
                if (!ModQ3SafeDestination(bodyDestination)) continue;
                Teleport(bodyDestination, new Vector3(MathF.Sin(yaw), 0, MathF.Cos(yaw)), MphRead.Formats.Culling.NodeRef.None);
                PrevPosition = Position; IdlePosition = Position;
                _volume = CollisionVolume.Move(_volumeUnxf, Position);
                Speed = Vector3.Zero; Acceleration = Vector3.Zero; _accelerationTimer = 0;
                _q3LaunchGravity = 0; _q3LaunchFrames = 0;
                _jumpPadControlLock = _jumpPadControlLockMin = 0;
                Flags1 &= ~PlayerFlags1.UsedJumpPad;
                Array.Fill(_q3Cooldowns, trigger.CooldownFrames);
                Mods.WorldEvents.NoteTeleport(this, checked((short)trigger.SourceEntity));
                break; // No second traversal at a destination in the same fixed step.
            }
        }
    }
    private bool ModQ3SafeDestination(Vector3 destination)
    {
        if (_scene.Room == null || destination.Y < _scene.Room.Meta.KillHeight) return false;
        float radius = MathF.Max(Volume.SphereRadius, 0.01f);
        Vector3 center = destination + Volume.SpherePosition - Position;
        Array.Clear(_penetrationScratch);
        int count = CollisionDetection.CheckSpherePenetration(center, radius,
            _penetrationScratch.Length, TestFlags.Players, _scene, _penetrationScratch);
        if (count == _penetrationScratch.Length) return false;
        for (int i = 0; i < count; i++)
            if (_penetrationScratch[i].Field14 > CollisionRecoverySkin) return false;
        return true;
    }
    private void ModNoteQ3PadAudio()
    {
        if (!NetSession.Active || NetSession.IsAuthority)
        { _jumpPadAudioEventId++; if (_jumpPadAudioEventId == 0) _jumpPadAudioEventId = 1; }
    }
    internal void ModObserveQ3ReportedTraversal(Vector3 reported, uint frame)
    {
        if (!NetSession.IsAuthority || !NetHooks.IsPuppet(this) || Health == 0 || _scene.Services.IsReplica) return;
        ModLoadQ3Traversal();
        if (_q3ReportedFrame != 0 && !NetLifecycleTracker.Newer(frame, _q3ReportedFrame)) return;
        int elapsed = _q3ReportedFrame == 0 ? 1 : (int)Math.Min(unchecked(frame - _q3ReportedFrame), 600u);
        _q3ReportedFrame = frame;
        for (int i = 0; i < _q3Cooldowns.Length; i++) _q3Cooldowns[i] = Math.Max(0, _q3Cooldowns[i] - elapsed);
        Vector3 feet = ModQ3Feet;
        Vector3 reportedFeet = feet + reported - Position;
        for (int i = 0; i < _q3Triggers!.Count; i++)
        {
            var trigger = _q3Triggers[i];
            if (_q3Cooldowns[i] > 0) continue;
            if (trigger.Kind == "push" && Q3Traversal.Contact(trigger, feet, reportedFeet))
            {
                _q3Cooldowns[i] = trigger.CooldownFrames;
                _timeSinceJumpPad = 0;
                ModNoteQ3PadAudio();
            }
            else if (trigger.Kind == "teleport" && Q3Traversal.Contact(trigger, feet)
                && (reportedFeet - Q3Traversal.ToVector(trigger.Destination)).LengthSquared < 16)
            {
                Array.Fill(_q3Cooldowns, trigger.CooldownFrames);
                ModNoteMovementValidationTeleport();
                break;
            }
        }
    }
    internal MovementValidationExemption ModQ3MovementProof(Vector3 reported)
    {
        ModLoadQ3Traversal();
        MovementValidationExemption result = 0;
        foreach (var trigger in _q3Triggers!)
        {
            if (trigger.Kind == "push" && (Q3Traversal.Contact(trigger, ModQ3Feet)
                || Q3Traversal.Contact(trigger, PrevPosition + ModQ3Feet - Position))) result |= MovementValidationExemption.JumpPad;
            if (trigger.Kind == "teleport" && Q3Traversal.Contact(trigger, ModQ3Feet)
                && (reported + ModQ3Feet - Position - Q3Traversal.ToVector(trigger.Destination)).LengthSquared < 16)
                result |= MovementValidationExemption.Teleport;
        }
        return result;
    }
    internal void ModWriteQ3Traversal(BinaryWriter writer)
    {
        ModLoadQ3Traversal();
        writer.Write(_q3LaunchGravity); writer.Write(_q3LaunchFrames); writer.Write(_q3ReportedFrame);
        writer.Write(_q3Cooldowns.Length); foreach (int value in _q3Cooldowns) writer.Write(value);
    }
    internal void ModReadQ3Traversal(BinaryReader reader)
    {
        ModLoadQ3Traversal();
        float gravity = reader.ReadSingle(); int frames = reader.ReadInt32(); uint reportedFrame = reader.ReadUInt32(); int count = reader.ReadInt32();
        if (!float.IsFinite(gravity) || gravity > 0 || gravity < -10000f / 900 || frames is < 0 or > 600
            || count != _q3Cooldowns.Length || count > 4096) throw new InvalidDataException("Invalid Q3 traversal checkpoint.");
        _q3LaunchGravity = gravity; _q3LaunchFrames = frames; _q3ReportedFrame = reportedFrame;
        for (int i = 0; i < count; i++)
        {
            int cooldown = reader.ReadInt32();
            if (cooldown is < 0 or > 600) throw new InvalidDataException("Invalid Q3 cooldown checkpoint.");
            _q3Cooldowns[i] = cooldown;
        }
    }
}
