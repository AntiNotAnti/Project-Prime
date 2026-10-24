using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using OpenTK.Mathematics;

namespace MphRead.Mods.Replay;

internal readonly record struct ReplayOwnerCoverage(long Samples, long Dropped, long Skipped, long Gaps,
    double FirstFrame, double LastFrame);

// Render thread owns admission; the sole worker owns compression and all disk writes.
// Saturation drops optional pictures explicitly, never stalls authoritative capture.
internal sealed class ReplayOwnerStream : IDisposable
{
    internal const int ChunkSamples = 256;
    internal const uint ChunkMagic = 0x34434F50, FooterMagic = 0x34464F50;
    private readonly Channel<ReplayOwnerPose> _queue;
    private readonly string _spool = Path.Combine(Path.GetTempPath(), "prime-owner-" + Guid.NewGuid().ToString("N") + ".tmp");
    private readonly Task _worker;
    private readonly CancellationTokenSource _cancel = new();
    private ReplayOwnerPose _last;
    private long _accepted, _dropped, _skipped, _gaps;
    private double _first;
    private bool _disposed, _closed;
    internal ReplayOwnerCoverage Coverage => new(_accepted, _dropped, _skipped, _gaps, _first, _last.Frame);
    internal ReplayOwnerStream(int queueSamples = 4096, Action? beforeDrainForCheck = null)
    {
        _queue = Channel.CreateBounded<ReplayOwnerPose>(new BoundedChannelOptions(queueSamples)
            { SingleWriter = true, SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
        _worker = Task.Run(async () =>
        {
            try { beforeDrainForCheck?.Invoke(); await WriteChunks().ConfigureAwait(false); }
            catch (Exception error) { _queue.Writer.TryComplete(error); throw; }
        });
    }
    internal void Skip() => _skipped++;
    internal bool TryWrite(in ReplayOwnerPose pose)
    {
        if (_closed || _disposed || !ReplayOwnerTrack.Valid(pose)) { _dropped++; return false; }
        // A second draw of the same canonical time is not another source picture.
        if (_accepted > 0 && (pose.Frame <= _last.Frame || pose.RenderSeconds < _last.RenderSeconds)) return false;
        if (_worker.IsFaulted || !_queue.Writer.TryWrite(pose)) { _dropped++; return false; }
        Accepted(pose); return true;
    }
    // Storage-only path: preserve every captured picture when cropping an archive.
    // This method is never called from the live render producer.
    internal void WriteOffline(in ReplayOwnerPose pose, CancellationToken cancellation)
    {
        if (_closed || _disposed || !ReplayOwnerTrack.Valid(pose)
            || _accepted > 0 && (pose.Frame <= _last.Frame || pose.RenderSeconds < _last.RenderSeconds))
            throw new InvalidDataException("Invalid offline owner sample ordering.");
        try { _queue.Writer.WriteAsync(pose,cancellation).AsTask().GetAwaiter().GetResult(); }
        catch (ChannelClosedException) { _worker.GetAwaiter().GetResult(); throw; }
        Accepted(pose);
    }
    private void Accepted(in ReplayOwnerPose pose)
    {
        if (_accepted == 0) _first = pose.Frame;
        else if (pose.Frame - _last.Frame > 1) _gaps++;
        _last = pose; _accepted++;
    }
    private async Task WriteChunks()
    {
        try
        {
            using var stream = new FileStream(_spool, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            using var writer = new BinaryWriter(stream);
            writer.Write(0x31504F50u); writer.Write((ushort)4); writer.Write((ushort)60);
            var chunk = new List<ReplayOwnerPose>(ChunkSamples);
            await foreach (var pose in _queue.Reader.ReadAllAsync(_cancel.Token).ConfigureAwait(false))
            {
                if (chunk.Count != 0 && (!SameLife(chunk[0], pose) || chunk.Count == ChunkSamples)) Flush();
                chunk.Add(pose);
            }
            Flush();
            void Flush()
            {
                if (chunk.Count == 0) return;
                using var raw = new MemoryStream(ChunkSamples * ReplayOwnerTrack.RecordBytes);
                using (var payload = new BinaryWriter(raw, System.Text.Encoding.UTF8, true))
                    foreach (var pose in chunk) ReplayOwnerTrack.WritePose(payload, pose);
                byte[] bytes = raw.ToArray();
                using var compressed = new MemoryStream();
                using (var zip = new DeflateStream(compressed, CompressionLevel.Fastest, true)) zip.Write(bytes);
                writer.Write(ChunkMagic); writer.Write(chunk.Count); writer.Write((int)compressed.Length);
                writer.Write(chunk[0].Frame); writer.Write(chunk[^1].Frame); writer.Write(SHA256.HashData(bytes));
                writer.Write(compressed.GetBuffer(), 0, (int)compressed.Length); chunk.Clear();
                if (stream.Position > ReplayOwnerTrack.MaximumFileBytes - 1024) throw new IOException("Owner presentation storage bound exceeded.");
            }
        }
        catch { try { File.Delete(_spool); } catch (IOException) { } throw; }
    }
    private static bool SameLife(ReplayOwnerPose a, ReplayOwnerPose b) =>
        a.Match == b.Match && a.Epoch == b.Epoch && a.Slot == b.Slot && a.Generation == b.Generation && a.Life == b.Life;
    internal async Task Complete(string replay, Task canonicalCompletion, Func<bool> canonicalSucceeded, CancellationToken cancellation = default)
    {
        _closed = true; _queue.Writer.TryComplete();
        await _worker.ConfigureAwait(false);
        await canonicalCompletion.ConfigureAwait(false);
        cancellation.ThrowIfCancellationRequested();
        if (!canonicalSucceeded() || _accepted == 0) return;
        byte[] identity = ReplayOwnerTrack.HashSource(replay, cancellation);
        using (var file = new FileStream(_spool, FileMode.Append, FileAccess.Write, FileShare.None))
        using (var writer = new BinaryWriter(file))
        {
            writer.Write(FooterMagic); writer.Write(identity);
            writer.Write(_accepted); writer.Write(_dropped); writer.Write(_skipped); writer.Write(_gaps);
            writer.Write(_first); writer.Write(_last.Frame);
        }
        using (var file = new FileStream(_spool, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            byte[] digest = SHA256.HashData(file); file.Write(digest); file.Flush(true);
        }
        cancellation.ThrowIfCancellationRequested();
        // Spool may be on another volume; publish through a destination-local temporary.
        string temporary = replay + ReplayOwnerTrack.Extension + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.Copy(_spool, temporary); cancellation.ThrowIfCancellationRequested(); File.Move(temporary, replay + ReplayOwnerTrack.Extension, true); }
        finally { File.Delete(temporary); }
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true; _closed = true;
        _queue.Writer.TryComplete(); _cancel.Cancel();
        _ = _worker.ContinueWith(t => { _ = t.Exception; try { File.Delete(_spool); } catch (IOException) { } _cancel.Dispose(); }, TaskScheduler.Default);
    }
}

internal sealed partial class ReplayOwnerTrack
{
    private sealed record Chunk(long Offset, int Count, int Bytes, double First, double Last);
    private List<Chunk>? _indexed;
    private FileStream? _indexedFile;
    private string? _privateCopy;
    private int _cachedChunk = -1;
    private ReplayOwnerPose[]? _cachedSamples;
    internal ReplayOwnerCoverage Coverage { get; private set; }
    private string[]? _provenance;
    private static readonly string[] ProvenanceLabels =
    [
        "Partial/legacy \u00b7 owner camera/gun; animation reconstructed",
        "Partial/legacy \u00b7 owner camera/gun; animation reconstructed; horizontal coverage adapted",
        "Owner captured \u00b7 camera/gun/animation/cosmetics; vertical FOV preserved",
        "Owner captured \u00b7 camera/gun/animation/cosmetics; vertical FOV preserved; horizontal coverage adapted"
    ];
    internal string Provenance(bool visualApplied, bool adaptedAspect)
        => (_provenance ?? ProvenanceLabels)[(visualApplied ? 2 : 0) + (adaptedAspect ? 1 : 0)];
    private void PrepareProvenance()
    {
        string suffix = $"; {Coverage.Samples} pictures, {Coverage.Gaps} gaps, {Coverage.Dropped} dropped";
        _provenance = new string[ProvenanceLabels.Length];
        for (int i = 0; i < _provenance.Length; i++) _provenance[i] = ProvenanceLabels[i] + suffix;
    }
    public void Dispose()
    {
        lock (this) { _indexedFile?.Dispose(); _indexedFile = null; _cachedSamples = null;
            if (_privateCopy != null) { File.Delete(_privateCopy); _privateCopy = null; } }
    }
    internal static byte[] HashSource(string path, CancellationToken cancellation) => Hash(path, cancellation);
    internal static void WritePose(BinaryWriter writer, ReplayOwnerPose v)
    {
        writer.Write(v.Frame); writer.Write(v.RenderSeconds); writer.Write(v.Match); writer.Write(v.Epoch);
        writer.Write(v.Slot); writer.Write(v.Generation); writer.Write(v.Life);
        for (int r = 0; r < 4; r++) for (int c = 0; c < 4; c++) writer.Write(v.View[r, c]);
        writer.Write(v.WorldFov); writer.Write(v.GunFov); Write(writer, v.GunPosition); Write(writer, v.GunFacing); Write(writer, v.GunUp);
        writer.Write(v.Aspect); v.Visual.Write(writer); writer.Write(v.CustomScopeBlend.HasValue); writer.Write(v.CustomScopeBlend ?? 0);
    }
    private static ReplayOwnerPose ReadPose(BinaryReader reader)
    {
        double frame = reader.ReadDouble(), seconds = reader.ReadDouble(); ushort match = reader.ReadUInt16();
        ulong epoch = reader.ReadUInt64(); byte slot = reader.ReadByte(); ushort generation = reader.ReadUInt16(), life = reader.ReadUInt16();
        Matrix4 view = default;
        for (int r = 0; r < 4; r++) for (int c = 0; c < 4; c++) view[r,c] = reader.ReadSingle();
        var pose = new ReplayOwnerPose(frame, seconds, match, epoch, slot, generation, life, view,
            reader.ReadSingle(), reader.ReadSingle(), Read(reader), Read(reader), Read(reader))
            { Aspect = reader.ReadSingle(), Visual = ReplayOwnerVisual.Read(reader) };
        byte present = reader.ReadByte(); float blend = reader.ReadSingle();
        if (present > 1 || !float.IsFinite(blend) || blend is < 0 or > 1 || present == 0 && blend != 0)
            throw new InvalidDataException("Invalid owner scope blend.");
        return pose with { CustomScopeBlend = present == 1 ? blend : null };
    }
    private static ReplayOwnerPose[] DecodeChunk(BinaryReader reader, Chunk chunk)
    {
        reader.BaseStream.Position = chunk.Offset;
        byte[] expected = reader.ReadBytes(32), compressed = reader.ReadBytes(chunk.Bytes);
        if (compressed.Length != chunk.Bytes) throw new InvalidDataException("Truncated owner chunk.");
        using var input = new MemoryStream(compressed);
        using var zip = new DeflateStream(input, CompressionMode.Decompress);
        byte[] raw = new byte[checked(chunk.Count * RecordBytes)]; zip.ReadExactly(raw);
        if (zip.ReadByte() != -1 || !CryptographicOperations.FixedTimeEquals(expected, SHA256.HashData(raw)))
            throw new InvalidDataException("Invalid owner chunk digest or extent.");
        using var payload = new BinaryReader(new MemoryStream(raw));
        var samples = new ReplayOwnerPose[chunk.Count];
        for (int i = 0; i < samples.Length; i++) samples[i] = ReadPose(payload);
        return samples;
    }
    private static ReplayOwnerTrack? LoadIndexed(string replay, FileStream source, CancellationToken cancellation)
    {
        if (source.Length > MaximumFileBytes || source.Length < 124) return null;
        var track = new ReplayOwnerTrack(1) { _indexed = new() };
        try
        {
            // Private immutable snapshot survives sidecar edits and source scene replacement.
            track._privateCopy = Path.Combine(Path.GetTempPath(), "prime-owner-read-" + Guid.NewGuid().ToString("N") + ".tmp");
            using (var copy = new FileStream(track._privateCopy, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                source.Position = 0; byte[] buffer = new byte[65536]; int read;
                while ((read = source.Read(buffer)) > 0) { cancellation.ThrowIfCancellationRequested(); copy.Write(buffer, 0, read); }
            }
            track._indexedFile = new FileStream(track._privateCopy, FileMode.Open, FileAccess.Read, FileShare.Read);
            var file = track._indexedFile; using var reader = new BinaryReader(file, System.Text.Encoding.UTF8, true);
            file.Position = file.Length - 32; byte[] checksum = reader.ReadBytes(32); file.Position = 0;
            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                byte[] buffer = new byte[65536]; long remaining = file.Length - 32;
                while (remaining > 0) { cancellation.ThrowIfCancellationRequested(); int n = file.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining)); if (n == 0) throw new InvalidDataException(); hash.AppendData(buffer.AsSpan(0,n)); remaining -= n; }
                if (!CryptographicOperations.FixedTimeEquals(checksum, hash.GetHashAndReset())) throw new InvalidDataException("Owner track checksum mismatch.");
            }
            file.Position = 8; ReplayOwnerPose previous = default; long count = 0, gaps = 0; double first = 0;
            while (reader.ReadUInt32() == ReplayOwnerStream.ChunkMagic)
            {
                cancellation.ThrowIfCancellationRequested();
                int n = reader.ReadInt32(), bytes = reader.ReadInt32(); double start = reader.ReadDouble(), end = reader.ReadDouble();
                if (n is < 1 or > ReplayOwnerStream.ChunkSamples || bytes is < 1 or > ReplayOwnerStream.ChunkSamples * RecordBytes + 1024
                    || file.Position + 32 + bytes > file.Length - 32 || track._indexed.Count >= 1_000_000) throw new InvalidDataException("Owner chunk bounds.");
                var chunk = new Chunk(file.Position, n, bytes, start, end);
                var samples = DecodeChunk(reader, chunk);
                for (int i = 0; i < n; i++)
                {
                    var pose = samples[i];
                    if (!Valid(pose) || count > 0 && (pose.Frame <= previous.Frame || pose.RenderSeconds < previous.RenderSeconds)) throw new InvalidDataException("Owner sample ordering.");
                    if (count == 0) first = pose.Frame; else if (pose.Frame - previous.Frame > 1) gaps++;
                    if (i > 0 && (pose.Match != samples[0].Match || pose.Epoch != samples[0].Epoch || pose.Slot != samples[0].Slot || pose.Generation != samples[0].Generation || pose.Life != samples[0].Life)) throw new InvalidDataException("Owner chunk crosses lifecycle.");
                    previous = pose; count++;
                }
                if (samples[0].Frame != start || previous.Frame != end || count > int.MaxValue) throw new InvalidDataException("Owner index mismatch.");
                track._indexed.Add(chunk);
            }
            file.Position -= 4;
            if (reader.ReadUInt32() != ReplayOwnerStream.FooterMagic || file.Length - file.Position != 112) throw new InvalidDataException("Owner footer extent.");
            if (!CryptographicOperations.FixedTimeEquals(reader.ReadBytes(32), Hash(replay,cancellation))) throw new InvalidDataException("Owner source mismatch.");
            var coverage = new ReplayOwnerCoverage(reader.ReadInt64(),reader.ReadInt64(),reader.ReadInt64(),reader.ReadInt64(),reader.ReadDouble(),reader.ReadDouble());
            if (count == 0 || coverage.Samples != count || coverage.Gaps != gaps || coverage.Dropped < 0 || coverage.Skipped < 0 || coverage.FirstFrame != first || coverage.LastFrame != previous.Frame) throw new InvalidDataException("Owner coverage mismatch.");
            track.Count = (int)count; track.Coverage = coverage; track.PrepareProvenance();
            return track;
        }
        catch { track.Dispose(); throw; }
    }
    internal System.Collections.Generic.IEnumerable<ReplayOwnerPose> Pictures()
    {
        if (_indexed == null) { for (int i = 0; i < Count; i++) yield return _samples[i]; }
        else for (int c = 0; c < _indexed.Count; c++) foreach (var pose in ChunkSamples(c)) yield return pose;
    }
    internal ReplayOwnerPose At(int index)
    {
        if (_indexed == null) return _samples[index];
        int offset = index;
        for (int c = 0; c < _indexed.Count; c++) { if (offset < _indexed[c].Count) return ChunkSamples(c)[offset]; offset -= _indexed[c].Count; }
        throw new ArgumentOutOfRangeException(nameof(index));
    }
    private ReplayOwnerPose[] ChunkSamples(int index)
    {
        if (_cachedChunk != index || _cachedSamples == null)
        {
            if (_indexedFile == null) throw new ObjectDisposedException(nameof(ReplayOwnerTrack));
            using var reader = new BinaryReader(_indexedFile, System.Text.Encoding.UTF8, true);
            _cachedSamples = DecodeChunk(reader, _indexed![index]); _cachedChunk = index;
        }
        return _cachedSamples;
    }
    private bool SampleIndexed(double frame, ushort match, ulong epoch, int slot, ushort generation, ushort life, out ReplayOwnerPose pose)
    {
        pose = default;
        if (!double.IsFinite(frame) || _indexed!.Count == 0 || frame < _indexed[0].First) return false;
        lock (this)
        {
            if (_indexedFile == null) return false;
            int lo = 0, hi = _indexed.Count - 1;
            while (lo < hi) { int mid = lo + (hi-lo+1)/2; if (_indexed[mid].First <= frame) lo = mid; else hi = mid - 1; }
            ReplayOwnerPose[] samples;
            try { samples = ChunkSamples(lo); } catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException) { return false; }
            lo = 0; hi = samples.Length - 1;
            while (lo < hi) { int mid = lo + (hi-lo+1)/2; if (samples[mid].Frame <= frame) lo = mid; else hi = mid - 1; }
            var sample = samples[lo];
            if (frame - sample.Frame > 1 || sample.Match != match || sample.Epoch != epoch || sample.Slot != slot || sample.Generation != generation || sample.Life != life) return false;
            pose = sample; return true;
        }
    }
}
