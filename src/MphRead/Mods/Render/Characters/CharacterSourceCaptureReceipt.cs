using System;
using System.IO;
using System.Buffers.Binary;
using System.Security.Cryptography;
namespace MphRead.Mods.Render.Characters;

internal sealed record CharacterSourceCaptureReceipt(string FileName,long Bytes,string Sha256,int Width,int Height)
{
 internal const int MaximumPngBytes=32*1024*1024;
 // The actual existing ScreenCapture delegate is supplied only by the private
 // GL consumer. Source-linked tests inject a closed writer here, not a product
 // ScreenCapture hook. A failed/no-op writer cannot count an old file.
 internal static CharacterSourceCaptureReceipt Write(string path,Func<string,bool> writer)
 {
  if(File.Exists(path)||Directory.Exists(path))throw new InvalidDataException("Capture output must be new.");
  if(!writer(path))throw new InvalidDataException("ScreenCapture.SaveWindow returned false.");
  if(!File.Exists(path)||(File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)
   throw new InvalidDataException("Capture did not create a regular PNG file.");
  byte[] png=CharacterSourceProofIO.ReadFile(path,MaximumPngBytes);
  var (width,height)=ValidatePng(png);
  return new(Path.GetFileName(path),png.LongLength,Convert.ToHexString(SHA256.HashData(png)).ToLowerInvariant(),width,height);
 }
 private static (int Width,int Height) ValidatePng(ReadOnlySpan<byte> png)
 {
  ReadOnlySpan<byte> signature=new byte[]{137,80,78,71,13,10,26,10};
  if(png.Length<45||!png[..8].SequenceEqual(signature))throw new InvalidDataException("Capture PNG signature is invalid.");
  int offset=8,width=0,height=0,chunks=0;bool data=false,end=false;
  while(offset<png.Length)
  {
   if(png.Length-offset<12||++chunks>65536)throw new InvalidDataException("Capture PNG chunks exceed bounds.");
   uint count=BinaryPrimitives.ReadUInt32BigEndian(png[offset..]);
   if(count>(uint)(png.Length-offset-12))throw new InvalidDataException("Capture PNG chunk exceeds file.");
   int length=checked((int)count);var type=png.Slice(offset+4,4);var payload=png.Slice(offset+8,length);
   uint expected=BinaryPrimitives.ReadUInt32BigEndian(png.Slice(offset+8+length,4));
   if(Crc32(png.Slice(offset+4,length+4))!=expected)throw new InvalidDataException("Capture PNG CRC is invalid.");
   if(chunks==1)
   {
    if(!type.SequenceEqual("IHDR"u8)||length!=13)throw new InvalidDataException("Capture PNG IHDR is invalid.");
    uint w=BinaryPrimitives.ReadUInt32BigEndian(payload),h=BinaryPrimitives.ReadUInt32BigEndian(payload[4..]);
    if(w==0||h==0||w>8192||h>8192||(ulong)w*h>32*1024*1024)throw new InvalidDataException("Capture PNG dimensions exceed bounds.");
    width=(int)w;height=(int)h;
   }
   else if(type.SequenceEqual("IHDR"u8))throw new InvalidDataException("Capture PNG has duplicate IHDR.");
   if(type.SequenceEqual("IDAT"u8))data=true;
   offset+=length+12;
   if(type.SequenceEqual("IEND"u8))
   {if(length!=0||offset!=png.Length||!data)throw new InvalidDataException("Capture PNG IEND is invalid.");end=true;break;}
  }
  if(!end)throw new InvalidDataException("Capture PNG is incomplete.");return(width,height);
 }
 private static uint Crc32(ReadOnlySpan<byte> bytes)
 {
  uint crc=uint.MaxValue;
  foreach(byte value in bytes){crc^=value;for(int bit=0;bit<8;bit++)crc=(crc>>1)^((crc&1)!=0?0xedb88320u:0);}
  return ~crc;
 }
}
