using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.Mods.Replay
{
    internal enum ReplayCameraInterpolation : byte
    {
        Linear,
        Smooth,
        Spline,
        Hold
    }

    internal enum ReplayCameraEase : byte
    {
        None,
        In,
        Out,
        InOut
    }

    internal readonly record struct ReplayCameraKeyframe(
        uint Frame,
        Vector3 Position,
        Quaternion Rotation,
        float Fov,
        sbyte LookAtSlot = -1,
        float Roll = 0,
        ReplayCameraInterpolation Interpolation = ReplayCameraInterpolation.Spline,
        ReplayCameraEase Ease = ReplayCameraEase.InOut);

    /// <summary>
    /// A bounded, presentation-only track. Version 3 binds authored keys to the
    /// durable replay identity and clip range, while retaining v1/v2 readers.
    /// </summary>
    internal sealed class ReplayCameraTrack
    {
        internal const int MaxKeys = 64;
        private const uint Magic = 0x4D435046; // FPCM
        private const int HeaderSize = 23;
        private const int KeySizeV1 = 37;
        private const int KeySizeV2 = 43;
        private const int MaxIdentityBytes = 4096;
        private readonly List<ReplayCameraKeyframe> _keys = new();

        public IReadOnlyList<ReplayCameraKeyframe> Keys => _keys;
        public string? LastError { get; private set; }

        public void Clear()
        {
            _keys.Clear();
            LastError = null;
        }

        public bool Put(ReplayCameraKeyframe key)
        {
            if (!Valid(key))
            {
                LastError = "Camera keyframe contains an invalid position, rotation, FOV, roll, interpolation or target.";
                return false;
            }
            int index = _keys.FindIndex(k => k.Frame >= key.Frame);
            if (index >= 0 && _keys[index].Frame == key.Frame)
                _keys[index] = key;
            else
            {
                if (_keys.Count == MaxKeys)
                {
                    LastError = $"A camera track can contain up to {MaxKeys} keyframes.";
                    return false;
                }
                _keys.Insert(index < 0 ? _keys.Count : index, key);
            }
            LastError = null;
            return true;
        }

        public bool Remove(uint frame)
        {
            int index = _keys.FindIndex(k => k.Frame == frame);
            if (index < 0) return false;
            _keys.RemoveAt(index);
            return true;
        }

        internal bool EditAndSave(string replay, Func<ReplayCameraTrack, bool> edit)
        {
            ReplayCameraKeyframe[] before = _keys.ToArray();
            bool committed = false;
            try
            {
                committed = edit(this) && Save(replay);
                return committed;
            }
            finally
            {
                if (!committed)
                {
                    _keys.Clear();
                    _keys.AddRange(before);
                }
            }
        }

        public bool Sample(double frame, out ReplayCameraKeyframe sample,
            bool constantSpeed = false)
        {
            sample = default;
            if (_keys.Count == 0) return false;
            if (frame <= _keys[0].Frame)
            {
                sample = _keys[0] with { Frame = (uint)Math.Clamp(frame, 0, UInt32.MaxValue) };
                return true;
            }

            for (int i = 1; i < _keys.Count; i++)
            {
                ReplayCameraKeyframe right = _keys[i];
                if (frame > right.Frame) continue;

                ReplayCameraKeyframe left = _keys[i - 1];
                float raw = (float)((frame - left.Frame) / Math.Max(1u, right.Frame - left.Frame));
                float t = left.Interpolation == ReplayCameraInterpolation.Hold
                    ? raw >= 1 ? 1 : 0 : ApplyEase(raw, left.Ease);
                Vector3 p0 = i >= 2 ? _keys[i - 2].Position : left.Position;
                Vector3 p3 = i + 1 < _keys.Count ? _keys[i + 1].Position : right.Position;
                if (constantSpeed && left.Interpolation == ReplayCameraInterpolation.Spline)
                {
                    t = ArcLengthParameter(p0, left.Position, right.Position, p3, t);
                }
                Vector3 position = left.Interpolation switch
                {
                    ReplayCameraInterpolation.Linear => Vector3.Lerp(left.Position, right.Position, t),
                    ReplayCameraInterpolation.Smooth => Vector3.Lerp(left.Position, right.Position,
                        t * t * (3 - 2 * t)),
                    ReplayCameraInterpolation.Hold => raw >= 1 ? right.Position : left.Position,
                    _ => CatmullRom(p0, left.Position, right.Position, p3, t)
                };

                sample = new ReplayCameraKeyframe(
                    (uint)Math.Clamp(frame, 0, UInt32.MaxValue),
                    position,
                    Quaternion.Slerp(left.Rotation, right.Rotation, t).Normalized(),
                    left.Fov + (right.Fov - left.Fov) * t,
                    frame == right.Frame ? right.LookAtSlot : left.LookAtSlot,
                    left.Roll + (right.Roll - left.Roll) * t,
                    left.Interpolation,
                    left.Ease);
                return true;
            }

            sample = _keys[^1] with { Frame = (uint)Math.Clamp(frame, 0, UInt32.MaxValue) };
            return true;
        }

        public static Quaternion FacingRotation(Vector3 facing)
        {
            if (!Finite(facing.X) || !Finite(facing.Y) || !Finite(facing.Z)
                || facing.LengthSquared < 0.000001f)
                return Quaternion.Identity;
            facing.Normalize();
            float yaw = MathF.Atan2(-facing.X, -facing.Z);
            float pitch = MathF.Asin(Math.Clamp(facing.Y, -1, 1));
            return (Quaternion.FromAxisAngle(Vector3.UnitY, yaw)
                * Quaternion.FromAxisAngle(Vector3.UnitX, pitch)).Normalized();
        }

        private static float ApplyEase(float t, ReplayCameraEase ease)
        {
            t = Math.Clamp(t, 0, 1);
            return ease switch
            {
                ReplayCameraEase.In => t * t,
                ReplayCameraEase.Out => 1 - (1 - t) * (1 - t),
                ReplayCameraEase.InOut => t < 0.5f
                    ? 2 * t * t
                    : 1 - MathF.Pow(-2 * t + 2, 2) / 2,
                _ => t
            };
        }

        private static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float t2 = t * t;
            float t3 = t2 * t;
            return 0.5f * ((2 * p1)
                + (-p0 + p2) * t
                + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2
                + (-p0 + 3 * p1 - 3 * p2 + p3) * t3);
        }

        private static float ArcLengthParameter(Vector3 p0, Vector3 p1,
            Vector3 p2, Vector3 p3, float fraction)
        {
            const int Steps = 16;
            Span<float> lengths = stackalloc float[Steps + 1];
            Vector3 previous = p1;
            float total = 0;
            for (int step = 1; step <= Steps; step++)
            {
                float t = step / (float)Steps;
                Vector3 current = CatmullRom(p0, p1, p2, p3, t);
                total += (current - previous).Length;
                lengths[step] = total;
                previous = current;
            }
            if (total <= 0.00001f) return fraction;

            float target = Math.Clamp(fraction, 0, 1) * total;
            int index = 1;
            while (index < Steps && lengths[index] < target) index++;
            float before = lengths[index - 1];
            float after = lengths[index];
            float local = after <= before ? 0 : (target - before) / (after - before);
            return ((index - 1) + local) / Steps;
        }

        private static bool Finite(float value) => float.IsFinite(value);

        private static bool Valid(ReplayCameraKeyframe key)
        {
            return Finite(key.Position.X) && Finite(key.Position.Y) && Finite(key.Position.Z)
                && Math.Abs(key.Position.X) <= 1000000
                && Math.Abs(key.Position.Y) <= 1000000
                && Math.Abs(key.Position.Z) <= 1000000
                && Finite(key.Rotation.X) && Finite(key.Rotation.Y)
                && Finite(key.Rotation.Z) && Finite(key.Rotation.W)
                && Math.Abs(key.Rotation.LengthSquared - 1) < 0.001f
                && Finite(key.Fov)
                && key.Fov >= MathHelper.DegreesToRadians(1)
                && key.Fov <= MathHelper.DegreesToRadians(175)
                && key.LookAtSlot is >= -1 and < 8
                && Finite(key.Roll) && Math.Abs(key.Roll) <= MathHelper.TwoPi
                && Enum.IsDefined(key.Interpolation)
                && Enum.IsDefined(key.Ease);
        }

        public bool Load(string replay)
        {
            Clear();
            string sidecar = replay + ".camera";
            try
            {
                if (!File.Exists(sidecar)) return true;
                using var input = new FileStream(sidecar, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (input.Length < HeaderSize + 32
                    || input.Length > MaxIdentityBytes + MaxKeys * KeySizeV2 + 64)
                    throw new InvalidDataException("Camera track has an invalid size.");

                byte[] bytes = new byte[(int)input.Length];
                input.ReadExactly(bytes);
                if (input.Length != bytes.Length)
                    throw new InvalidDataException("Camera track changed while reading.");
                ReadOnlySpan<byte> body = bytes.AsSpan(0, bytes.Length - 32);
                if (!CryptographicOperations.FixedTimeEquals(
                    SHA256.HashData(body), bytes.AsSpan(bytes.Length - 32)))
                    throw new InvalidDataException("Camera track checksum failed.");

                using var stream = new MemoryStream(bytes, writable: false);
                using var reader = new BinaryReader(stream);
                if (reader.ReadUInt32() != Magic)
                    throw new InvalidDataException("Unsupported camera track.");
                byte version = reader.ReadByte();
                if (version is not (1 or 2 or 3))
                    throw new InvalidDataException("Unsupported camera track.");

                if (version == 3)
                {
                    ReplayCameraTrackIdentity expected = ReplayCameraTrackIdentity.Create(replay);
                    string logicalId = reader.ReadString();
                    byte[] sourceHash = reader.ReadBytes(32);
                    uint startFrame = reader.ReadUInt32(), endFrame = reader.ReadUInt32();
                    if (sourceHash.Length != 32
                        || !ReplayPathComparer.Comparer.Equals(logicalId, expected.LogicalReplayId)
                        || !CryptographicOperations.FixedTimeEquals(sourceHash, expected.SourceContentHash)
                        || startFrame != expected.StartFrame || endFrame != expected.EndFrame)
                        throw new InvalidDataException("Camera track belongs to a different replay or clip range.");
                }
                else
                {
                    var source = new FileInfo(replay);
                    if (!source.Exists
                        || reader.ReadInt64() != source.Length
                        || reader.ReadInt64() != source.LastWriteTimeUtc.Ticks)
                        throw new InvalidDataException("Camera track belongs to a different version of this replay.");
                }

                int count = reader.ReadUInt16();
                int keySize = version == 1 ? KeySizeV1 : KeySizeV2;
                long expectedLength = version == 3
                    ? stream.Position + count * KeySizeV2 + 32
                    : HeaderSize + count * keySize + 32;
                if (count > MaxKeys || bytes.Length != expectedLength)
                    throw new InvalidDataException("Invalid camera keyframe count.");

                var parsed = new List<ReplayCameraKeyframe>(count);
                for (int i = 0; i < count; i++)
                {
                    uint frame = reader.ReadUInt32();
                    Vector3 position = new(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                    Quaternion rotation = new(reader.ReadSingle(), reader.ReadSingle(),
                        reader.ReadSingle(), reader.ReadSingle());
                    float fov = reader.ReadSingle();
                    sbyte lookAt = reader.ReadSByte();
                    float roll = 0;
                    ReplayCameraInterpolation interpolation = ReplayCameraInterpolation.Linear;
                    ReplayCameraEase ease = ReplayCameraEase.None;
                    if (version >= 2)
                    {
                        roll = reader.ReadSingle();
                        interpolation = (ReplayCameraInterpolation)reader.ReadByte();
                        ease = (ReplayCameraEase)reader.ReadByte();
                    }

                    var key = new ReplayCameraKeyframe(frame, position, rotation, fov,
                        lookAt, roll, interpolation, ease);
                    if (!Valid(key) || (i > 0 && key.Frame <= parsed[i - 1].Frame))
                        throw new InvalidDataException("Invalid camera keyframe.");
                    parsed.Add(key);
                }
                _keys.AddRange(parsed);
                return true;
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException
                or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                LastError = ex.Message;
                return false;
            }
        }

        public bool Save(string replay)
        {
            string? temporary = null;
            try
            {
                ReplayCameraTrackIdentity identity = ReplayCameraTrackIdentity.Create(replay);
                using var stream = new MemoryStream(HeaderSize + MaxKeys * KeySizeV2 + 32);
                using var writer = new BinaryWriter(stream);
                writer.Write(Magic);
                writer.Write((byte)3);
                writer.Write(identity.LogicalReplayId);
                writer.Write(identity.SourceContentHash);
                writer.Write(identity.StartFrame);
                writer.Write(identity.EndFrame);
                writer.Write((ushort)_keys.Count);
                foreach (ReplayCameraKeyframe key in _keys)
                {
                    writer.Write(key.Frame);
                    writer.Write(key.Position.X);
                    writer.Write(key.Position.Y);
                    writer.Write(key.Position.Z);
                    writer.Write(key.Rotation.X);
                    writer.Write(key.Rotation.Y);
                    writer.Write(key.Rotation.Z);
                    writer.Write(key.Rotation.W);
                    writer.Write(key.Fov);
                    writer.Write(key.LookAtSlot);
                    writer.Write(key.Roll);
                    writer.Write((byte)key.Interpolation);
                    writer.Write((byte)key.Ease);
                }
                writer.Flush();
                byte[] body = stream.ToArray();
                temporary = replay + $".camera.{Guid.NewGuid():N}.tmp";
                using (var output = new FileStream(temporary, FileMode.CreateNew,
                    FileAccess.Write, FileShare.None))
                {
                    output.Write(body);
                    output.Write(SHA256.HashData(body));
                    output.Flush(flushToDisk: true);
                }
                File.Move(temporary, replay + ".camera", overwrite: true);
                LastError = null;
                return true;
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException
                or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                LastError = ex.Message;
                return false;
            }
            finally
            {
                if (temporary != null)
                {
                    try { File.Delete(temporary); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
        }
    }

    internal readonly record struct ReplayCameraTrackIdentity(
        string LogicalReplayId, byte[] SourceContentHash, uint StartFrame, uint EndFrame)
    {
        internal static ReplayCameraTrackIdentity Create(string logicalReplay)
        {
            string fullPath = Path.GetFullPath(logicalReplay);
            string sourcePath = fullPath;
            uint start = 0, end = 0;
            if (fullPath.EndsWith(ReplayVirtualClips.Extension, StringComparison.OrdinalIgnoreCase))
            {
                if (!ReplayVirtualClips.TryLoad(fullPath, out ReplayVirtualClipDocument? clip) || clip == null)
                    throw new InvalidDataException("Virtual replay clip is invalid.");
                sourcePath = Path.GetFullPath(clip.SourceReplay);
                start = clip.StartFrame;
                end = clip.EndFrame;
            }
            using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            return new ReplayCameraTrackIdentity(fullPath, SHA256.HashData(source), start, end);
        }
    }
}
