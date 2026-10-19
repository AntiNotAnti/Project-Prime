using System;
using System.IO;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
namespace MphRead.Mods.Render.Characters;

// Strong proof only: load, explicit refresh, decode and pre-publication.
// Immutable cached draw never calls this owner.
internal static class CharacterSourceProofIO
{
#if CPP08_DRAFT_CHECK
 internal static long OpenedReads, PathWitnesses;
#endif
 private static long Length(Stream stream,long maximum)
 {
  if(!stream.CanSeek||stream.Position!=0||maximum<=0)throw new InvalidDataException("Proof stream must be opened, seekable and bounded.");
  long length=stream.Length;
  if(length<=0||length>maximum)throw new InvalidDataException("Proof stream is missing or oversized.");
  return length;
 }
 private static void End(Stream stream,long expected)
 {
  if(stream.ReadByte()!=-1||stream.Length!=expected||stream.Position!=expected)
   throw new InvalidDataException("Proof stream changed length during capture.");
 }
 internal static byte[] ReadOpened(Stream stream,int maximum)
 {
  long length=Length(stream,maximum);var bytes=new byte[checked((int)length)];
  stream.ReadExactly(bytes);End(stream,length);return bytes;
 }
 internal static string HashOpened(Stream stream,long maximum)
 {
  long length=Length(stream,maximum),remaining=length;
  using var sha=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
  var buffer=new byte[(int)Math.Min(64*1024,length)];
  while(remaining>0)
  {
   int count=stream.Read(buffer,0,(int)Math.Min(buffer.Length,remaining));
   if(count<=0)throw new EndOfStreamException("Proof stream shrank during capture.");
   sha.AppendData(buffer,0,count);remaining-=count;
  }
  End(stream,length);return Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant();
 }
 internal static byte[] ReadFile(string path,int maximum)
 {
#if CPP08_DRAFT_CHECK
  System.Threading.Interlocked.Increment(ref OpenedReads);
#endif
  using var stream=File.OpenRead(path);return ReadOpened(stream,maximum);
 }
 internal static string HashFile(string path,long maximum)
 {
#if CPP08_DRAFT_CHECK
  System.Threading.Interlocked.Increment(ref OpenedReads);
#endif
  using var stream=File.OpenRead(path);return HashOpened(stream,maximum);
 }
}

// macOS-only source-preview boundary, using the installed Darwin SDK's
// stat64 layout (sys/stat.h). Device+inode pins directory identity without
// rejecting stable existing aliases such as /var -> /private/var.
internal sealed class CharacterSourceRootWitness
{
 private readonly record struct Ancestor(string Lexical,string Physical,int Device,ulong Inode);
 private readonly Ancestor[] _ancestors;
 private CharacterSourceRootWitness(Ancestor[] ancestors)=>_ancestors=ancestors;
 [StructLayout(LayoutKind.Explicit,Size=144)]
 private struct DarwinStat
 {
  [FieldOffset(0)]internal int Device;
  [FieldOffset(4)]internal ushort Mode;
  [FieldOffset(8)]internal ulong Inode;
 }
 [DllImport("/usr/lib/libSystem.B.dylib",EntryPoint="stat",SetLastError=true)]
 private static extern int Stat(string path,out DarwinStat stat);
 [DllImport("/usr/lib/libSystem.B.dylib",EntryPoint="realpath",SetLastError=true)]
 private static extern IntPtr RealPath(string path,IntPtr destination);
 [DllImport("/usr/lib/libSystem.B.dylib",EntryPoint="free")]
 private static extern void Free(IntPtr pointer);
 private static Ancestor Resolve(string path)
 {
  IntPtr resolved=RealPath(path,IntPtr.Zero);
  if(resolved==IntPtr.Zero)throw new IOException("Source root ancestry cannot be resolved.");
  string physical;
  try{physical=Marshal.PtrToStringUTF8(resolved)??throw new IOException("Source physical root is missing.");}
  finally{Free(resolved);}
  if(Stat(physical,out var info)!=0||(info.Mode&0xf000)!=0x4000)
   throw new IOException("Source root ancestry is not an existing directory.");
  return new(path,physical,info.Device,info.Inode);
 }
 internal const string UnsupportedPlatformMessage="Private source physical-identity proof is currently qualified only on macOS arm64; this platform is unsupported.";
 internal static bool QualifiedForCurrentProcess=>OperatingSystem.IsMacOS()&&RuntimeInformation.ProcessArchitecture==Architecture.Arm64&&IntPtr.Size==8;
 internal static void RequireQualifiedPlatform(bool macOS,Architecture architecture,int pointerBytes)
 {
  if(!macOS||architecture!=Architecture.Arm64||pointerBytes!=8)
   throw new InvalidDataException(UnsupportedPlatformMessage);
 }
 internal static CharacterSourceRootWitness Capture(string root)
 {
  // Only the actual process platform reaches private Darwin imports. This
  // explicit source mode cannot activate from the native/default pack path.
  RequireQualifiedPlatform(OperatingSystem.IsMacOS(),RuntimeInformation.ProcessArchitecture,IntPtr.Size);
#if CPP08_DRAFT_CHECK
  System.Threading.Interlocked.Increment(ref CharacterSourceProofIO.PathWitnesses);
#endif
  root=Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
  var ancestry=new System.Collections.Generic.List<Ancestor>();
  for(string? current=root;current!=null;current=Path.GetDirectoryName(current))ancestry.Add(Resolve(current));
  return new(ancestry.ToArray());
 }
 internal void RequireUnchanged(string root)
 {
  var current=Capture(root);
  if(!_ancestors.AsSpan().SequenceEqual(current._ancestors))
   throw new InvalidDataException("Source physical root or existing ancestry changed.");
 }
}
