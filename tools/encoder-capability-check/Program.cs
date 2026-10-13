using System.Diagnostics;
using System.Reflection;
using System.Text;
using MphRead.Mods.Replay;

if (args.FirstOrDefault() == "--hold-pipes") { await Task.Delay(TimeSpan.FromSeconds(2)); Console.WriteLine(" V..... hevc_amf late descendant capability before deadline"); Console.Out.Flush(); await Task.Delay(TimeSpan.FromSeconds(30)); return 0; }
if (args.FirstOrDefault() == "-hide_banner")
{
    string fixture = Environment.GetEnvironmentVariable("PRIME_CAPABILITY_FIXTURE")!;
    string childDirectory = Environment.GetEnvironmentVariable("PRIME_CAPABILITY_DIRECTORY")!;
    File.WriteAllText(Path.Combine(childDirectory, "parent.pid"), Environment.ProcessId.ToString());
    if (fixture == "large")
    {
        char[] buffer = new string('z', 8192).ToCharArray();
        for (int i = 0; i < 2048; i++) Console.Out.Write(buffer);
        Console.Write("\n V..... h264_nvenc late valid capability\n V..... hevc_amf more\n");
    }
    else if (fixture is "inherited" or "delayed-inherited")
    {
        if(fixture=="delayed-inherited") await Task.Delay(TimeSpan.FromSeconds(2));
        Console.WriteLine(" V..... hevc_qsv available before parent exit"); Console.Out.Flush();
        var info = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
        info.ArgumentList.Add("--hold-pipes");
        using var child = Process.Start(info)!;
        File.WriteAllText(Path.Combine(childDirectory, "child.pid"), child.Id.ToString());
    }
    else if (fixture == "active")
    {
        Console.WriteLine(" V..... hevc_nvenc parsed before active timeout"); Console.Out.Flush();
        await Task.Delay(TimeSpan.FromSeconds(30));
    }
    else if (fixture == "active-unterminated")
    {
        Console.Write(" V..... hevc_nvenc"); Console.Out.Flush(); await Task.Delay(TimeSpan.FromSeconds(30));
    }
    else if (fixture == "failure") { Console.WriteLine(" V..... libx265 retained despite exit7"); return 7; }
    else if (fixture == "ordinary") Console.Write(" V..... hevc_amf third\n V..... h264_qsv description\n V..... h264_nvenc\r\n V..... hevc_qsv tab\tname\n");
    else if (fixture == "unknown") Console.WriteLine(" V..... unsupported encoder");
    else if (fixture == "stderr")
    {
        char[] buffer = new string('z',8192).ToCharArray();
        for(int i=0;i<2048;i++) Console.Error.Write(buffer);
        Console.WriteLine(" V..... h264_amf valid after large stderr");
    }
    return 0;
}
int passed = 0, failed = 0;
void Check(bool ok, string name) { Console.WriteLine($"{(ok ? "PASS" : "FAIL")} {name}"); if (ok) passed++; else failed++; }
Task<IReadOnlyList<string>> Probe() => (Task<IReadOnlyList<string>>)typeof(ReplayEncoderCapabilities).GetMethod("Probe", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, null)!;
bool Alive(int pid) { try { using var p = Process.GetProcessById(pid); return !p.HasExited; } catch (ArgumentException) { return false; } }
string directory = Path.Combine(Path.GetTempPath(), "prime-capability-check-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
string host = Path.Combine(AppContext.BaseDirectory, "encoder-capability-check" + (OperatingSystem.IsWindows() ? ".exe" : ""));
string fake = Path.Combine(AppContext.BaseDirectory, "ffmpeg" + (OperatingSystem.IsWindows() ? ".exe" : ""));
File.Copy(host, fake, true); if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(fake, File.GetUnixFileMode(host));
string? priorPath=Environment.GetEnvironmentVariable("PATH"); Environment.SetEnvironmentVariable("PATH", AppContext.BaseDirectory + Path.PathSeparator + priorPath);
Environment.SetEnvironmentVariable("PRIME_CAPABILITY_DIRECTORY", directory);
int childPid=0;
async Task CleanupChild()
{
    if(childPid!=0 && Alive(childPid))
    {
        using var p=Process.GetProcessById(childPid);
        if(!string.Equals(Path.GetFullPath(p.MainModule!.FileName),Path.GetFullPath(fake),OperatingSystem.IsWindows()?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal))
            throw new InvalidOperationException("Fixture refuses to kill a PID with another executable identity.");
        p.Kill(true); await p.WaitForExitAsync();
    }
    childPid=0;
}
try
{
    Environment.SetEnvironmentVariable("PRIME_CAPABILITY_FIXTURE", "ordinary");
    Check((await Probe().WaitAsync(TimeSpan.FromSeconds(8))).SequenceEqual(new[]{"libx264","h264_qsv","hevc_qsv","hevc_amf"}), "ordinary probe preserves literal-space grammar, CR and Allowed order");
    Environment.SetEnvironmentVariable("PRIME_CAPABILITY_FIXTURE", "unknown");
    Check((await Probe().WaitAsync(TimeSpan.FromSeconds(8))).SequenceEqual(new[]{"libx264"}), "unknown-only output preserves libx264 fallback");
    Environment.SetEnvironmentVariable("PRIME_CAPABILITY_FIXTURE", "large");
    long before=GC.GetTotalAllocatedBytes(true); var names=await Probe().WaitAsync(TimeSpan.FromSeconds(10)); long allocated=GC.GetTotalAllocatedBytes(true)-before;
    Console.WriteLine($"allocation actual-process 16MiB unknown/no-newline managed={allocated}");
    Check(names.SequenceEqual(new[]{"libx264","h264_nvenc","hevc_amf"}), "late valid capabilities retained in Allowed order with libx264 fallback");
    Check(allocated < 8*1024*1024, "actual long stdout working allocation stays below fixture 8MiB threshold");
    File.Delete(Path.Combine(directory,"parent.pid"));
    Environment.SetEnvironmentVariable("PRIME_CAPABILITY_FIXTURE", "inherited");
    var watch=Stopwatch.StartNew(); var task=Probe();
    for(int i=0;i<100 && !File.Exists(Path.Combine(directory,"child.pid"));i++) await Task.Delay(20);
    childPid=int.Parse(File.ReadAllText(Path.Combine(directory,"child.pid"))); int parentPid=int.Parse(File.ReadAllText(Path.Combine(directory,"parent.pid")));
    for(int i=0;i<100 && Alive(parentPid);i++) await Task.Delay(20);
    Check(!Alive(parentPid) && Alive(childPid), "parent exited while explicitly owned fixture child holds both pipes");
    var done=await Task.WhenAny(task,Task.Delay(TimeSpan.FromSeconds(5.7)-watch.Elapsed));
    Check(done==task, "existing 5s probe budget also retires inherited readers after parent exit");
    if(done==task)
    {
        Check((await task).SequenceEqual(new[]{"libx264","hevc_qsv","hevc_amf"}), "deadline retains early and late-before-budget capabilities and fallback");
        Check(Alive(childPid), "completed probe does not claim orphan process-tree ownership");
    }
    else { await CleanupChild(); await task.WaitAsync(TimeSpan.FromSeconds(3)); }
    await CleanupChild();
    File.Delete(Path.Combine(directory,"child.pid")); File.Delete(Path.Combine(directory,"parent.pid"));
    Environment.SetEnvironmentVariable("PRIME_CAPABILITY_FIXTURE", "delayed-inherited");
    watch.Restart(); task=Probe();
    for(int i=0;i<200 && !File.Exists(Path.Combine(directory,"child.pid"));i++) await Task.Delay(20);
    childPid=int.Parse(File.ReadAllText(Path.Combine(directory,"child.pid"))); parentPid=int.Parse(File.ReadAllText(Path.Combine(directory,"parent.pid")));
    for(int i=0;i<100 && Alive(parentPid);i++) await Task.Delay(20);
    Check(!Alive(parentPid) && Alive(childPid), "delayed owned parent exited while child retains pipes");
    done=await Task.WhenAny(task,Task.Delay(TimeSpan.FromSeconds(5.7)-watch.Elapsed));
    Check(done==task, "post-exit drain uses remaining original budget rather than a new five seconds");
    if(done==task)
    {
        Check((await task).SequenceEqual(new[]{"libx264","hevc_qsv","hevc_amf"}), "delayed exit retains late-before-budget capability");
        Check(Alive(childPid), "delayed completed probe leaves fixture-owned child alive");
    }
    else { await CleanupChild(); await task.WaitAsync(TimeSpan.FromSeconds(3)); }
    await CleanupChild();
    Environment.SetEnvironmentVariable("PRIME_CAPABILITY_FIXTURE", "failure");
    Check((await Probe().WaitAsync(TimeSpan.FromSeconds(8))).SequenceEqual(new[]{"libx264","libx265"}), "nonzero parent exit preserves original discovered-name behavior");
    Environment.SetEnvironmentVariable("PRIME_CAPABILITY_FIXTURE", "stderr");
    Check((await Probe().WaitAsync(TimeSpan.FromSeconds(8))).SequenceEqual(new[]{"libx264","h264_amf"}), "large discarded stderr drains alongside stdout");
    Environment.SetEnvironmentVariable("PRIME_CAPABILITY_FIXTURE", "active");
    var active=Stopwatch.StartNew();
    Check((await Probe().WaitAsync(TimeSpan.FromSeconds(7))).SequenceEqual(new[]{"libx264","hevc_nvenc"}), "active five-second timeout preserves kill/wait and discovered name");
    Check(active.Elapsed < TimeSpan.FromSeconds(7) && !Alive(int.Parse(File.ReadAllText(Path.Combine(directory,"parent.pid")))), "active timed-out owned parent is gone before probe returns");
    Environment.SetEnvironmentVariable("PRIME_CAPABILITY_FIXTURE", "active-unterminated");
    string[] incompleteExpected=typeof(ReplayEncoderCapabilities).GetMethod("ReadCapabilities",BindingFlags.Static|BindingFlags.NonPublic)==null?new[]{"libx264","hevc_nvenc"}:new[]{"libx264"};
    Check((await Probe().WaitAsync(TimeSpan.FromSeconds(7))).SequenceEqual(incompleteExpected), "deadline-cut unterminated token excludes later EOF; normal EOF grammar checked separately");
}
finally
{
    await CleanupChild();
    Environment.SetEnvironmentVariable("PATH",priorPath); Environment.SetEnvironmentVariable("PRIME_CAPABILITY_FIXTURE",null); Environment.SetEnvironmentVariable("PRIME_CAPABILITY_DIRECTORY",null);
    File.Delete(fake); Directory.Delete(directory,true);
}
if(!args.Contains("--process-only")) await GrammarChecks.Run(Check);
Console.WriteLine($"{passed} PASS / {failed} FAIL (actual source-linked probe/synthetic processes; no GL/native audio/real FFmpeg)"); return failed==0 ? 0 : 1;
