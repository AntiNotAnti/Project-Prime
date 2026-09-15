using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using MphRead.Cosmetics;
using MphRead.Identity;
using MphRead.Mods;
using MphRead.Mods.Accounts;
using MphRead.Mods.Launcher.Gui;
using Xunit;
using AvaloniaButton = Avalonia.Controls.Button;

namespace MphRead.Tests.Cosmetics;

public sealed class HunterAppearanceTests
{
    [Fact]
    public void ControllerKeepsPreviewSeparateUntilEquipAndPersistsPerHunter()
    {
        var store = new MemoryStore();
        var controller = new HunterAppearanceController(CosmeticCatalog.BuiltIn, store);

        Assert.Equal("prime.skin.samus.classic", controller.State.Equipped.SkinKey);
        controller.SelectNextSkin();
        Assert.Equal("prime.skin.samus.obsidian", controller.State.Preview.SkinKey);
        Assert.True(controller.State.Dirty);
        Assert.Equal("prime.skin.samus.classic", controller.State.Equipped.SkinKey);

        controller.Equip();
        Assert.False(controller.State.Dirty);
        Assert.Equal(controller.State.Equipped, store.Load(Hunter.Samus));

        controller.SelectHunter(Hunter.Kanden);
        Assert.Equal("prime.skin.kanden.classic", controller.State.Preview.SkinKey);
        controller.SelectHunter(Hunter.Samus);
        Assert.Equal("prime.skin.samus.obsidian", controller.State.Equipped.SkinKey);
    }

    [Fact]
    public void UnknownPersistedKeysFallBackIndependently()
    {
        var store = new MemoryStore();
        store.Save(Hunter.Samus, new CosmeticLoadout(
            "prime.skin.samus.missing", "prime.armor_fx.lightning",
            "prime.death.missing"));

        var controller = new HunterAppearanceController(CosmeticCatalog.BuiltIn, store);

        Assert.Equal("prime.skin.samus.classic", controller.State.Equipped.SkinKey);
        Assert.Equal("prime.armor_fx.lightning", controller.State.Equipped.ArmorEffectKey);
        Assert.Equal("prime.death.default", controller.State.Equipped.DeathEffectKey);
    }

    [Fact]
    public void AccountStateNeverReadsOrOverwritesGuestPreferences()
    {
        var store = new MemoryStore();
        CosmeticLoadout guest = CosmeticLoadout.DefaultFor(Hunter.Samus) with
        {
            SkinKey = "prime.skin.samus.solar"
        };
        store.Save(Hunter.Samus, guest);
        var controller = new HunterAppearanceController(CosmeticCatalog.BuiltIn, store);
        Assert.Equal(guest, controller.State.Equipped);

        PlayerId first = new(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        controller.UseAccountStorage(first);
        Assert.True(controller.UsesAccountStorage);
        Assert.Equal(CosmeticLoadout.DefaultFor(Hunter.Samus),
            controller.State.Equipped);

        CosmeticLoadout account = CosmeticLoadout.DefaultFor(Hunter.Samus) with
        {
            ArmorEffectKey = "prime.armor_fx.phase"
        };
        controller.ApplyAuthoritative(Hunter.Samus, account);
        Assert.Equal(account, controller.State.Equipped);
        Assert.Equal(guest, store.Load(Hunter.Samus));

        PlayerId second = new(Guid.Parse("22222222-2222-2222-2222-222222222222"));
        controller.UseAccountStorage(second);
        Assert.Equal(CosmeticLoadout.DefaultFor(Hunter.Samus),
            controller.State.Equipped);
        controller.UseGuestStorage();
        Assert.Equal(guest, controller.State.Equipped);
    }

    [Fact]
    public void AuthoritativeLoadoutCannotBeAppliedInGuestScope()
    {
        var controller = new HunterAppearanceController(CosmeticCatalog.BuiltIn,
            new MemoryStore());

        Assert.Throws<InvalidOperationException>(() => controller.ApplyAuthoritative(
            Hunter.Samus, CosmeticLoadout.DefaultFor(Hunter.Samus)));
    }

    [Fact]
    public async Task AuthenticatedOperationRejectsPrincipalAndGenerationButNotLobbyDrift()
    {
        PlayerId principal = new(Guid.Parse("33333333-3333-3333-3333-333333333333"));
        using var account = new AccountSession(new Uri("https://accounts.example.test/"),
            new SignInHandler(principal));
        await account.SignInAsync("hunter@example.test", "A-long-password-1!");
        var operation = new AuthenticatedCosmeticOperation(account, principal,
            IdentityGeneration: 7);

        Assert.True(operation.IsAccountCurrent(account, principal, 7));
        Assert.False(operation.IsAccountCurrent(null, principal, 7));
        Assert.False(operation.IsAccountCurrent(account,
            new PlayerId(Guid.NewGuid()), 7));
        Assert.False(operation.IsAccountCurrent(account, principal, 8));

        // Node session and lobby identity are intentionally absent: an
        // account GET/PUT remains current while that same principal moves
        // between lobbies. Lobby synchronization captures its own current
        // target only after the account cache has been applied.
    }

    [Fact]
    public void EquippedLookupDoesNotChangeThePreviewedHunter()
    {
        var store = new MemoryStore();
        CosmeticLoadout kanden = CosmeticLoadout.DefaultFor(Hunter.Kanden)
            with { ArmorEffectKey = "prime.armor_fx.phase" };
        store.Save(Hunter.Kanden, kanden);
        var controller = new HunterAppearanceController(CosmeticCatalog.BuiltIn,
            store, Hunter.Samus);

        Assert.Equal(kanden, controller.GetEquipped(Hunter.Kanden));
        Assert.Equal(Hunter.Samus, controller.State.Hunter);
    }

    [Fact]
    public void HunterRestrictedDeathEffectIsOnlyExposedForSamus()
    {
        var controller = new HunterAppearanceController(CosmeticCatalog.BuiltIn,
            new MemoryStore(), Hunter.Samus);
        Assert.Contains(controller.State.DeathEffects,
            effect => effect.Id == BuiltInCosmeticIds.DeathSamusBackwardCollapse);

        controller.SelectHunter(Hunter.Kanden);
        Assert.DoesNotContain(controller.State.DeathEffects,
            effect => effect.Id == BuiltInCosmeticIds.DeathSamusBackwardCollapse);
        Assert.Contains(controller.State.DeathEffects,
            effect => effect.Id == BuiltInCosmeticIds.DeathBackwardCollapse);

        controller.SelectHunter(Hunter.Guardian);
        Assert.Equal(Hunter.Guardian, controller.State.Hunter);
        Assert.DoesNotContain(controller.State.DeathEffects,
            effect => effect.Id == BuiltInCosmeticIds.DeathSamusBackwardCollapse);
        Assert.Contains(controller.State.DeathEffects,
            effect => effect.Id == BuiltInCosmeticIds.DeathBackwardCollapse);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            controller.SelectHunter(Hunter.Random));
    }

    [Fact]
    public void LocalStoreRoundTripsStableKeysAndMalformedFilesFailSoft()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"prime-cosmetics-{Guid.NewGuid():N}");
        string path = Path.Combine(directory, "loadouts.json");
        try
        {
            var store = new LocalHunterAppearanceStore(path);
            CosmeticLoadout selected = new("prime.skin.samus.solar",
                "prime.armor_fx.phase", "prime.death.spectral");
            store.Save(Hunter.Samus, selected);
            Assert.Equal(selected, store.Load(Hunter.Samus));
            Assert.Equal(CosmeticLoadout.DefaultFor(Hunter.Kanden), store.Load(Hunter.Kanden));

            File.WriteAllText(path, "{not-json");
            Assert.Equal(CosmeticLoadout.DefaultFor(Hunter.Samus), store.Load(Hunter.Samus));
        }
        finally
        {
            try { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
            catch { }
        }
    }

    [Fact]
    public void AppearanceControlsUseLabelsAndInvokeSelectionAndEquip()
    {
        var store = new MemoryStore();
        var controller = new HunterAppearanceController(CosmeticCatalog.BuiltIn, store);
        Control view = HunterAppearancePresentation.Build(new(
            controller.State,
            controller.SelectPreviousSkin,
            controller.SelectNextSkin,
            controller.SelectPreviousArmorEffect,
            controller.SelectNextArmorEffect,
            controller.SelectPreviousDeathEffect,
            controller.SelectNextDeathEffect,
            CanPreviewDeath: false, () => { }, controller.Equip,
            controller.ResetPreview));

        string text = String.Join('\n', Walk(view).OfType<TextBlock>().Select(value => value.Text));
        Assert.Contains("Appearance", text, StringComparison.Ordinal);
        Assert.Contains("Base", text, StringComparison.Ordinal);
        Assert.DoesNotContain("prime.skin", text, StringComparison.Ordinal);
        Assert.Contains(Walk(view).OfType<AvaloniaButton>(), button => Equals(button.Content, "Equip"));

        Walk(view).OfType<AvaloniaButton>().First(button => Equals(button.Content, "›")).RaiseEvent(
            new Avalonia.Interactivity.RoutedEventArgs(AvaloniaButton.ClickEvent));
        Assert.Equal("prime.skin.samus.obsidian", controller.State.Preview.SkinKey);
    }

    [Fact]
    public void PreviewIdentityIncludesAuthoredSkinRecolor()
    {
        Assert.True(CosmeticCatalog.BuiltIn.TryGetSkin("prime.skin.samus.solar",
            out SkinDefinition solar));
        Assert.True(ModelPreviewCatalog.TryHunter(Hunter.Samus, solar,
            out ModelPreviewSpec? spec));
        Assert.NotNull(spec);
        Assert.Equal((byte)2, spec!.BaseRecolor);
        Assert.Equal("hunter:samus:2", spec.WorkerKey);
        Assert.Contains("samus-r2", ModelPreviewCatalog.PathFor(spec, "AMHE1", "hash"),
            StringComparison.Ordinal);
        Assert.True(ModelPreviewCatalog.TryWorkerKey(spec.WorkerKey,
            out ModelPreviewSpec? parsed));
        Assert.Equal(spec.BaseRecolor, parsed!.BaseRecolor);
    }

    [Fact]
    public void AlivePreviewIdentityIncludesArmorAndChangesCachePath()
    {
        Assert.True(CosmeticCatalog.BuiltIn.TryGetSkin(
            "prime.skin.trace.obsidian", out SkinDefinition skin));
        Assert.True(CosmeticCatalog.BuiltIn.TryGetArmorEffect(
            BuiltInCosmeticIds.ArmorLightning, out ArmorEffectDefinition armor));
        Assert.True(ModelPreviewCatalog.TryHunter(Hunter.Trace, skin, armor,
            out ModelPreviewSpec? armored));
        Assert.Equal("hunter:trace:1:armor-1", armored!.WorkerKey);
        Assert.Contains("trace-r1-a1-v3-",
            ModelPreviewCatalog.PathFor(armored, "AMHE1", "hash"),
            StringComparison.Ordinal);
        Assert.True(ModelPreviewCatalog.TryWorkerKey(armored.WorkerKey,
            out ModelPreviewSpec? parsed));
        Assert.Equal(armor.Id, parsed!.ArmorEffectId);
        Assert.True(ModelPreviewCatalog.TryHunter(Hunter.Trace, skin,
            armorEffect: null, out ModelPreviewSpec? plain));
        Assert.NotEqual(ModelPreviewCatalog.PathFor(plain!, "AMHE1", "hash"),
            ModelPreviewCatalog.PathFor(armored, "AMHE1", "hash"));
        Assert.False(ModelPreviewCatalog.TryWorkerKey(
            "hunter:trace:1:armor-999", out _));
    }

    [Fact]
    public void DeathStagePreviewIdentityIsBoundedAndIncludesSelectedAppearance()
    {
        Assert.True(CosmeticCatalog.BuiltIn.TryGetSkin("prime.skin.samus.solar",
            out SkinDefinition solar));
        Assert.True(CosmeticCatalog.BuiltIn.TryGetDeathEffect(
            BuiltInCosmeticIds.DeathQuantum, out DeathEffectDefinition death));
        Assert.True(CosmeticCatalog.BuiltIn.TryGetArmorEffect(
            BuiltInCosmeticIds.ArmorInferno, out ArmorEffectDefinition armor));
        Assert.True(ModelPreviewCatalog.TryHunterDeath(Hunter.Samus, solar, death,
            out ModelPreviewSpec? spec, armor));
        Assert.NotNull(spec);
        Assert.Equal(BuiltInCosmeticIds.ArmorInferno, spec!.ArmorEffectId);
        Assert.Equal(BuiltInCosmeticIds.DeathQuantum, spec!.DeathEffectId);
        Assert.Equal(ModelPreviewCatalog.DeathStageSampleTick,
            spec.DeathSampleTick);
        Assert.Equal("hunter:samus:2:death-1-t36-a4", spec.WorkerKey);
        Assert.Contains("samus-r2-d1-t36-a4",
            ModelPreviewCatalog.PathFor(spec, "AMHE1", "hash"),
            StringComparison.Ordinal);
        Assert.True(ModelPreviewCatalog.TryWorkerKey(spec.WorkerKey,
            out ModelPreviewSpec? parsed));
        Assert.Equal(spec.BaseRecolor, parsed!.BaseRecolor);
        Assert.Equal(spec.ArmorEffectId, parsed.ArmorEffectId);
        Assert.Equal(spec.DeathEffectId, parsed.DeathEffectId);
        Assert.Equal(spec.DeathSampleTick, parsed.DeathSampleTick);
        Assert.False(ModelPreviewCatalog.TryWorkerKey(
            "hunter:samus:2:death-1-t999", out _));
        Assert.False(ModelPreviewCatalog.TryWorkerKey(
            "hunter:samus:2:death-999-t36", out _));
        Assert.False(ModelPreviewCatalog.TryHunterDeath(Hunter.Kanden,
            skin: null, CosmeticCatalog.BuiltIn.DeathEffects[
                "prime.death.samus_backward_collapse"], out _));
    }

    private static IEnumerable<Control> Walk(Control control)
    {
        yield return control;
        if (control is Panel panel)
        {
            foreach (Control child in panel.Children)
                foreach (Control descendant in Walk(child)) yield return descendant;
        }
        else if (control is ContentControl content && content.Content is Control contentChild)
        {
            foreach (Control descendant in Walk(contentChild)) yield return descendant;
        }
        else if (control is Decorator decorator && decorator.Child is Control decoratorChild)
        {
            foreach (Control descendant in Walk(decoratorChild)) yield return descendant;
        }
    }

    private sealed class MemoryStore : IHunterAppearanceStore
    {
        private readonly Dictionary<Hunter, CosmeticLoadout> _values = [];
        public CosmeticLoadout Load(Hunter hunter)
            => _values.TryGetValue(hunter, out CosmeticLoadout value)
                ? value : CosmeticLoadout.DefaultFor(hunter);
        public void Save(Hunter hunter, CosmeticLoadout loadout)
            => _values[hunter] = loadout;
    }

    private sealed class SignInHandler(PlayerId principal) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            object value = request.RequestUri!.AbsolutePath switch
            {
                "/v1/auth/login" => new
                {
                    tokenType = "Bearer",
                    accessToken = "access-token",
                    expiresIn = 3600,
                    refreshToken = "refresh-token"
                },
                "/v1/me" => new AccountIdentity(principal, true, true),
                _ => throw new InvalidOperationException(
                    $"Unexpected account request {request.RequestUri.AbsolutePath}.")
            };
            string json = JsonSerializer.Serialize(value);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }
    }
}
