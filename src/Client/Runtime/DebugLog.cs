using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Launcher;
using MphRead.Mods.Update;

namespace MphRead.Mods
{
    /// <summary>
    /// Everything the program can say about itself, in a file, when the player
    /// asks for it.
    ///
    /// This exists for one kind of report: "it crashes when the map loads" --
    /// on a machine nobody here can plug in, sent by somebody who has no
    /// console window to copy anything out of (the Windows build is a GUI
    /// binary and deliberately opens none). Without a file there is nothing to
    /// ask for except a description.
    ///
    /// Two halves, and the first is most of the value for none of the work:
    /// <see cref="Console.Out"/> is *teed* into the file, so every line the
    /// program already prints -- the net session's, the launcher's, the
    /// renderer's one-line summary of what the options came out as -- is
    /// captured without a single call site being added. The second half is the
    /// handful of places that say something a log needs and a terminal does
    /// not: the machine, the build, the room being loaded and how far it got,
    /// what the driver calls itself, and the stack of anything that killed the
    /// process.
    ///
    /// Off by default and never on by accident. It costs a file handle, a lock
    /// per line and a directory that grows, which is not something to hand
    /// somebody who did not ask -- so it is one switch in the corner of the
    /// launcher, and it stays where they left it.
    /// </summary>
    public static class DebugLog
    {
        static DebugLog()
        {
            ContentFiles.ModelReading += (name, dir, firstHunt) =>
                Line("model", $"reading \"{name}\" (dir={dir}"
                    + (firstHunt ? ", first hunt)" : ")"));
        }

        private static StreamWriter? _writer;
        private static FileStream? _nativeStream;
        private static readonly object _lock = new();
        private static bool _hooked;
        private static bool _processHooksInstalled;
        private static int _processHookInstallCount;
        private static bool _forced;
        private static TextWriter? _consoleWas;
        private static int _savedStderrFd = -1;
        private static IntPtr _savedWindowsStderr = IntPtr.Zero;
        private static bool _windowsStderrAttached;
        private static bool _windowsStderrWasValid;

        /// <summary>Whether lines are going anywhere.</summary>
        public static bool Active => _writer != null;

        /// <summary>Where the file ended up, for the launcher to show.</summary>
        public static string? Path { get; private set; }
        public static string? NativePath { get; private set; }

        /// <summary>How many logs are kept before the oldest is deleted.</summary>
        private const int KeepSessions = 8;

        /// <summary>
        /// Turn it on for this run whatever the setting says. The
        /// <c>-debuglog</c> flag, which is how somebody who cannot reach the
        /// launcher -- because the launcher is what is crashing -- still gets
        /// a file.
        /// </summary>
        public static void Force() => _forced = true;

        /// <summary>
        /// Start logging if it has been asked for. Safe to call as often as
        /// anybody likes: the second call does nothing.
        /// </summary>
        public static void Attach()
        {
            lock (_lock)
            {
                if (_writer != null || (!_forced && !LauncherPrefs.DebugLogs)) return;
                try
                {
                    string directory = System.IO.Path.Combine(LauncherPrefs.Directory, "logs");
                    System.IO.Directory.CreateDirectory(directory);
                    Prune(directory);
                    (Path, NativePath) = CreateSessionPaths(directory,
                        Branding.Name.Replace(" ", ""), DateTime.Now);
                    // Shared, so the file can be read while the game is still
                    // running -- which is the only way to read the tail of one
                    // that is about to crash.
                    var stream = new FileStream(Path, FileMode.Create, FileAccess.Write,
                        FileShare.ReadWrite);
                    _writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };
                    AttachNativeStderr(NativePath);
                }
                catch (Exception ex)
                {
                    // A log that cannot be opened must not be the reason a session
                    // does not start.
                    _writer = null;
                    DetachNativeStderr();
                    Console.WriteLine($"[debug] could not open a log: {ex.Message}");
                    return;
                }
                Hook();
                WriteHeader();
            }
        }

        /// <summary>
        /// Stop, and put the console back the way it was. Called when the
        /// player turns the switch off.
        /// </summary>
        public static void Detach()
        {
            lock (_lock)
            {
                if (_consoleWas != null)
                {
                    Console.SetOut(_consoleWas);
                    _consoleWas = null;
                }
                _writer?.Flush();
                DetachNativeStderr();
                _writer?.Dispose();
                _writer = null;
                _hooked = false;
            }
        }

        private static void Prune(string directory)
        {
            try
            {
                FileInfo[] files = new DirectoryInfo(directory).GetFiles("*.log");
                var sessions = files.GroupBy(file => SessionKey(file.Name), StringComparer.OrdinalIgnoreCase)
                    .Select(group => new
                    {
                        Files = group.ToArray(),
                        Latest = group.Max(file => file.LastWriteTimeUtc)
                    })
                    .OrderByDescending(group => group.Latest).ToList();
                for (int i = KeepSessions - 1; i < sessions.Count; i++)
                {
                    foreach (FileInfo file in sessions[i].Files) file.Delete();
                }
            }
            catch (Exception)
            {
                // A directory that cannot be tidied is still a directory that
                // can be written to.
            }
        }

        private static void Hook()
        {
            if (_hooked)
            {
                return;
            }
            _hooked = true;
            // The half that costs nothing: everything already printed is
            // written to the file as well, in the order it was printed.
            _consoleWas = Console.Out;
            Console.SetOut(new TeeWriter(_consoleWas));
            EnsureProcessEventHooks();
        }

        internal static void EnsureProcessEventHooks()
        {
            lock (_lock)
            {
                if (_processHooksInstalled) return;
                AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
                TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
                AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
                _processHooksInstalled = true;
                _processHookInstallCount++;
            }
        }

        internal static int ProcessEventHookInstallCount => _processHookInstallCount;

        private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Line("crash", "the process is going down with an exception "
                + $"(terminating={e.IsTerminating})");
            Exception("crash", e.ExceptionObject as Exception);
            lock (_lock) _writer?.Flush();
        }

        private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            Exception("task", e.Exception);
            e.SetObserved();
        }

        private static void OnProcessExit(object? sender, EventArgs e)
        {
            Line("exit", "process exiting");
            lock (_lock) _writer?.Flush();
        }

        private static void WriteHeader()
        {
            Line("build", $"{Branding.Name} {BuildVersion.Display}, "
                + $"data format {Branding.EngineVersion}");
            Line("build", $"protocol {Network.NetConfig.ProtocolVersion}, "
                + $"log started {DateTime.Now:yyyy-MM-dd HH:mm:ss zzz}");
            Line("system", $"{Environment.OSVersion} {RuntimeArchitecture()}, "
                + $".NET {Environment.Version}, {Environment.ProcessorCount} cpu(s)");
            Line("system", $"64-bit process={Environment.Is64BitProcess}, "
                + $"culture={CultureInfo.CurrentCulture.Name}");
            Line("paths", $"base={AppContext.BaseDirectory}");
            Line("paths", $"prefs={LauncherPrefs.Directory}");
            Line("paths", $"log={Path}");
            Line("paths", $"native log={NativePath}");
            try
            {
                Line("paths", $"game files ready={GameFiles.Ready}");
            }
            catch (Exception ex)
            {
                Line("paths", $"game files could not be checked: {ex.Message}");
            }
            if (OperatingSystem.IsLinux())
            {
                Line("linux", $"XDG_SESSION_TYPE={Environment.GetEnvironmentVariable("XDG_SESSION_TYPE") ?? "<unset>"}");
                Line("linux", $"XDG_CURRENT_DESKTOP={Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP") ?? "<unset>"}");
                Line("linux", $"DISPLAY={Environment.GetEnvironmentVariable("DISPLAY") ?? "<unset>"}");
                Line("linux", $"WAYLAND_DISPLAY={Environment.GetEnvironmentVariable("WAYLAND_DISPLAY") ?? "<unset>"}");
            }
            Line("args", SanitizeArguments(Environment.GetCommandLineArgs()));
            Line("render", $"cel={RenderOptions.OnOff(RenderOptions.CelShading)} "
                + $"fog={RenderOptions.OnOff(RenderOptions.Fog)} "
                + $"window={LauncherPrefs.WindowMode}");
            if (Network.NetLag.Active)
            {
                Line("net", $"simulated line: {Network.NetLag.Describe()}");
            }
        }

        private static string RuntimeArchitecture()
        {
            return System.Runtime.InteropServices.RuntimeInformation.OSArchitecture
                + "/" + System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture;
        }

        internal static (string Managed, string Native) CreateSessionPaths(string directory,
            string prefix, DateTime timestamp, Func<string, bool>? exists = null)
        {
            exists ??= File.Exists;
            string stem = $"{prefix}-{timestamp:yyyyMMdd-HHmmss}";
            for (int suffix = 0; ; suffix++)
            {
                string candidate = suffix == 0 ? stem : $"{stem}-{suffix + 1}";
                string managed = System.IO.Path.Combine(directory, candidate + ".log");
                string native = System.IO.Path.Combine(directory, candidate + "-native.log");
                if (!exists(managed) && !exists(native)) return (managed, native);
            }
        }

        internal static string SessionKey(string fileName)
        {
            const string native = "-native.log";
            if (fileName.EndsWith(native, StringComparison.OrdinalIgnoreCase))
                return fileName[..^native.Length];
            return fileName.EndsWith(".log", StringComparison.OrdinalIgnoreCase)
                ? fileName[..^4] : fileName;
        }

        private static readonly Regex SensitiveAssignment = new(
            @"(?i)(token|password|passwd|pwd|secret|signature|authorization|bearer|connectionstring|db_url)=([^;\s&]+)",
            RegexOptions.CultureInvariant);
        private static readonly Regex UrlUserInfo = new(@"(?<=://)[^/@\s]+@",
            RegexOptions.CultureInvariant);

        internal static string SanitizeArguments(IEnumerable<string> arguments)
        {
            string[] values = arguments.ToArray();
            bool redactNext = false;
            for (int i = 0; i < values.Length; i++)
            {
                string value = values[i] ?? "";
                if (redactNext)
                {
                    values[i] = "<redacted>";
                    redactNext = false;
                    continue;
                }
                value = UrlUserInfo.Replace(value, "<redacted>@");
                int equals = value.IndexOf('=');
                string option = equals >= 0 ? value[..equals] : value;
                if (IsSensitiveOption(option))
                {
                    if (equals >= 0) values[i] = value[..(equals + 1)] + "<redacted>";
                    else redactNext = true;
                    continue;
                }
                values[i] = SensitiveAssignment.Replace(value, match => match.Groups[1].Value + "=<redacted>");
            }
            return String.Join(' ', values);
        }

        private static bool IsSensitiveOption(string option)
        {
            string name = option.TrimStart('-').Replace("_", "", StringComparison.Ordinal)
                .Replace("-", "", StringComparison.Ordinal);
            return name.Contains("token", StringComparison.OrdinalIgnoreCase)
                || name.Contains("password", StringComparison.OrdinalIgnoreCase)
                || name.Equals("pwd", StringComparison.OrdinalIgnoreCase)
                || name.Contains("secret", StringComparison.OrdinalIgnoreCase)
                || name.Contains("signature", StringComparison.OrdinalIgnoreCase)
                || name.Contains("authorization", StringComparison.OrdinalIgnoreCase)
                || name.Contains("connectionstring", StringComparison.OrdinalIgnoreCase)
                || name.Equals("dburl", StringComparison.OrdinalIgnoreCase);
        }

        private static void AttachNativeStderr(string nativePath)
        {
            try
            {
                _nativeStream = new FileStream(nativePath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
                if (OperatingSystem.IsAndroid()) return;
                Console.Error.Flush();
                if (OperatingSystem.IsWindows()) AttachWindowsStderr();
                else if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()) AttachUnixStderr();
            }
            catch (Exception ex)
            {
                DetachNativeStderr();
                Line("native", $"native stderr capture unavailable: {ex.Message}");
            }
        }

        private static void AttachUnixStderr()
        {
            int target = checked((int)_nativeStream!.SafeFileHandle.DangerousGetHandle());
            int saved = dup(2);
            if (saved < 0) throw new IOException($"dup(stderr) failed ({Marshal.GetLastPInvokeError()})");
            if (dup2(target, 2) < 0)
            {
                close(saved);
                throw new IOException($"dup2(stderr) failed ({Marshal.GetLastPInvokeError()})");
            }
            _savedStderrFd = saved;
        }

        private static void AttachWindowsStderr()
        {
            IntPtr process = GetCurrentProcess();
            IntPtr current = GetStdHandle(StandardErrorHandle);
            bool currentIsValid = current != IntPtr.Zero && current != new IntPtr(-1);
            int savedFd = _dup(2);
            if (!DuplicateHandle(process, _nativeStream!.SafeFileHandle.DangerousGetHandle(), process,
                out IntPtr nativeCrtHandle, 0, false, DuplicateSameAccess))
            {
                if (savedFd >= 0) _close(savedFd);
                throw new IOException($"could not duplicate native log handle ({Marshal.GetLastPInvokeError()})");
            }
            int nativeFd = _open_osfhandle(nativeCrtHandle, 0x0001 | 0x0008);
            if (nativeFd < 0)
            {
                CloseHandle(nativeCrtHandle);
                if (savedFd >= 0) _close(savedFd);
                throw new IOException("could not open native log CRT handle");
            }
            if (!SetStdHandle(StandardErrorHandle, _nativeStream.SafeFileHandle.DangerousGetHandle())
                || _dup2(nativeFd, 2) != 0)
            {
                _close(nativeFd);
                if (savedFd >= 0)
                {
                    _dup2(savedFd, 2);
                    _close(savedFd);
                }
                SetStdHandle(StandardErrorHandle,
                    currentIsValid ? current : IntPtr.Zero);
                throw new IOException($"could not redirect Windows stderr ({Marshal.GetLastPInvokeError()})");
            }
            _close(nativeFd);
            _savedWindowsStderr = current;
            _windowsStderrWasValid = currentIsValid;
            _windowsStderrAttached = true;
            _savedStderrFd = savedFd;
        }

        private static void DetachNativeStderr()
        {
            try
            {
                Console.Error.Flush();
                if (_savedStderrFd >= 0)
                {
                    if (OperatingSystem.IsWindows())
                    {
                        _dup2(_savedStderrFd, 2);
                        _close(_savedStderrFd);
                    }
                    else
                    {
                        dup2(_savedStderrFd, 2);
                        close(_savedStderrFd);
                    }
                    _savedStderrFd = -1;
                }
                else if (OperatingSystem.IsWindows() && _windowsStderrAttached)
                {
                    _close(2);
                }
                if (_windowsStderrAttached)
                {
                    SetStdHandle(StandardErrorHandle, _windowsStderrWasValid
                        ? _savedWindowsStderr : IntPtr.Zero);
                    _savedWindowsStderr = IntPtr.Zero;
                    _windowsStderrWasValid = false;
                    _windowsStderrAttached = false;
                }
            }
            catch
            {
                // Diagnostics must remain fail-open even while restoring stderr.
            }
            try
            {
                _nativeStream?.Flush();
                _nativeStream?.Dispose();
            }
            catch
            {
                // The diagnostic stream is never allowed to block shutdown.
            }
            finally
            {
                _nativeStream = null;
            }
        }

        private const int StandardErrorHandle = -12;
        private const uint DuplicateSameAccess = 0x2;
        [DllImport("libc", SetLastError = true)] private static extern int dup(int oldfd);
        [DllImport("libc", SetLastError = true)] private static extern int dup2(int oldfd, int newfd);
        [DllImport("libc", SetLastError = true)] private static extern int close(int fd);
        [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
        [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr GetStdHandle(int nStdHandle);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetStdHandle(int nStdHandle, IntPtr handle);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool DuplicateHandle(IntPtr sourceProcess,
            IntPtr sourceHandle, IntPtr targetProcess, out IntPtr targetHandle, uint desiredAccess,
            [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint options);
        [DllImport("msvcrt.dll")] private static extern int _dup(int fd);
        [DllImport("msvcrt.dll")] private static extern int _dup2(int source, int target);
        [DllImport("msvcrt.dll")] private static extern int _close(int fd);
        [DllImport("msvcrt.dll")] private static extern int _open_osfhandle(IntPtr osfhandle, int flags);

        /// <summary>One line, with a category in front of it. Cheap when off.</summary>
        public static void Line(string category, string message)
        {
            if (_writer == null)
            {
                return;
            }
            lock (_lock)
            {
                _writer?.WriteLine(
                    $"[{DateTime.Now:HH:mm:ss.fff}] [{category}] {message}");
            }
        }

        /// <summary>An exception and everything under it, indented.</summary>
        public static void Exception(string category, Exception? ex)
        {
            if (_writer == null || ex == null)
            {
                return;
            }
            Line(category, $"{ex.GetType().FullName}: {ex.Message}");
            lock (_lock)
            {
                _writer?.WriteLine(ex.StackTrace);
            }
            if (ex.InnerException != null)
            {
                Line(category, "caused by:");
                Exception(category, ex.InnerException);
            }
        }

        /// <summary>
        /// A step of something that can fail half way, with how long it took.
        /// Used around room loading, which is where the report that this was
        /// written for says the crash happens.
        /// </summary>
        public static IDisposable? Step(string category, string what)
        {
            return _writer == null ? null : new Timed(category, what);
        }

        private sealed class Timed : IDisposable
        {
            private readonly string _category;
            private readonly string _what;
            private readonly System.Diagnostics.Stopwatch _clock
                = System.Diagnostics.Stopwatch.StartNew();

            public Timed(string category, string what)
            {
                _category = category;
                _what = what;
                Line(category, $"{what}: started");
            }

            public void Dispose()
            {
                Line(_category, $"{_what}: done in {_clock.ElapsedMilliseconds} ms");
            }
        }

        /// <summary>
        /// The console, and the file, in that order.
        ///
        /// Writing to the console first means a line reaches the terminal
        /// whether or not the file is still there to take it, and the lock is
        /// the same one every other writer takes -- the log is written from the
        /// game thread, the net thread and the launcher's dispatcher, and
        /// interleaved half-lines would be worse than no log at all.
        /// </summary>
        private sealed class TeeWriter : TextWriter
        {
            private readonly TextWriter _console;

            public TeeWriter(TextWriter console)
            {
                _console = console;
            }

            public override Encoding Encoding => _console.Encoding;

            public override void Write(char value)
            {
                _console.Write(value);
                lock (_lock)
                {
                    _writer?.Write(value);
                }
            }

            public override void Write(string? value)
            {
                _console.Write(value);
                lock (_lock)
                {
                    _writer?.Write(value);
                }
            }

            public override void WriteLine(string? value)
            {
                _console.WriteLine(value);
                lock (_lock)
                {
                    _writer?.WriteLine(value);
                }
            }

            public override void Flush()
            {
                _console.Flush();
                lock (_lock)
                {
                    _writer?.Flush();
                }
            }
        }
    }
}
