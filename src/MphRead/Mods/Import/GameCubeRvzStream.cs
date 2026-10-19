using System;
using System.IO;
using System.Buffers.Binary;
using MphRead.Mods.Import.Rvz;
namespace MphRead.Mods.Import;

// Private draft: decompression only. Existing GameCubeDiscReader remains FST,
// exact edition, archive hash and file-view owner. No Wii/keys/import/output path.
internal sealed class GameCubeRvzStream : Stream
{
    private readonly WiaReader _reader;
    private readonly object _gate = new();
    private long _position;
    private bool _disposed;
    private GameCubeRvzStream(WiaReader reader) { _reader = reader; }
    internal static Stream Open(Stream source,bool leaveOpen=true)
    {
        ArgumentNullException.ThrowIfNull(source);
        if(!source.CanRead||!source.CanSeek)throw new ArgumentException("RVZ needs a readable seekable source.");
        long position=source.Position;
        try
        {
            if(source.Length<0x124)throw new InvalidDataException("Truncated RVZ source.");
            Span<byte> prefix=stackalloc byte[0x4c];source.Position=0;source.ReadExactly(prefix);
            if(BinaryPrimitives.ReadUInt32BigEndian(prefix)!=WiaReader.RvzMagic)
                throw new NotSupportedException("Expected RVZ magic, not WIA/ISO/WBFS.");
            if(BinaryPrimitives.ReadUInt32BigEndian(prefix[0x48..])!=1)
                throw new NotSupportedException("Wii/Trilogy/Corruption RVZ is outside the GameCube source owner.");
        }
        finally { source.Position=position; }
        return new GameCubeRvzStream(new WiaReader(source,leaveOpen));
    }
    public override bool CanRead { get { lock(_gate) return !_disposed; } }
    public override bool CanSeek { get { lock(_gate) return !_disposed; } }
    public override bool CanWrite=>false;
    public override long Length { get { lock(_gate){ObjectDisposedException.ThrowIf(_disposed,this);return _reader.Size;} } }
    public override long Position { get {lock(_gate){ObjectDisposedException.ThrowIf(_disposed,this);return _position;}} set {lock(_gate){ObjectDisposedException.ThrowIf(_disposed,this);_position=value<0?throw new ArgumentOutOfRangeException(nameof(value)):value;}} }
    public override int Read(byte[] buffer,int offset,int count)=>Read(buffer.AsSpan(offset,count));
    public override int Read(Span<byte> buffer)
    {
        lock(_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed,this);
            int total=(int)Math.Min(buffer.Length,Math.Max(0,Length-_position));
            int at=0;
            while(at<total)
            {
                int take=Math.Min(total-at,1024*1024);
                _reader.Read(_position,buffer.Slice(at,take));
                _position=checked(_position+take);at+=take;
            }
            return total;
        }
    }
    public override long Seek(long offset,SeekOrigin origin)
    {
        lock(_gate)
        {
            Position=origin switch {SeekOrigin.Begin=>offset,SeekOrigin.Current=>checked(Position+offset),SeekOrigin.End=>checked(Length+offset),_=>throw new ArgumentOutOfRangeException(nameof(origin))};return Position;
        }
    }
    public override void Flush() {lock(_gate)ObjectDisposedException.ThrowIf(_disposed,this);}
    public override void SetLength(long length)=>throw new NotSupportedException("Read-only source.");
    public override void Write(byte[] buffer,int offset,int count)=>throw new NotSupportedException("Read-only source.");
    protected override void Dispose(bool disposing)
    {
        if(disposing)lock(_gate){if(_disposed)return;_disposed=true;_reader.Dispose();}
        base.Dispose(disposing);
    }
}
