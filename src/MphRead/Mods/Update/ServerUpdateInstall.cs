using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace MphRead.Mods.Update;

/// <summary>Exact process identity; the owner releases listeners before requesting a standalone relaunch.</summary>
public sealed record ServerRestart(string Executable, string[] Arguments, string WorkingDirectory, bool Supervised)
{
    public ProcessStartInfo? CreateStartInfo()
    {
        if (Supervised) { return null; }
        var info = new ProcessStartInfo(Executable) { WorkingDirectory = WorkingDirectory, UseShellExecute = false };
        foreach (string argument in Arguments) { info.ArgumentList.Add(argument); }
        return info;
    }
}

public static class ServerUpdateInstall
{
    private sealed record Plan(ServerUpdateStage Stage, string Installation, ServerRestart Restart, int ParentId);
    internal static string WritePlan(ServerUpdateStage stage, string installation, ServerRestart restart, int parent)
    {
        string path = Path.Combine(stage.Directory, ".handoff.json");
        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        JsonSerializer.Serialize(file, new Plan(stage, installation, restart, parent));
        file.Flush(true);
        return path;
    }

    internal static ProcessStartInfo HelperStart(ServerUpdateStage stage, string planPath)
    {
        var info = new ProcessStartInfo(Path.Combine(stage.Directory, stage.Manifest.Executable))
        { WorkingDirectory = stage.Directory, UseShellExecute = false, CreateNoWindow = true };
        info.ArgumentList.Add("-server-apply-update");
        info.ArgumentList.Add(planPath);
        return info;
    }

    /// <summary>Windows-only helper entry. Timeout refuses installation rather than touching a running process.</summary>
    public static int ApplyHandoff(string planPath)
    {
        try
        {
            if (!OperatingSystem.IsWindows()) { throw new InvalidOperationException("Unix updates install before owner exit."); }
            ServerUpdate.RejectLinks(planPath);
            using var file = File.OpenRead(planPath);
            if (file.Length > ServerUpdate.MetadataLimit) { throw new IOException("Oversized update handoff."); }
            Plan plan = JsonSerializer.Deserialize<Plan>(file) ?? throw new IOException("Invalid update handoff.");
            if (plan.ParentId <= 0 || plan.ParentId == Environment.ProcessId) { throw new IOException("Invalid update parent."); }
            if (!Path.GetFullPath(plan.Stage.Directory).Equals(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory), StringComparison.OrdinalIgnoreCase))
            { throw new IOException("The updater must run from its own staged build."); }
            try
            {
                using Process parent = Process.GetProcessById(plan.ParentId);
                if (!parent.WaitForExit(30_000)) { throw new IOException("Server did not exit; update was not applied."); }
            }
            catch (ArgumentException) { }
            using (var held = new FileStream(Path.Combine(ServerUpdate.StageRoot(plan.Installation), "installation.lock"),
                FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                ServerUpdate.VerifyStage(plan.Stage);
                Apply(plan.Stage, plan.Installation);
            }
            // The replacement server acquires this same installation lock at startup.
            // Release the helper's ownership before launching it.
            ProcessStartInfo? next = plan.Restart.CreateStartInfo();
            if (next != null && Process.Start(next) == null) { throw new IOException("Updated server did not restart."); }
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine("[update] " + error.Message); return 1; }
    }

    /// <summary>
    /// Owner has stopped admission. Replaces individual paths by same-filesystem rename;
    /// a failed multi-file installation restores every prior file before returning failure.
    /// This is not a whole-directory atomic switch or a power-failure recovery journal.
    /// </summary>
    internal static void Apply(ServerUpdateStage stage, string installation, Action<int>? beforeReplace = null,
        Action<string, Exception>? cleanupFailure = null)
    {
        ServerUpdate.VerifyStage(stage);
        ServerUpdate.RejectLinks(installation);
        string transaction = Path.Combine(stage.Directory, "rollback-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(transaction);
        var replaced = new List<(string Target, string Backup, bool Existed)>(stage.Manifest.Files.Length);
        var temporaries = new List<string>(stage.Manifest.Files.Length);
        try
        {
            for (int index = 0; index < stage.Manifest.Files.Length; index++)
            {
                ServerUpdateFile entry = stage.Manifest.Files[index];
                ServerUpdate.SafeRelative(entry.Path);
                string target = Path.Combine(installation, entry.Path);
                ServerUpdate.RejectLinks(target);
                string directory = Path.GetDirectoryName(target)!;
                Directory.CreateDirectory(directory);
                string temporary = Path.Combine(directory, ".server-update-" + Guid.NewGuid().ToString("N"));
                temporaries.Add(temporary);
                File.Copy(Path.Combine(stage.Directory, entry.Path), temporary, overwrite: false);
                using (var flushed = new FileStream(temporary, FileMode.Open, FileAccess.Write, FileShare.None)) { flushed.Flush(true); }
                string backup = Path.Combine(transaction, index.ToString());
                bool existed = File.Exists(target);
                if (existed) { File.Copy(target, backup, overwrite: false); }
                beforeReplace?.Invoke(index);
                File.Move(temporary, target, overwrite: true);
                replaced.Add((target, backup, existed));
            }
        }
        catch (Exception failure)
        {
            List<Exception>? rollbackFailures = null;
            for (int index = replaced.Count - 1; index >= 0; index--)
            {
                var previous = replaced[index];
                try
                {
                    if (previous.Existed) { File.Move(previous.Backup, previous.Target, overwrite: true); }
                    else { File.Delete(previous.Target); }
                }
                catch (Exception rollback)
                {
                    rollbackFailures ??= new List<Exception>(replaced.Count + 1) { failure };
                    rollbackFailures.Add(rollback);
                }
            }
            if (rollbackFailures != null)
            {
                throw new AggregateException("Update rollback failed; preserve " + transaction + " for recovery.", rollbackFailures);
            }
            throw;
        }
        finally
        {
            foreach (string temporary in temporaries)
            {
                try { File.Delete(temporary); }
                catch (Exception error) { ReportCleanupFailure(temporary, error, cleanupFailure); }
            }
        }
        // Installation is committed. Cleanup must neither trigger a rollback
        // nor prevent the owner from requesting its restart.
        try { Directory.Delete(transaction, true); }
        catch (Exception error) { ReportCleanupFailure(transaction, error, cleanupFailure); }
    }

    private static void ReportCleanupFailure(string path, Exception error, Action<string, Exception>? report)
    {
        try
        {
            if (report != null) { report(path, error); }
            else { Console.Error.WriteLine($"[update] Cleanup failed for {path}: {error.Message}"); }
        }
        catch (Exception)
        {
            // A closed diagnostic stream or failing observer must not mask the
            // transaction's original exception or change a committed outcome.
        }
    }
}
