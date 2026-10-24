using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using MphRead.Mods.Network;

namespace MphRead.NetTest;

// Production detached checkpoint reader, synthetic compressed bytes only.
// No game assets, world restoration, display or source-recording parity claim.
internal static class ReplayCheckpointPrefetchTests
{
    internal static int Run()
    {
        int checks = 0;
        void Check(bool value, string message) { if (!value) throw new InvalidDataException(message); checks++; }
        string path = Path.Combine(Path.GetTempPath(), "prime-checkpoint-read-" + Guid.NewGuid().ToString("N"));
        try
        {
            byte[] raw = Enumerable.Range(0, 8192).Select(i => (byte)(i * 17)).ToArray();
            using var compressed = new MemoryStream();
            using (var zip = new DeflateStream(compressed, CompressionLevel.Fastest, true)) zip.Write(raw);
            File.WriteAllBytes(path, new byte[17].Concat(compressed.ToArray()).ToArray());
            var index = new ReplayCheckpointIndex(300, 17, (int)compressed.Length, raw.Length, ReplayFormatV3.Crc(raw));
            ReplayReaderV3 Reader(FileStream stream, ReplayCheckpointIndex entry)
            {
                // Isolate the byte-reader contract without inventing a valid WorldK.
                // Footer membership is supplied explicitly; archive parsing is covered separately.
                var reader = (ReplayReaderV3)RuntimeHelpers.GetUninitializedObject(typeof(ReplayReaderV3));
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                typeof(ReplayReaderV3).GetField("_stream", flags)!.SetValue(reader, stream);
                typeof(ReplayReaderV3).GetField("_checkpoints", flags)!.SetValue(reader, new List<ReplayCheckpointIndex> { entry });
                return reader;
            }
            using (var source = File.OpenRead(path))
            {
                var reader = Reader(source, index); var read = reader.PrepareCheckpointRead(index);
                source.Position = 5;
                Check(Task.Run(read).GetAwaiter().GetResult().SequenceEqual(raw), "worker decodes exact CRC-validated checkpoint bytes");
                Check(source.Position == 5, "worker never changes sequential packet cursor");
                bool reused = false; try { read(); } catch (InvalidOperationException) { reused = true; }
                Check(reused, "native handle lease has exactly one consumer");
                bool unknown = false; try { reader.PrepareCheckpointRead(index with { Frame = 301 }); } catch (InvalidDataException) { unknown = true; }
                Check(unknown, "unindexed checkpoint cannot acquire a read lease");
            }
            Func<byte[]> retiredRead;
            using (var source = File.OpenRead(path)) retiredRead = Reader(source, index).PrepareCheckpointRead(index);
            Check(Task.Run(retiredRead).GetAwaiter().GetResult().SequenceEqual(raw), "queued native read lease survives source Scene/reader retirement");
            using (var source = File.OpenRead(path))
            {
                var corrupt = index with { Crc = index.Crc ^ 1 };
                bool refused = false;
                try { Task.Run(Reader(source, corrupt).PrepareCheckpointRead(corrupt)).GetAwaiter().GetResult(); }
                catch (InvalidDataException) { refused = true; }
                Check(refused, "worker rejects corrupt checkpoint payload without restoring a world");
            }
            File.Delete(path); Check(!File.Exists(path), "all native read leases retire on success and error");
            Console.WriteLine($"PASS {checks} production detached checkpoint read/CRC/cursor/lifetime assertions; synthetic payload, no Scene or GL");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { File.Delete(path); }
    }
}
