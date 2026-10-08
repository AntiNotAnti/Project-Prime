using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace MphRead.Mods.Launcher.RmlUi.Host
{
    /// <summary>
    /// Owns native lifetime on the engine thread. Workers may enqueue immutable
    /// snapshots, but may never call the bridge or publish into a retired document.
    /// </summary>
    public sealed class RmlUiHost : IDisposable
    {
        private sealed class DocumentState
        {
            public readonly CancellationTokenSource Cancellation = new();
            public readonly Dictionary<string, RmlUiBindingValue> Bindings = new(StringComparer.Ordinal);
            public readonly RmlUiDocumentLayer Layer;
            public bool Visible = true;
            public long Revision = -1;

            public DocumentState(RmlUiDocumentLayer layer) => Layer = layer;
        }

        private readonly record struct PendingSnapshot(RmlUiBindingSnapshot Snapshot, CancellationToken Cancellation);
        private static long _legacyGeneration;
        private readonly IRmlUiNativeBridge _native;
        private readonly Dictionary<ulong, DocumentState> _documents = new();
        private readonly List<ulong> _documentOrder = new();
        private readonly ConcurrentQueue<PendingSnapshot> _pending = new();
        private readonly byte[] _actionBuffer = new byte[256];
        private int _ownerThread;
        private ulong _generation;
        private ulong _nativeSequence;
        private ulong _managedSequence;
        private static readonly string[] NativeControlledBindings =
        {
            "home_mode", "multiplayer_mode", "play_create_mode", "play_browser_mode",
            "activity_selector_open", "lobby_rules_open"
        };
        private uint _protocol;
        private string _root = "";
        private int _width, _height;
        private float _density;
        private RmlUiRenderBackend _backend;
        private volatile bool _active;
        private bool _lifetimeTransition;

        public bool Active => _active;
        public uint ProtocolVersion => _protocol;
        public RmlUiDocumentToken HomeDocument { get; private set; }
        public RmlUiInput Input { get; }
        public Exception? LastCleanupError { get; private set; }
        public event Action<Exception>? CleanupFailed;

        public RmlUiHost() : this(new RmlUiNativeBridge()) { }
        internal RmlUiHost(IRmlUiNativeBridge native)
        {
            _native = native;
            Input = new RmlUiInput(this, native);
        }

        public bool Initialize(int width, int height, float density, string assetRoot,
            RmlUiRenderBackend backend = RmlUiRenderBackend.OpenGl)
        {
            VerifyNativeCallAllowed();
            if (_active)
            {
                VerifyOwnerThread();
                return true;
            }
            ValidateViewport(width, height, density);
            if (!Enum.IsDefined(backend)) throw new ArgumentOutOfRangeException(nameof(backend));
            _ownerThread = Environment.CurrentManagedThreadId;
            _root = Path.GetFullPath(assetRoot);
            _width = width; _height = height; _density = density; _backend = backend;
            try
            {
                try { _protocol = _native.ProtocolVersion(); }
                catch (EntryPointNotFoundException) { _protocol = 0; }
                if (_protocol != 0 && _protocol != RmlUiIntentRegistry.ProtocolVersion)
                    throw new NotSupportedException($"Unsupported native RmlUi protocol {_protocol}.");
                if (_protocol == 0 && backend != RmlUiRenderBackend.OpenGl)
                    throw new NotSupportedException("The legacy RmlUi bridge supports OpenGL only.");
                int result = _protocol == 0
                    ? _native.Initialize(width, height, density, _root)
                    : _native.InitializeBackend(width, height, density, _root, (int)backend);
                if (result == 0)
                {
                    CleanupNative();
                    return false;
                }
                _generation = _protocol == 0
                    ? (ulong)Interlocked.Increment(ref _legacyGeneration) : _native.Generation();
                ulong home = _protocol == 0 ? 1 : _native.HomeDocument();
                if (_generation == 0 || home == 0)
                    throw new InvalidOperationException("Native RmlUi returned an invalid document lifetime.");
                HomeDocument = new(_generation, home);
                _documents.Add(home, new DocumentState(RmlUiDocumentLayer.Page));
                _documentOrder.Add(home);
                _nativeSequence = _managedSequence = 0;
                _active = true;
                Input.Reset();
                return true;
            }
            catch
            {
                CleanupNative();
                throw;
            }
        }

        public bool Reinitialize()
        {
            VerifyNativeCallAllowed();
            if (!_active) return false;
            Shutdown();
            return Initialize(_width, _height, _density, _root, _backend);
        }

#if DEBUG
        /// <summary>Explicit developer reload. All previous document tokens retire.</summary>
        public bool ReloadAssets() => Reinitialize();
#endif

        public RmlUiDocumentToken OpenDocument(string relativePath, RmlUiDocumentLayer layer)
        {
            RequireActive();
            if (_protocol == 0) throw new NotSupportedException("Multiple documents require the versioned RmlUi bridge.");
            if (!Enum.IsDefined(layer)) throw new ArgumentOutOfRangeException(nameof(layer));
            ValidateRelativeDocumentPath(relativePath);
            ulong id = _native.OpenDocument(relativePath.Replace('\\', '/'), (int)layer);
            if (id == 0 || _documents.ContainsKey(id))
                throw new InvalidOperationException($"Could not open RmlUi document '{relativePath}'.");
            _documents.Add(id, new DocumentState(layer));
            _documentOrder.Add(id);
            return new(_generation, id);
        }

        public bool CloseDocument(RmlUiDocumentToken document)
        {
            VerifyNativeCallAllowed();
            if (!IsAlive(document) || document == HomeDocument) return false;
            if (_native.CloseDocument(document.DocumentId) == 0) return false;
            RetireDocument(document.DocumentId);
            return true;
        }

        public bool ShowDocument(RmlUiDocumentToken document, bool show)
        {
            VerifyNativeCallAllowed();
            if (!IsAlive(document)) return false;
            // Legacy composition is controlled by Shell and does not have a document API.
            bool shown = _protocol == 0 || _native.ShowDocument(document.DocumentId, show ? 1 : 0) != 0;
            if (shown)
            {
                _documents[document.DocumentId].Visible = show;
                if (show)
                {
                    _documentOrder.Remove(document.DocumentId);
                    _documentOrder.Add(document.DocumentId);
                }
            }
            return shown;
        }

        public bool FocusDocument(RmlUiDocumentToken document, string element)
        {
            VerifyNativeCallAllowed();
            return IsAlive(document) && _protocol != 0
                && _native.FocusDocument(document.DocumentId, element) != 0;
        }

        public bool IsAlive(RmlUiDocumentToken document)
        {
            VerifyOwnerThread();
            return _active && document.Generation == _generation && _documents.ContainsKey(document.DocumentId);
        }

        public CancellationToken DocumentCancellation(RmlUiDocumentToken document)
        {
            VerifyOwnerThread();
            return IsAlive(document) ? _documents[document.DocumentId].Cancellation.Token
                : new CancellationToken(canceled: true);
        }

        /// <summary>Safe on a worker thread. It is applied only by Update on the owner thread.</summary>
        public void EnqueueSnapshot(RmlUiBindingSnapshot snapshot, CancellationToken cancellation = default)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            if (!cancellation.IsCancellationRequested && _active)
                _pending.Enqueue(new(snapshot, cancellation));
        }

        public bool Present(RmlUiBindingSnapshot snapshot)
        {
            VerifyNativeCallAllowed();
            if (!IsAlive(snapshot.Document)) return false;
            DocumentState state = _documents[snapshot.Document.DocumentId];
            if (snapshot.Revision <= state.Revision) return false;
            foreach (var binding in snapshot.Bindings)
                SetBinding(snapshot.Document, binding.Key, binding.Value);
            state.Revision = snapshot.Revision;
            return true;
        }

        public void SetText(RmlUiDocumentToken document, string name, string value) =>
            SetBinding(document, name, RmlUiBindingValue.FromText(value));
        public void SetBool(RmlUiDocumentToken document, string name, bool value) =>
            SetBinding(document, name, RmlUiBindingValue.FromBoolean(value));

        private void SetBinding(RmlUiDocumentToken document, string name, RmlUiBindingValue value)
        {
            VerifyNativeCallAllowed();
            if (!IsAlive(document)) return;
            DocumentState state = _documents[document.DocumentId];
            if (state.Bindings.TryGetValue(name, out RmlUiBindingValue previous) && previous == value) return;
            if (value.Kind == RmlUiBindingKind.Text)
            {
                if (_protocol == 0) _native.SetText(name, value.Text);
                else if (_native.DocumentSetText(document.DocumentId, name, value.Text) == 0)
                    throw new InvalidOperationException($"Native RmlUi rejected text binding '{name}'.");
            }
            else
            {
                if (_protocol == 0) _native.SetBool(name, value.Boolean ? 1 : 0);
                else if (_native.DocumentSetBool(document.DocumentId, name, value.Boolean ? 1 : 0) == 0)
                    throw new InvalidOperationException($"Native RmlUi rejected boolean binding '{name}'.");
            }
            state.Bindings[name] = value;
        }

        public void SetField(RmlUiDocumentToken document, string id, string value)
        {
            VerifyNativeCallAllowed();
            if (!IsAlive(document)) return;
            if (_protocol == 0) _native.SetField(id, value ?? "");
            else _native.DocumentSetField(document.DocumentId, id, value ?? "");
        }

        public string ReadField(RmlUiDocumentToken document, string id)
        {
            VerifyNativeCallAllowed();
            if (!IsAlive(document)) return "";
            byte[] buffer = new byte[4096];
            int length = _protocol == 0 ? _native.ReadField(id, buffer, buffer.Length)
                : _native.DocumentReadField(document.DocumentId, id, buffer, buffer.Length);
            return length <= 0 ? "" : Encoding.UTF8.GetString(buffer, 0, Math.Min(length, buffer.Length - 1));
        }

        public void Resize(int width, int height, float density)
        {
            RequireActive();
            ValidateViewport(width, height, density);
            _native.Resize(width, height, density);
            _width = width; _height = height; _density = density;
        }

        public void Update()
        {
            RequireActive();
            for (int i = 0; i < 256 && _pending.TryDequeue(out PendingSnapshot pending); i++)
                if (!pending.Cancellation.IsCancellationRequested) Present(pending.Snapshot);
            _native.Update();
        }

        public void Render(int width, int height)
        {
            RequireActive();
            if (width > 0 && height > 0) _native.Render(width, height);
        }

        public bool TryTakeIntent(out RmlUiIntent intent)
        {
            VerifyNativeCallAllowed();
            intent = default;
            if (!_active) return false;
            // Bound malformed-event draining so a faulty bridge cannot monopolize a frame.
            for (int i = 0; i < 256; i++)
            {
                if (_protocol != 0)
                {
                    var packet = new RmlUiNativeIntent
                    {
                        Size = RmlUiIntentRegistry.NativeIntentSize,
                        Version = RmlUiIntentRegistry.ProtocolVersion
                    };
                    if (_native.TakeIntent(ref packet) <= 0) return false;
                    InvalidateNativeControlledBindings();
                    if (!RmlUiIntentRegistry.TryDecode(packet, out intent)
                        || packet.Generation != _generation || packet.Sequence <= _nativeSequence) continue;
                    _nativeSequence = packet.Sequence;
                    if (IsAlive(intent.Document)) return true;
                }
                else
                {
                    Array.Clear(_actionBuffer);
                    int length = _native.TakeAction(_actionBuffer, _actionBuffer.Length);
                    if (length <= 0) return false;
                    InvalidateNativeControlledBindings();
                    if (length >= _actionBuffer.Length) continue;
                    string action = Encoding.UTF8.GetString(_actionBuffer, 0, length);
                    if (RmlUiIntentRegistry.TryParseLegacy(action, HomeDocument, ++_managedSequence, out intent))
                        return true;
                }
            }
            intent = default;
            return false;
        }

        public RmlUiIntent CreateIntent(RmlUiIntentKind kind, int argument = 0)
        {
            RequireActive();
            if (!RmlUiIntentRegistry.IsValid(kind, argument)) throw new ArgumentException("Invalid intent.");
            return new(kind, argument, HomeDocument, ++_managedSequence);
        }

        public void DiscardIntents()
        {
            VerifyOwnerThread();
            for (int i = 0; i < 1024 && TryTakeIntent(out _); i++) { }
        }

        public bool Back()
        {
            RequireActive();
            // Retire managed ownership and worker cancellation before falling
            // through to the home document's built-in drawer/rules navigation.
            // The native compatibility Back entry point can also close modals,
            // so delegating it first would leave a live managed token for a
            // document whose native resources had already been released.
            for (int i = _documentOrder.Count - 1; i >= 0; i--)
            {
                ulong id = _documentOrder[i];
                DocumentState state = _documents[id];
                if (state.Visible && state.Layer == RmlUiDocumentLayer.Modal)
                    return CloseDocument(new(_generation, id));
            }
            bool handled = _native.Back() != 0;
            InvalidateNativeControlledBindings();
            return handled;
        }

        public void ReleaseInput()
        {
            VerifyNativeCallAllowed();
            if (_active && _protocol != 0) _native.FocusLost();
            InvalidateNativeControlledBindings();
        }

        public bool TextInputActive
        {
            get
            {
                VerifyNativeCallAllowed();
                return _active && _protocol != 0 && _native.TextInputActive() != 0;
            }
        }

        public string FocusedElement()
        {
            VerifyNativeCallAllowed();
            if (!_active || _protocol == 0) return "";
            byte[] buffer = new byte[1024];
            int length = _native.FocusedElement(buffer, buffer.Length);
            return length <= 0 ? "" : Encoding.UTF8.GetString(buffer, 0, Math.Min(length, buffer.Length - 1));
        }

        public void SetClipboard(string text)
        {
            VerifyNativeCallAllowed();
            if (_active && _protocol != 0) _native.SetClipboard(text ?? "");
        }

        public string ReadClipboard()
        {
            VerifyNativeCallAllowed();
            if (!_active || _protocol == 0) return "";
            byte[] buffer = new byte[65536];
            int length = _native.ReadClipboard(buffer, buffer.Length);
            return length <= 0 ? "" : Encoding.UTF8.GetString(buffer, 0, Math.Min(length, buffer.Length - 1));
        }

        private void InvalidateNativeControlledBindings()
        {
            if (_documents.TryGetValue(HomeDocument.DocumentId, out DocumentState? state))
                foreach (string name in NativeControlledBindings) state.Bindings.Remove(name);
        }

        public void SetLobbyAnchor(int slot, float x, float y)
        {
            RequireActive();
            _native.SetLobbyAnchor(slot, x, y);
        }

        public void DeviceLost() => Shutdown();
        public void Dispose() => Shutdown();

        public void Shutdown()
        {
            VerifyNativeCallAllowed();
            if (!_active) return;
            _active = false;
            _lifetimeTransition = true;
            try { _native.Shutdown(); }
            finally
            {
                try { ClearManagedState(); }
                finally { _lifetimeTransition = false; }
            }
        }

        internal void VerifyOwnerThread()
        {
            if (_ownerThread != 0 && _ownerThread != Environment.CurrentManagedThreadId)
                throw new InvalidOperationException("RmlUi native calls must run on the engine owner thread.");
        }

        internal void VerifyNativeCallAllowed()
        {
            VerifyOwnerThread();
            if (_lifetimeTransition)
                throw new InvalidOperationException("RmlUi native calls cannot reenter a document lifetime transition.");
        }

        private void RequireActive()
        {
            VerifyNativeCallAllowed();
            if (!_active) throw new InvalidOperationException("RmlUi is not initialized.");
        }

        private void CleanupNative()
        {
            _active = false;
            try { _native.Shutdown(); }
            catch { /* Preserve the initialization error; managed lifetime still retires. */ }
            ClearManagedState();
        }

        private void ClearManagedState()
        {
            foreach (ulong id in new List<ulong>(_documents.Keys)) RetireDocument(id);
            _pending.Clear();
            _documentOrder.Clear();
            HomeDocument = default;
            _generation = 0;
            Input.Reset();
        }

        private void RetireDocument(ulong id)
        {
            // Drop ownership before callbacks, so a closed native document can
            // never appear live to a cancellation observer. Callback failures
            // must not leave the remaining runtime resources owned indefinitely.
            if (!_documents.Remove(id, out DocumentState? state)) return;
            _documentOrder.Remove(id);
            bool previousTransition = _lifetimeTransition;
            _lifetimeTransition = true;
            try { state.Cancellation.Cancel(); }
            catch (AggregateException ex) { ReportCleanupFailure(ex); }
            finally
            {
                state.Cancellation.Dispose();
                _lifetimeTransition = previousTransition;
            }
        }

        private void ReportCleanupFailure(Exception error)
        {
            LastCleanupError = LastCleanupError == null ? error : new AggregateException(LastCleanupError, error);
            if (CleanupFailed == null) return;
            foreach (Delegate subscriber in CleanupFailed.GetInvocationList())
            {
                try { ((Action<Exception>)subscriber)(error); }
                catch (Exception observerError)
                {
                    // An error reporter must not become another teardown failure.
                    LastCleanupError = new AggregateException(LastCleanupError, observerError);
                }
            }
        }

        private void ValidateRelativeDocumentPath(string path)
        {
            if (String.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path)
                || !String.Equals(Path.GetExtension(path), ".rml", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("A relative .rml document path is required.", nameof(path));
            string full = Path.GetFullPath(Path.Combine(_root, path.Replace('\\', Path.DirectorySeparatorChar)));
            if (!full.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                throw new ArgumentException("Document path escapes the asset directory.", nameof(path));
        }

        private static void ValidateViewport(int width, int height, float density)
        {
            if (width <= 0 || height <= 0 || !Single.IsFinite(density) || density <= 0)
                throw new ArgumentOutOfRangeException(nameof(width), "A finite, positive viewport is required.");
        }
    }
}
