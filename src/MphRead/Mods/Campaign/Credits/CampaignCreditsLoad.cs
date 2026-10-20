using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using MphRead.Mods.Import;

namespace MphRead.Mods.Campaign;

internal readonly record struct CampaignCreditsSourceIdentity(string Root, string Edition, Language Language);

/// <summary>Bounded detached source admission. The caller owns scheduling, cancellation and publication epochs.</summary>
internal static class CampaignCreditsLoad
{
    internal const int MaxReadBytes = 4 * 1024 * 1024;
    private const int WidthOffset = 0xC020C, VerticalOffset = 0xC03EC, EmbeddedFontOffset = 0xC7F5C;
    internal sealed record Input(string Path, int Bytes, string Sha256);
    private static readonly Input[] Entries =
    {
        new("logo_screen/MAYA/flare2_Model.bin", 2564, "c9cc6ec7c38d97c0b56fa2724fc6e6e070022497e346c06344351e3b58df9c6c"),
        new("logo_screen/MAYA/flare3_Model.bin", 2564, "efeff3196f27de722818892607f5955894b1538d174a6c53ade478f7f28e4a55"),
        new("logo_screen/MAYA/logo2_Model.bin", 42640, "7682bffcfe9fec8300bf4a44d5ebcd16f5431383ebddc4d6ac0cdf381ec897d6"),
        new("logo_screen/MAYA/logo3_Model.bin", 38632, "2393ea89063137312d71f86edc9f5cd6cbae526ad320863bda7636718f871acd"),
        new("main menu/toplogoR_Model.bin", 900, "d5133b37d5d9dedb1104a13c150cf94a160675ee28f025c4a6833f4e9b7e45ce"),
        new("tostart/logofinal_Model.bin", 54100, "a0ce0bcb402b2952e51fbaf586583c6c321d7e00dc480a1a97f8af1aecebfabc"),
        new("gameoptions/ok_Model.bin", 1684, "2724cb0c747292f8cd55a9abbb4644e382e706a0367dcc1b187fdb0915609a99"),
        new("models/hudfont_Model.bin", 33412, "3f89b432c47b96e52d0837fdd1fa2eb1165df9309665640a0bea02208300e7ef"),
        new("gameoptions/ok_Hidden_Idle_Anim.bin", 304, "193971d218b7342e45052fcabd0910d0d66095fb34d9b4a995a758f7ce114b9e"),
        new("gameoptions/ok_Idle_Anim.bin", 296, "998d81b6885fac0cf8e553677db59d5f3814202ce2ae6de0f91a026e738ddd0c"),
        new("gameoptions/ok_Idle_Hidden_Anim.bin", 300, "175bb330503edbaa513a99cbb37007b705dd93b73efa0f5deed6b4ede3caea29"),
        new("gameoptions/ok_Selected_Anim.bin", 296, "998d81b6885fac0cf8e553677db59d5f3814202ce2ae6de0f91a026e738ddd0c"),
        new("gameoptions/ok_Selected_Hidden_Anim.bin", 320, "a0dabc9b0e7152f4aada1ba5bb3dc3f92b3170b7c1f115812bbbef222b01a443"),
        new("logo_screen/MAYA/flare2_Hidden_Idle_Anim.bin", 1352, "a1c9af11acafd6ed560f05938cf927cba21f5d388335d8885db70f53872d3d8a"),
        new("logo_screen/MAYA/flare2_Idle_Hidden_Anim.bin", 400, "9fd3cbdb70b524ea11c70f4443e11e51a7fa65715ab86ba42cebbf6f3b52cc3d"),
        new("logo_screen/MAYA/flare3_Hidden_Idle_Anim.bin", 1352, "f3dca70097f7fe550734df84e3153337c9b99b090fc4762d47a20de7700f6ca7"),
        new("logo_screen/MAYA/flare3_Idle_Hidden_Anim.bin", 400, "728d246aa884c3403dd3e2dea60206bd34c1db5197a17b6c108737be73a3e361"),
        new("logo_screen/MAYA/logo2_Hidden_Idle_Anim.bin", 2400, "1d87b65e1bd1cf5a111a7e26bbb6254f391cdde44d7eb6b1ac45f1bc581d7e10"),
        new("logo_screen/MAYA/logo2_Idle_Hidden_Anim.bin", 1636, "f34f6366534cbc119a418902e221d99819b9a9e49c910ce79b74d06dc8369376"),
        new("logo_screen/MAYA/logo3_Hidden_Idle_Anim.bin", 2400, "eddd200c2f864ebfe1fc751986e023b9940f61f7ac8dd6cb82533b63e55927f4"),
        new("logo_screen/MAYA/logo3_Idle_Hidden_Anim.bin", 1636, "0c33ea91069518c193563deca2d44dd88c4e2d632d09eb3377620f2077d83af8"),
        new("main menu/toplogoR_Hidden_Idle_Anim.bin", 352, "fd3ae105e15af2b57e672664454f79b4d35c3b3785a34b43559cedf1f1f6e21e"),
        new("main menu/toplogoR_Idle_Anim.bin", 296, "99f3e05d430d4526dd4f2e37ab10db0b6d076629233028de2e5c6ab865c1af49"),
        new("main menu/toplogoR_Idle_Hidden_Anim.bin", 344, "09c4d9edfdd3c33f4feaba369ed2433b097a7b3be6ee33129eff4430cab192bc"),
        new("tostart/logofinal_Hidden_Idle_Anim.bin", 4692, "58b44907bf0731b67d7299b8227f215140745e72517a2ff853aa91510e30b06b"),
        new("tostart/logofinal_Idle_Anim.bin", 1256, "922ba68c9412dd4a56289aeed390c6644274743f5cbc76ebb96b693baf85b72f"),
        new("_bin/arm9.bin", 909912, "1b70b078ec1b026004c89272acf619e7510e60fd294aa776b7bda48733ae850b"),
        new("sourceimages/bg/good1.bin", 49664, "257122d6e9cacfec2e67ce5d2cacb0fec2e77580dc24478c4e3950dd93b61783"),
        new("sourceimages/bg/good2.bin", 49664, "46c7fca5db4627bb5149ff3bf362a52cac2ba81aeba8fdfa192a170d7c5299ef"),
        new("sourceimages/bg/bad1.bin", 49664, "fec414ff7557c6194ffe041e10d4d2d2da22414438052fae3f398af5702418f2"),
        new("sourceimages/bg/bad2.bin", 49664, "ef92fd690be9b62556c0c3f1b1e90642f8f6f5967c4886f172ac1edbfdfd495f"),
        new("frontend/metroidhunters.bin", 512232, "1140e510b420a9da5f4547154f0c164511a697ccaf5a249c0652625806a9d059"),
        new("frontend/metroidhunters_text_en.bin", 30604, "742e6819be3e9f8b55be65e66cc8a4d9a8aefc0a93d2f1e4813bb663d262d114"),
    };
    private static readonly IReadOnlyList<Input> Manifest = Array.AsReadOnly(Entries);
    private static readonly IReadOnlyDictionary<string, Input> ByPath = Entries.ToDictionary(p => p.Path, StringComparer.Ordinal);
    internal static IReadOnlyList<Input> Inputs => Manifest;
    internal static bool Supports(CampaignCreditsSourceIdentity source) => source.Edition == "AMHE1"
        && source.Language == Language.English && !String.IsNullOrWhiteSpace(source.Root);

    internal static CampaignCreditsCatalog Read(CampaignCreditsSourceIdentity source, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if (!Supports(source)) throw new NotSupportedException("Original credits source profile is unsupported.");
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(source.Root));
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException("Original credits source is unavailable.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        byte[] ReadEntry(string path)
        {
            cancellation.ThrowIfCancellationRequested();
            if (!ByPath.TryGetValue(path, out var pin) || !seen.Add(path))
                throw new InvalidDataException("Original credits source inventory is unsupported.");
            return ReadPinned(root, pin, cancellation);
        }
        T Admit<T>(string path, Func<byte[], T> admit)
        {
            byte[] bytes = ReadEntry(path);
            try { cancellation.ThrowIfCancellationRequested(); T result = admit(bytes); cancellation.ThrowIfCancellationRequested(); return result; }
            finally { Array.Clear(bytes); }
        }
        byte[] marm = ReadEntry("frontend/metroidhunters.bin");
        CampaignCreditsSource.Document document;
        try
        {
            byte[] text = ReadEntry("frontend/metroidhunters_text_en.bin");
            try { document = CampaignCreditsSource.Read(marm, text); }
            finally { Array.Clear(text); }
        }
        finally { Array.Clear(marm); }
        cancellation.ThrowIfCancellationRequested();
        var models = new Dictionary<string, CampaignCreditsModelSource.Snapshot>(StringComparer.Ordinal);
        foreach (string path in document.Widgets.Values.Select(w => w.Files[0].Path!).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
            models.Add(path, Admit(path, bytes => CampaignCreditsModelSource.Read(bytes)));
        var animations = new Dictionary<string, CampaignCreditsAnimationBinding.Snapshot>(StringComparer.Ordinal);
        foreach (string path in document.Widgets.Values.Where(w => w.Files.Count == 2).Select(w => w.Files[1].Path!).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            string[] owners = document.Widgets.Values.Where(w => w.Files.Count == 2 && w.Files[1].Path == path)
                .Select(w => w.Files[0].Path!).Distinct(StringComparer.Ordinal).ToArray();
            if (owners.Length != 1 || !models.TryGetValue(owners[0], out var model))
                throw new InvalidDataException("Original credits animation source is ambiguous.");
            animations.Add(path, Admit(path, bytes => CampaignCreditsAnimationBinding.Read(model,
                CampaignCreditsAnimationDuration.Read(bytes), bytes)));
        }
        byte[] fontBytes = ReadEntry("models/hudfont_Model.bin");
        CampaignCreditsFontSource.Snapshot font;
        try
        {
            byte[] arm9 = ReadEntry("_bin/arm9.bin");
            try
            {
                if (!arm9.AsSpan(EmbeddedFontOffset, fontBytes.Length).SequenceEqual(fontBytes))
                    throw new InvalidDataException("Original credits font identity differs from its source profile.");
                font = CampaignCreditsFontSource.Read(CampaignCreditsModelSource.Read(fontBytes),
                    arm9.AsSpan(WidthOffset, CampaignCreditsFontSource.GlyphCount),
                    arm9.AsSpan(VerticalOffset, CampaignCreditsFontSource.GlyphCount));
            }
            finally { Array.Clear(arm9); }
        }
        finally { Array.Clear(fontBytes); }
        CampaignCreditsBackgroundSource.Snapshot Background(string path) => Admit(path, bytes => CampaignCreditsBackgroundSource.Read(bytes));
        var backgrounds = new CampaignCreditsCatalog.Backgrounds(Background("sourceimages/bg/good1.bin"),
            Background("sourceimages/bg/good2.bin"), Background("sourceimages/bg/bad1.bin"), Background("sourceimages/bg/bad2.bin"));
        cancellation.ThrowIfCancellationRequested();
        if (seen.Count != Entries.Length) throw new InvalidDataException("Original credits source inventory is incomplete.");
        var catalog = new CampaignCreditsCatalog(document, models, animations, font, backgrounds);
        cancellation.ThrowIfCancellationRequested();
        return catalog;
    }

    // The manifest is fixed in production; this same bounded file boundary also
    // admits invented fixtures. No global Paths, Model/Font cache or output IO.
    internal static byte[] ReadPinned(string root, Input input, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(input);
        string relative = input.Path;
        if (input.Bytes is < 1 or > MaxReadBytes || input.Sha256 is not { Length: 64 } || !input.Sha256.All(Uri.IsHexDigit)
            || String.IsNullOrEmpty(relative) || relative.Length > 260 || Path.IsPathRooted(relative)
            || relative.Contains('\0') || relative.Contains('\\') || relative.Contains(':') || relative.Split('/').Any(p => p.Length == 0 || p is "." or ".."))
            throw new InvalidDataException("Original credits source file identity is invalid.");
        string path = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        LocalImportBundle.CheckPath(path, root);
        if ((File.GetAttributes(path) & FileAttributes.Directory) != 0)
            throw new InvalidDataException("Original credits input must be a regular source file.");
        using var stream = CampaignCreditsSourceFile.Open(path, cancellation);
        if (stream.Length != input.Bytes) throw new InvalidDataException("Original credits source extent differs from its profile.");
        byte[] bytes = new byte[input.Bytes];
        try
        {
            int read = 0;
            while (read < bytes.Length)
            {
                cancellation.ThrowIfCancellationRequested();
                int count = stream.Read(bytes, read, Math.Min(65536, bytes.Length - read));
                if (count == 0) throw new EndOfStreamException("Original credits source was truncated.");
                read += count;
            }
            if (stream.ReadByte() != -1 || stream.Length != input.Bytes)
                throw new InvalidDataException("Original credits source changed during its read.");
            LocalImportBundle.CheckPath(path, root);
            cancellation.ThrowIfCancellationRequested();
            if (!String.Equals(Convert.ToHexStringLower(SHA256.HashData(bytes)), input.Sha256, StringComparison.Ordinal))
                throw new InvalidDataException("Original credits source identity differs from its supported profile.");
            cancellation.ThrowIfCancellationRequested();
            return bytes;
        }
        catch { Array.Clear(bytes); throw; }
    }
}
