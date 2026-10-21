using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace MphRead.Mods.Render.Characters;

internal static partial class CharacterModelAssetCheck
{
    // Real filesystem identities, using only fresh synthetic directories. Keeping
    // retired directories alive prevents immediate inode/file-ID reuse from
    // weakening replacement checks.
    private static void RunSourceRootChecks()
    {
        int checks = 0;
        void Check(bool value, string label)
        {
            if (!value) throw new InvalidOperationException(label);
            checks++;
        }
        void Refuse(Action action, string label)
        {
            try { action(); }
            catch (InvalidDataException) { checks++; return; }
            catch (IOException) { checks++; return; }
            catch (UnauthorizedAccessException) { checks++; return; }
            throw new InvalidOperationException("Source root proof accepted " + label);
        }
        void Platform(bool macOS, Architecture architecture, int pointerBytes, bool windows, bool accepted)
        {
            bool actual = true;
            try { CharacterSourceRootWitness.RequireQualifiedPlatform(macOS, architecture, pointerBytes, windows); }
            catch (InvalidDataException) { actual = false; }
            Check(actual == accepted, $"Source root platform contract {macOS}/{windows}/{architecture}/{pointerBytes}.");
        }

        Platform(true, Architecture.Arm64, 8, false, true);
        Platform(false, Architecture.X64, 8, true, true);
        Platform(false, Architecture.X64, 8, false, false);
        Platform(true, Architecture.X64, 8, false, false);
        Platform(false, Architecture.Arm64, 8, true, false);
        Platform(false, Architecture.X86, 4, true, false);
        Platform(false, Architecture.X64, 4, true, false);
        Platform(true, Architecture.Arm64, 4, false, false);

        if (!CharacterSourceRootWitness.QualifiedForCurrentProcess)
        {
            Console.WriteLine($"[charactermodelcheck] source root platform PASS {checks}; filesystem checks skipped: "
                + CharacterSourceRootWitness.UnsupportedPlatformMessage);
            return;
        }

        string temporary = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "prime-source-root-check-" + Guid.NewGuid().ToString("N")));
        var aliases = new List<string>();
        Directory.CreateDirectory(temporary);
        string Make(string relative)
        {
            string result = Path.Combine(temporary, relative);
            Directory.CreateDirectory(result);
            return result;
        }
        void Alias(string link, string target)
        {
            if (OperatingSystem.IsWindows())
            {
                // Junctions need neither administrator rights nor Developer
                // Mode. Both paths are generated inside this test's own root.
                var start = new ProcessStartInfo("cmd.exe")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                start.ArgumentList.Add("/d");
                start.ArgumentList.Add("/c");
                start.ArgumentList.Add($"mklink /J \"{link}\" \"{target}\"");
                using var process = Process.Start(start) ?? throw new IOException("Junction fixture process did not start.");
                if (!process.WaitForExit(10000))
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit();
                    throw new IOException("Junction fixture process timed out.");
                }
                string diagnostic = process.StandardError.ReadToEnd();
                if (process.ExitCode != 0) throw new IOException("Junction fixture creation failed: " + diagnostic);
            }
            else Directory.CreateSymbolicLink(link, target);
            aliases.Add(link);
        }
        try
        {
            string stable = Make("stable/body");
            var stableProof = CharacterSourceRootWitness.Capture(stable);
            stableProof.RequireUnchanged(stable);
            stableProof.RequireUnchanged(stable + Path.DirectorySeparatorChar);
            checks += 2;
            File.WriteAllText(Path.Combine(stable, "synthetic.txt"), "directory identity survives ordinary child writes");
            stableProof.RequireUnchanged(stable);
            checks++;

            string missing = Path.Combine(temporary, "missing", "body");
            Refuse(() => CharacterSourceRootWitness.Capture(missing), "missing root and ancestor");
            string file = Path.Combine(temporary, "file-root");
            File.WriteAllText(file, "synthetic file, not a directory");
            Refuse(() => CharacterSourceRootWitness.Capture(file), "ordinary file root");

            string replaced = Make("replaced/body");
            var replacementProof = CharacterSourceRootWitness.Capture(replaced);
            Directory.Move(replaced, replaced + "-retired");
            Refuse(() => replacementProof.RequireUnchanged(replaced), "renamed root without replacement");
            Directory.CreateDirectory(replaced);
            Refuse(() => replacementProof.RequireUnchanged(replaced), "different directory at the same lexical root");
            var freshReplacement = CharacterSourceRootWitness.Capture(replaced);
            freshReplacement.RequireUnchanged(replaced);
            checks++;

            string ancestor = Make("ancestor/original");
            string descendant = Make("ancestor/original/body");
            var ancestorProof = CharacterSourceRootWitness.Capture(descendant);
            Directory.Move(ancestor, ancestor + "-retired");
            Refuse(() => ancestorProof.RequireUnchanged(descendant), "renamed ancestor");
            Directory.CreateDirectory(ancestor);
            Directory.Move(Path.Combine(ancestor + "-retired", "body"), descendant);
            // The leaf is the same directory object; only its ancestor changed.
            Refuse(() => ancestorProof.RequireUnchanged(descendant), "replacement ancestor retaining the original leaf");
            CharacterSourceRootWitness.Capture(descendant).RequireUnchanged(descendant);
            checks++;

            string firstTarget = Make("targets/first/body");
            string secondTarget = Make("targets/second/body");
            string alias = Path.Combine(temporary, "stable-alias");
            Alias(alias, Path.GetDirectoryName(firstTarget)!);
            string aliasedBody = Path.Combine(alias, "body");
            var aliasProof = CharacterSourceRootWitness.Capture(aliasedBody);
            aliasProof.RequireUnchanged(aliasedBody);
            File.WriteAllText(Path.Combine(firstTarget, "alias-sentinel.txt"), "stable alias target");
            aliasProof.RequireUnchanged(aliasedBody);
            checks += 2;
            Directory.Delete(alias); // Remove only the junction/symlink itself.
            Alias(alias, Path.GetDirectoryName(secondTarget)!);
            Refuse(() => aliasProof.RequireUnchanged(aliasedBody), "junction or symlink retarget");
            Check(File.ReadAllText(Path.Combine(firstTarget, "alias-sentinel.txt")) == "stable alias target",
                "Retarget fixture preserves the original target and its child.");
            CharacterSourceRootWitness.Capture(aliasedBody).RequireUnchanged(aliasedBody);
            checks++;

            Console.WriteLine($"[charactermodelcheck] source root real filesystem identity PASS {checks}; synthetic directories only");
        }
        finally
        {
            // Unlink before recursive cleanup so no alias can redirect cleanup.
            foreach (string alias in aliases)
                if (Directory.Exists(alias)) Directory.Delete(alias);
            string expectedParent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
            if (!String.Equals(Path.GetDirectoryName(temporary), expectedParent,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                throw new IOException("Synthetic source root cleanup escaped its temporary parent.");
            Directory.Delete(temporary, recursive: true);
        }
    }
}
