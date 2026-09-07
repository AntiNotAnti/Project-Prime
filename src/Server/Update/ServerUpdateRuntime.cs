using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.MapGen;
using MphRead.Mods.Network;

namespace MphRead.Mods.Update;

public sealed record ServerValidationReport(string Kind, int Format, NetWireFamily Family, byte Protocol,
    bool Success, int Scenarios, string Error);

/// <summary>CLI and owner-loop adapter. Network/simulation state remains on its existing owner thread.</summary>
public sealed class ServerUpdateRuntime : IDisposable
{
    private readonly ServerUpdate _service;
    private readonly ServerRestart _restart;
    private string? _reportedError;
    private bool _restartRequested;
    private readonly bool _helperRestarts;

    private ServerUpdateRuntime(ServerUpdate service, ServerRestart restart)
    { _service = service; _restart = restart; _helperRestarts = OperatingSystem.IsWindows(); }

    public static ServerUpdateRuntime? Create(string[] arguments)
    {
        if (!ServerUpdate.Enabled(arguments)) { return null; }
        try
        {
            Version version = BuildVersion.Current ?? throw new ProgramException("Local unstamped builds cannot auto-update.");
            string repository = Value(arguments, "update-repository")
                ?? throw new ProgramException("-autoupdate requires an explicit -update-repository OWNER/REPO authoritative fork.");
            string installation = Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);
            string launchDirectory = ConsoleSetup.LaunchDirectory;
            string executable = Environment.ProcessPath ?? throw new ProgramException("Cannot locate the current executable.");
            string[] original = Environment.GetCommandLineArgs().Skip(1).ToArray();
            // dotnet needs the managed entry point in addition to the user's original argv.
            if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            { original = new[] { Environment.GetCommandLineArgs()[0] }.Concat(original).ToArray(); }
            bool supervised = OperatingSystem.IsLinux() && !String.IsNullOrEmpty(Environment.GetEnvironmentVariable("INVOCATION_ID"));
            var restart = new ServerRestart(executable, original, launchDirectory, supervised);
            string rid = RuntimePlatform.Rid();
            var options = new ServerUpdateOptions(repository, $"authoritative-update-{rid}-server.json", rid,
                version, installation, restart);
            string[] validation = ValidationArguments(arguments, installation, launchDirectory);
            var service = new ServerUpdate(options, (stage, cancel) => ValidateStagedAsync(stage, validation, launchDirectory, cancel));
            var runtime = new ServerUpdateRuntime(service, restart);
            service.CheckWhenDue();
            Console.WriteLine("[update] automatic authoritative updates enabled from " + repository);
            return runtime;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("[update] disabled: " + error.Message);
            return null;
        }
    }

    /// <summary>Returns true only after install/handoff; caller must leave its loop and dispose listeners.</summary>
    public bool PollIdle(Func<bool> idle, Action stopAdmission, Action resumeAdmission)
    {
        _service.CheckWhenDue();
        bool ready = _service.PollIdle(idle, stopAdmission, resumeAdmission, _ => _restartRequested = true);
        if (_service.LastError != null && _service.LastError != _reportedError)
        {
            _reportedError = _service.LastError;
            Console.Error.WriteLine("[update] " + _reportedError);
        }
        return ready;
    }

    /// <summary>Call only after the server/master Run returned and all listeners/owned resources are disposed.</summary>
    public void RestartAfterShutdown()
    {
        if (!_restartRequested) { return; }
        // Release the install lock before a new owner starts its own updater.
        _service.Dispose();
        _restartRequested = false;
        if (_helperRestarts) { return; }
        ProcessStartInfo? restart = _restart.CreateStartInfo();
        if (restart != null && Process.Start(restart) == null) { throw new IOException("Updated server failed to restart."); }
        // Supervised Unix returns without spawning: the old process now exits.
    }

    public void Dispose() => _service.Dispose();

    internal static bool Has(string[] arguments, string flag)
        => arguments.Any(argument => argument.TrimStart('-').Equals(flag, StringComparison.OrdinalIgnoreCase));
    internal static string? Value(string[] arguments, string flag)
    {
        for (int i = 0; i < arguments.Length - 1; i++)
        { if (arguments[i].TrimStart('-').Equals(flag, StringComparison.OrdinalIgnoreCase)) { return arguments[i + 1]; } }
        return null;
    }

    public static string[] ValidationArguments(string[] original, string installation, string launchDirectory)
    {
        var result = new List<string> { "-authoritative-server-validate", "-noupdate" };
        void Add(string flag, string? value = null) { result.Add("-" + flag); if (value != null) { result.Add(value); } }
        if (Has(original, "masterserver")) { Add("masterserver"); }
        string? data = Value(original, "data");
        if (data != null) { Add("data", Path.GetFullPath(data, installation)); }
        Add("dataversion", Value(original, "dataversion") ?? "AMHE1");
        string? rotation = Value(original, "rotation");
        if (rotation != null) { Add("rotation", Path.GetFullPath(rotation, installation)); }
        string? mapDirectory = Value(original, "mapdir");
        Add("mapdir", mapDirectory == null ? Path.Combine(installation, "maps") : Path.GetFullPath(mapDirectory, launchDirectory));
        string? room = Value(original, "server") ?? Value(original, "authoritative-server");
        Add("server", room != null && !room.StartsWith('-') ? room : "MP1 SANCTORUS");
        Add("mode", Value(original, "mode") ?? nameof(GameMode.Battle));
        return result.ToArray();
    }

    /// <summary>Early CLI, before settings, updater cleanup, content generation or any socket.</summary>
    public static int RunValidation(string[] arguments)
    {
        TextWriter output = Console.Out, errors = Console.Error;
        ServerValidationReport report;
        try
        {
            Console.SetOut(TextWriter.Null); Console.SetError(TextWriter.Null);
            string[] normalized = ValidationArguments(arguments, AppContext.BaseDirectory, ConsoleSetup.LaunchDirectory);
            CustomRooms.MapDirectory = Value(normalized, "mapdir")!;
            string? directory = Value(normalized, "data");
            bool master = Has(normalized, "masterserver");
            int scenarios = 0;
            if (directory != null)
            {
                string? cycle = Value(normalized, "rotation");
                IReadOnlyList<RotationEntry> rotation = cycle == null && master ? Array.Empty<RotationEntry>() : cycle == null
                    ? new[] { new RotationEntry { RoomKey = Value(normalized, "server")!,
                        Mode = Enum.TryParse(Value(normalized, "mode"), true, out GameMode mode) ? mode : GameMode.Battle } }
                    : MapRotation.Load(cycle).Entries;
                scenarios = ServerContentValidation.Validate(directory, Value(normalized, "dataversion")!, rotation, hosting: master);
            }
            else if (!master) { throw new ProgramException("Validation requires -data DIRECTORY for an authoritative match."); }
            report = new("authoritative-server-validation", 1, NetWireIdentity.Family, NetHeader.Version, true, scenarios, "");
        }
        catch (Exception error)
        {
            string message = error.Message.Length <= 512 ? error.Message : error.Message[..512];
            report = new("authoritative-server-validation", 1, NetWireIdentity.Family, NetHeader.Version, false, 0, message);
        }
        finally { Console.SetOut(output); Console.SetError(errors); }
        output.WriteLine(JsonSerializer.Serialize(report));
        return report.Success ? 0 : 1;
    }

    public static async Task<string?> ValidateStagedAsync(ServerUpdateStage stage, string[] validationArguments,
        string launchDirectory, CancellationToken cancel)
    {
        // Even direct callers cannot execute a stage whose bytes/identity were not checked.
        ServerUpdate.VerifyStage(stage);
        string executable = Path.Combine(stage.Directory, stage.Manifest.Executable);
        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = launchDirectory, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        if (Path.GetExtension(executable).Equals(".dll", StringComparison.OrdinalIgnoreCase))
        {
            start.FileName = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")
                ?? (Path.GetFileNameWithoutExtension(Environment.ProcessPath) == "dotnet" ? Environment.ProcessPath! : "dotnet");
            start.ArgumentList.Add(executable);
        }
        foreach (string argument in validationArguments) { start.ArgumentList.Add(argument); }
        using var process = new Process { StartInfo = start };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        bool started = false;
        try
        {
            timeout.Token.ThrowIfCancellationRequested();
            if (!(started = process.Start())) { return "Staged validator did not start."; }
            Task<string> output = ReadBounded(process.StandardOutput, timeout);
            Task<string> errors = ReadBounded(process.StandardError, timeout);
            await Task.WhenAll(output, errors, process.WaitForExitAsync(timeout.Token)).WaitAsync(timeout.Token);
            ServerValidationReport? report = JsonSerializer.Deserialize<ServerValidationReport>(await output);
            if (report?.Kind != "authoritative-server-validation" || report.Format != 1
                || report.Family != stage.Manifest.Family || report.Protocol != stage.Manifest.Protocol
                || report.Scenarios is < 0 or > 768 || report.Error == null || report.Error.Length > 512)
            { return "Staged validator did not return a valid success report."; }
            if (process.ExitCode != 0 || !report.Success) { return "Staged validation failed: " + report.Error; }
            return null;
        }
        catch (Exception error)
        {
            if (error is OperationCanceledException) { return "Staged validation cancelled or timed out."; }
            return "Staged validation failed: " + error.Message;
        }
        finally
        {
            try
            {
                if (started && !process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
                }
            }
            catch (Exception error)
            {
                // A failed kill or reap must not let CheckAsync delete a stage whose
                // validator may still be executing. An exit race needs no recovery.
                bool exited = false;
                try { exited = process.HasExited; }
                catch (Exception) { } // An unavailable exit status is not proof of termination.
                if (!exited)
                { throw new ServerUpdateValidatorCleanupException("Could not stop the staged validator.", error); }
            }
        }
    }

    private static async Task<string> ReadBounded(StreamReader reader, CancellationTokenSource cancel)
    {
        char[] buffer = new char[1024];
        var text = new StringBuilder();
        int length;
        while ((length = await reader.ReadAsync(buffer, cancel.Token)) != 0)
        {
            if (text.Length + length > 4096) { cancel.Cancel(); throw new IOException("Staged validator output exceeded its bound."); }
            text.Append(buffer, 0, length);
        }
        return text.ToString();
    }
}
