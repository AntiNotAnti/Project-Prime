using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace MphRead.Mods.Launcher.RmlUi.Host
{
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal delegate nint RmlUiWindowSubclass(nint window, uint message, nuint wParam, nint lParam, nuint id, nuint data);

    internal interface IRmlUiImeWindowApi
    {
        bool Attach(nint window, RmlUiWindowSubclass callback, nuint id);
        bool Detach(nint window, RmlUiWindowSubclass callback, nuint id);
        nint Forward(nint window, uint message, nuint wParam, nint lParam);
        string CompositionText(nint window, bool result);
        int Cursor(nint window);
        void CancelComposition(nint window);
        void PositionCandidate(nint window, RmlUiTextInputBounds bounds);
    }

    /// <summary>
    /// Observes the existing GLFW HWND. IME-owned messages are consumed once,
    /// while ordinary window messages still follow the existing subclass chain.
    /// The rooted callback and HWND hook retire before the host/window does.
    /// </summary>
    public sealed class RmlUiWindowsIme : IDisposable
    {
        private const nuint SubclassId = 0x5050524d;
        private const uint Start = 0x010d, End = 0x010e, Compose = 0x010f;
        private static readonly HashSet<RmlUiWindowsIme> LiveHooks = new();
        private readonly RmlUiHost _host;
        private readonly IRmlUiImeWindowApi _api;
        private readonly Func<bool> _visible;
        private readonly RmlUiWindowSubclass _callback;
        private readonly int _ownerThread;
        private nint _window;
        private RmlUiTextInputState? _composition;
        private RmlUiTextInputState? _sequenceScope;
        private bool _ownsImeSequence;
        private bool _discardImeSequence;
        public bool Attached => _window != 0;
        public string LastFailure { get; private set; } = "";

        public static RmlUiWindowsIme? TryAttach(RmlUiHost host, nint window, Func<bool> visible)
        {
            if (!OperatingSystem.IsWindows() || window == 0) return null;
            var adapter = new RmlUiWindowsIme(host, window, visible, new WindowsImeWindowApi());
            if (adapter.Attached) return adapter;
            adapter.Dispose();
            return null;
        }

        internal RmlUiWindowsIme(RmlUiHost host, nint window, Func<bool> visible, IRmlUiImeWindowApi api)
        {
            _host = host; _api = api; _visible = visible;
            _ownerThread = Environment.CurrentManagedThreadId;
            _callback = WindowMessage;
            try
            {
                host.VerifyOwnerThread();
                if (window != 0 && api.Attach(window, _callback, SubclassId))
                {
                    _window = window;
                    lock (LiveHooks) LiveHooks.Add(this); // Native HWND callbacks do not root managed delegates.
                }
            }
            catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException)
            {
                LastFailure = "Windows IME window adapter is unavailable";
            }
        }

        public void RefreshCandidatePosition()
        {
            VerifyOwner();
            if (_window != 0 && _visible() && _host.TryGetTextInputState(out var state)
                && (state.Capabilities & RmlUiTextInputCapabilities.Bounds) != 0)
                _api.PositionCandidate(_window, state.Bounds);
        }

        public void Cancel() => CancelCore(notifyPlatform: true);

        private void CancelCore(bool notifyPlatform)
        {
            VerifyOwner();
            var composition = _composition;
            _composition = null;
            // Keep swallowing promoted characters until WM_IME_ENDCOMPOSITION.
            // Otherwise a delayed result could enter the next focused field.
            if (_ownsImeSequence) _discardImeSequence = true;
            if (composition is { } scope && _host.Active)
                Dispatch(scope, RmlUiPlatformInputKind.CompositionCancel);
            if (notifyPlatform && _ownsImeSequence && _window != 0)
                _api.CancelComposition(_window);
        }

        public void Dispose()
        {
            VerifyOwner();
            try { Cancel(); }
            catch { LastFailure = "Windows IME composition cancellation failed during hook removal"; }
            nint window = _window;
            if (window == 0) return;
            bool detached = false;
            try { detached = _api.Detach(window, _callback, SubclassId); }
            catch { /* Keep the delegate rooted if native removal cannot be confirmed. */ }
            if (detached)
            {
                _window = 0;
                lock (LiveHooks) LiveHooks.Remove(this);
            }
            else LastFailure = "Windows IME hook removal failed; callback remains rooted until window destruction";
        }

        private nint WindowMessage(nint window, uint message, nuint wParam, nint lParam, nuint id, nuint data)
        {
            try
            {
                VerifyOwner();
                if (message == 0x0082) // WM_NCDESTROY: native window destruction always detaches the callback.
                {
                    try { CancelCore(notifyPlatform: false); }
                    finally
                    {
                        try { _api.Detach(window, _callback, id); }
                        finally
                        {
                            _window = 0;
                            lock (LiveHooks) LiveHooks.Remove(this);
                        }
                    }
                }
                else if (message == 0x0008) // WM_KILLFOCUS
                {
                    Cancel();
                    if (_host.Active) _host.ReleaseInput();
                }
                else if (message == End && _ownsImeSequence)
                {
                    try { CancelCore(notifyPlatform: false); }
                    finally
                    {
                        _ownsImeSequence = _discardImeSequence = false;
                        _sequenceScope = null;
                    }
                    return 0;
                }
                else if (message == Start && _visible() && _host.Active)
                {
                    CancelCore(notifyPlatform: false);
                    _ownsImeSequence = _discardImeSequence = false;
                    _sequenceScope = null;
                    if (Begin()) return 0;
                }
                else if (message == Compose && _ownsImeSequence)
                {
                    if (_discardImeSequence || !_visible() || !_host.Active || !SequenceFocusMatches())
                    {
                        Cancel();
                        return 0;
                    }
                    if (_composition.HasValue || Begin())
                    {
                        var scope = _composition!.Value;
                        if (lParam == 0) { Cancel(); return 0; }
                        if ((lParam & 0x0800) != 0) // GCS_RESULTSTR
                        {
                            string result = _api.CompositionText(window, true);
                            Dispatch(scope, RmlUiPlatformInputKind.CompositionCommit, result);
                            _composition = null;
                        }
                        if ((lParam & 0x0008) != 0) // GCS_COMPSTR; some editors send both committed and new preedit.
                        {
                            if (_composition.HasValue || Begin())
                            {
                                string preedit = _api.CompositionText(window, false);
                                int cursorUtf16 = _api.Cursor(window);
                                int cursor = cursorUtf16 < 0 ? -1 : ScalarOffset(preedit, cursorUtf16);
                                Dispatch(_composition!.Value, RmlUiPlatformInputKind.CompositionUpdate, preedit, cursor);
                            }
                        }
                        RefreshCandidatePosition();
                        return 0; // Block GLFW/DefWindowProc from duplicating the composition commit.
                    }
                }
                else if (message is 0x0286 or 0x0109 or 0x0102 or 0x0106 && _ownsImeSequence)
                    // WM_IME_CHAR, WM_UNICHAR, WM_CHAR and WM_SYSCHAR can be
                    // generated from a single result string; the composition
                    // ABI has already applied that result once.
                    return 0;
            }
            catch
            {
                // Never unwind through an unmanaged window procedure or log
                // text read from an IME/password field. Lifecycle failure is
                // observable through metadata only.
                LastFailure = Environment.CurrentManagedThreadId == _ownerThread
                    ? "Windows IME event rejected" : "Windows IME callback arrived off the owner thread";
            }
            return _api.Forward(window, message, wParam, lParam);
        }

        private bool Begin()
        {
            if (!_host.TryGetTextInputState(out var scope)
                || (scope.Capabilities & RmlUiTextInputCapabilities.Composition) == 0) return false;
            if (_ownsImeSequence && _sequenceScope is { } original
                && (scope.Document != original.Document || scope.FocusEpoch != original.FocusEpoch))
            {
                _discardImeSequence = true;
                return false;
            }
            if (Dispatch(scope, RmlUiPlatformInputKind.CompositionBegin) != RmlUiInputResult.Accepted) return false;
            _composition = scope;
            _sequenceScope = scope;
            _ownsImeSequence = true;
            _discardImeSequence = false;
            return true;
        }

        private bool SequenceFocusMatches() => _sequenceScope is { } original
            && _host.TryGetTextInputState(out var current)
            && current.Document == original.Document && current.FocusEpoch == original.FocusEpoch;

        private RmlUiInputResult Dispatch(RmlUiTextInputState scope, RmlUiPlatformInputKind kind,
            string text = "", int cursor = -1) => _host.Input.Dispatch(new(scope.Document, kind,
                RmlUiInputDevice.InputMethod, scope.FocusEpoch, Text: text, Cursor: cursor));

        internal static int ScalarOffset(string text, int utf16Offset)
        {
            int count = 0, offset = 0;
            foreach (Rune rune in text.EnumerateRunes())
            {
                if (offset + rune.Utf16SequenceLength > utf16Offset) break;
                offset += rune.Utf16SequenceLength;
                count++;
            }
            return count;
        }

        private void VerifyOwner()
        {
            if (Environment.CurrentManagedThreadId != _ownerThread)
                throw new InvalidOperationException("Windows IME hooks must be attached, used and detached on the window owner thread.");
        }
    }

    internal sealed class WindowsImeWindowApi : IRmlUiImeWindowApi
    {
        public bool Attach(nint window, RmlUiWindowSubclass callback, nuint id)
            => GetWindowThreadProcessId(window, 0) == GetCurrentThreadId() && SetWindowSubclass(window, callback, id, 0);
        public bool Detach(nint window, RmlUiWindowSubclass callback, nuint id) => RemoveWindowSubclass(window, callback, id);
        public nint Forward(nint window, uint message, nuint wParam, nint lParam) => DefSubclassProc(window, message, wParam, lParam);
        public string CompositionText(nint window, bool result)
        {
            nint context = ImmGetContext(window);
            if (context == 0) return "";
            try
            {
                uint kind = result ? 0x0800u : 0x0008u;
                int bytes = ImmGetCompositionStringW(context, kind, 0, 0);
                if (bytes <= 0 || bytes > 131072 || (bytes & 1) != 0) return "";
                nint buffer = Marshal.AllocHGlobal(bytes);
                try
                {
                    int read = ImmGetCompositionStringW(context, kind, buffer, (uint)bytes);
                    return read <= 0 ? "" : Marshal.PtrToStringUni(buffer, Math.Min(read, bytes) / 2) ?? "";
                }
                finally { Marshal.FreeHGlobal(buffer); }
            }
            finally { ImmReleaseContext(window, context); }
        }
        public int Cursor(nint window)
        {
            nint context = ImmGetContext(window);
            if (context == 0) return -1;
            try { return ImmGetCompositionStringW(context, 0x0080, 0, 0); }
            finally { ImmReleaseContext(window, context); }
        }
        public void CancelComposition(nint window)
        {
            nint context = ImmGetContext(window);
            if (context == 0) return;
            try { ImmNotifyIME(context, 0x0015, 4, 0); } // NI_COMPOSITIONSTR / CPS_CANCEL
            finally { ImmReleaseContext(window, context); }
        }
        public void PositionCandidate(nint window, RmlUiTextInputBounds bounds)
        {
            if (!Single.IsFinite(bounds.X) || !Single.IsFinite(bounds.Y) || !Single.IsFinite(bounds.Width)
                || !Single.IsFinite(bounds.Height)) return;
            nint context = ImmGetContext(window);
            if (context == 0) return;
            try
            {
                var form = new CandidateForm { Style = 0x0080, X = (int)bounds.X, Y = (int)(bounds.Y + bounds.Height),
                    Left = (int)bounds.X, Top = (int)bounds.Y,
                    Right = (int)(bounds.X + bounds.Width), Bottom = (int)(bounds.Y + bounds.Height) };
                ImmSetCandidateWindow(context, ref form);
            }
            finally { ImmReleaseContext(window, context); }
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct CandidateForm { public uint Index, Style; public int X, Y, Left, Top, Right, Bottom; }
        [DllImport("comctl32.dll", CallingConvention = CallingConvention.Winapi)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowSubclass(nint window, RmlUiWindowSubclass callback, nuint id, nuint data);
        [DllImport("comctl32.dll", CallingConvention = CallingConvention.Winapi)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool RemoveWindowSubclass(nint window, RmlUiWindowSubclass callback, nuint id);
        [DllImport("comctl32.dll", CallingConvention = CallingConvention.Winapi)]
        private static extern nint DefSubclassProc(nint window, uint message, nuint wParam, nint lParam);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, nint processId);
        [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
        [DllImport("imm32.dll")] private static extern nint ImmGetContext(nint window);
        [DllImport("imm32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ImmNotifyIME(nint context, uint action, uint index, uint value);
        [DllImport("imm32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ImmReleaseContext(nint window, nint context);
        [DllImport("imm32.dll")] private static extern int ImmGetCompositionStringW(nint context, uint kind, nint buffer, uint bytes);
        [DllImport("imm32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ImmSetCandidateWindow(nint context, ref CandidateForm form);
    }
}
