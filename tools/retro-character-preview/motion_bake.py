#!/usr/bin/env python3
"""PORT-21: bounded, offline sparse Retro animation-to-source-rig preview.

The original source-to-native bind calibration is not proven. Every animation
is deliberately a *delta from its first source pose*, leaving the imported
CINF rest pose untouched at t=0. No clip is interpreted as gameplay motion.
"""
from __future__ import annotations

from dataclasses import dataclass
import math
import re

MAX_INDEXED_CLIPS = 4096
MAX_PREVIEW_CLIPS = 12
MAX_SAMPLES = 5
MAX_ANIMATED_BONES = 256
MAX_DURATION = 120.0
MAX_COMPONENT = 100_000.0


@dataclass(frozen=True)
class BonePose:
    bone: int
    rotation: tuple[float, ...] | None  # source (x,y,z,w)
    translation: tuple[float, ...] | None


@dataclass(frozen=True)
class Pose:
    seconds: float
    bones: tuple[BonePose, ...]


@dataclass(frozen=True)
class Clip:
    name: str
    resource_id: int
    duration: float
    poses: tuple[Pose, ...]


def _real(value, what):
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        raise ValueError(f"{what} must be a real number")
    number = float(value)
    if not math.isfinite(number) or abs(number) > MAX_COMPONENT:
        raise ValueError(f"{what} must be finite and bounded")
    return number


def _vector(value, size, what):
    if not isinstance(value, list) or len(value) != size:
        raise ValueError(f"{what} must contain {size} components")
    return tuple(_real(x, what) for x in value)


def _u32(value, what):
    if type(value) is not int or not 0 <= value <= 0xFFFFFFFF:
        raise ValueError(f"{what} must be an unsigned source ID")
    return value


def _quaternion(value, what):
    q = _vector(value, 4, what)
    norm = math.sqrt(sum(x * x for x in q))
    if abs(norm - 1) > 0.02:
        raise ValueError(f"{what} is not a unit rotation")
    return tuple(x / norm for x in q)


def validate_motion_inventory(skeleton, motions, requested=None):
    """Refuse malformed clips, but preserve explicit unsupported status entries.

    No guessing or interpolation of composite/missing clips. All sampled
    channels and times must be consistent to be used as preview keyframes.
    """
    if not isinstance(skeleton, dict) or not isinstance(skeleton.get("Bones"), list):
        raise ValueError("A validated CINF skeleton is required")
    known = {_u32(b["Id"], "CINF bone") for b in skeleton["Bones"]}
    if not known or len(known) != len(skeleton["Bones"]):
        raise ValueError("Duplicate or empty CINF skeleton")
    if not isinstance(motions, dict) or motions.get("Format") != 1:
        raise ValueError("Unsupported Retro motion inventory")
    entries = motions.get("Clips")
    if not isinstance(entries, list) or len(entries) > MAX_INDEXED_CLIPS:
        raise ValueError("Oversized or missing Retro clip inventory")
    if requested is not None and (not isinstance(requested, str) or not requested):
        raise ValueError("Requested animation clip must have a name")
    result = []
    matching = 0
    for i, entry in enumerate(entries):
        if not isinstance(entry, dict):
            raise ValueError("Invalid Retro clip record")
        name = entry.get("Name")
        if not isinstance(name, str) or not 1 <= len(name) <= 128 or any(ord(c) < 32 for c in name):
            raise ValueError("Invalid Retro clip name")
        if requested is not None and name != requested:
            continue
        matching += 1
        status = entry.get("Status")
        if status != "sparse-source-pose-preview":
            # The ANCS inventory retains unsupported, unavailable and
            # composite entries. Never fabricate an animation for them.
            if requested is not None:
                raise ValueError(f"Requested '{name}' is not a sampled primitive clip ({status})")
            continue
        if len(result) >= MAX_PREVIEW_CLIPS:
            raise ValueError("More than twelve source animations marked sampled")
        ids = entry.get("ResourceIds")
        if not isinstance(ids, list) or len(ids) != 1:
            raise ValueError("Sampled source clip requires exactly one resource ID")
        resource = _u32(ids[0], "ANIM resource")
        duration = _real(entry.get("DurationSeconds"), "clip duration")
        if not 0 < duration <= MAX_DURATION:
            raise ValueError("Clip has invalid duration")
        samples = entry.get("Samples")
        if not isinstance(samples, list) or len(samples) != MAX_SAMPLES:
            raise ValueError("A preview clip needs all five sampled poses")
        poses = []
        channel_layout = None
        previous_time = -1.0
        for j, sample in enumerate(samples):
            if not isinstance(sample, dict):
                raise ValueError("Invalid sampled source pose")
            at = _real(sample.get("TimeSeconds"), "sample time")
            if at <= previous_time or at < 0 or at > duration + 1e-5:
                raise ValueError("Sample times must be increasing and within the clip")
            previous_time = at
            raw_bones = sample.get("Bones")
            if not isinstance(raw_bones, list) or not 1 <= len(raw_bones) <= MAX_ANIMATED_BONES:
                raise ValueError("Missing or oversized sampled bone channel list")
            bones = []
            seen = set()
            layout = set()
            for raw_bone in raw_bones:
                if not isinstance(raw_bone, dict):
                    raise ValueError("Invalid sampled bone")
                bone_id = _u32(raw_bone.get("Bone"), "ANIM bone")
                if bone_id not in known or bone_id in seen:
                    raise ValueError("Unknown or repeated source animation bone ID")
                seen.add(bone_id)
                rotation = raw_bone.get("Rotation")
                translation = raw_bone.get("Translation")
                if rotation is None and translation is None:
                    raise ValueError("Source bone has no sampled channels")
                r = _quaternion(rotation, "sample rotation") if rotation is not None else None
                t = _vector(translation, 3, "sample translation") if translation is not None else None
                bones.append(BonePose(bone_id, r, t))
                layout.add((bone_id, r is not None, t is not None))
            if channel_layout is not None and layout != channel_layout:
                raise ValueError("Source channel topology changed between poses")
            channel_layout = layout
            if j == 0 and abs(at) > 1e-5:
                raise ValueError("First sparse source pose must begin at zero")
            poses.append(Pose(at, tuple(sorted(bones, key=lambda p: p.bone))))
        if abs(poses[-1].seconds - duration) > 1e-4:
            raise ValueError("Last source pose must cover the clip duration")
        result.append(Clip(name, resource, duration, tuple(poses)))
    if requested is not None and matching == 0:
        raise ValueError("Requested source clip is absent")
    if requested is not None and matching > 1:
        raise ValueError("Requested source clip name is ambiguous")
    if not result:
        raise ValueError("No safe primitive source clips available for offline animation")
    return result


def _multiply(a, b):
    # Source quaternions are (x,y,z,w), not Blender's (w,x,y,z).
    x, y, z, w = a
    X, Y, Z, W = b
    return (w*X+x*W+y*Z-z*Y, w*Y-x*Z+y*W+z*X,
            w*Z+x*Y-y*X+z*W, w*W-x*X-y*Y-z*Z)


def pose_deltas(clip: Clip):
    """Turn absolute source samples into bind-preserving diagnostic deltas."""
    first = {p.bone: p for p in clip.poses[0].bones}
    last_rotation = {}
    output = []
    for sample in clip.poses:
        frame = {}
        for item in sample.bones:
            original = first[item.bone]
            rot = None
            if item.rotation is not None:
                q = original.rotation
                inverse = (-q[0], -q[1], -q[2], q[3])
                rot = _multiply(item.rotation, inverse)
                if item.bone in last_rotation and sum(a*b for a,b in zip(rot, last_rotation[item.bone])) < 0:
                    rot = tuple(-x for x in rot)
                last_rotation[item.bone] = rot
            pos = None
            if item.translation is not None:
                pos = tuple(a-b for a,b in zip(item.translation, original.translation))
            frame[item.bone] = (rot, pos)
        output.append((sample.seconds, frame))
    return output


def _clean_action_name(clip, ordinal):
    clean = re.sub(r"[^a-zA-Z0-9_ -]", "_", clip.name)[:60].strip() or "source"
    return f"Retro_{ordinal:02d}_{clean}_{clip.resource_id:08X}"


def apply_blender_motion(armature, source_bone_names, clips):
    """Bake sampled *source* deltas to individual NLA preview clips in Blender.

    This is not a native MPH retarget: root motion, calibrated bind rotation,
    authored viewmodels, and animation blend trees remain separate work.
    """
    import bpy
    from mathutils import Quaternion, Vector

    bpy.context.scene.render.fps = 60
    armature.animation_data_create()
    if armature.animation_data.nla_tracks:
        raise RuntimeError("Expected a fresh offline preview armature")
    for ordinal, clip in enumerate(clips):
        action = bpy.data.actions.new(_clean_action_name(clip, ordinal))
        armature.animation_data.action = action
        for pose_bone in armature.pose.bones:
            pose_bone.rotation_mode = "QUATERNION"
            pose_bone.rotation_quaternion = (1, 0, 0, 0)
            pose_bone.location = (0, 0, 0)
        last_frame = 1.0
        for at, frame in pose_deltas(clip):
            frame_number = 1.0 + at * 60.0
            last_frame = max(last_frame, frame_number)
            for ident, (rotation, translation) in frame.items():
                name = source_bone_names[ident]
                pose_bone = armature.pose.bones[name]
                rest = armature.data.bones[name].matrix_local.to_quaternion()
                if rotation is not None:
                    q = Quaternion((rotation[3], rotation[0], rotation[1], rotation[2]))
                    pose_bone.rotation_quaternion = rest.inverted() @ q @ rest
                    pose_bone.keyframe_insert(data_path="rotation_quaternion", frame=frame_number, group=name)
                if translation is not None:
                    pose_bone.location = rest.inverted() @ Vector(translation)
                    pose_bone.keyframe_insert(data_path="location", frame=frame_number, group=name)
        armature.animation_data.action = None
        track = armature.animation_data.nla_tracks.new()
        track.name = action.name
        strip = track.strips.new(action.name, 1, action)
        strip.action_frame_start = 1.0
        strip.action_frame_end = max(1.00001, last_frame)
    # Multiple NLA tracks are distinct exported animations, not blend inputs.
    for pose_bone in armature.pose.bones:
        pose_bone.rotation_quaternion = (1, 0, 0, 0)
        pose_bone.location = (0, 0, 0)
    bpy.context.scene.frame_set(1)
