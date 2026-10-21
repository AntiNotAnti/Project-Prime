using System;
using MphRead.Entities;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using OpenTK.Mathematics;
using MphRead.Mods.Network;

namespace MphRead.Mods.Replay;

// Optional local presentation, never a packet, checkpoint, collision or hit input.
internal readonly record struct ReplayOwnerPose(double Frame, double RenderSeconds,
    ushort Match, ulong Epoch, byte Slot, ushort Generation, ushort Life,
    Matrix4 View, float WorldFov, float GunFov, Vector3 GunPosition,
    Vector3 GunFacing, Vector3 GunUp)
{
    internal float Aspect { get; init; }
    internal ReplayOwnerVisual Visual { get; init; }
    internal float? CustomScopeBlend { get; init; }
}

internal sealed class ReplayOwnerTrack
{
    internal const string Extension = ".ownerpose";
    internal const int MaximumSamples = 65536;
    private const uint Magic = 0x31504F50; // POP1, independent of replay schema
    internal const int LegacyRecordBytes = 139;
    internal const int Version2RecordBytes = LegacyRecordBytes + 4 + ReplayOwnerVisual.Bytes;
    internal const int RecordBytes = Version2RecordBytes + 5;
    private const int HeaderBytes = 4 + 2 + 2 + 4 + 32;
    internal const long MaximumFileBytes = HeaderBytes + (long)MaximumSamples * RecordBytes + 32;
    private readonly ReplayOwnerPose[] _samples;
    internal int Count { get; private set; }
    internal int Dropped { get; private set; }
    internal ReplayOwnerTrack(int capacity = MaximumSamples)
    {
        if (capacity is < 1 or > MaximumSamples) throw new ArgumentOutOfRangeException(nameof(capacity));
        _samples = new ReplayOwnerPose[capacity];
    }
    internal bool Add(in ReplayOwnerPose sample)
    {
        if (!Valid(sample) || Count > 0 && (sample.Frame <= _samples[Count - 1].Frame
            || sample.RenderSeconds < _samples[Count - 1].RenderSeconds)) return false;
        if (Count == _samples.Length) { Dropped++; return false; }
        _samples[Count++] = sample; return true;
    }
    internal static bool Valid(in ReplayOwnerPose value)
    {
        if (!double.IsFinite(value.Frame) || value.Frame < 0 || value.Frame > ReplayFormatV3.MaxFrame
            || !double.IsFinite(value.RenderSeconds) || value.RenderSeconds < 0
            || value.Match == 0 || value.Epoch == 0 || value.Slot >= 8 || value.Generation == 0 || value.Life == 0
            || !float.IsFinite(value.WorldFov) || value.WorldFov is < .01f or > 3.06f
            || !float.IsFinite(value.GunFov) || value.GunFov is < .01f or > 3.06f
            || !Finite(value.GunPosition) || !Unit(value.GunFacing) || !Unit(value.GunUp)
            || MathF.Abs(Vector3.Dot(value.GunFacing, value.GunUp)) > .01f
            || !float.IsFinite(value.Aspect) || value.Aspect != 0 && value.Aspect is < .2f or > 8
            || !value.Visual.Valid || value.CustomScopeBlend is { } blend && (!float.IsFinite(blend) || blend is <0 or >1)) return false;
        for (int r = 0; r < 4; r++) for (int c = 0; c < 4; c++)
            if (!float.IsFinite(value.View[r, c])) return false;
        // Require a rigid affine camera; determinant alone accepts shears/scales.
        return MathF.Abs(value.View.M14) < .001f && MathF.Abs(value.View.M24) < .001f
            && MathF.Abs(value.View.M34) < .001f && MathF.Abs(value.View.M44 - 1) < .001f
            && Unit(value.View.Column0.Xyz) && Unit(value.View.Column1.Xyz) && Unit(value.View.Column2.Xyz)
            && MathF.Abs(Vector3.Dot(value.View.Column0.Xyz, value.View.Column1.Xyz)) < .001f
            && MathF.Abs(Vector3.Dot(value.View.Column1.Xyz, value.View.Column2.Xyz)) < .001f
            && MathF.Abs(Vector3.Dot(value.View.Column0.Xyz, value.View.Column2.Xyz)) < .001f
            && MathF.Abs(value.View.Determinant - 1) < .001f;
    }
    private static bool Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
    private static bool Unit(Vector3 v) => Finite(v) && MathF.Abs(v.LengthSquared - 1) < .001f;

    // Nearest recorded picture at/before the requested canonical time, within
    // one simulation tick. No invented high-Hz samples or cross-life smoothing.
    internal bool Sample(double frame, ushort match, ulong epoch, int slot, ushort generation,
        ushort life, out ReplayOwnerPose pose)
    {
        pose = default;
        if (!double.IsFinite(frame) || Count == 0 || frame < _samples[0].Frame) return false;
        int lo = 0, hi = Count - 1;
        while (lo < hi)
        {
            int mid = lo + (hi - lo + 1) / 2;
            if (_samples[mid].Frame <= frame) lo = mid; else hi = mid - 1;
        }
        var sample = _samples[lo];
        if (frame - sample.Frame > 1 || sample.Match != match || sample.Epoch != epoch || sample.Slot != slot
            || sample.Generation != generation || sample.Life != life) return false;
        pose = sample; return true;
    }
    internal void Save(string replay, CancellationToken cancellation = default)
    {
        if (Count == 0) return;
        byte[] identity = Hash(replay, cancellation);
        string temporary = replay + Extension + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
            {
                writer.Write(Magic); writer.Write((ushort)3); writer.Write((ushort)60);
                writer.Write(Count); writer.Write(identity);
                for (int i = 0; i < Count; i++)
                {
                    cancellation.ThrowIfCancellationRequested(); var v = _samples[i];
                    writer.Write(v.Frame); writer.Write(v.RenderSeconds); writer.Write(v.Match); writer.Write(v.Epoch);
                    writer.Write(v.Slot); writer.Write(v.Generation); writer.Write(v.Life);
                    for (int r = 0; r < 4; r++) for (int c = 0; c < 4; c++) writer.Write(v.View[r, c]);
                    writer.Write(v.WorldFov); writer.Write(v.GunFov);
                    Write(writer, v.GunPosition); Write(writer, v.GunFacing); Write(writer, v.GunUp);
                    writer.Write(v.Aspect); v.Visual.Write(writer);
                    writer.Write(v.CustomScopeBlend.HasValue); writer.Write(v.CustomScopeBlend ?? 0);
                }
                writer.Flush(); stream.Position = 0;
                byte[] checksum = SHA256.HashData(stream);
                stream.Position = stream.Length; stream.Write(checksum); stream.Flush();
            }
            cancellation.ThrowIfCancellationRequested();
            File.Move(temporary, replay + Extension, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private static int _activeLoads;
    internal static void CopyRange(string source, string output, uint first, uint last, CancellationToken cancellation)
    {
        var original = Load(source, cancellation);
        if(original == null) return;
        var selected = new ReplayOwnerTrack(Math.Max(1, original.Count));
        for(int i=0;i<original.Count;i++)
        {
            cancellation.ThrowIfCancellationRequested();
            var sample=original._samples[i];
            if(sample.Frame>=first&&sample.Frame<=last)selected.Add(sample);
        }
        try { selected.Save(output, cancellation); }
        catch(Exception error) when(error is IOException or UnauthorizedAccessException)
        { DebugLog.Line("replay", "Optional owner presentation range could not be saved: "+error.GetType().Name); }
    }
    internal static Task<ReplayOwnerTrack?> LoadAsyncBounded(string replay, CancellationToken cancellation = default,
        Action? beforeLoadForCheck = null)
    {
        // Rapid seeks may retire worlds faster than disk hashing completes.
        // Admit at most four jobs; saturation uses ordinary authority playback.
        if (Interlocked.Increment(ref _activeLoads) > 4)
        { Interlocked.Decrement(ref _activeLoads); return Task.FromResult<ReplayOwnerTrack?>(null); }
        return Task.Run(() =>
        {
            try { beforeLoadForCheck?.Invoke(); return Load(replay,cancellation); }
            finally { Interlocked.Decrement(ref _activeLoads); }
        });
    }

    internal static ReplayOwnerTrack? Load(string replay, CancellationToken cancellation = default)
    {
        try
        {
            using var stream = new FileStream(replay + Extension, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length < HeaderBytes + LegacyRecordBytes + 32
                || stream.Length > HeaderBytes + (long)MaximumSamples * RecordBytes + 32) return null;
            using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
            if (reader.ReadUInt32() != Magic) return null;
            ushort schema = reader.ReadUInt16();
            if (schema is not (1 or 2 or 3) || reader.ReadUInt16() != 60) return null;
            int recordBytes = schema == 1 ? LegacyRecordBytes : schema == 2 ? Version2RecordBytes : RecordBytes;
            int count = reader.ReadInt32();
            if (count is < 1 or > MaximumSamples || stream.Length != HeaderBytes + (long)count * recordBytes + 32) return null;
            byte[] identity = reader.ReadBytes(32);
            if (!CryptographicOperations.FixedTimeEquals(identity, Hash(replay, cancellation))) return null;
            var track = new ReplayOwnerTrack(count);
            for (int i = 0; i < count; i++)
            {
                cancellation.ThrowIfCancellationRequested();
                double frame = reader.ReadDouble(), seconds = reader.ReadDouble(); ushort match = reader.ReadUInt16();
                ulong epoch = reader.ReadUInt64(); byte slot = reader.ReadByte();
                ushort generation = reader.ReadUInt16(), life = reader.ReadUInt16();
                Matrix4 view = default;
                for (int r = 0; r < 4; r++) for (int c = 0; c < 4; c++) view[r, c] = reader.ReadSingle();
                var sample = new ReplayOwnerPose(frame, seconds, match, epoch, slot, generation, life,
                    view, reader.ReadSingle(), reader.ReadSingle(), Read(reader), Read(reader), Read(reader));
                if (schema >= 2) sample = sample with { Aspect = reader.ReadSingle(), Visual = ReplayOwnerVisual.Read(reader) };
                if (schema == 3)
                {
                    byte present = reader.ReadByte(); float blend = reader.ReadSingle();
                    if(present > 1 || !float.IsFinite(blend) || blend is <0 or >1 || present == 0 && blend != 0) return null;
                    sample = sample with { CustomScopeBlend = present == 1 ? blend : null };
                }
                if (!track.Add(sample)) return null;
            }
            long length = stream.Length - 32;
            byte[] expected = reader.ReadBytes(32);
            stream.Position = 0;
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            byte[] bytes = new byte[8192];
            while (length > 0)
            {
                cancellation.ThrowIfCancellationRequested();
                int read = stream.Read(bytes, 0, (int)Math.Min(bytes.Length, length));
                if (read == 0) return null; hash.AppendData(bytes.AsSpan(0, read)); length -= read;
            }
            return CryptographicOperations.FixedTimeEquals(expected, hash.GetHashAndReset()) ? track : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or OperationCanceledException)
        { return null; }
    }
    private static byte[] Hash(string path, CancellationToken cancellation)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] bytes = new byte[65536]; int read;
        while ((read = stream.Read(bytes)) != 0) { cancellation.ThrowIfCancellationRequested(); hash.AppendData(bytes.AsSpan(0, read)); }
        return hash.GetHashAndReset();
    }
    private static void Write(BinaryWriter w, Vector3 v) { w.Write(v.X); w.Write(v.Y); w.Write(v.Z); }
    private static Vector3 Read(BinaryReader r) => new(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
}

internal static class ReplayOwnerCapture
{
    private static readonly bool Enabled = Environment.GetEnvironmentVariable("PRIME_REPLAY_OWNER_CAPTURE") == "1";
    private static ReplayOwnerTrack? _track;
    private static long _started;
    private static int _finishing;
    internal static string? LastError { get; private set; }
    internal static void Begin()
    {
        _track = Enabled && Volatile.Read(ref _finishing) < 2 ? new() : null;
        _started = System.Diagnostics.Stopwatch.GetTimestamp(); LastError = null;
    }
    internal static void Capture(Scene scene, Matrix4 view, float worldFov, float gunFov)
    {
        if (_track == null || Headless.Active || scene.Services.IsReplica || !NetSession.Active
            || DemoPlayback.IsActive || Mods.SpectatorMode.IsSpectating || scene.GameState.ThirdPersonCamera
            || Mods.PauseMenu.Open || scene.GameState.MenuPause || scene.GameState.DialogPause) return;
        var player = scene.Players.Main;
        if (player.SlotIndex != NetHooks.LocalSlot || player.IsBot || player.Health <= 0
            || player.CameraType != CameraType.First || !player.ModTryOwnerRenderPose(out var gun)) return;
        double frame = Math.Max(0, NetSession.NetFrame - 1d + (Render.FrameTiming.Active ? Render.FrameTiming.PresentationAlpha : 1));
        _track.Add(new(frame, (System.Diagnostics.Stopwatch.GetTimestamp() - _started) / (double)System.Diagnostics.Stopwatch.Frequency,
            NetSession.CurrentMatchId, NetSession.AuthorityEpoch, (byte)player.SlotIndex,
            NetPlayerLifecycle.Generation(player.SlotIndex), NetPlayerLifecycle.Get(player.SlotIndex),
            view, worldFov, gunFov, gun.GunPosition, gun.GunFacing, gun.GunUp)
            { Aspect = scene.Size.Y > 0 ? scene.Size.X / (float)scene.Size.Y : 0, Visual = player.ModCaptureOwnerVisual(),
                CustomScopeBlend = player.ModCapturedCustomScopeBlend });
    }
    internal static void Finish(string path, Task canonicalCompletion, Func<bool> canonicalSucceeded)
    {
        var track = _track; _track = null;
        if (track == null || track.Count == 0) return;
        Interlocked.Increment(ref _finishing);
        _ = Task.Run(async () =>
        {
            try
            {
                await canonicalCompletion.ConfigureAwait(false);
                if (canonicalSucceeded()) track.Save(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            { LastError = "Owner presentation unavailable: " + ex.Message; }
            finally { Interlocked.Decrement(ref _finishing); }
        });
    }
    internal static void Abort() => _track = null;
}
