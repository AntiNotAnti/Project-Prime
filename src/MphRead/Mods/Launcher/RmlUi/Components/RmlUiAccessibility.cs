using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using MphRead.Mods.Launcher.RmlUi.Host;

namespace MphRead.Mods.Launcher.RmlUi.Components;

[Flags]
public enum RmlUiAccessibilityActions { None = 0, Focus = 1, Press = 2, Scroll = 4, SetText = 8 }
public enum RmlUiAccessibilityAction { Focus = 0, Press = 1, ScrollForward = 2, ScrollBackward = 3, SetText = 4 }
public sealed record RmlUiAccessibilityNode(string Key, string Id, string Role, string Label,
    bool Enabled, bool Focused, bool Protected, bool Offscreen,
    float X, float Y, float Width, float Height, RmlUiAccessibilityActions Actions);
public sealed record RmlUiAccessibilitySnapshot(RmlUiDocumentToken Document, ulong Revision,
    int FramebufferWidth, int FramebufferHeight, IReadOnlyList<RmlUiAccessibilityNode> Nodes)
{
    public static RmlUiAccessibilitySnapshot Empty { get; } = new(default, 0, 0, 0, Array.Empty<RmlUiAccessibilityNode>());
}
public readonly record struct RmlUiAccessibilityCommand(RmlUiDocumentToken Document, ulong Revision,
    string Key, RmlUiAccessibilityAction Action, string Text = "")
{
    public override string ToString() => $"RmlUi accessibility {Action}, document {Document.DocumentId}, revision {Revision}";
}
public readonly record struct RmlUiAccessibilityCaptureMetrics(long Requests, long NativeReads,
    long DecodedSnapshots, long ReusedSnapshots, long IdleSkips);

/// <summary>Platform providers read immutable snapshots and queue actions; only
/// the engine owner captures the DOM or drains native commands.</summary>
public sealed class RmlUiAccessibilityService
{
    private const int MaximumBytes = 1024 * 1024, MaximumCommands = 256;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly ConcurrentQueue<RmlUiAccessibilityCommand> _commands = new();
    private readonly IRmlUiAccessibilityNative _native;
    private RmlUiAccessibilitySnapshot _snapshot = RmlUiAccessibilitySnapshot.Empty;
    private int _queued;
    private bool _available = true;
    private byte[] _buffer = new byte[65536];
    private long _captureRequests, _nativeReads, _decodedSnapshots, _reusedSnapshots, _idleSkips;
    private ulong _capturedVisualRevision;
    private bool _hasVisualStamp;
    public RmlUiAccessibilitySnapshot Snapshot => Volatile.Read(ref _snapshot);
    public bool Available => _available;
    public RmlUiAccessibilityCaptureMetrics CaptureMetrics => new(Interlocked.Read(ref _captureRequests),
        Interlocked.Read(ref _nativeReads), Interlocked.Read(ref _decodedSnapshots), Interlocked.Read(ref _reusedSnapshots),
        Interlocked.Read(ref _idleSkips));
    public RmlUiAccessibilityService() : this(new RmlUiAccessibilityNative()) { }
    internal RmlUiAccessibilityService(IRmlUiAccessibilityNative native) => _native = native;

    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(Packet))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(NodePacket))]
    public RmlUiAccessibilitySnapshot Capture(RmlUiHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        var document = host.CurrentInputDocument; // Also enforces the owner thread.
        ++_captureRequests;
        if (!_available || !host.IsVisible(document)) return Publish(RmlUiAccessibilitySnapshot.Empty);
        try
        {
            var previous = Snapshot;
            if (_hasVisualStamp && previous.Document == document
                && previous.FramebufferWidth == host.FramebufferWidth
                && previous.FramebufferHeight == host.FramebufferHeight
                && host.TryGetUpdateState(out var state) && state.Generation == document.Generation
                && state.VisualRevision == _capturedVisualRevision && !state.Dirty
                && state.NextUpdateDelaySeconds > 0)
            {
                ++_idleSkips; ++_reusedSnapshots;
                return previous;
            }
            _hasVisualStamp = false;
            ++_nativeReads;
            int written = _native.Snapshot(document.DocumentId, _buffer, _buffer.Length);
            if (written < 0)
            {
                if (written == int.MinValue || -written > MaximumBytes) return Publish(RmlUiAccessibilitySnapshot.Empty);
                int capacity = Math.Min(MaximumBytes, Math.Max(-written, _buffer.Length * 2));
                _buffer = new byte[capacity];
                ++_nativeReads;
                written = _native.Snapshot(document.DocumentId, _buffer, _buffer.Length);
            }
            if (written <= 0 || written > _buffer.Length) return Publish(RmlUiAccessibilitySnapshot.Empty);
            if (previous.Document == document && previous.FramebufferWidth == host.FramebufferWidth
                && previous.FramebufferHeight == host.FramebufferHeight
                && HeaderMatches(_buffer.AsSpan(0, written), document, previous.Revision))
            {
                RememberVisualStamp(host, document);
                ++_reusedSnapshots;
                return previous;
            }
            ++_decodedSnapshots;
            var packet = JsonSerializer.Deserialize<Packet>(_buffer.AsSpan(0, written));
            if (packet == null || packet.Version != 1 || packet.Generation != document.Generation
                || packet.Document != document.DocumentId || packet.Revision == 0 || packet.Nodes.Length > 4096)
                return Publish(RmlUiAccessibilitySnapshot.Empty);
            var nodes = new List<RmlUiAccessibilityNode>(packet.Nodes.Length);
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var node in packet.Nodes)
            {
                if (string.IsNullOrEmpty(node.Key) || node.Key.Length > 1024 || !keys.Add(node.Key)
                    || node.Label.Length > 8192 || !float.IsFinite(node.X) || !float.IsFinite(node.Y)
                    || !float.IsFinite(node.Width) || !float.IsFinite(node.Height) || node.Width <= 0 || node.Height <= 0
                    || (node.Actions & ~15) != 0) return Publish(RmlUiAccessibilitySnapshot.Empty);
                nodes.Add(new(node.Key, node.Id, node.Role, node.Label, node.Enabled, node.Focused,
                    node.Protected, node.Offscreen, node.X, node.Y, node.Width, node.Height,
                    (RmlUiAccessibilityActions)node.Actions));
            }
            var snapshot = Publish(new(document, packet.Revision, host.FramebufferWidth, host.FramebufferHeight,
                new ReadOnlyCollection<RmlUiAccessibilityNode>(nodes)));
            RememberVisualStamp(host, document);
            return snapshot;
        }
        catch (EntryPointNotFoundException) { _available = false; return Publish(RmlUiAccessibilitySnapshot.Empty); }
        catch (JsonException) { return Publish(RmlUiAccessibilitySnapshot.Empty); }
    }
    public void Retire()
    {
        Publish(RmlUiAccessibilitySnapshot.Empty);
        while (_commands.TryDequeue(out _)) Interlocked.Decrement(ref _queued);
    }
    public bool Enqueue(in RmlUiAccessibilityCommand command)
    {
        var snapshot = Snapshot;
        if (command.Document != snapshot.Document || command.Revision != snapshot.Revision || command.Revision == 0
            || !Enum.IsDefined(command.Action)) return false;
        string key = command.Key;
        var node = snapshot.Nodes.FirstOrDefault(n => n.Key == key);
        var required = command.Action switch { RmlUiAccessibilityAction.Focus => RmlUiAccessibilityActions.Focus,
            RmlUiAccessibilityAction.Press => RmlUiAccessibilityActions.Press,
            RmlUiAccessibilityAction.SetText => RmlUiAccessibilityActions.SetText, _ => RmlUiAccessibilityActions.Scroll };
        if (node == null || !node.Enabled || (node.Actions & required) == 0) return false;
        if (command.Text == null || command.Text.Length > 32767) return false;
        try { if (StrictUtf8.GetByteCount(command.Text) > 131072) return false; }
        catch (EncoderFallbackException) { return false; }
        if (Interlocked.Increment(ref _queued) > MaximumCommands) { Interlocked.Decrement(ref _queued); return false; }
        _commands.Enqueue(command); return true;
    }
    public int Drain(RmlUiHost host)
    {
        var top = host.CurrentInputDocument; int accepted = 0;
        while (_commands.TryDequeue(out var command))
        {
            Interlocked.Decrement(ref _queued);
            if (!_available || command.Document != top || !host.IsVisible(top)) continue;
            try
            {
                int result = command.Action == RmlUiAccessibilityAction.SetText
                    ? _native.SetText(command.Document.Generation, command.Document.DocumentId, command.Revision, command.Key, command.Text)
                    : _native.Action(command.Document.Generation, command.Document.DocumentId, command.Revision, command.Key, (int)command.Action);
                if (result != 0) { ++accepted; _hasVisualStamp = false; }
            }
            catch (EntryPointNotFoundException) { _available = false; }
        }
        return accepted;
    }
    private void RememberVisualStamp(RmlUiHost host, RmlUiDocumentToken document)
    {
        _hasVisualStamp = host.TryGetUpdateState(out var state) && state.Generation == document.Generation
            && state.VisualRevision != 0 && !state.Dirty && state.NextUpdateDelaySeconds > 0;
        if (_hasVisualStamp) _capturedVisualRevision = state.VisualRevision;
    }
    private RmlUiAccessibilitySnapshot Publish(RmlUiAccessibilitySnapshot snapshot)
    {
        if (snapshot == RmlUiAccessibilitySnapshot.Empty) _hasVisualStamp = false;
        Volatile.Write(ref _snapshot, snapshot); return snapshot;
    }
    private static bool HeaderMatches(ReadOnlySpan<byte> json, RmlUiDocumentToken document, ulong revision)
    {
        if (revision == 0) return false;
        // Native revision covers the complete semantic payload and private
        // action identities. Read only its fixed header for an unchanged tree.
        var reader = new Utf8JsonReader(json);
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject) return false;
        bool version = false, generation = false, id = false, current = false;
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            bool isVersion = reader.ValueTextEquals("version"), isGeneration = reader.ValueTextEquals("generation"),
                isDocument = reader.ValueTextEquals("document"), isRevision = reader.ValueTextEquals("revision"),
                isNodes = reader.ValueTextEquals("nodes");
            if (!reader.Read()) return false;
            if (isNodes) return version && generation && id && current && reader.TokenType == JsonTokenType.StartArray;
            if (!reader.TryGetUInt64(out ulong value)) return false;
            if (isVersion) version = value == 1;
            else if (isGeneration) generation = value == document.Generation;
            else if (isDocument) id = value == document.DocumentId;
            else if (isRevision) current = value == revision;
            else return false;
        }
        return false;
    }
    private sealed class Packet
    {
        [JsonPropertyName("version")] public int Version { get; set; }
        [JsonPropertyName("generation")] public ulong Generation { get; set; }
        [JsonPropertyName("document")] public ulong Document { get; set; }
        [JsonPropertyName("revision")] public ulong Revision { get; set; }
        [JsonPropertyName("nodes")] public NodePacket[] Nodes { get; set; } = Array.Empty<NodePacket>();
    }
    private sealed class NodePacket
    {
        [JsonPropertyName("key")] public string Key { get; set; } = "";
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("role")] public string Role { get; set; } = "";
        [JsonPropertyName("label")] public string Label { get; set; } = "";
        [JsonPropertyName("enabled")] public bool Enabled { get; set; }
        [JsonPropertyName("focused")] public bool Focused { get; set; }
        [JsonPropertyName("protected")] public bool Protected { get; set; }
        [JsonPropertyName("offscreen")] public bool Offscreen { get; set; }
        [JsonPropertyName("x")] public float X { get; set; }
        [JsonPropertyName("y")] public float Y { get; set; }
        [JsonPropertyName("width")] public float Width { get; set; }
        [JsonPropertyName("height")] public float Height { get; set; }
        [JsonPropertyName("actions")] public int Actions { get; set; }
    }
}
internal interface IRmlUiAccessibilityNative
{
    int Snapshot(ulong document, byte[] buffer, int capacity);
    int Action(ulong generation, ulong document, ulong revision, string key, int action);
    int SetText(ulong generation, ulong document, ulong revision, string key, string text);
}
internal sealed class RmlUiAccessibilityNative : IRmlUiAccessibilityNative
{
    private const string Library = "ProjectPrime.RmlUi.Native";
    [DllImport(Library, EntryPoint = "pp_rmlui_document_accessibility_snapshot", CallingConvention = CallingConvention.Cdecl)]
    private static extern int Read(ulong document, [Out] byte[] buffer, int capacity);
    [DllImport(Library, EntryPoint = "pp_rmlui_accessibility_action", CallingConvention = CallingConvention.Cdecl)]
    private static extern int Invoke(ulong generation, ulong document, ulong revision, [MarshalAs(UnmanagedType.LPUTF8Str)] string key, int action);
    [DllImport(Library, EntryPoint = "pp_rmlui_accessibility_set_text", CallingConvention = CallingConvention.Cdecl)]
    private static extern int Write(ulong generation, ulong document, ulong revision, [MarshalAs(UnmanagedType.LPUTF8Str)] string key,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string text);
    public int Snapshot(ulong document, byte[] buffer, int capacity) => Read(document, buffer, capacity);
    public int Action(ulong generation, ulong document, ulong revision, string key, int action) => Invoke(generation, document, revision, key, action);
    public int SetText(ulong generation, ulong document, ulong revision, string key, string text) => Write(generation, document, revision, key, text);
}
