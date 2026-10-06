using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.MapEditor
{
    public sealed record MapAuditResult(bool Passed, int ExitCode, IReadOnlyList<string> Lines);
    public sealed record MapAuditLaunchOptions(string GameExecutable, string PrivateDirectory);

    public static class MapAuditRunner
    {
        public static Task<MapAuditResult> Run(MapProject project,CancellationToken cancellation)
            => Run(project, cancellation, null);

        public static async Task<MapAuditResult> Run(MapProject project,CancellationToken cancellation, MapAuditLaunchOptions? options)
        {
            var snapshot = MapBuildSnapshot.Capture(project);
            var configuredPaths = Paths.AllPaths.ToArray();
            string directory=Path.Combine(options?.PrivateDirectory ?? Path.GetTempPath(),"project-prime-map-audit-"+Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                var definition=snapshot.CreateDefinition();definition.Name="AUDIT "+Guid.NewGuid().ToString("N");
                await Task.Run(() =>
                {
                    cancellation.ThrowIfCancellationRequested();
                    MapProjectExport.Save(definition,Path.Combine(directory,"audit.json"));
                    cancellation.ThrowIfCancellationRequested();
                }, cancellation).ConfigureAwait(false);
                string executable = options?.GameExecutable ?? Environment.ProcessPath ?? throw new IOException("Executable path is unavailable.");
                var start=new ProcessStartInfo(executable)
                {UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,WorkingDirectory=options == null ? AppContext.BaseDirectory : Path.GetDirectoryName(Path.GetFullPath(executable))!};
                if (Path.GetExtension(executable).Equals(".dll",StringComparison.OrdinalIgnoreCase))
                {
                    string? runtime = Environment.GetEnvironmentVariable("DOTNET_ROOT");
                    start.FileName = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")
                        ?? (runtime == null ? "dotnet" : Path.Combine(runtime, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet"));
                    start.ArgumentList.Add(executable);
                }
                else if(Path.GetFileNameWithoutExtension(start.FileName).Equals("dotnet",StringComparison.OrdinalIgnoreCase))start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "ProjectPrime.dll"));
                if (options != null)
                {
                    string state = Path.Combine(directory, "worker-state");
                    Directory.CreateDirectory(state);
                    string export = Path.Combine(directory, "exports");
                    Directory.CreateDirectory(export);
                    // Give this process its own writable state/cache while retaining read-only stock asset roots.
                    var configuration = new List<string> { "0.19.0.0" };
                    configuration.AddRange(configuredPaths.Select(pair => pair.Key + "=" + (pair.Key == "Export" ? export : pair.Value)));
                    await File.WriteAllLinesAsync(Path.Combine(state, "paths.txt"), configuration, cancellation).ConfigureAwait(false);
                    start.Environment["PROJECT_PRIME_USER_DATA"] = state;
                    foreach(string arg in new[] { "-customruntimeroot", Path.Combine(directory, "runtime"),
                        "-customruntimenamespace", Guid.NewGuid().ToString("N"), "-usermapdirectory", Path.Combine(directory, "packages") })
                        start.ArgumentList.Add(arg);
                }
                string mode=definition.Capabilities?.SupportedModes.FirstOrDefault()??"Battle";
                int players=definition.Capabilities?.MaxPlayers??8;
                foreach(string arg in new[]{"-mapdir",directory,"-maptest",definition.Name,"-players",players.ToString(),"-mode",mode,"-seconds","22","-bots","-noupdate"})start.ArgumentList.Add(arg);
                using var process=Process.Start(start)??throw new IOException("Could not start the map audit.");
                using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellation);timeout.CancelAfter(TimeSpan.FromMinutes(4));
                var lines=new List<string>();
                async Task Read(StreamReader reader)
                {
                    while(await reader.ReadLineAsync(timeout.Token) is {} line)
                    {lock(lines){if(lines.Count<2000)lines.Add(line.Length>4096?line[..4096]:line);}}
                }
                try
                {
                    await Task.WhenAll(Read(process.StandardOutput),Read(process.StandardError),process.WaitForExitAsync(timeout.Token));
                    return new(process.ExitCode==0,process.ExitCode,lines);
                }
                finally{if(!process.HasExited){process.Kill(entireProcessTree:true);await process.WaitForExitAsync().ConfigureAwait(false);}}
            }
            finally{Directory.Delete(directory,true);}
        }
    }
}
