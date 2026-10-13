using System.Reflection;
using System.Text;
using MphRead.Mods.Replay;

internal static class GrammarChecks
{
    static readonly string[] Allowed=(string[])typeof(ReplayEncoderCapabilities).GetField("Allowed",BindingFlags.NonPublic|BindingFlags.Static)!.GetValue(null)!;
    static Task Read(StreamReader reader,Action<int> publish,CancellationToken token=default) => (Task)typeof(ReplayEncoderCapabilities).GetMethod("ReadCapabilities",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,new object[]{reader,publish,token})!;
    static string[] Original(string output)
    {
        var supported=output.Split('\n').Select(line=>line.Split(' ',StringSplitOptions.RemoveEmptyEntries)).Where(parts=>parts.Length>1).Select(parts=>parts[1]).ToHashSet(StringComparer.Ordinal);
        return Allowed.Where(supported.Contains).ToArray();
    }
    static async Task<string[]> Parsed(StreamReader reader)
    {
        var found=new bool[Allowed.Length]; await Read(reader,index=>found[index]=true); return Allowed.Where((_,index)=>found[index]).ToArray();
    }
    internal static async Task Run(Action<bool,string> check)
    {
        string[] lines={"","libx265","a libx265","  a   h264_nvenc  detail","a H264_NVENC","a h264_nvencx","a h264_nvenc\0","a h264_nvenc\t","a\th264_nvenc","a\th264_nvenc desc","a h264_nvenc\r","a h264_nvenc \r","a h264_nvenc \0","a\u00a0h264_nvenc","a h264_nvenc\u00a0","a hevc_qsv","a hevc_amf","a libx264","a libx264","a x libx265","a h264_videotoolbox","a hevc_videotoolbox","a h264_amf","a libx265\tignored","a hevc_nvenc","a h264_qsv","a libx265"};
        foreach(int fragment in new[]{1,2,7,2048}) foreach(string separator in new[]{"\n","\r","\r\n"})
        {
            string text=string.Join(separator,lines); using var stream=new Fragments(Encoding.UTF8.GetBytes(text),fragment); using var reader=new StreamReader(stream);
            check((await Parsed(reader)).SequenceEqual(Original(text)),$"exact space-only/LF-only CR/tab/NUL/Unicode/EOF grammar fragment={fragment} separator={System.Text.Json.JsonSerializer.Serialize(separator)}");
            check(!stream.Closed && reader.Peek()==-1,"parser leaves EOF reader/stream caller-owned");
        }
        var random=new Random(20719); var corpus=new StringBuilder(); string[] tokens=Allowed.Concat(new[]{"unknown","x","libx264x","LIBX265","","\0","\r","\t","\u00a0"}).ToArray();string[] separators={" ","  ","\t","\r","\u00a0","\0"};
        for(int i=0;i<10000;i++){corpus.Append(i%3==0?" ":"");corpus.Append(tokens[random.Next(tokens.Length)]);corpus.Append(separators[random.Next(separators.Length)]);corpus.Append(tokens[random.Next(tokens.Length)]);if(i%2==0)corpus.Append(" description");corpus.Append(i%3==0?"\r\n":i%3==1?"\r":"\n");}
        string textCorpus=corpus.ToString();using(var stream=new Fragments(Encoding.UTF8.GetBytes(textCorpus),3))using(var reader=new StreamReader(stream))check((await Parsed(reader)).SequenceEqual(Original(textCorpus)),"10000 deterministic differential lines match original Split/HashSet grammar");
        foreach(bool secondToken in new[]{false,true})
        {
            using var stream=new RepeatedSegments(secondToken?new[]{("flags ",1),("z",16*1024*1024),("\n flags hevc_amf late",1)}:new[]{("z",16*1024*1024),(" h264_qsv",1)});using var reader=new StreamReader(stream);
            long before=GC.GetTotalAllocatedBytes(true);string[] found=await Parsed(reader);long allocated=GC.GetTotalAllocatedBytes(true)-before;
            Console.WriteLine($"allocation generated-stream overlong-{(secondToken?"second":"first")}-token managed={allocated}");
            check(found.SequenceEqual(new[]{secondToken?"hevc_amf":"h264_qsv"}) && stream.Remaining==0,"overlong token drained; late/EOF valid name retained without output cap");
            check(allocated<1024*1024,"generated capability stream allocation stays under fixture 1MiB threshold");
        }
        foreach(bool cancel in new[]{false,true})
        {
            using var cts=new CancellationTokenSource();using var stream=new FaultStream(" V hevc_qsv ok\n",!cancel,false);using var reader=new StreamReader(stream);var found=new List<int>();Task read=Read(reader,found.Add,cts.Token);await stream.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));cts.Cancel();bool expected=false;
            try{await read.WaitAsync(TimeSpan.FromSeconds(3));}catch(IOException)when(!cancel){expected=true;}catch(OperationCanceledException)when(cancel){expected=true;}
            check(expected && found.SequenceEqual(new[]{Array.IndexOf(Allowed,"hevc_qsv")}),"reader fault/cancellation propagates after published capability");check(!stream.Closed,"parser fault/cancellation keeps caller reader ownership");
        }
        foreach(bool stderr in new[]{false,true})
        using(var reader=new ReadyReader()) using(var timeout=new CancellationTokenSource(TimeSpan.FromMilliseconds(50))) using(var linked=CancellationTokenSource.CreateLinkedTokenSource(timeout.Token))
        {
            Task read=Task.Run(()=>stderr?(Task)typeof(ReplayEncoderErrorDrain).GetMethods(BindingFlags.Static|BindingFlags.NonPublic).Single(method=>method.Name=="Drain" && method.GetParameters().Length==2).Invoke(null,new object[]{reader,linked.Token})!:Read(reader,_=>{},linked.Token));bool cancelled=false;try{await read.WaitAsync(TimeSpan.FromSeconds(2));}catch(OperationCanceledException){cancelled=true;}catch(TimeoutException){}
            check(cancelled && reader.Reads>0,$"continuous synchronously-ready {(stderr?"stderr":"stdout")} respects linked deadline even when reader ignores token");check(!reader.Closed,"continuous read cancellation leaves parser reader caller-owned");
        }
        foreach(bool stdoutFault in new[]{false,true})
        {
            using var cts=new CancellationTokenSource();using var output=new FaultStream(" V h264_qsv ready\n",stdoutFault,!stdoutFault);using var error=new FaultStream("ignored",!stdoutFault,stdoutFault);using var stdout=new StreamReader(output);using var stderr=new StreamReader(error);
            Task drain=(Task)typeof(ReplayEncoderErrorDrain).GetMethods(BindingFlags.Static|BindingFlags.NonPublic).Single(method=>method.Name=="Drain" && method.GetParameters().Length==2)!.Invoke(null,new object[]{stderr,cts.Token})!;
            Task reads=Task.WhenAll(Read(stdout,_=>{},cts.Token),drain);await Task.WhenAll(output.Entered.Task,error.Entered.Task).WaitAsync(TimeSpan.FromSeconds(3));
            Task retirement=(Task)typeof(ReplayEncoderCapabilities).GetMethod("RetireReaders",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,new object[]{reads,cts,stdout,stderr})!;
            var held=stdoutFault?error:output;await held.CancelObserved.Task.WaitAsync(TimeSpan.FromSeconds(3));
            check(!retirement.IsCompleted && !output.Closed && !error.Closed,"one reader IO fault does not close either reader before other retires");held.Release.TrySetResult();bool propagated=false;try{await retirement.WaitAsync(TimeSpan.FromSeconds(3));}catch(IOException){propagated=true;}
            check(propagated && output.Closed && error.Closed && !output.ClosedBeforeRetirement && !error.ClosedBeforeRetirement,"both reader retirement/finally closure preserves IO fault");
        }
        var manifest=new ReplayVideoExportManifest("input -c:v libx264 -preset slow -crf 18 output");check(ReplayEncoderCapabilities.Apply(manifest,"libx265").FfmpegArguments=="input -c:v libx265 -preset slow -crf 18 output","software encoder Apply semantics unchanged");check(ReplayEncoderCapabilities.Apply(manifest,"h264_amf").FfmpegArguments=="input -c:v h264_amf -b:v 20M output","hardware encoder Apply semantics unchanged");bool rejected=false;try{ReplayEncoderCapabilities.Apply(manifest,"unknown");}catch(ArgumentException){rejected=true;}check(rejected,"unknown encoder Apply still rejects");
        Func<StreamReader,Task> originalDrainMethodGroup=ReplayEncoderErrorDrain.Drain;using(var reader=new StreamReader(new MemoryStream()))await originalDrainMethodGroup(reader);check(true,"existing one-argument stderr Drain method-group contract preserved");
    }
}
sealed class Fragments(byte[] bytes,int fragment):Stream
{
    int offset;public bool Closed;public override int Read(byte[] b,int o,int n)=>Read(b.AsSpan(o,n));public override int Read(Span<byte>b){int n=Math.Min(Math.Min(fragment,b.Length),bytes.Length-offset);bytes.AsSpan(offset,n).CopyTo(b);offset+=n;return n;}public override ValueTask<int> ReadAsync(Memory<byte>b,CancellationToken t=default)=>ValueTask.FromResult(Read(b.Span));protected override void Dispose(bool d){Closed=true;base.Dispose(d);}public override bool CanRead=>true;public override bool CanSeek=>false;public override bool CanWrite=>false;public override long Length=>bytes.Length;public override long Position{get=>offset;set=>throw new NotSupportedException();}public override void Flush(){}public override long Seek(long o,SeekOrigin s)=>throw new NotSupportedException();public override void SetLength(long l)=>throw new NotSupportedException();public override void Write(byte[]b,int o,int n)=>throw new NotSupportedException();
}
sealed class RepeatedSegments((string Text,int Repeats)[] segments):Stream
{
    int segment,repeat,character;public long Remaining{get;private set;}=segments.Sum(s=>(long)s.Text.Length*s.Repeats);public override int Read(byte[]b,int o,int n)=>Read(b.AsSpan(o,n));public override int Read(Span<byte>b){int i=0;while(i<b.Length&&segment<segments.Length){var current=segments[segment];b[i++]=(byte)current.Text[character++];Remaining--;if(character==current.Text.Length){character=0;if(++repeat==current.Repeats){repeat=0;segment++;}}}return i;}public override ValueTask<int> ReadAsync(Memory<byte>b,CancellationToken t=default)=>ValueTask.FromResult(Read(b.Span));public override bool CanRead=>true;public override bool CanSeek=>false;public override bool CanWrite=>false;public override long Length=>throw new NotSupportedException();public override long Position{get=>throw new NotSupportedException();set=>throw new NotSupportedException();}public override void Flush(){}public override long Seek(long o,SeekOrigin s)=>throw new NotSupportedException();public override void SetLength(long l)=>throw new NotSupportedException();public override void Write(byte[]b,int o,int n)=>throw new NotSupportedException();
}
sealed class FaultStream(string prefix,bool ioFault,bool hold):Stream
{
    bool first=true,retired;public bool Closed,ClosedBeforeRetirement;public TaskCompletionSource Entered=new(TaskCreationOptions.RunContinuationsAsynchronously),CancelObserved=new(TaskCreationOptions.RunContinuationsAsynchronously),Release=new(TaskCreationOptions.RunContinuationsAsynchronously);
    public override async ValueTask<int> ReadAsync(Memory<byte>b,CancellationToken t=default)
    {
        if(first){first=false;byte[] bytes=Encoding.UTF8.GetBytes(prefix);bytes.CopyTo(b);return bytes.Length;}
        Entered.TrySetResult();try{await Task.Delay(Timeout.Infinite,t);throw new InvalidOperationException();}catch(OperationCanceledException){CancelObserved.TrySetResult();if(hold)await Release.Task;if(ioFault)throw new IOException("synthetic owned read failure");throw;}finally{retired=true;}
    }
    protected override void Dispose(bool d){Closed=true;ClosedBeforeRetirement|=!retired;base.Dispose(d);}public override int Read(byte[]b,int o,int n)=>throw new NotSupportedException();public override bool CanRead=>true;public override bool CanSeek=>false;public override bool CanWrite=>false;public override long Length=>throw new NotSupportedException();public override long Position{get=>throw new NotSupportedException();set=>throw new NotSupportedException();}public override void Flush(){}public override long Seek(long o,SeekOrigin s)=>throw new NotSupportedException();public override void SetLength(long l)=>throw new NotSupportedException();public override void Write(byte[]b,int o,int n)=>throw new NotSupportedException();
}

sealed class ReadyReader():StreamReader(new MemoryStream())
{
    public int Reads;public bool Closed;
    public override ValueTask<int> ReadAsync(Memory<char> buffer,CancellationToken token=default){Reads++;buffer.Span.Fill('z');return ValueTask.FromResult(buffer.Length);}
    protected override void Dispose(bool disposing){Closed=true;base.Dispose(disposing);}
}
