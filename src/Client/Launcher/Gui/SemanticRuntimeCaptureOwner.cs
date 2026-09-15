using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Testing;

namespace MphRead.Mods.Launcher.Gui;

internal enum SemanticRuntimeCaptureSubmission
{
    Accepted,
    QueueFull,
    StaleIdentity,
    Canceled,
    InvalidArguments,
    Unavailable
}

internal sealed record SemanticRuntimeCaptureCompletion(
    bool Accepted, string? Path, string? Error);

/// <summary>
/// Owns a bounded bridge from semantic commands to the shipping SDL readback
/// queue. The host thread only moves requests/results; PNG encoding runs on a
/// worker and writes exclusively below the coordinator-owned evidence root.
/// </summary>
internal sealed class SemanticRuntimeCaptureOwner : IDisposable
{
    private const int MaximumPendingCaptures = 8;
    private const int MaximumLabelLength = 64;

    private readonly object _gate = new();
    private readonly Queue<PendingCapture> _queued = new(MaximumPendingCaptures);
    private readonly Dictionary<Guid, PendingCapture> _inFlight = new();
    private readonly Dictionary<Guid, PendingCapture> _encoding = new();
    private readonly string? _screenshotsRoot;
    private SemanticControlIdentity _identity;
    private bool _hostAttached;
    private bool _shutdown;

    internal SemanticRuntimeCaptureOwner(SemanticControlIdentity identity,
        string? evidenceRoot, string clientSlot)
    {
        identity.Validate();
        if (clientSlot is not ("a" or "b"))
            throw new ArgumentException("Client slot must be a or b.", nameof(clientSlot));
        _identity = identity;
        if (!String.IsNullOrWhiteSpace(evidenceRoot))
        {
            string root = Path.GetFullPath(evidenceRoot);
            _screenshotsRoot = Path.Combine(root, $"client-{clientSlot}", "screenshots");
        }
    }

    internal SemanticControlIdentity Identity
    {
        get { lock (_gate) return _identity; }
    }

    internal bool IsHostAttached
    {
        get { lock (_gate) return _hostAttached && !_shutdown; }
    }

    internal bool HasEvidenceRoot => _screenshotsRoot != null;

    internal bool AttachHost()
    {
        lock (_gate)
        {
            if (_shutdown) return false;
            _hostAttached = true;
            return true;
        }
    }

    internal void DetachHost()
    {
        lock (_gate)
        {
            _hostAttached = false;
            RejectAllLocked("capture-owner-detached");
        }
    }

    internal void AdvanceIdentity(SemanticControlIdentity identity)
    {
        identity.Validate();
        lock (_gate)
        {
            if (_identity == identity) return;
            _identity = identity;
            RejectAllLocked("stale-identity");
        }
    }

    internal SemanticRuntimeCaptureSubmission TrySubmit(
        SemanticControlIdentity identity, string label,
        CancellationToken cancellationToken,
        out Task<SemanticRuntimeCaptureCompletion>? completion)
    {
        completion = null;
        if (!IsValidLabel(label))
            return SemanticRuntimeCaptureSubmission.InvalidArguments;
        if (cancellationToken.IsCancellationRequested)
            return SemanticRuntimeCaptureSubmission.Canceled;
        lock (_gate)
        {
            if (_shutdown || !_hostAttached || _screenshotsRoot == null)
                return SemanticRuntimeCaptureSubmission.Unavailable;
            if (_identity != identity)
                return SemanticRuntimeCaptureSubmission.StaleIdentity;
            if (_queued.Count + _inFlight.Count + _encoding.Count
                >= MaximumPendingCaptures)
                return SemanticRuntimeCaptureSubmission.QueueFull;
            var pending = new PendingCapture(Guid.NewGuid(), label);
            _queued.Enqueue(pending);
            completion = pending.Completion.Task;
            return SemanticRuntimeCaptureSubmission.Accepted;
        }
    }

    internal bool TryTakeForHost(int width, int height, long originatingFrame,
        out SemanticRuntimeCaptureHostRequest? hostRequest)
    {
        hostRequest = null;
        lock (_gate)
        {
            if (_shutdown || !_hostAttached || _screenshotsRoot == null
                || _queued.Count == 0) return false;
            PendingCapture pending = _queued.Dequeue();
            var request = new RenderCaptureRequest(pending.RequestId,
                originatingFrame, CaptureTargetKind.FinalPresentedFrame,
                width, height, CapturePixelFormat.Rgb8,
                CaptureRowOrientation.BottomUp, CaptureDeliveryKind.Screenshot,
                pending.Label);
            pending.Request = request;
            _inFlight.Add(pending.RequestId, pending);
            hostRequest = new SemanticRuntimeCaptureHostRequest(request);
            return true;
        }
    }

    internal bool TryDeliver(RenderCaptureResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        PendingCapture? pending;
        string? root;
        lock (_gate)
        {
            if (!_inFlight.Remove(result.RequestId, out pending)) return false;
            _encoding.Add(result.RequestId, pending);
            root = _screenshotsRoot;
        }
        if (root == null || pending.Request == null
            || result.Target != CaptureTargetKind.FinalPresentedFrame
            || result.Delivery != CaptureDeliveryKind.Screenshot
            || result.Width != pending.Request.Width
            || result.Height != pending.Request.Height)
        {
            pending.Completion.TrySetResult(new SemanticRuntimeCaptureCompletion(
                false, null, "capture-result-mismatch"));
            lock (_gate) _encoding.Remove(result.RequestId);
            return true;
        }
        string output = Path.Combine(root,
            $"{pending.Label}-{result.OriginatingFrame}.png");
        _ = Task.Run(() => Encode(pending, result, output));
        return true;
    }

    internal bool TryDeliver(RenderCaptureFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        PendingCapture? pending;
        lock (_gate)
        {
            if (!_inFlight.Remove(failure.RequestId, out pending)) return false;
        }
        pending.Completion.TrySetResult(new SemanticRuntimeCaptureCompletion(
            false, null, "capture-readback-failed"));
        return true;
    }

    internal void RejectHostRequest(Guid requestId, string error)
    {
        PendingCapture? pending;
        lock (_gate) _inFlight.Remove(requestId, out pending);
        pending?.Completion.TrySetResult(new SemanticRuntimeCaptureCompletion(
            false, null, error));
    }

    internal void RejectQueued(string error)
    {
        lock (_gate)
        {
            while (_queued.TryDequeue(out PendingCapture? pending))
                pending.Completion.TrySetResult(new SemanticRuntimeCaptureCompletion(
                    false, null, error));
        }
    }

    internal void Shutdown()
    {
        lock (_gate)
        {
            if (_shutdown) return;
            _shutdown = true;
            _hostAttached = false;
            RejectAllLocked("capture-owner-shutdown");
        }
    }

    public void Dispose() => Shutdown();

    private void Encode(PendingCapture pending, RenderCaptureResult result,
        string output)
    {
        try
        {
            lock (_gate)
            {
                if (pending.Invalidated) return;
            }
            string root = _screenshotsRoot!;
            EnsureRegularDirectory(root);
            string full = Path.GetFullPath(output);
            string prefix = root.EndsWith(Path.DirectorySeparatorChar)
                ? root : root + Path.DirectorySeparatorChar;
            if (!full.StartsWith(prefix, StringComparison.Ordinal))
                throw new InvalidDataException("Capture path escaped the evidence root.");
            RenderCapturePng.Write(result, full);
            pending.Completion.TrySetResult(new SemanticRuntimeCaptureCompletion(
                true, full, null));
        }
        catch (Exception error)
        {
            pending.Completion.TrySetResult(new SemanticRuntimeCaptureCompletion(
                false, null, $"capture-encode-failed:{error.GetType().Name}"));
        }
        finally
        {
            lock (_gate) _encoding.Remove(result.RequestId);
        }
    }

    private static void EnsureRegularDirectory(string path)
    {
        Directory.CreateDirectory(path);
        var info = new DirectoryInfo(path);
        if (info.LinkTarget != null
            || (info.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException(
                "Capture evidence directory must not be a symbolic link.");
    }

    private void RejectAllLocked(string error)
    {
        while (_queued.TryDequeue(out PendingCapture? pending))
            pending.Completion.TrySetResult(new SemanticRuntimeCaptureCompletion(
                false, null, error));
        foreach (PendingCapture pending in _inFlight.Values)
            pending.Completion.TrySetResult(new SemanticRuntimeCaptureCompletion(
                false, null, error));
        _inFlight.Clear();
        foreach (PendingCapture pending in _encoding.Values)
        {
            pending.Invalidated = true;
            pending.Completion.TrySetResult(new SemanticRuntimeCaptureCompletion(
                false, null, error));
        }
    }

    private static bool IsValidLabel(string label)
    {
        if (String.IsNullOrEmpty(label) || label.Length > MaximumLabelLength
            || !IsLabelCharacter(label[0], first: true)) return false;
        for (int i = 1; i < label.Length; i++)
            if (!IsLabelCharacter(label[i], first: false)) return false;
        return true;
    }

    private static bool IsLabelCharacter(char value, bool first)
        => value is >= 'A' and <= 'Z' or >= 'a' and <= 'z'
            or >= '0' and <= '9'
            || !first && value is '-' or '_' or '.';

    internal sealed record SemanticRuntimeCaptureHostRequest(
        RenderCaptureRequest Request);

    private sealed class PendingCapture
    {
        internal PendingCapture(Guid requestId, string label)
        {
            RequestId = requestId;
            Label = label;
        }

        internal Guid RequestId { get; }
        internal string Label { get; }
        internal RenderCaptureRequest? Request { get; set; }
        internal bool Invalidated { get; set; }
        internal TaskCompletionSource<SemanticRuntimeCaptureCompletion> Completion { get; }
            = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
