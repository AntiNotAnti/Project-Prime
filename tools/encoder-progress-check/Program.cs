using System.Globalization;
using System.Text;
using MphRead.Mods.Replay;

bool baseline=args.Contains("--baseline");int pass=0,fail=0;
void Check(bool value,string name){Console.WriteLine($"{(value?"PASS":"FAIL")} {name}");if(value)pass++;else fail++;}
async Task Original(StreamReader reader,Action<int> publish)
{
 while(await reader.ReadLineAsync() is {} line)
  if(line.StartsWith("frame=",StringComparison.Ordinal)&&int.TryParse(line.AsSpan(6),NumberStyles.Integer,CultureInfo.InvariantCulture,out int frame))publish(frame);
}
Task Read(StreamReader reader,Action<int> publish)=>baseline?Original(reader,publish):ReplayEncoderProgress.Read(reader,publish);
string[] values={""," ","+","-","0","-0","+0","12","  +0012 \t","-2147483648","2147483647","2147483648","-2147483649","99999999999999999999999","+ 12","12 3","12\0","12\0\0","12 \0\0","12\0 ","12\0x","\u00a012","12\u00a0","１２","١٢","\v\f42\v\f","--12","0x12"};
string[] lines=values.Select(v=>"frame="+v).Concat(new[]{"xframe=12","FRAME=12","frame =12","frame=1=2","unknown=9","","frame=0000000000000000000000000000000000000012"}).ToArray();
foreach(int fragment in new[]{1,2,7,2048})
 foreach(string separator in new[]{"\n","\r","\r\n"})
 {
  string text=string.Join(separator,lines);var expected=new List<int>();using(var original=new StreamReader(new MemoryStream(Encoding.UTF8.GetBytes(text))))await Original(original,expected.Add);
  using var stream=new Fragments(Encoding.UTF8.GetBytes(text),fragment);using var reader=new StreamReader(stream);var actual=new List<int>();await Read(reader,actual.Add);
  Check(actual.SequenceEqual(expected),$"integer/prefix/overflow/Unicode/NUL/EOF parity fragment={fragment} separator={Json(separator)}");
  Check(!stream.Closed && reader.Peek()==-1,$"caller retains reader/stream at EOF fragment={fragment} separator={Json(separator)}");
 }
// Deterministic differential corpus exercises every short combination around whitespace,
// signs, digits, overflow and terminal NULs, with actual UTF-8 reader fragmentation.
var random=new Random(9187);string alphabet="0123456789+- \t\v\f\0xyz\u00a0";var corpus=new StringBuilder();
for(int i=0;i<10000;i++)
{
 corpus.Append(i%11==0?"FRAME=":"frame=");
 if(i%7==0)corpus.Append(i%2==0?"214748364":"-214748364");
 for(int j=0,n=random.Next(0,18);j<n;j++)corpus.Append(alphabet[random.Next(alphabet.Length)]);
 corpus.Append(i%3==0?"\r\n":i%3==1?"\r":"\n");
}
var wanted=new List<int>();using(var original=new StreamReader(new MemoryStream(Encoding.UTF8.GetBytes(corpus.ToString()))))await Original(original,wanted.Add);
using(var stream=new Fragments(Encoding.UTF8.GetBytes(corpus.ToString()),3))using(var reader=new StreamReader(stream))
{var actual=new List<int>();await Read(reader,actual.Add);Check(actual.SequenceEqual(wanted),"10000 deterministic differential lines preserve every publication");}
foreach(bool cancel in new[]{false,true})
{
 using var cancellation=new CancellationTokenSource();using var stream=new BoundaryStream(cancel,cancellation.Token);using var reader=new StreamReader(stream);var actual=new List<int>();Task read=Read(reader,actual.Add);
 await stream.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));if(cancel)cancellation.Cancel();else stream.Release.TrySetResult();
 bool expected=false;try{await read.WaitAsync(TimeSpan.FromSeconds(5));}catch(OperationCanceledException)when(cancel){expected=true;}catch(IOException)when(!cancel){expected=true;}
 Check(expected && actual.SequenceEqual(new[]{12}),cancel?"cancelled read propagates after published frame":"IO failure propagates after published frame");
 Check(!stream.Closed,cancel?"cancellation preserves caller stream ownership":"IO failure preserves caller stream ownership");
}
using(var reader=new StreamReader(new MemoryStream()))await Read(reader,_=>{}); // warm allocation path
foreach(bool valid in new[]{false,true})
{
 // Fixture generates bytes lazily: no input-sized source buffer is allocated.
 using var stream=new RepeatedSegments(valid?new[]{("frame=",1),(" ",8*1024*1024),("+",1),("0",8*1024*1024),("12 \0\0",1)}:new[]{("x",16*1024*1024)});
 using var reader=new StreamReader(stream);var actual=new List<int>();long before=GC.GetTotalAllocatedBytes(true);await Read(reader,actual.Add);long allocated=GC.GetTotalAllocatedBytes(true)-before;
 Console.WriteLine($"allocation fixture={(valid?"valid":"unknown")} bytes={stream.Total} managed={allocated}");
 Check(stream.Remaining==0 && (valid?actual.SequenceEqual(new[]{12}):actual.Count==0),valid?"16MiB valid whitespace/leading-zero final progress accepted":"16MiB unknown unterminated line drained");
 Check(allocated<1024*1024,valid?"valid long progress uses fixed working allocation":"unknown long progress uses fixed working allocation");
}
Console.WriteLine($"{pass} PASS / {fail} FAIL ({(baseline?"baseline":"candidate")}; no subprocess/native/device startup)");return fail==0?0:1;
string Json(string s)=>System.Text.Json.JsonSerializer.Serialize(s);

sealed class Fragments(byte[] bytes,int fragment):Stream
{
 int offset;public bool Closed;public override bool CanRead=>true;public override bool CanSeek=>false;public override bool CanWrite=>false;public override long Length=>bytes.Length;public override long Position{get=>offset;set=>throw new NotSupportedException();}
 public override int Read(byte[] b,int o,int n)=>Read(b.AsSpan(o,n));public override int Read(Span<byte>b){int n=Math.Min(Math.Min(fragment,b.Length),bytes.Length-offset);bytes.AsSpan(offset,n).CopyTo(b);offset+=n;return n;}
 public override ValueTask<int> ReadAsync(Memory<byte>b,CancellationToken t=default)=>ValueTask.FromResult(Read(b.Span));protected override void Dispose(bool disposing){Closed=true;base.Dispose(disposing);}
 public override void Flush(){}public override long Seek(long o,SeekOrigin s)=>throw new NotSupportedException();public override void SetLength(long l)=>throw new NotSupportedException();public override void Write(byte[]b,int o,int n)=>throw new NotSupportedException();
}
sealed class BoundaryStream(bool cancel,CancellationToken token):Stream
{
 bool first=true;public bool Closed;public TaskCompletionSource Entered=new(TaskCreationOptions.RunContinuationsAsynchronously),Release=new(TaskCreationOptions.RunContinuationsAsynchronously);
 public override async ValueTask<int> ReadAsync(Memory<byte>b,CancellationToken t=default)
 {
  if(first){first=false;byte[] bytes=Encoding.UTF8.GetBytes("frame=12\n");bytes.CopyTo(b);return bytes.Length;}
  Entered.TrySetResult();if(cancel)await Task.Delay(Timeout.Infinite,token);else await Release.Task;throw new IOException("synthetic read failure");
 }
 public override bool CanRead=>true;public override bool CanSeek=>false;public override bool CanWrite=>false;public override long Length=>throw new NotSupportedException();public override long Position{get=>throw new NotSupportedException();set=>throw new NotSupportedException();}
 protected override void Dispose(bool disposing){Closed=true;base.Dispose(disposing);}public override int Read(byte[]b,int o,int n)=>throw new NotSupportedException();public override void Flush(){}public override long Seek(long o,SeekOrigin s)=>throw new NotSupportedException();public override void SetLength(long l)=>throw new NotSupportedException();public override void Write(byte[]b,int o,int n)=>throw new NotSupportedException();
}
sealed class RepeatedSegments((string Text,int Repeats)[] segments):Stream
{
 int segment,repeat,character;public long Total{get;}=segments.Sum(s=>(long)s.Text.Length*s.Repeats);public long Remaining{get;private set;}=segments.Sum(s=>(long)s.Text.Length*s.Repeats);
 public override int Read(byte[]b,int o,int n)=>Read(b.AsSpan(o,n));public override int Read(Span<byte>b)
 {
  int i=0;while(i<b.Length && segment<segments.Length){var current=segments[segment];b[i++]=(byte)current.Text[character++];Remaining--;if(character==current.Text.Length){character=0;if(++repeat==current.Repeats){repeat=0;segment++;}}}return i;
 }
 public override ValueTask<int> ReadAsync(Memory<byte>b,CancellationToken t=default)=>ValueTask.FromResult(Read(b.Span));
 public override bool CanRead=>true;public override bool CanSeek=>false;public override bool CanWrite=>false;public override long Length=>Total;public override long Position{get=>Total-Remaining;set=>throw new NotSupportedException();}
 public override void Flush(){}public override long Seek(long o,SeekOrigin s)=>throw new NotSupportedException();public override void SetLength(long l)=>throw new NotSupportedException();public override void Write(byte[]b,int o,int n)=>throw new NotSupportedException();
}
