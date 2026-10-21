using System;
using System.IO;

namespace MphRead.Mods.Network;

/// <summary>Full recordings are a disk sink of the accepted-fact recorder.</summary>
internal static class DemoRecorder
{
    private static ReplayWritePump? _writer;
    private static uint _origin, _lastFrame;
    private static ReplayWritePump? _lastWriter;
    private static bool _pending;
    private static ReplayCustomWeaponContext? _customContext;
    internal static bool IsCustomRecording => IsRecording && _customContext != null;
    internal static bool HasCurrentCustomScope => IsCustomRecording && ReplayCapture.CustomClientRecordingScope()
        && ReplayCapture.Recorder.CustomWeaponContext?.Digest == _customContext!.Digest;
    internal static void InterruptCustomRecording()
        => Fail(new InvalidOperationException("The exact custom recording scope or readiness was retired."));
    private static readonly System.Collections.Generic.List<(ReplayWritePump Writer, string Path)> Finishing = new();
    public static void ToggleWithFeedback()
    {
        if (IsRecording)
        {
            bool hadWriter = _writer != null;
            Stop();
            Chat.ChatBox.System(hadWriter ? "Finishing replay save..." : "No replay frames were recorded yet. Resume the match before stopping the recording.");
        }
        else Chat.ChatBox.System(Start() ? "Replay recording started. Stop recording to save it to the theatre archive."
            : LastError ?? "Cannot record until a match is active.");
    }
    internal static void PollSaves()
    {
        ReplayCapture.WorldCapture.DrainCustomRetirement();
        for (int i = Finishing.Count - 1; i >= 0; i--)
        {
            var save = Finishing[i];
            if (!save.Writer.Completion.IsCompleted) continue;
            Finishing.RemoveAt(i);
            if (save.Writer.Error is { } error)
            {
                _lastError = "Could not save replay: " + error.Message;
                Chat.ChatBox.System(_lastError);
            }
            else Chat.ChatBox.System("Replay saved: " + save.Path);
        }
    }
    static DemoRecorder()
    {
        ReplayCapture.Recorder.Accepted += Accept;
        ReplayCapture.Recorder.CheckpointCaptured += checkpoint =>
        {
            if (_writer == null || checkpoint.RecordingFrame <= _origin) return;
            try { if (!_writer.Checkpoint(checkpoint)) Fail(_writer.Error!); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException) { Fail(ex); }
        };
        ReplayCapture.Recorder.Resetting += Stop;
    }
    public static bool IsRecording => _pending || _writer != null;
    public static string? CurrentPath { get; private set; }
    private static string? _lastError;
    public static string? LastError => _lastError ?? _lastWriter?.Error?.Message;
    public static bool Start()
    {
        if (!ReplayCapture.CanStartClientRecording(out var error)) { _lastError = error; return false; }
        if (IsRecording || !NetSession.Active || DemoPlayback.IsActive || NetSession.ServerMatch == null) return false;
        string room = NetSession.ServerMatch.Value.RoomKey;
        foreach (char c in Path.GetInvalidFileNameChars()) room = room.Replace(c, '_');
        CurrentPath = Paths.Combine(Paths.Export, "_demos", $"{room}_{DateTime.Now:yyyy-MM-dd_HH-mm-ss-fff}_{Guid.NewGuid():N}{DemoFile.Extension}");
        _customContext = CustomReplayBoundary.IsBlocked() ? ReplayCapture.Recorder.CustomWeaponContext : null;
        _lastError = null; _lastWriter = null; _pending = true;
        return true;
    }
    internal static void Tick()
    {
        try
        {
            if (IsCustomRecording && (!HasCurrentCustomScope || ReplayCapture.WorldCapture.LastError != null))
            { InterruptCustomRecording(); return; }
            if (_pending && ReplayCapture.WorldCapture.World is { } world)
            {
                var metadata = ReplayTimelineArchive.Metadata(world, ReplayType.FullMatch);
                _origin = _lastFrame = world.Session.RecordingFrame;
                _writer = new ReplayWritePump(CurrentPath!, metadata, _origin); _pending = false; _lastWriter = _writer;
                if (!IsCustomRecording) _writer.EndFrame(0);
                if (!IsCustomRecording) Replay.ReplayOwnerCapture.Begin();
            }
            if (_writer != null)
            {
                _lastFrame = Math.Max(_lastFrame, NetSession.NetFrame);
                // Preserve EOF in quiet periods as well as during packet traffic.
                if ((_lastFrame > _origin || !IsCustomRecording) && !_writer.EndFrame(_lastFrame - _origin)) Fail(_writer.Error!);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException)
        { Fail(ex); }
    }
    private static void Accept(ReplayTimelineRecord record)
    {
        if (_writer == null) return;
        if (IsCustomRecording && record.RecordingFrame <= _origin)
        {
            // The custom Scene capsule already represents a completed frame.
            // Late facts at that same frontier cannot be silently dropped or
            // represented as an ambiguous frame-zero simulation suffix.
            Fail(new InvalidDataException("Custom recording received a fact after its completed baseline frame. Resume simulation before starting a new recording."));
            return;
        }
        try { if (!_writer.Record(record)) { Fail(_writer.Error!); return; } _lastFrame = Math.Max(_lastFrame, record.RecordingFrame); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException) { Fail(ex); }
    }
    public static void Stop()
    {
        bool custom = _customContext != null;
        try
        {
            if (_writer is { } writer)
            {
                writer.Complete();
                Finishing.Add((writer, CurrentPath!));
                if (!IsCustomRecording) Replay.ReplayOwnerCapture.Finish(CurrentPath!, writer.Completion, () => writer.Error == null);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException) { Fail(ex); }
        finally
        {
            Replay.ReplayOwnerCapture.Abort(); _pending = false; _writer = null; _customContext = null; CurrentPath = null;
            if (custom) ReplayCapture.WorldCapture.RetireCustomRecording();
        }
    }
    private static void Fail(Exception error)
    {
        bool custom = _customContext != null;
        _lastError = "Recording interrupted; recover completed chunks from its .part file. " + error.Message;
        Console.WriteLine("[replay] " + LastError);
        Chat.ChatBox.System(_lastError);
        Replay.ReplayOwnerCapture.Abort();
        _writer?.Abort(); _writer = null; _pending = false; _customContext = null; CurrentPath = null;
        if (custom) ReplayCapture.WorldCapture.RetireCustomRecording();
    }
    internal static void RecordOwnSnapshot(ReadOnlySpan<byte> payload)
    {
        Span<byte> packet = stackalloc byte[1 + payload.Length];
        packet[0] = (byte)PacketType.Snapshot; payload.CopyTo(packet[1..]);
        ReplayCapture.Observe(packet);
        ReplayCapture.AcceptedSnapshot(packet, NetSession.NetFrame);
    }
}
