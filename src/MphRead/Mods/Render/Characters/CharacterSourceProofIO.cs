using System;
using System.IO;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
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

// Physical directory identity: Darwin device/inode or Windows volume/128-bit
// file ID from an opened directory handle. Final paths retain stable aliases
// while detecting replacement or retargeting anywhere in the chosen ancestry.
internal sealed class CharacterSourceRootWitness
{
 private readonly record struct Ancestor(string Lexical,string Physical,ulong Device,ulong Inode,ulong InodeHigh=0);
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
 [StructLayout(LayoutKind.Sequential)]
 private struct WindowsFileId { internal ulong Volume, Low, High; }
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true,EntryPoint="CreateFileW")]
 private static extern SafeFileHandle OpenDirectory(string path,uint access,uint share,IntPtr security,uint disposition,uint flags,IntPtr template);
 [DllImport("kernel32.dll",SetLastError=true)]
 [return:MarshalAs(UnmanagedType.Bool)]
 private static extern bool GetFileInformationByHandleEx(SafeFileHandle handle,int infoClass,out WindowsFileId info,uint size);
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true,EntryPoint="GetFinalPathNameByHandleW")]
 private static extern uint FinalPath(SafeFileHandle handle,StringBuilder path,uint capacity,uint flags);
 private static Ancestor ResolveWindows(string path)
 {
  // Query metadata only, follow an existing alias to its actual target. Share
  // delete so a concurrent directory replacement is observed by the next proof.
  using var handle=OpenDirectory(path,0,7,IntPtr.Zero,3,0x02000000,IntPtr.Zero);
  if(handle.IsInvalid)throw new IOException("Source root ancestry cannot be opened.",new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()));
  if(!Directory.Exists(path)||!GetFileInformationByHandleEx(handle,18,out var id,24))
   throw new IOException("Source root ancestry must expose a stable directory file ID.");
  var buffer=new StringBuilder(512);uint length=FinalPath(handle,buffer,(uint)buffer.Capacity,0);
  if(length>=buffer.Capacity&&length<32768){buffer.EnsureCapacity(checked((int)length+1));length=FinalPath(handle,buffer,(uint)buffer.Capacity,0);}
  if(length==0||length>=buffer.Capacity)throw new IOException("Source physical root cannot be resolved.");
  // The final handle path must still be a directory. Leaves use their own
  // existing reparse/hash checks; this witness pins the complete root ancestry.
  string physical=buffer.ToString();
  if(!Directory.Exists(physical))throw new IOException("Source physical root is not an existing directory.");
  return new(path,physical,id.Volume,id.Low,id.High);
 }
 private static Ancestor Resolve(string path)
 {
  if(OperatingSystem.IsWindows())return ResolveWindows(path);
  IntPtr resolved=RealPath(path,IntPtr.Zero);
  if(resolved==IntPtr.Zero)throw new IOException("Source root ancestry cannot be resolved.");
  string physical;
  try{physical=Marshal.PtrToStringUTF8(resolved)??throw new IOException("Source physical root is missing.");}
  finally{Free(resolved);}
  if(Stat(physical,out var info)!=0||(info.Mode&0xf000)!=0x4000)
   throw new IOException("Source root ancestry is not an existing directory.");
  return new(path,physical,unchecked((ulong)info.Device),info.Inode);
 }
 internal const string UnsupportedPlatformMessage="Private source physical-identity proof requires Windows x64 or macOS arm64; this platform is unsupported.";
 internal static bool QualifiedForCurrentProcess=>IntPtr.Size==8&&(OperatingSystem.IsMacOS()&&RuntimeInformation.ProcessArchitecture==Architecture.Arm64
  ||OperatingSystem.IsWindows()&&RuntimeInformation.ProcessArchitecture==Architecture.X64);
 internal static void RequireQualifiedPlatform(bool macOS,Architecture architecture,int pointerBytes,bool windows=false)
 {
  if(pointerBytes!=8||!(macOS&&architecture==Architecture.Arm64||windows&&architecture==Architecture.X64))
   throw new InvalidDataException(UnsupportedPlatformMessage);
 }
 internal static CharacterSourceRootWitness Capture(string root)
 {
  // Only the actual process platform reaches private Darwin imports. This
  // explicit source mode cannot activate from the native/default pack path.
  RequireQualifiedPlatform(OperatingSystem.IsMacOS(),RuntimeInformation.ProcessArchitecture,IntPtr.Size,OperatingSystem.IsWindows());
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
