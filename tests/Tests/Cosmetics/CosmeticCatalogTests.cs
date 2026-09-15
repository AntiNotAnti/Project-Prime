using System;
using System.IO;
using System.Linq;
using MphRead;
using MphRead.Cosmetics;
using MphRead.Cosmetics.Tools;
using MphRead.Mods.Network;
using Xunit;

namespace MphRead.Tests.Cosmetics;

public sealed class CosmeticCatalogTests
{
    [Fact]
    public void BuiltInCatalogUsesExplicitStableIdsAndZeroFallbacks()
    {
        CosmeticCatalog catalog = CosmeticCatalog.BuiltIn;
        Assert.Equal(16, catalog.Skins.Count);
        Assert.Equal(16, catalog.ArmorEffects.Count);
        Assert.True(catalog.TryGetArmorEffect(BuiltInCosmeticIds.ArmorThunderstorm, out var thunderstorm));
        Assert.Equal("prime.armor_fx.thunderstorm", thunderstorm.Key);
        Assert.True(catalog.TryGetDeathEffect(BuiltInCosmeticIds.DeathQuantum, out var quantum));
        Assert.Equal(DeathBodyMode.Dissolve, quantum.BodyMode);
        Assert.False(catalog.TryGetSkin(0, out _));
        Assert.False(catalog.TryGetArmorEffect(0, out _));
        Assert.False(catalog.TryGetDeathEffect(0, out _));

        CosmeticLoadout defaults = CosmeticLoadout.DefaultFor(Hunter.Trace);
        Assert.True(catalog.TryResolve(defaults, Hunter.Trace, out CosmeticLoadoutIds ids, out var issue));
        Assert.Equal(CosmeticLoadoutIssue.None, issue);
        Assert.Equal(CosmeticLoadoutIds.Default, ids);
    }

    [Fact]
    public void EverySelectableHunterHasObsidianAndSolarRecolors()
    {
        foreach (Hunter hunter in PlayableHunterCatalog.All)
        {
            SkinDefinition[] skins = CosmeticCatalog.BuiltIn.Skins.Values
                .Where(value => value.Hunter == hunter).OrderBy(value => value.Id)
                .ToArray();
            Assert.Equal(2, skins.Length);
            Assert.Equal(new byte[] { 1, 2 }, skins.Select(value => value.BaseRecolor));
            foreach (SkinDefinition skin in skins)
            {
                CosmeticLoadout loadout = CosmeticLoadout.DefaultFor(hunter)
                    with { SkinKey = skin.Key,
                        DeathEffectKey = "prime.death.backward_collapse" };
                Assert.True(CosmeticCatalog.BuiltIn.TryResolve(loadout, hunter,
                    out CosmeticLoadoutIds ids, out _));
                Assert.Equal(skin.Id, ids.SkinId);
                Assert.Equal(BuiltInCosmeticIds.DeathBackwardCollapse,
                    ids.DeathEffectId);
                Assert.Equal(ids, CosmeticCatalog.BuiltIn.Sanitize(ids, hunter));
            }
        }
        Assert.Equal(new[] { Hunter.Guardian, Hunter.Guardian },
            skinsByHunter(Hunter.Guardian).Select(value => value.Hunter));
    }

    [Fact]
    [Trait("RequiresGameContent", "true")]
    public void EverySelectableHunterLod0HasTwoAuthoredRecolors()
    {
        string data = Environment.GetEnvironmentVariable("GAME_DATA_DIRECTORY")
            ?? Path.Combine(FindRepositoryRoot(), "AMHE1");
        using var content = ServerContent.PreserveContext("AMHE1");
        Read.ServerMode = false;
        ServerContent.Open(data, "AMHE1");
        foreach (Hunter hunter in PlayableHunterCatalog.All)
        {
            string modelName = Metadata.HunterModels[hunter][0];
            Model model = Read.GetModelInstance(modelName).Model;
            Assert.True(model.Recolors.Count >= 3,
                $"{modelName} has {model.Recolors.Count} recolors.");
            AssertAuthoredRecolor(model.Recolors[1]);
            AssertAuthoredRecolor(model.Recolors[2]);
        }
        Model psychoBit = Read.GetModelInstance(
            Metadata.HunterModels[Hunter.Guardian][2]).Model;
        Assert.Equal(5, psychoBit.Recolors.Count);
        Assert.All(psychoBit.Recolors, AssertAuthoredRecolor);
    }

    private static void AssertAuthoredRecolor(Recolor recolor)
    {
        Assert.NotEmpty(recolor.Textures);
        Assert.Equal(recolor.Textures.Count, recolor.TextureData.Count);
        Assert.All(recolor.TextureData, Assert.NotEmpty);
        if (recolor.Textures.Any(texture => texture.Format != TextureFormat.DirectRgb))
        {
            Assert.NotEmpty(recolor.Palettes);
            Assert.Equal(recolor.Palettes.Count, recolor.PaletteData.Count);
            Assert.All(recolor.PaletteData, Assert.NotEmpty);
        }
    }

    private static SkinDefinition[] skinsByHunter(Hunter hunter)
        => CosmeticCatalog.BuiltIn.Skins.Values
            .Where(value => value.Hunter == hunter).OrderBy(value => value.Id)
            .ToArray();

    [Fact]
    public void CatalogHashAndLookupsDoNotDependOnInputOrdering()
    {
        var first = new SkinDefinition(40, "prime.skin.samus.zeta", "Zeta", Hunter.Samus);
        var second = new SkinDefinition(10, "prime.skin.samus.alpha", "Alpha", Hunter.Samus);
        var forward = new CosmeticCatalog([first, second], [], []);
        var reverse = new CosmeticCatalog([second, first], [], []);
        Assert.Equal(forward.CatalogHash, reverse.CatalogHash);
        Assert.Equal(second, forward.Skins[second.Key]);
        Assert.Throws<ArgumentException>(() => new CosmeticCatalog(
            [first, first with { Key = "prime.skin.samus.other" }], [], []));
        Assert.Throws<ArgumentException>(() => new CosmeticCatalog(
            [first, second with { Key = first.Key }], [], []));
    }

    [Fact]
    public void LoadoutResolutionIsHunterAwareAndSanitizesEachWireSlotIndependently()
    {
        CosmeticCatalog catalog = CosmeticCatalog.BuiltIn;
        CosmeticLoadout loadout = new("prime.skin.samus.obsidian", "prime.armor_fx.lightning",
            "prime.death.quantum");
        Assert.True(catalog.TryResolve(loadout, Hunter.Samus, out CosmeticLoadoutIds ids, out _));
        Assert.Equal(new CosmeticLoadoutIds(1, 1, 1), ids);
        Assert.False(catalog.TryResolve(loadout, Hunter.Trace, out _, out CosmeticLoadoutIssue issue));
        Assert.Equal(CosmeticLoadoutIssue.SkinHunterMismatch, issue);
        Assert.Equal(new CosmeticLoadoutIds(0, 1, 0),
            catalog.Sanitize(new CosmeticLoadoutIds(1, 1, UInt16.MaxValue), Hunter.Trace));

        CosmeticLoadout samusDeath = CosmeticLoadout.DefaultFor(Hunter.Samus) with
        {
            DeathEffectKey = "prime.death.samus_backward_collapse"
        };
        Assert.True(catalog.TryResolve(samusDeath, Hunter.Samus,
            out CosmeticLoadoutIds samusIds, out _));
        Assert.Equal(BuiltInCosmeticIds.DeathSamusBackwardCollapse,
            samusIds.DeathEffectId);
        Assert.False(catalog.TryResolve(samusDeath with
        {
            SkinKey = CosmeticCatalog.DefaultSkinKey(Hunter.Kanden)
        }, Hunter.Kanden, out _, out CosmeticLoadoutIssue deathIssue));
        Assert.Equal(CosmeticLoadoutIssue.DeathEffectHunterMismatch, deathIssue);
        Assert.False(catalog.IsValid(new CosmeticLoadoutIds(0, 0,
            BuiltInCosmeticIds.DeathSamusBackwardCollapse), Hunter.Kanden));
        Assert.Equal(CosmeticLoadoutIds.Default, catalog.Sanitize(
            new CosmeticLoadoutIds(0, 0,
                BuiltInCosmeticIds.DeathSamusBackwardCollapse), Hunter.Kanden));
    }

    [Fact]
    public void StrictPackageLoaderRejectsTraversalAndLoadsBoundedDefinitions()
    {
        string root = Path.Combine(Path.GetTempPath(), "prime-cosmetics-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "skins", "samus", "test"));
        try
        {
            File.WriteAllBytes(Path.Combine(root, "skins", "samus", "test", "albedo.png"), [1]);
            File.WriteAllText(Path.Combine(root, "skins", "samus", "test", "skin.json"), """
                {"format":1,"id":20,"key":"prime.skin.samus.test","displayName":"Test","hunter":"Samus","baseRecolor":0,
                 "materials":[{"source":"model/samus/texture/3/palette/0","albedo":"albedo.png"}]}
                """);
            File.WriteAllText(Path.Combine(root, "catalog.json"), """
                {"format":1,"skins":["skins/samus/test/skin.json"],"armorEffects":[],"deathEffects":[]}
                """);
            CosmeticCatalogLoadResult loaded = CosmeticCatalogLoader.Load(root);
            Assert.True(loaded.IsValid, string.Join(Environment.NewLine, loaded.Issues));
            Assert.True(loaded.Catalog.TryGetSkin(20, out SkinDefinition skin));
            Assert.Equal("skins/samus/test/albedo.png", skin.Materials![0].Albedo);

            File.WriteAllText(Path.Combine(root, "catalog.json"), """
                {"format":1,"skins":["../outside.json"],"armorEffects":[],"deathEffects":[]}
                """);
            CosmeticCatalogLoadResult invalid = CosmeticCatalogLoader.Load(root);
            Assert.False(invalid.IsValid);
            Assert.Contains(invalid.Issues, value => value.Kind == CosmeticValidationIssueKind.InvalidAssetPath);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void RepeatedCookOfIdenticalCatalogIsByteForByteDeterministic()
    {
        string root = Path.Combine(Path.GetTempPath(), "prime-cosmetic-cook-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "catalog.json"),
                "{\"format\":1,\"skins\":[],\"armorEffects\":[],\"deathEffects\":[]}");
            string first = CosmeticPackCompiler.Compile(root, Path.Combine(root, "first.ppcos"));
            string second = CosmeticPackCompiler.Compile(root, Path.Combine(root, "second.ppcos"));
            Assert.Equal(File.ReadAllBytes(first), File.ReadAllBytes(second));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void StrictPackageLoaderDoesNotFollowSymlinksOutsideThePack()
    {
        string root = Path.Combine(Path.GetTempPath(), "prime-cosmetics-" + Guid.NewGuid().ToString("N"));
        string outside = Path.Combine(Path.GetTempPath(), "prime-cosmetics-outside-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(outside);
        try
        {
            File.WriteAllText(Path.Combine(outside, "skin.json"), "{}\n");
            try { Directory.CreateSymbolicLink(Path.Combine(root, "linked"), outside); }
            catch (Exception error) when (error is UnauthorizedAccessException or PlatformNotSupportedException)
            {
                return;
            }
            File.WriteAllText(Path.Combine(root, "catalog.json"), """
                {"format":1,"skins":["linked/skin.json"],"armorEffects":[],"deathEffects":[]}
                """);

            CosmeticCatalogLoadResult result = CosmeticCatalogLoader.Load(root);

            Assert.False(result.IsValid);
            Assert.Contains(result.Issues,
                issue => issue.Kind == CosmeticValidationIssueKind.InvalidAssetPath);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            Directory.Delete(outside, recursive: true);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("Prime.skin.samus.test")]
    [InlineData("prime..skin")]
    [InlineData("prime.skin/escape")]
    public void StableKeyValidationRejectsNonCanonicalValues(string value)
        => Assert.False(CosmeticId.IsValid(value));

    private static string FindRepositoryRoot()
    {
        string? directory = AppContext.BaseDirectory;
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory, "Game.sln"))) return directory;
            directory = Directory.GetParent(directory)?.FullName;
        }
        throw new InvalidOperationException("Repository root was not found.");
    }
}
