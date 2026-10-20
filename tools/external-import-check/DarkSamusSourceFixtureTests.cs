using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MphRead.Mods.Import;

internal static class DarkSamusSourceFixtureTests
{
    public static void Run()
    {
        Check(!ExternalCharacterCatalog.EnabledByDefault, "new characters disabled by default");
        var dark = ExternalCharacterCatalog.Find("darksamus");
        Check(dark != null && dark.BaseHunter == MphRead.Hunter.Samus && !dark.ChangesGameplay,
            "Dark Samus gets independent ID and vanilla Samus simulation");
        Check(ExternalCharacterCatalog.Find("trooper")?.ChangesGameplay == true,
            "Trooper has separate ability flag");
        Check(ExternalCharacterCatalog.AllowsSkin("darksamus", "MP2DarkSamus"), "Echoes skin");
        Check(ExternalCharacterCatalog.AllowsSkin("darksamus", "MP2DarkSamusFinal") && ExternalCharacterCatalog.All.Count==2,
            "Overcharged is a reserved cosmetic of the same Dark Samus character");
        Check(!ExternalCharacterCatalog.AllowsSkin("trooper", "MP2DarkSamusFinal"), "Overcharged cannot select another parent character");
        Check(!ExternalCharacterCatalog.AllowsSkin("darksamus", "../mp2"), "reject arbitrary asset identifier");

        byte[] ancs = Ancs();
        var character = RetroCharacterSource.FindCharacter(ancs, "Dark_Samus");
        Check(character.Cmdl == 0x11111111 && character.Cskr == 0x22222222 &&
            character.Cinf == 0x33333333, "ANCS character resource triplet");
        Expect<KeyNotFoundException>(() => RetroCharacterSource.FindCharacter(ancs, "Missing"), "unknown character");
        byte[] bad = (byte[])ancs.Clone(); Put32(bad, 4, uint.MaxValue);
        Expect<InvalidDataException>(() => RetroCharacterSource.FindCharacter(bad, "Dark_Samus"), "count bound");
        bad = (byte[])ancs.Clone(); Put32(bad, 8, 17);
        Expect<InvalidDataException>(() => RetroCharacterSource.FindCharacter(bad, "Dark_Samus"), "invalid character index");

        byte[] variants=new byte[128];
        Put32(variants,4,2);Put32(variants,8,0);Put16(variants,12,2);
        Encoding.ASCII.GetBytes("Dark_Samus\0").CopyTo(variants,14);
        Put32(variants,25,0x11111111);Put32(variants,29,0x22222222);Put32(variants,33,0x33333333);
        Put32(variants,64,1);Put16(variants,68,2);
        Encoding.ASCII.GetBytes("Battle_3\0").CopyTo(variants,70);
        Put32(variants,79,0x44444444);Put32(variants,83,0x55555555);Put32(variants,87,0x33333333);
        Check(RetroCharacterSource.FindCharacter(variants,"Dark_Samus").Cmdl==0x11111111
            && RetroCharacterSource.FindCharacter(variants,"Battle_3").Cmdl==0x44444444,
            "bounded multi-entry ANCS lookup keeps normal and Overcharged model identities distinct");
        byte[] cinf = Cinf();
        var skeleton = RetroCharacterSource.ReadSkeleton(cinf);
        Check(skeleton.Bones.Count == 1 && skeleton.Bones[10].Name == "Root" &&
            skeleton.Bones[10].X == 1f, "CINF bone and name");
        bad = (byte[])cinf.Clone(); Put32(bad, 0, uint.MaxValue);
        Expect<InvalidDataException>(() => RetroCharacterSource.ReadSkeleton(bad), "bone count cap");
        bad = (byte[])cinf.Clone();
        Put32(bad, 12, 0x7f800000);
        Expect<InvalidDataException>(() => RetroCharacterSource.ReadSkeleton(bad), "nonfinite bone");
    }

    private static byte[] Ancs()
    {
        var b = new byte[48];
        Put32(b, 4, 1); Put32(b, 8, 0); Put16(b, 12, 2);
        string name = "Dark_Samus";
        Encoding.ASCII.GetBytes(name).CopyTo(b, 14);
        int p = 14 + name.Length + 1;
        Put32(b, p, 0x11111111); Put32(b, p + 4, 0x22222222); Put32(b, p + 8, 0x33333333);
        return b;
    }
    private static byte[] Cinf()
    {
        var b = new byte[64];
        Put32(b, 0, 1);
        Put32(b, 4, 10); Put32(b, 8, uint.MaxValue);
        PutFloat(b, 12, 1f); PutFloat(b, 16, 2f); PutFloat(b, 20, 3f);
        Put32(b, 24, 1); Put32(b, 28, 10);
        Put32(b, 32, 1); Put32(b, 36, 10);
        Put32(b, 40, 1);
        Encoding.ASCII.GetBytes("Root\0").CopyTo(b, 44);
        Put32(b, 49, 10);
        return b;
    }

    private static void Put32(byte[] dst, int at, uint value) =>
        BinaryPrimitives.WriteUInt32BigEndian(dst.AsSpan(at, 4), value);
    private static void Put16(byte[] dst, int at, ushort value) =>
        BinaryPrimitives.WriteUInt16BigEndian(dst.AsSpan(at, 2), value);
    private static void PutFloat(byte[] dst, int at, float value) =>
        BinaryPrimitives.WriteSingleBigEndian(dst.AsSpan(at, 4), value);
    private static void Check(bool value, string why)
    {
        if (!value) throw new InvalidOperationException("FAIL: " + why);
    }
    private static void Expect<T>(Action action, string why) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name + ": " + why);
    }
}
