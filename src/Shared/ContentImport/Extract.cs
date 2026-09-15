using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using MphRead.Archive;
using CommunityToolkit.HighPerformance.Buffers;
using NCSFCommon.NC;
using NCSFCommon.ReplayGain;

namespace MphRead
{
    public static class Extract
    {
        private const string SetupLockFileName = ".project-prime-setup.lock";

        public static void ExtractArchive(string path)
        {
            string name = Path.GetFileNameWithoutExtension(path);
            string output = Path.GetFullPath(Paths.Combine(Path.GetDirectoryName(path) ?? "", "..", "_archives", name));
            try
            {
                int filesWritten = 0;
                Directory.CreateDirectory(output);
                // A redirected parent receives output one complete line at a
                // time. Announce the archive before the potentially long read
                // and extraction instead of completing this line afterwards.
                Console.WriteLine($"Reading {name}...");
                var bytes = new ReadOnlySpan<byte>(ContentFiles.ReadBytes(path));
                if (Encoding.ASCII.GetString(bytes[0..8]) == Archiver.MagicString)
                {
                    Console.Write(" Extracting archive...");
                    filesWritten = Archiver.Extract(path, output);
                }
                else if (bytes[0] == LZ10.MagicByte)
                {
                    string temp = Paths.Combine(Paths.Export, "__temp");
                    try
                    {
                        Directory.Delete(temp, recursive: true);
                    }
                    catch { }
                    Directory.CreateDirectory(temp);
                    string destination = Paths.Combine(temp, $"{name}.arc");
                    Console.Write(" Decompressing...");
                    LZ10.Decompress(path, destination);
                    Console.Write(" Extracting archive...");
                    filesWritten = Archiver.Extract(destination, output);
                    Directory.Delete(temp, recursive: true);
                }
                Console.WriteLine();
                Console.WriteLine($"Extracted {filesWritten} file{(filesWritten == 1 ? "" : "s")}.");
            }
            catch
            {
                Console.WriteLine();
                Console.WriteLine($"Failed to extract archive. Verify an archive exists at {path}.");
            }
        }

        public static bool Setup(string path, bool replaceConfiguredPaths = false)
        {
            using IDisposable? setupLock = TryAcquireSetupLock();
            if (setupLock == null)
            {
                Console.WriteLine("Game-file setup is already running in another Project Prime process. "
                    + "Wait for it to finish before trying again.");
                return false;
            }
            Console.WriteLine("Reading cartridge dump...");
            byte[] bytes;
            CartridgeValidationResult validation;
            try
            {
                // Keep one read handle for identity verification and the bytes
                // handed to extraction. FileShare.Read prevents replacement on
                // platforms that enforce sharing modes, and avoids a
                // validation/re-open time-of-check window everywhere else.
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                validation = CartridgeCatalog.Supported.Validate(stream);
                if (!validation.IsValid)
                {
                    PrintCartridgeValidationFailure(validation);
                    return false;
                }
                if (validation.ActualLength > int.MaxValue)
                {
                    PrintCartridgeValidationFailure(new CartridgeValidationResult(
                        CartridgeValidationStatus.InvalidLength, validation.Header,
                        validation.Identity, validation.ActualLength,
                        validation.ActualSha256, "The cartridge image is too large to extract."));
                    return false;
                }
                stream.Position = 0;
                bytes = new byte[checked((int)validation.ActualLength)];
                stream.ReadExactly(bytes);
            }
            catch (IOException)
            {
                PrintCartridgeValidationFailure(new CartridgeValidationResult(
                    CartridgeValidationStatus.ReadError, default, null, 0, null, null));
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                PrintCartridgeValidationFailure(new CartridgeValidationResult(
                    CartridgeValidationStatus.ReadError, default, null, 0, null, null));
                return false;
            }
            catch (NotSupportedException)
            {
                PrintCartridgeValidationFailure(new CartridgeValidationResult(
                    CartridgeValidationStatus.ReadError, default, null, 0, null, null));
                return false;
            }

            RomHeader header = Read.ReadStruct<RomHeader>(bytes);
            CartridgeIdentity identity = validation.Identity!;
            if (!identity.MatchesHeader(validation.Header)
                || !String.Equals(identity.GameCode, header.GameCode.MarshalString(),
                    StringComparison.OrdinalIgnoreCase)
                || identity.Revision != header.Version)
            {
                // The validated bytes and the parsed extraction header must
                // agree. A discrepancy here is a malformed image, not a
                // reason to bypass the catalog.
                PrintCartridgeValidationFailure(new CartridgeValidationResult(
                    CartridgeValidationStatus.Unsupported, validation.Header, identity,
                    validation.ActualLength, validation.ActualSha256,
                    "The validated cartridge header could not be parsed consistently."));
                return false;
            }
            bool isFh = identity.Family == CartridgeFamily.FirstHunt;
            Paths.UpdatePaths();
            if (!replaceConfiguredPaths && !OperatingSystem.IsAndroid() && File.Exists("paths.txt"))
            {
                if ((!isFh && !String.IsNullOrWhiteSpace(Paths.FileSystem))
                    || (isFh && !String.IsNullOrWhiteSpace(Paths.FhFileSystem)))
                {
                    Console.Write($"A path has already been specified for {(isFh ? "FH" : "MPH")} files. " +
                        $"Do you want to update it? (y/n) ");
                    string input = (Console.ReadLine() ?? "").Trim().ToLower();
                    if (input != "y" && input != "yes")
                    {
                        return false;
                    }
                }
            }
            string rootName = $"{header.GameCode.MarshalString()}{header.Version}";
            ExtractRomFs(header, bytes, rootName, hasArchives: !isFh);
            ExtractRomData(rootName);
            string newPath;
            if (isFh)
            {
                newPath = Path.GetFullPath(Paths.Combine("files", rootName, "data"));
            }
            else
            {
                newPath = Path.GetFullPath(Paths.Combine("files", rootName));
            }
            Paths.SetPath(rootName, newPath);
            var lines = new List<string>();
            lines.Add(ContentFormatVersion.Current.ToString());
            lines.Add($"{Ver.AMFE0}={Paths.AllPaths[Ver.AMFE0]}");
            lines.Add($"{Ver.AMFP0}={Paths.AllPaths[Ver.AMFP0]}");
            lines.Add($"{Ver.A76E0}={Paths.AllPaths[Ver.A76E0]}");
            lines.Add($"{Ver.AMHE0}={Paths.AllPaths[Ver.AMHE0]}");
            lines.Add($"{Ver.AMHE1}={Paths.AllPaths[Ver.AMHE1]}");
            lines.Add($"{Ver.AMHP0}={Paths.AllPaths[Ver.AMHP0]}");
            lines.Add($"{Ver.AMHP1}={Paths.AllPaths[Ver.AMHP1]}");
            lines.Add($"{Ver.AMHJ0}={Paths.AllPaths[Ver.AMHJ0]}");
            lines.Add($"{Ver.AMHJ1}={Paths.AllPaths[Ver.AMHJ1]}");
            lines.Add($"{Ver.AMHK0}={Paths.AllPaths[Ver.AMHK0]}");
            lines.Add($"Export={Paths.AllPaths["Export"]}");
            File.WriteAllText("paths.txt", String.Join(Environment.NewLine, lines));
            Nop();
            return true;
        }

        private static void PrintCartridgeValidationFailure(CartridgeValidationResult result)
        {
            switch (result.Status)
            {
                case CartridgeValidationStatus.HeaderValidWrongHash:
                    string wrongHashName = result.Identity?.DisplayName ?? "This cartridge";
                    PrintExit($"{wrongHashName} was detected, but the cartridge image does not match the "
                        + "supported retail revision.\n\nThe file may be modified, trimmed, corrupted, "
                        + "or dumped incorrectly.\nNo files were extracted.");
                    break;
                case CartridgeValidationStatus.InvalidLength:
                    string expected = result.Identity == null
                        ? "the expected cartridge image length"
                        : $"the supported {result.Identity.VariantCode} image length of {result.Identity.Size} bytes";
                    PrintExit($"The cartridge image does not match {expected} (received {result.ActualLength} bytes).\n"
                        + "No files were extracted.");
                    break;
                case CartridgeValidationStatus.ReadError:
                    PrintExit("Project Prime could not read this cartridge image.\nNo files were extracted.");
                    break;
                default:
                    PrintExit("This cartridge image is not a supported Metroid Prime Hunters or First Hunt release.\n"
                        + "No files were extracted.");
                    break;
            }
        }

        /// <summary>
        /// Serializes setup across processes that share an install directory. The lock file is
        /// intentionally persistent: deleting a lock file while another process is waiting on it
        /// can create two independently locked files at the same path.
        /// </summary>
        internal static IDisposable? TryAcquireSetupLock(string? lockPath = null)
        {
            try
            {
                return new FileStream(Path.GetFullPath(lockPath ?? SetupLockFileName),
                    FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }

        private static void ExtractRomData(string rootName)
        {
            RuntimeData.RomDataValues? font = RuntimeData.GetFontModel(rootName);
            if (font == null)
            {
                return;
            }
            byte[] bytes = File.ReadAllBytes(Paths.Combine("files", rootName, "_bin", font.File));
            File.WriteAllBytes(Paths.Combine("files", rootName, @"models\hudfont_Model.bin"),
                bytes[font.Offset..(font.Offset + font.Size)]);
        }

        private static void ExtractRomFs(RomHeader header, byte[] bytes, string rootName, bool hasArchives)
        {
            Debug.Assert(header.FntOffset > 0 && header.FatSize > 0);
            Debug.Assert(header.FatOffset > 0 && header.FatSize > 0 && header.FatSize % 8 == 0);
            DirTableEntry dirStart = Read.DoOffset<DirTableEntry>(bytes, header.FntOffset);
            IReadOnlyList<DirTableEntry> entries = Read.DoOffsets<DirTableEntry>(bytes, header.FntOffset, dirStart.DirNum);
            var fileOffsets = new List<(int, int)>();
            IReadOnlyList<uint> addresses = Read.DoOffsets<uint>(bytes, header.FatOffset, header.FatSize / 4);
            for (int i = 0; i < addresses.Count; i += 2)
            {
                fileOffsets.Add(((int)addresses[i], (int)addresses[i + 1]));
            }
            void PopulateDir(DirInfo dir)
            {
                DirTableEntry entry = entries[(int)dir.Index];
                uint offset = header.FntOffset + entry.Offset;
                ushort fileIndex = entry.FirstFileIndex;
                byte type = 1;
                while (type != 0)
                {
                    type = bytes[offset];
                    offset++;
                    if (type >= 1 && type <= 127)
                    {
                        int length = type;
                        string name = Read.ReadString(bytes, offset, length);
                        offset += (uint)length;
                        dir.Files.Add(new FileInfo(name, fileIndex++));
                    }
                    else if (type >= 129 && type <= 255)
                    {
                        int length = type - 128;
                        string name = Read.ReadString(bytes, offset, length);
                        offset += (uint)length;
                        ushort id = Read.SpanReadUshort(bytes, offset);
                        offset += sizeof(ushort);
                        dir.Subdirectories.Add(new DirInfo(name, id - 0xF000u));
                    }
                }
                foreach (DirInfo subdir in dir.Subdirectories)
                {
                    PopulateDir(subdir);
                }
            }
            void WriteFiles(DirInfo dir, string path)
            {
                Console.WriteLine($"Writing {path}...");
                Directory.CreateDirectory(path);
                foreach (FileInfo file in dir.Files)
                {
                    (int start, int end) = fileOffsets[(int)file.Index];
                    Debug.Assert(start > 0 && end > start);
                    File.WriteAllBytes(Paths.Combine(path, file.Name), bytes[start..end]);
                }
                foreach (DirInfo subdir in dir.Subdirectories)
                {
                    WriteFiles(subdir, Paths.Combine(path, subdir.Name));
                }
            }
            var root = new DirInfo(rootName, index: 0);
            PopulateDir(root);
            WriteFiles(root, Paths.Combine("files", root.Name));
            if (hasArchives)
            {
                foreach (string path in Directory.EnumerateFiles(Paths.Combine("files", root.Name, "archives")))
                {
                    if (Path.GetExtension(path).ToLower() == ".arc")
                    {
                        ExtractArchive(path);
                    }
                }
                Console.WriteLine("Converting sound_data.sdat...");
                string sdatDest = Paths.Combine("files", root.Name, "_seq");
                Directory.CreateDirectory(sdatDest);
                ConvertSdat(Paths.Combine("files", root.Name, "data", "sound", "sound_data.sdat"), sdatDest);
            }
            string ftcDir = Paths.Combine("files", root.Name, "ftc");
            Directory.CreateDirectory(ftcDir);
            byte[] WriteFile(string name, int offset, int size)
            {
                byte[] fileBytes = bytes[offset..(offset + size)];
                File.WriteAllBytes(Paths.Combine(ftcDir, name), fileBytes);
                return fileBytes;
            }
            WriteFile("arm9.bin", header.ARM9Offset, header.ARM9Size);
            WriteFile("arm7.bin", header.ARM7Offset, header.ARM7Size);
            WriteFile("fat.bin", (int)header.FatOffset, (int)header.FatSize);
            WriteFile("fnt.bin", (int)header.FntOffset, (int)header.FntSize);
            WriteFile("banner.bin", header.BannerOffset, 0x840);
            byte[] overlayInfo = WriteFile("y9.bin", header.Overlay9Offset, header.Overlay9Size);
            Debug.Assert(overlayInfo.Length % 32 == 0);
            for (int i = 0; i < overlayInfo.Length / 32; i++)
            {
                var items = new List<int>();
                for (int j = 0; j < 8; j++)
                {
                    int start = i * 32 + j * 4;
                    byte[] value = overlayInfo[start..(start + 4)];
                    items.Add(BitConverter.ToInt32(value));
                }
                int overlayId = items[0];
                int fileId = items[6];
                (int overlayStart, int overlayEnd) = fileOffsets[fileId];
                Debug.Assert(overlayStart > 0 && overlayEnd > overlayStart);
                File.WriteAllBytes(Paths.Combine(ftcDir, $"overlay9_{overlayId}"), bytes[overlayStart..overlayEnd]);
            }
            string ftcDest = Paths.Combine("files", root.Name, "_bin");
            Directory.CreateDirectory(ftcDest);
            foreach (string path in Directory.EnumerateFiles(ftcDir))
            {
                string filename = Path.GetFileName(path);
                if (filename == "arm9.bin" || filename.StartsWith("overlay9_"))
                {
                    Console.WriteLine($"Decompressing {filename}...");
                    LZBackward.Decompress(path, Paths.Combine(ftcDest, filename));
                }
            }
            Nop();
        }

        private static void ConvertSdat(string inputPath, string outputDir)
        {
            ReadOnlySpan<byte> sdatBytes = File.ReadAllBytes(inputPath);
            var finalSDAT = new SDAT();
            int sdatNumber = 1;
            var sdat = new SDAT();
            sdat.Read(sdatNumber.ToString(), sdatBytes);
            finalSDAT += sdat;
            finalSDAT.FixOffsetsAndSizes();
            using var memoryOwner = MemoryOwner<byte>.Allocate((int)finalSDAT.Size);
            finalSDAT.Write(memoryOwner.Span);
            var seqEntries = finalSDAT.INFOSection.SEQRecord.Entries;
            string ncsflibFilename = "mph.ncsflib";
            NCSFCommon.NCSF.MakeNCSF(Paths.Combine(outputDir, ncsflibFilename), [], memoryOwner.Span);
            NCSFCommon.TagList tags = [("_lib", ncsflibFilename), ("utf8", "1"), ("ncsfby", "MphRead")];
            AlbumGain albumGain = new();
            Dictionary<uint, NCSFCommon.TagList> fileTags = new(seqEntries.Length);
            for (uint i = 0, count = (uint)seqEntries.Length; i < count; ++i)
            {
                (uint offset, INFOEntrySEQ? entry) = seqEntries[(int)i];
                if (offset != 0 && entry is not null)
                {
                    if (entry.SSEQ!.Filename!.StartsWith("SSEQ"))
                    {
                        entry.SSEQ!.Filename = $"{i:X4} - {entry.SSEQ!.Filename}";
                    }
                    string minincsfFilename = $"{entry.SSEQ!.Filename}.minincsf";
                    var thisTags = tags.Clone();
                    string fullFilename = entry.FullFilename(sdatNumber > 1);
                    thisTags.AddOrReplace(("origFilename", entry.SSEQ.OriginalFilename!));
                    if (sdatNumber > 1)
                    {
                        thisTags.AddOrReplace(("origSDAT", entry.SDATNumber));
                    }
                    fileTags[i] = thisTags;
                }
            }
            for (uint i = 0, count = (uint)seqEntries.Length; i < count; ++i)
            {
                (uint offset, INFOEntrySEQ? entry) = seqEntries[(int)i];
                if (offset != 0 && entry is not null)
                {
                    string minincsfFilename = $"{entry.SSEQ!.Filename}.minincsf";
                    var thisTags = fileTags[i];
                    NCSFCommon.NCSF.MakeNCSF(Paths.Combine(outputDir, minincsfFilename), BitConverter.GetBytes(i), [], thisTags);
                }
            }
        }

        private static void PrintExit(string message)
        {
            Console.WriteLine(message);
            if (!OperatingSystem.IsAndroid() && !Console.IsInputRedirected)
            {
                Console.WriteLine("Press any key to exit...");
                Console.ReadKey();
            }
        }

        private static void Nop()
        {
        }

        public class DirInfo
        {
            public string Name { get; }
            public uint Index { get; }
            public List<DirInfo> Subdirectories { get; set; } = new List<DirInfo>();
            public List<FileInfo> Files { get; set; } = new List<FileInfo>();

            public DirInfo(string name, uint index)
            {
                Name = name;
                Index = index;
            }
        }

        public class FileInfo
        {
            public string Name { get; }
            public uint Index { get; }

            public FileInfo(string name, uint index)
            {
                Name = name;
                Index = index;
            }
        }

        public readonly struct DirTableEntry
        {
            public readonly uint Offset;
            public readonly ushort FirstFileIndex;
            public readonly ushort DirNum;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public readonly struct RomHeader
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 12)]
            public readonly char[] Title;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
            public readonly char[] GameCode;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
            public readonly char[] MakerCode;
            public readonly byte UnitCode;
            public readonly byte Seed;
            public readonly byte Capacity;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 7)]
            public readonly char[] Reserved15;
            public readonly byte Reserved16;
            public readonly byte Region;
            public readonly byte Version;
            public readonly byte AutoStart;
            public readonly int ARM9Offset;
            public readonly int ARM9EntryAddress;
            public readonly int ARM9RamAddress;
            public readonly int ARM9Size;
            public readonly int ARM7Offset;
            public readonly int ARM7EntryAddress;
            public readonly int ARM7RamAddress;
            public readonly int ARM7Size;
            public readonly uint FntOffset;
            public readonly uint FntSize;
            public readonly uint FatOffset;
            public readonly uint FatSize;
            public readonly int Overlay9Offset;
            public readonly int Overlay9Size;
            public readonly int Overlay7Offset;
            public readonly int Overlay7Size;
            public readonly uint ReadFlags;
            public readonly uint InitFlags;
            public readonly int BannerOffset;
        }

    }
}
