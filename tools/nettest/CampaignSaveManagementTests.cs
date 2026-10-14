using System;
using System.IO;
using System.Linq;
using MphRead.Mods.Launcher;

namespace MphRead.NetTest;

// CAMP-01: the imported file-select operations must preserve the exact native
// Project Prime save bytes and never redirect Menu.SaveSlot or touch gameplay.
internal static class CampaignSaveManagementTests
{
    internal static int Run()
    {
        string previousDirectory = Directory.GetCurrentDirectory();
        byte previousSlot = Menu.SaveSlot;
        string root = Path.Combine(Path.GetTempPath(), "prime-campaign-save-" + Guid.NewGuid().ToString("N"));
        int checks = 0;
        void Check(bool condition, string label)
        {
            if (!condition)
                throw new InvalidOperationException("CAMPAIGN SAVE FAIL: " + label);
            checks++;
            Console.WriteLine("CAMPAIGN SAVE PASS " + label);
        }

        void Reject<T>(Action action, string label) where T : Exception
        {
            bool rejected = false;
            try { action(); }
            catch (T) { rejected = true; }
            Check(rejected, label);
        }

        try
        {
            Directory.CreateDirectory(Path.Combine(root, "Savedata"));
            Directory.SetCurrentDirectory(root);
            // Exact byte preservation, not a lossy conversion to SecondHunt's
            // distinct CampaignSaveFile envelope. This intentionally contains
            // bytes the save serializer cannot reinterpret or normalize.
            byte[] original = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray();
            byte[] destination = new byte[] { 83, 65, 86, 69, 45, 50, 0, 255 };
            string first = Path.Combine("Savedata", "save001.json");
            string second = Path.Combine("Savedata", "save002.json");
            string third = Path.Combine("Savedata", "save003.json");
            File.WriteAllBytes(first, original);
            File.WriteAllBytes(second, destination);

            Menu.SaveSlot = 3;
            Check(AdventureSave.HasFile(1) && AdventureSave.HasFile(2) && !AdventureSave.HasFile(3),
                "slot discovery uses native paths");
            AdventureSave.Copy(1, 2);
            Check(File.ReadAllBytes(first).SequenceEqual(original), "copy leaves source unchanged");
            Check(File.ReadAllBytes(second).SequenceEqual(original), "copy preserves every source byte");
            Check(File.ReadAllBytes(second + ".bak").SequenceEqual(destination),
                "destination bytes archived before replacement");
            Check(Menu.SaveSlot == 3, "copy never redirects active story slot");

            AdventureSave.Copy(1, 3);
            Check(File.ReadAllBytes(third).SequenceEqual(original) && !File.Exists(third + ".bak"),
                "copy into empty slot creates no spurious backup");
            AdventureSave.Copy(1, 2);
            Check(File.ReadAllBytes(second + ".bak").SequenceEqual(destination)
                && File.ReadAllBytes(second + ".bak001").SequenceEqual(original),
                "repeated copy never overwrites previous backup");

            Check(AdventureSave.Archive(2) && !AdventureSave.HasFile(2),
                "archive empties only selected slot");
            Check(File.ReadAllBytes(second + ".bak002").SequenceEqual(original),
                "archive after multiple copies retains all previous backups");
            Check(!AdventureSave.Archive(2), "archiving an empty slot is a no-op");
            Check(AdventureSave.HasFile(1) && AdventureSave.HasFile(3),
                "other story saves remain available");

            Reject<ArgumentOutOfRangeException>(() => AdventureSave.HasFile(0),
                "slot zero cannot be managed");
            Reject<ArgumentOutOfRangeException>(() => AdventureSave.Archive(4),
                "slot four cannot be managed");
            Reject<ArgumentException>(() => AdventureSave.Copy(1, 1),
                "copy-to-self is rejected");
            Reject<ArgumentOutOfRangeException>(() => AdventureSave.Copy(1, 4),
                "invalid copy destination is rejected");
            Reject<FileNotFoundException>(() => AdventureSave.Copy(2, 3),
                "missing source is rejected without touching destination");
            Check(File.ReadAllBytes(third).SequenceEqual(original),
                "failed copy leaves destination intact");

            // A directory occupying a slot path causes the publication to
            // fail; the temporary copy must be removed and the source kept.
            Directory.CreateDirectory(second);
            Reject<IOException>(() => AdventureSave.Copy(1, 2),
                "failed destination publish is surfaced");
            Check(Directory.Exists(second) && File.ReadAllBytes(first).SequenceEqual(original),
                "failed publication leaves source and destination untouched");
            Check(Directory.GetFiles("Savedata", "*.copy-*").Length == 0,
                "staged copies cleaned after failure");
            Directory.Delete(second);

            Check(AdventureSave.Archive(1), "source save may be archived");
            Check(!File.Exists(first) && File.ReadAllBytes(first + ".bak").SequenceEqual(original),
                "archive retains raw bytes for recovery");
            Check(File.ReadAllBytes(third).SequenceEqual(original) && Menu.SaveSlot == 3,
                "archive does not disturb unrelated slot or active selection");

            Console.WriteLine($"CAMPAIGN SAVE summary checks={checks} native JSON untouched; no assets or GL required");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine("CAMPAIGN SAVE FAIL " + ex);
            return 1;
        }
        finally
        {
            Menu.SaveSlot = previousSlot;
            Directory.SetCurrentDirectory(previousDirectory);
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
