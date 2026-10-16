using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MphRead.Mods.Launcher.RmlUi.Host;

/// <summary>
/// System libibus owns D-Bus discovery/serialization. One private GLib context
/// owns every proxy and callback; it never reads the GLFW display event queue.
/// </summary>
internal sealed class RmlUiIbusApi : IRmlUiLinuxImeApi
{
    private const int KeyTimeoutMilliseconds = 80, OwnerTimeoutMilliseconds = 120;
    private const int MaximumMessages = 128, MaximumTextBytes = 128 * 1024;
    private readonly ConcurrentQueue<Work> _work = new();
    private Work? _pendingFocus;
    private readonly ConcurrentQueue<RmlUiLinuxImeSignal> _signals = new();
    private readonly AutoResetEvent _wake = new(false);
    private readonly Thread _thread;
    private readonly List<Delegate> _contextCallbacks = new();
    private readonly List<nuint> _contextHandlers = new();
    private readonly UTF8Encoding _utf8 = new(false, true);
    private volatile bool _disposed;
    private int _status, _queuedSignals;
    private long _requestedSession, _readySession;
    private nint _bus, _context, _mainContext;
    private Work? _focus;
    private RmlUiLinuxImeRectangle? _lastRectangle;
    private AsyncCallback? _connectionCallback;
    private nint _connectionCancellable;
    private long _connectionDeadline, _nextConnectionAttempt;
    private AsyncCallback? _creationCallback;
    private nint _creationCancellable;
    private long _nextContextAttempt;
    private readonly string? _testEngine;
    public RmlUiLinuxImeStatus Status => (RmlUiLinuxImeStatus)Volatile.Read(ref _status);
    private sealed record Work(int Kind, long Session, bool Protected = false,
        RmlUiLinuxImeRectangle Rectangle = default, uint Symbol = 0, uint ScanCode = 0,
        uint Modifiers = 0, TaskCompletionSource<RmlUiLinuxImeReply>? Completion = null);

    internal RmlUiIbusApi(string? testEngine = null)
    {
        _testEngine = testEngine;
        _thread = new Thread(Run) { IsBackground = true, Name = "Prime RmlUi IBus" };
        _thread.Start();
    }
    public void Focus(long session, bool protectedField, RmlUiLinuxImeRectangle rectangle)
    {
        if (_disposed) return;
        // Invalidate callbacks immediately, before the worker can retire its proxy.
        Volatile.Write(ref _requestedSession, session);
        Interlocked.Exchange(ref _pendingFocus, new(0, session, protectedField, rectangle));
        try { _wake.Set(); } catch (ObjectDisposedException) { }
    }
    public void Move(long session, RmlUiLinuxImeRectangle rectangle)
    {
        if (!_disposed && _lastRectangle != rectangle)
        { _lastRectangle = rectangle; Enqueue(new(1, session, Rectangle: rectangle)); }
    }
    public RmlUiLinuxImeReply ProcessKey(long session, uint symbol, uint scanCode, uint modifiers)
    {
        if (_disposed || Status != RmlUiLinuxImeStatus.Available
            || Volatile.Read(ref _readySession) != session) return RmlUiLinuxImeReply.Declined;
        var completion = new TaskCompletionSource<RmlUiLinuxImeReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!Enqueue(new(2, session, Symbol: symbol, ScanCode: scanCode, Modifiers: modifiers, Completion: completion)))
            return RmlUiLinuxImeReply.Failed;
        if (!completion.Task.Wait(OwnerTimeoutMilliseconds))
        {
            Focus(0, false, default);
            return RmlUiLinuxImeReply.Failed;
        }
        return completion.Task.Result;
    }
    public bool TryTake(out RmlUiLinuxImeSignal signal)
    {
        if (!_signals.TryDequeue(out signal)) return false;
        Interlocked.Decrement(ref _queuedSignals); return true;
    }
    private bool Enqueue(Work work)
    {
        if (_disposed || _work.Count >= MaximumMessages) return false;
        _work.Enqueue(work);
        try { _wake.Set(); return true; } catch (ObjectDisposedException) { return false; }
    }
    private void Publish(RmlUiLinuxImeSignal signal)
    {
        if (_disposed || signal.Session == 0 || signal.Session != Volatile.Read(ref _requestedSession)) return;
        if (Interlocked.Increment(ref _queuedSignals) > MaximumMessages)
        {
            Interlocked.Decrement(ref _queuedSignals);
            while (TryTake(out _)) { }
            Interlocked.Increment(ref _queuedSignals);
            _signals.Enqueue(new(signal.Session, RmlUiLinuxImeSignalKind.Disconnected));
            Volatile.Write(ref _requestedSession, 0);
            return;
        }
        _signals.Enqueue(signal);
    }
    private void Run()
    {
        try
        {
            _mainContext = Native.g_main_context_new();
            Native.g_main_context_push_thread_default(_mainContext);
            Native.ibus_init();
            var connecting = Stopwatch.StartNew();
            while (!_disposed || _creationCallback != null || _connectionCallback != null)
            {
                PumpNative();
                if (_disposed)
                {
                    if (_creationCancellable != 0) Native.g_cancellable_cancel(_creationCancellable);
                    if (_connectionCancellable != 0) Native.g_cancellable_cancel(_connectionCancellable);
                    _wake.WaitOne(8); continue;
                }
                if (_connectionCancellable != 0 && Environment.TickCount64 >= _connectionDeadline)
                    Native.g_cancellable_cancel(_connectionCancellable);
                if (_bus != 0 && Native.g_dbus_connection_is_closed(_bus) != 0)
                {
                    Publish(new(Volatile.Read(ref _readySession), RmlUiLinuxImeSignalKind.Disconnected));
                    RetireContext(); Native.g_object_unref(_bus); _bus = 0;
                    _nextConnectionAttempt = Environment.TickCount64 + 1000;
                }
                if (_bus == 0 && _connectionCallback == null && Environment.TickCount64 >= _nextConnectionAttempt)
                    ConnectBus();
                bool connected = _bus != 0 && Native.g_dbus_connection_is_closed(_bus) == 0;
                Volatile.Write(ref _status, connected ? (int)RmlUiLinuxImeStatus.Available
                    : connecting.ElapsedMilliseconds > 1500 ? (int)RmlUiLinuxImeStatus.Unavailable : (int)RmlUiLinuxImeStatus.Connecting);
                if (Interlocked.Exchange(ref _pendingFocus, null) is { } latestFocus) Execute(latestFocus, connected);
                while (_work.TryDequeue(out var work))
                {
                    if (_disposed) { work.Completion?.TrySetResult(RmlUiLinuxImeReply.Failed); continue; }
                    Execute(work, connected);
                }
                if (connected && _context == 0 && _creationCallback == null
                    && Environment.TickCount64 >= _nextContextAttempt && _focus is { Session: > 0 } focus
                    && focus.Session == Volatile.Read(ref _requestedSession)) CreateContext(focus);
                _wake.WaitOne(8);
            }
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        { /* Optional system input method library is absent. */ }
        catch { /* IPC failure never exposes bus errors or input payloads. */ }
        finally
        {
            Publish(new(Volatile.Read(ref _readySession), RmlUiLinuxImeSignalKind.Disconnected));
            Volatile.Write(ref _status, (int)RmlUiLinuxImeStatus.Unavailable);
            try { RetireContext(); } catch { }
            if (_bus != 0)
            {
                try { CloseConnection(_bus); } catch { }
                _bus = 0;
            }
            if (_mainContext != 0)
            {
                try { Native.g_main_context_pop_thread_default(_mainContext); Native.g_main_context_unref(_mainContext); } catch { }
                _mainContext = 0;
            }
            while (_work.TryDequeue(out var work)) work.Completion?.TrySetResult(RmlUiLinuxImeReply.Failed);
            _wake.Dispose();
        }
    }
    private void PumpNative()
    {
        for (int i = 0; i < 64 && Native.g_main_context_iteration(_mainContext, 0) != 0; i++) { }
    }
    private void ConnectBus()
    {
        _nextConnectionAttempt = Environment.TickCount64 + 1000;
        string address = Marshal.PtrToStringUTF8(Native.ibus_get_address()) ?? "";
        if (address.Length == 0) return;
        _connectionCancellable = Native.g_cancellable_new(); _connectionDeadline = Environment.TickCount64 + 1000;
        _connectionCallback = (_, result, _) =>
        {
            try
            {
                nint connection = Native.g_dbus_connection_new_for_address_finish(result, out nint error);
                if (error != 0) Native.g_error_free(error);
                if (connection == 0) return;
                if (_disposed) { CloseConnection(connection); return; }
                _bus = connection; Native.g_dbus_connection_set_exit_on_close(_bus, 0);
            }
            catch { Volatile.Write(ref _status, (int)RmlUiLinuxImeStatus.Unavailable); }
            finally
            {
                if (_connectionCancellable != 0) Native.g_object_unref(_connectionCancellable);
                _connectionCancellable = 0; _connectionCallback = null;
            }
        };
        // IBusBus is a process singleton and can belong to another toolkit's
        // GLib context. Own a separate connection instead of reusing that bus.
        try { Native.g_dbus_connection_new_for_address(address, 1u | 8u, 0, _connectionCancellable, _connectionCallback, 0); }
        catch
        {
            Native.g_object_unref(_connectionCancellable); _connectionCancellable = 0; _connectionCallback = null;
            throw;
        }
    }
    private static void CloseConnection(nint connection)
    {
        Native.g_dbus_connection_close_sync(connection, 0, out nint error);
        if (error != 0) Native.g_error_free(error);
        Native.g_object_unref(connection);
    }
    private void Execute(Work work, bool connected)
    {
        if (work.Kind == 0)
        {
            if (work.Session != Volatile.Read(ref _requestedSession)) return;
            RetireContext(); _focus = work.Session > 0 ? work : null; _nextContextAttempt = 0;
            if (connected && _focus != null && _creationCallback == null) CreateContext(work);
        }
        else if (work.Kind == 1)
        {
            if (_context != 0 && work.Session == Volatile.Read(ref _readySession)
                && work.Session == Volatile.Read(ref _requestedSession)) MoveContext(work.Rectangle);
        }
        else if (work.Kind == 2)
        {
            var reply = RmlUiLinuxImeReply.Declined;
            if (_context != 0 && connected && work.Session == Volatile.Read(ref _readySession)
                && work.Session == Volatile.Read(ref _requestedSession))
            {
                // Explicit GError distinguishes a declined key from a timed-out
                // engine. A timed-out result must never later duplicate GLFW text.
                nint parameters = Native.g_variant_new_keys("(uuu)", work.Symbol, work.ScanCode, work.Modifiers);
                nint result = Native.g_dbus_proxy_call_sync(_context, "ProcessKeyEvent", parameters,
                    0, KeyTimeoutMilliseconds, 0, out nint error);
                if (result == 0)
                {
                    if (error != 0) Native.g_error_free(error);
                    Volatile.Write(ref _requestedSession, 0);
                    reply = RmlUiLinuxImeReply.Failed;
                }
                else
                {
                    nint value = Native.g_variant_get_child_value(result, 0);
                    try { reply = Native.g_variant_get_boolean(value) != 0 ? RmlUiLinuxImeReply.Accepted : RmlUiLinuxImeReply.Declined; }
                    finally { Native.g_variant_unref(value); Native.g_variant_unref(result); }
                }
                PumpNative();
            }
            work.Completion?.TrySetResult(reply);
        }
    }
    private void CreateContext(Work focus)
    {
        nint connection = Native.g_object_ref(_bus);
        _creationCancellable = Native.g_cancellable_new();
        nint parameters = Native.g_variant_new_client("(s)", "ProjectPrime.RmlUi");
        nint reply = Native.g_dbus_connection_call_sync(connection, "org.freedesktop.IBus", "/org/freedesktop/IBus",
            "org.freedesktop.IBus", "CreateInputContext", parameters, 0, 1, 250, _creationCancellable, out nint error);
        if (error != 0) Native.g_error_free(error);
        if (reply == 0)
        {
            Native.g_object_unref(_creationCancellable); _creationCancellable = 0;
            Native.g_object_unref(connection);
            _nextContextAttempt = Environment.TickCount64 + 1000; return;
        }
        string path;
        nint value = Native.g_variant_get_child_value(reply, 0);
        try { path = Marshal.PtrToStringUTF8(Native.g_variant_get_string(value, out _)) ?? ""; }
        finally { Native.g_variant_unref(value); Native.g_variant_unref(reply); }
        _creationCallback = (_, result, _) =>
        {
            try
            {
                nint context = Native.ibus_input_context_new_async_finish(result, out nint completionError);
                if (completionError != 0) Native.g_error_free(completionError);
                if (context == 0)
                {
                    DestroyRemoteContext(connection, path);
                    _nextContextAttempt = focus.Session == Volatile.Read(ref _requestedSession) ? Environment.TickCount64 + 1000 : 0; return;
                }
                if (_disposed || focus.Session != Volatile.Read(ref _requestedSession) || _bus != connection
                    || Native.g_dbus_connection_is_closed(connection) != 0)
                { Native.ibus_proxy_destroy(context); Native.g_object_unref(context); return; }
                _context = context; ConfigureContext(focus);
            }
            catch { Publish(new(focus.Session, RmlUiLinuxImeSignalKind.Disconnected)); }
            finally
            {
                if (_creationCancellable != 0) Native.g_object_unref(_creationCancellable);
                Native.g_object_unref(connection);
                _creationCancellable = 0; _creationCallback = null;
            }
        };
        // Both remote creation and local proxy initialization share a cancellable.
        // The worker keeps pumping until cancellation completion, even after Dispose.
        try { Native.ibus_input_context_new_async(path, connection, _creationCancellable, _creationCallback, 0); }
        catch
        {
            DestroyRemoteContext(connection, path);
            Native.g_object_unref(connection); Native.g_object_unref(_creationCancellable);
            _creationCancellable = 0; _creationCallback = null; throw;
        }
    }
    private static void DestroyRemoteContext(nint connection, string path)
    {
        if (connection == 0 || Native.g_dbus_connection_is_closed(connection) != 0) return;
        nint response = Native.g_dbus_connection_call_sync(connection, "org.freedesktop.IBus", path,
            "org.freedesktop.IBus.Service", "Destroy", 0, 0, 1, KeyTimeoutMilliseconds, 0, out nint error);
        if (response != 0) Native.g_variant_unref(response);
        if (error != 0) Native.g_error_free(error);
    }
    private void ConfigureContext(Work focus)
    {
        long session = focus.Session;
        TextCallback commit = (_, text, _) => Safe(() => Publish(new(session, RmlUiLinuxImeSignalKind.Commit, ReadText(text))));
        PreeditCallback preedit = (_, text, cursor, visible, _) => Safe(() =>
            Publish(new(session, RmlUiLinuxImeSignalKind.Preedit, ReadText(text), checked((int)cursor), visible != 0)));
        SimpleCallback hide = (_, _) => Publish(new(session, RmlUiLinuxImeSignalKind.Hide));
        Connect("commit-text", commit); Connect("update-preedit-text", preedit); Connect("hide-preedit-text", hide);
        Native.g_dbus_proxy_set_default_timeout(_context, KeyTimeoutMilliseconds);
        // The desktop panel owns candidate/auxiliary lists. Never advertise or
        // provide surrounding text; private input is not offered for learning.
        Native.ibus_input_context_set_capabilities(_context, 1u | 8u | 128u);
        Native.ibus_input_context_set_content_type(_context, focus.Protected ? 8u : 0u, (1u << 11) | (1u << 1));
        if (_testEngine != null) Native.ibus_input_context_set_engine(_context, _testEngine);
        Native.ibus_input_context_focus_in(_context);
        MoveContext(focus.Rectangle);
        Volatile.Write(ref _readySession, session);
    }
    private void Connect(string signal, Delegate callback)
    {
        _contextCallbacks.Add(callback);
        _contextHandlers.Add(Native.g_signal_connect_data(_context, signal, Marshal.GetFunctionPointerForDelegate(callback), 0, 0, 0));
    }
    private void RetireContext()
    {
        if (_creationCancellable != 0) Native.g_cancellable_cancel(_creationCancellable);
        Volatile.Write(ref _readySession, 0);
        if (_context != 0)
        {
            foreach (nuint handler in _contextHandlers) Native.g_signal_handler_disconnect(_context, handler);
            Native.ibus_input_context_reset(_context);
            Native.ibus_input_context_focus_out(_context);
            Native.ibus_proxy_destroy(_context);
            Native.g_object_unref(_context); _context = 0;
        }
        _contextHandlers.Clear(); _contextCallbacks.Clear();
    }
    private void MoveContext(RmlUiLinuxImeRectangle r)
    {
        if (r.Relative)
        {
            try { Native.ibus_input_context_set_cursor_location_relative(_context, r.X, r.Y, Math.Max(1, r.Width), Math.Max(1, r.Height)); return; }
            catch (EntryPointNotFoundException) { }
        }
        Native.ibus_input_context_set_cursor_location(_context, r.X, r.Y, Math.Max(1, r.Width), Math.Max(1, r.Height));
    }
    private void Safe(Action callback)
    {
        try { callback(); }
        catch { Publish(new(Volatile.Read(ref _readySession), RmlUiLinuxImeSignalKind.Disconnected)); }
    }
    private string ReadText(nint text)
    {
        nint serialized = Native.ibus_serializable_serialize_object(text), value = 0;
        try
        {
            if (serialized == 0 || Native.g_variant_n_children(serialized) < 3) throw new InvalidOperationException();
            value = Native.g_variant_get_child_value(serialized, 2);
            if (Marshal.PtrToStringUTF8(Native.g_variant_get_type_string(value)) != "s") throw new InvalidOperationException();
            nint data = Native.g_variant_get_string(value, out nuint size);
            if (size > MaximumTextBytes) throw new InvalidOperationException();
            byte[] bytes = new byte[checked((int)size)]; Marshal.Copy(data, bytes, 0, bytes.Length);
            return _utf8.GetString(bytes);
        }
        finally { if (value != 0) Native.g_variant_unref(value); if (serialized != 0) Native.g_variant_unref(serialized); }
    }
    public void Dispose()
    {
        if (_disposed) return;
        Volatile.Write(ref _requestedSession, 0); _disposed = true;
        try { _wake.Set(); } catch (ObjectDisposedException) { }
        // A stalled daemon can delay startup, but never blocks the render owner
        // during shutdown. The rooted worker disposes every proxy when it returns.
        _thread.Join(OwnerTimeoutMilliseconds);
        while (TryTake(out _)) { }
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void TextCallback(nint context, nint text, nint data);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void PreeditCallback(nint context, nint text, uint cursor, int visible, nint data);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void SimpleCallback(nint context, nint data);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void AsyncCallback(nint source, nint result, nint data);
    private static class Native
    {
        private const string Ibus = "libibus-1.0.so.5", Glib = "libglib-2.0.so.0", Gobject = "libgobject-2.0.so.0", Gio = "libgio-2.0.so.0";
        [DllImport(Ibus)] internal static extern void ibus_init();
        [DllImport(Ibus)] internal static extern nint ibus_get_address();
        [DllImport(Ibus)] internal static extern void ibus_input_context_new_async([MarshalAs(UnmanagedType.LPUTF8Str)] string path,
            nint connection, nint cancellable, AsyncCallback callback, nint data);
        [DllImport(Ibus)] internal static extern nint ibus_input_context_new_async_finish(nint result, out nint error);
        [DllImport(Ibus)] internal static extern void ibus_input_context_set_capabilities(nint context, uint capabilities);
        [DllImport(Ibus)] internal static extern void ibus_input_context_set_content_type(nint context, uint purpose, uint hints);
        [DllImport(Ibus)] internal static extern void ibus_input_context_set_engine(nint context, [MarshalAs(UnmanagedType.LPUTF8Str)] string engine);
        [DllImport(Ibus)] internal static extern void ibus_input_context_set_cursor_location(nint context, int x, int y, int width, int height);
        [DllImport(Ibus)] internal static extern void ibus_input_context_set_cursor_location_relative(nint context, int x, int y, int width, int height);
        [DllImport(Ibus)] internal static extern void ibus_input_context_focus_in(nint context);
        [DllImport(Ibus)] internal static extern void ibus_input_context_focus_out(nint context);
        [DllImport(Ibus)] internal static extern void ibus_input_context_reset(nint context);
        [DllImport(Ibus)] internal static extern void ibus_proxy_destroy(nint context);
        [DllImport(Ibus)] internal static extern nint ibus_serializable_serialize_object(nint text);
        [DllImport(Gobject)] internal static extern nuint g_signal_connect_data(nint instance, [MarshalAs(UnmanagedType.LPUTF8Str)] string signal, nint callback, nint data, nint destroy, uint flags);
        [DllImport(Gobject)] internal static extern void g_signal_handler_disconnect(nint instance, nuint handler);
        [DllImport(Gobject)] internal static extern void g_object_unref(nint instance);
        [DllImport(Gobject)] internal static extern nint g_object_ref(nint instance);
        [DllImport(Gio)] internal static extern void g_dbus_proxy_set_default_timeout(nint proxy, int milliseconds);
        [DllImport(Gio)] internal static extern nint g_cancellable_new();
        [DllImport(Gio)] internal static extern void g_cancellable_cancel(nint cancellable);
        [DllImport(Gio)] internal static extern void g_dbus_connection_new_for_address([MarshalAs(UnmanagedType.LPUTF8Str)] string address,
            uint flags, nint observer, nint cancellable, AsyncCallback callback, nint data);
        [DllImport(Gio)] internal static extern nint g_dbus_connection_new_for_address_finish(nint result, out nint error);
        [DllImport(Gio)] internal static extern int g_dbus_connection_close_sync(nint connection, nint cancellable, out nint error);
        [DllImport(Gio)] internal static extern int g_dbus_connection_is_closed(nint connection);
        [DllImport(Gio)] internal static extern void g_dbus_connection_set_exit_on_close(nint connection, int enabled);
        [DllImport(Gio)] internal static extern nint g_dbus_connection_call_sync(nint connection, [MarshalAs(UnmanagedType.LPUTF8Str)] string destination,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string path, [MarshalAs(UnmanagedType.LPUTF8Str)] string @interface,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string method, nint parameters, nint replyType, uint flags, int timeout, nint cancellable, out nint error);
        [DllImport(Gio)] internal static extern nint g_dbus_proxy_call_sync(nint proxy, [MarshalAs(UnmanagedType.LPUTF8Str)] string method,
            nint parameters, uint flags, int timeout, nint cancellable, out nint error);
        [DllImport(Glib)] internal static extern nint g_main_context_new();
        [DllImport(Glib)] internal static extern void g_main_context_push_thread_default(nint context);
        [DllImport(Glib)] internal static extern void g_main_context_pop_thread_default(nint context);
        [DllImport(Glib)] internal static extern int g_main_context_iteration(nint context, int block);
        [DllImport(Glib)] internal static extern void g_main_context_unref(nint context);
        [DllImport(Glib)] internal static extern nuint g_variant_n_children(nint variant);
        [DllImport(Glib)] internal static extern nint g_variant_get_child_value(nint variant, nuint index);
        [DllImport(Glib)] internal static extern nint g_variant_get_type_string(nint variant);
        [DllImport(Glib)] internal static extern nint g_variant_get_string(nint variant, out nuint length);
        [DllImport(Glib, EntryPoint = "g_variant_new", CallingConvention = CallingConvention.Cdecl)]
        internal static extern nint g_variant_new_keys([MarshalAs(UnmanagedType.LPUTF8Str)] string format, uint symbol, uint code, uint modifiers);
        [DllImport(Glib, EntryPoint = "g_variant_new", CallingConvention = CallingConvention.Cdecl)]
        internal static extern nint g_variant_new_client([MarshalAs(UnmanagedType.LPUTF8Str)] string format, [MarshalAs(UnmanagedType.LPUTF8Str)] string client);
        [DllImport(Glib)] internal static extern int g_variant_get_boolean(nint variant);
        [DllImport(Glib)] internal static extern void g_error_free(nint error);
        [DllImport(Glib)] internal static extern void g_variant_unref(nint variant);
    }
}
