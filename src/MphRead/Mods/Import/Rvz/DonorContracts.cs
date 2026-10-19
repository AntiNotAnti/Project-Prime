// Minimal identical DiscPlatform/IDiscBlob contracts from donor DiscFileSystem.cs.
// Does not include donor FST/importer; the existing Project reader remains owner.
namespace MphRead.Mods.Import.Rvz;
internal enum DiscPlatform { GameCube, Wii }
internal interface IDiscBlob { long Size {get;} void Read(long offset,System.Span<byte> dst); }
