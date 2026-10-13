using System.Diagnostics;
using System.Reflection;
using System.Text;
using MphRead.Mods.Replay;

string executable=Environment.ProcessPath!;
string prefix=Path.GetFileNameWithoutExtension(executable).Equals("dotnet",StringComparison.OrdinalIgnoreCase)?$"\"{Assembly.GetExecutingAssembly().Location}\" ":"";
string Child(string mode,string directory)=>prefix+$"--child {mode} \"{directory}\"";
if(args.Length>0 && args[0]=="--child")
{
 string mode=args[1],directory=args[2];
 if(mode=="hold") {File.WriteAllText(Path.Combine(directory,"child.pid"),Environment.ProcessId.ToString());await Task.Delay(15000);return 0;}
 File.WriteAllText(Path.Combine(directory,"parent.pid"),Environment.ProcessId.ToString());
 if(mode=="inherited")
 {
  using var child=Process.Start(new ProcessStartInfo(executable,Child("hold",directory)){UseShellExecute=false,CreateNoWindow=true})!;
  var deadline=DateTime.UtcNow.AddSeconds(5);while(!File.Exists(Path.Combine(directory,"child.pid"))&&DateTime.UtcNow<deadline)await Task.Delay(5);
  Console.WriteLine("frame=9");Console.Error.WriteLine("parent-tail");return 0;
 }
 if(mode=="success"){Console.WriteLine("frame=12");Console.Error.Write("success-tail");return 0;}
 if(mode=="failure"){Console.Error.Write("failure-tail");return 7;}
 if(mode=="active"){Console.WriteLine("frame=4");Console.Error.Write("active-tail");await Task.Delay(15000);return 0;}
 if(mode=="long")
 {
  using var output=Console.OpenStandardOutput();byte[] buffer=new byte[8192];Array.Fill(buffer,(byte)'x');
  for(int i=0;i<2048;i++)output.Write(buffer);output.Write(Encoding.UTF8.GetBytes("\nframe=13"));return 0;
 }
 throw new ArgumentException(mode);
}
int pass=0,fail=0;
void Check(bool value,string name){Console.WriteLine($"{(value?"PASS":"FAIL")} {name}");if(value)pass++;else fail++;}
bool Alive(int id){try{using var p=Process.GetProcessById(id);return !p.HasExited;}catch(ArgumentException){return false;}}
async Task KillOwned(string pidFile)
{
 if(!File.Exists(pidFile))return;int id=int.Parse(File.ReadAllText(pidFile));
 try{using var p=Process.GetProcessById(id);if(!p.HasExited && string.Equals(p.MainModule?.FileName,executable,OperatingSystem.IsWindows()?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal))p.Kill(entireProcessTree:true);await p.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));}catch(ArgumentException){}
}
string root=Path.Combine(Path.GetTempPath(),"prime-pipe-check-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
try
{
 foreach(string mode in new[]{"success","failure","long"})
 {
  string directory=Path.Combine(root,mode);Directory.CreateDirectory(directory);
  using var job=new ReplayEncoderJob(executable,Child(mode,directory),directory);
  try
  {
   var r=await job.Completion.WaitAsync(TimeSpan.FromSeconds(10));
   Check(!r.Cancelled && r.ExitCode==(mode=="failure"?7:0) && (r.Error!=null)==(mode=="failure"),mode+" exit/result semantics");
   Check(File.Exists(Path.Combine(directory,"encode.txt")) && File.ReadAllText(Path.Combine(directory,"encode.log"))==r.Stderr,mode+" command/log retained");
   Check(mode=="failure"?r.Stderr=="failure-tail":job.Frames==(mode=="long"?13:12),mode+" stderr/progress semantics");
  }
  finally{await KillOwned(Path.Combine(directory,"parent.pid"));}
 }
 string missing=Path.Combine(root,"missing");Directory.CreateDirectory(missing);
 using(var job=new ReplayEncoderJob(Path.Combine(root,"absent-encoder"),"",missing))
 {var r=await job.Completion.WaitAsync(TimeSpan.FromSeconds(5));Check(!r.Cancelled&&r.ExitCode==null&&r.Error!=null,"missing encoder remains explicit failure");}
 string active=Path.Combine(root,"active");Directory.CreateDirectory(active);
 using(var job=new ReplayEncoderJob(executable,Child("active",active),active))
 {
  try
  {
   var deadline=DateTime.UtcNow.AddSeconds(5);while(job.Frames!=4&&!job.Completion.IsCompleted&&DateTime.UtcNow<deadline)await Task.Delay(5);
   Check(job.Frames==4&&!job.Completion.IsCompleted,"active child observed before cancel");job.Cancel();
   var r=await job.Completion.WaitAsync(TimeSpan.FromSeconds(5));int id=int.Parse(File.ReadAllText(Path.Combine(active,"parent.pid")));
   Check(r.Cancelled && r.ExitCode.HasValue && !Alive(id),"active cancel still kills/waits owned process");
   Check(File.ReadAllText(Path.Combine(active,"encode.log"))==r.Stderr && r.Stderr.Contains("active-tail"),"active cancel preserves tail/log");
  }
  finally{await KillOwned(Path.Combine(active,"parent.pid"));}
 }
 string inherited=Path.Combine(root,"inherited");Directory.CreateDirectory(inherited);
 using(var job=new ReplayEncoderJob(executable,Child("inherited",inherited),inherited))
 {
  ReplayEncodingResult? result=null;
  try
  {
   var deadline=DateTime.UtcNow.AddSeconds(5);
   while(DateTime.UtcNow<deadline)
   {
    if(File.Exists(Path.Combine(inherited,"parent.pid"))&&File.Exists(Path.Combine(inherited,"child.pid"))&&!Alive(int.Parse(File.ReadAllText(Path.Combine(inherited,"parent.pid")))))break;
    await Task.Delay(5);
   }
   int parent=int.Parse(File.ReadAllText(Path.Combine(inherited,"parent.pid"))),child=int.Parse(File.ReadAllText(Path.Combine(inherited,"child.pid")));
   Check(!Alive(parent)&&Alive(child)&&!job.Completion.IsCompleted,"exited parent / live inherited-pipe child witnessed");
   job.Cancel();bool completed=await Task.WhenAny(job.Completion,Task.Delay(1000))==job.Completion;
   Check(completed,"parent-exit cancel completes within fixture watchdog");
   Check(Alive(child),"completion does not claim orphan process-tree ownership");
   if(completed)result=await job.Completion;
  }
  finally{await KillOwned(Path.Combine(inherited,"child.pid"));await KillOwned(Path.Combine(inherited,"parent.pid"));result??=await job.Completion.WaitAsync(TimeSpan.FromSeconds(5));}
  Check(result.Cancelled&&result.ExitCode==0&&result.Error==null&&job.Frames==9,"inherited-pipe cancel preserves exit/result/progress");
  Check(result.Stderr.Contains("parent-tail")&&File.ReadAllText(Path.Combine(inherited,"encode.log"))==result.Stderr,"inherited-pipe cancel preserves available stderr/log");
  Check(!Alive(int.Parse(File.ReadAllText(Path.Combine(inherited,"child.pid")))),"fixture explicitly retires its owned orphan child");
 }
}
finally{Directory.Delete(root,true);}
// Real pending reads: one faults on cancellation, the other cancels. Both must
// finish before either owned reader closes; the I/O fault must remain visible.
var retire=typeof(ReplayEncoderJob).GetMethod("RetireReaders",BindingFlags.Static|BindingFlags.NonPublic);
if(retire!=null)
{
 using var cancellation=new CancellationTokenSource();using var output=new FaultRead(true);using var error=new FaultRead(false);
 using var stdout=new StreamReader(output);using var stderr=new StreamReader(error);char[] first=new char[32],second=new char[32];
 Task one=stdout.ReadAsync(first.AsMemory(),cancellation.Token).AsTask(),two=stderr.ReadAsync(second.AsMemory(),cancellation.Token).AsTask();
 await Task.WhenAll(output.Entered.Task,error.Entered.Task).WaitAsync(TimeSpan.FromSeconds(5));
 output.SafeToClose=error.SafeToClose=()=>one.IsCompleted&&two.IsCompleted;
 Task readers=Task.WhenAll(one,two);bool fault=false;
 try
 {
  if(args.Contains("--baseline-reader-fault"))
  {
   // Exact pre-review70747bd6 retirement pattern: IOException bypasses close.
   cancellation.Cancel();try{await readers;}catch(OperationCanceledException)when(cancellation.IsCancellationRequested){}
   try{stdout.Dispose();}finally{stderr.Dispose();}
  }
  else await (Task)retire.Invoke(null,new object[]{readers,cancellation,stdout,stderr})!;
 }
 catch(IOException){fault=true;}
 Check(fault,"post-exit cancelled read IO fault remains visible");
 Check(output.Closed&&error.Closed&&output.ClosedAfterRetirement&&error.ClosedAfterRetirement,"fault closes both owned readers only after both reads retire");
}
else Console.WriteLine("NOT_RUN post-exit fault retirement seam absent in selected baseline job");
Console.WriteLine($"{pass} PASS / {fail} FAIL (actual synthetic subprocesses; no FFmpeg/GL/native audio/device)");return fail==0?0:1;

sealed class FaultRead(bool ioFailure):Stream
{
 public TaskCompletionSource Entered=new(TaskCreationOptions.RunContinuationsAsynchronously);public Func<bool> SafeToClose=()=>false;public bool Closed,ClosedAfterRetirement;
 public override async ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken token=default)
 {
  Entered.TrySetResult();try{await Task.Delay(Timeout.Infinite,token);return 0;}
  catch(OperationCanceledException)when(ioFailure){throw new IOException("synthetic post-exit read fault");}
 }
 protected override void Dispose(bool disposing){Closed=true;ClosedAfterRetirement=SafeToClose();base.Dispose(disposing);}
 public override bool CanRead=>true;public override bool CanSeek=>false;public override bool CanWrite=>false;public override long Length=>throw new NotSupportedException();public override long Position{get=>throw new NotSupportedException();set=>throw new NotSupportedException();}
 public override int Read(byte[]b,int o,int n)=>throw new NotSupportedException();public override void Flush(){}public override long Seek(long o,SeekOrigin s)=>throw new NotSupportedException();public override void SetLength(long l)=>throw new NotSupportedException();public override void Write(byte[]b,int o,int n)=>throw new NotSupportedException();
}
