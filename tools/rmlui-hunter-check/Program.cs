using System;
using System.Threading.Tasks;
using System.Threading;
using MphRead.Mods.Cosmetics;
using MphRead;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.RmlUi.Core;

static void Check(bool pass, string label)
{
    if (!pass) throw new InvalidOperationException(label);
    Console.WriteLine("RML HUNTER PASS " + label);
}
var store = new Fake();
using var model = new LauncherHunterChoiceController(store);
var first = model.Current();
Check(first.Choice.Hunter == Hunter.Samus && first.Choice.Suit == 0 && !first.Dirty,
    "launch from the persisted local Hunter identity");
Check(model.SelectHunter(1) && model.SelectSuit(3) && model.Current().Dirty,
    "native seven-Hunter and four-suit draft accepts valid selections");
Check(store.Writes == 0 && store.Current.Hunter == Hunter.Samus,
    "visual selection cannot silently modify saved identity");
Check(!model.SelectHunter(7) && !model.SelectSuit(4) && store.Writes == 0,
    "unsupported Hunter and suit inputs are refused");
Check(model.Apply() && store.Writes == 1
    && store.Current.Hunter == Hunter.Kanden && store.Current.Suit == 3,
    "explicit Apply commits one existing launcher identity");
Check(model.Apply() && store.Writes == 1,
    "duplicate Apply has no repeated disk write");
Check(model.SelectHunter(2), "next valid Hunter draft");
model.Discard();
Check(!model.Current().Dirty && model.Current().Choice.Hunter == Hunter.Kanden,
    "discard restores the exact currently saved loadout");
Check(model.SelectHunter(6), "new candidate Hunter staged");
store.Current = new(Hunter.Spire, 1);
Check(!model.Apply() && store.Writes == 1
    && model.Current().Status.Contains("CHANGED ELSEWHERE", StringComparison.Ordinal),
    "another surface's saved Hunter is protected from stale native Apply");
model.Discard();
Check(model.Current().Choice.Hunter == Hunter.Spire && model.Current().Choice.Suit == 1,
    "discard rehydrates an external change without losing it");
Check(model.SelectSuit(2), "suit change staged for failure test");
store.ThrowOnSave = true;
Check(!model.Apply() && model.Current().Dirty && store.Writes == 1,
    "failed disk save leaves draft unapplied");
Exception? crossThread = null;
Task.Run(() =>
{
    try { model.SelectHunter(0); }
    catch (Exception e) { crossThread = e; }
}).GetAwaiter().GetResult();
Check(crossThread is InvalidOperationException,
    "Hunter actions are confined to UI owner thread");
// RML-24: native cosmetics are a separate explicit draft from Hunter identity.
// They reuse the current catalog and one asynchronous authenticated equip.
var cosmeticStore = new CosmeticFake();
using (var cosmetics = new LauncherCosmeticDraftController(cosmeticStore, Hunter.Samus))
{
    var firstCosmetics = cosmetics.Current();
    Check(firstCosmetics.Hunter == Hunter.Samus && firstCosmetics.Draft == CosmeticLoadout.Default
        && !firstCosmetics.Dirty, "native cosmetics initialize from persisted Hunter loadout");
    Check(cosmetics.SelectBeam(8) && cosmetics.Current().Draft.BeamEffectKey == "beam.phantom"
        && cosmetics.SelectBeamColor(7) && cosmetics.Current().Draft.BeamColor == 7
        && !cosmetics.SelectBeam(9) && !cosmetics.SelectBeamColor(-1),
        "nine styles and nine colors can be directly selected without an account request");
    Check(cosmetics.Reset() && cosmetics.Current().Draft == CosmeticLoadout.Default,
        "direct beam selection is discardable");
    for (int i = 0; i < CosmeticCatalog.BeamEffects.Count; i++)
        Check(cosmetics.NextBeam(), "beam cycling visits style " + i);
    for (int i = 0; i < BeamPalette.Names.Count; i++)
        Check(cosmetics.NextBeamColor(), "color cycling visits preset " + i);
    Check(cosmetics.Current().Draft == CosmeticLoadout.Default,
        "full cycle wraps to original loadout without saving");
    Check(cosmetics.NextSkin() && cosmetics.NextArmor() && cosmetics.NextDeath()
        && cosmetics.NextBeam() && cosmetics.NextBeamColor()
        && cosmetics.Current().Dirty && cosmeticStore.EquipCalls == 0
        && cosmeticStore.Capture(Hunter.Samus) == CosmeticLoadout.Default,
        "skin, armor and death controls only stage a local draft");
    Check(cosmetics.Current().Draft.SkinKey == "skin.samus.obsidian"
        && cosmetics.Current().Draft.ArmorEffectKey == "armor.lightning"
        && cosmetics.Current().Draft.DeathEffectKey == "death.inferno_burst"
        && cosmetics.Current().Draft.BeamEffectKey == "beam.void_rift"
        && cosmetics.Current().Draft.BeamColor == 1
        && cosmetics.Current().BeamName == "Void Rift",
        "native cosmetic controls cycle skin, armor, death, beam and dye from stable catalog entries");
    Check(!cosmetics.SwitchHunter(Hunter.Kanden),
        "dirty cosmetic draft blocks silent Hunter switching");
    Check(cosmetics.Reset() && !cosmetics.Current().Dirty,
        "Discard restores the exact persisted Hunter loadout");
    Check(cosmetics.NextSkin() && cosmetics.Equip()
        && cosmeticStore.EquipCalls == 1 && cosmetics.Current().Busy
        && !cosmetics.Equip() && !cosmetics.NextArmor(),
        "explicit equip submits once and blocks duplicate actions during HTTPS");
    cosmeticStore.Finish("EQUIPPED LOCALLY // NOT SYNCED");
    cosmetics.Pump();
    Check(!cosmetics.Current().Busy && !cosmetics.Current().Dirty
        && cosmetics.Current().PendingSync
        && cosmetics.Current().Status.Contains("NOT SYNCED", StringComparison.Ordinal),
        "server sync failure keeps locally equipped cosmetics and pending status");
    Check(cosmetics.NextArmor(), "new cosmetic edit staged");
    cosmeticStore.ExternalChange(Hunter.Samus, new CosmeticLoadout(
        "skin.default", "armor.phase", "death.classic"));
    Check(!cosmetics.Equip() && cosmeticStore.EquipCalls == 1
        && cosmetics.Current().Status.Contains("CHANGED ELSEWHERE", StringComparison.Ordinal),
        "stale cosmetics cannot overwrite another UI's changes");
    Check(cosmetics.Reset()
        && cosmetics.Current().Draft.ArmorEffectKey == "armor.phase",
        "Discard rehydrates externally saved cosmetic choice");
    Check(cosmetics.SwitchHunter(Hunter.Kanden) && cosmetics.NextSkin()
        && cosmetics.Current().Draft.SkinKey.StartsWith("skin.kanden.", StringComparison.Ordinal),
        "switching Hunter restricts skin choices to its own catalog entries");
    Exception? cosmeticWrongThread = null;
    Task.Run(() =>
    {
        try { cosmetics.NextSkin(); }
        catch (Exception error) { cosmeticWrongThread = error; }
    }).GetAwaiter().GetResult();
    Check(cosmeticWrongThread is InvalidOperationException,
        "native cosmetic mutation rejects non-owner threads");
}
var canceledStore = new CosmeticFake();
using (var canceled = new LauncherCosmeticDraftController(canceledStore, Hunter.Trace))
{
    Check(canceled.NextDeath() && canceled.Equip(), "transient cosmetic upload starts");
    canceled.Dispose();
    canceledStore.Finish("IGNORED STALE RESULT");
    Check(canceledStore.Cancelled, "retiring native page cancels in-flight account synchronization");
}
var defaultStore = new CosmeticFake();
var equippedCustom = new CosmeticLoadout("skin.samus.obsidian", "armor.phase", "death.cryo_shatter");
defaultStore.ExternalChange(Hunter.Samus, equippedCustom);
using (var defaults = new LauncherCosmeticDraftController(defaultStore, Hunter.Samus))
{
    Check(defaults.SetDefaultLoadout() && defaults.Current().Draft == CosmeticLoadout.Default
        && defaults.Current().Equipped == equippedCustom && defaults.Current().Dirty
        && defaultStore.EquipCalls == 0 && defaultStore.Capture(Hunter.Samus) == equippedCustom,
        "Reset Loadout stages all native defaults without changing the equipped loadout or saving");
    Check(defaults.Reset() && defaults.Current().Draft == equippedCustom && !defaults.Current().Dirty,
        "Discard restores the exact equipped custom loadout after resetting defaults");
    Check(defaults.SetDefaultLoadout() && defaults.Equip() && defaultStore.EquipCalls == 1
        && defaults.Current().Busy && !defaults.SetDefaultLoadout(),
        "default loadout requires explicit Equip and cannot change during account synchronization");
    defaultStore.Finish("DEFAULTS EQUIPPED LOCALLY");
    defaults.Pump();
    Check(!defaults.Current().Busy && !defaults.Current().Dirty
        && defaults.Current().Equipped == CosmeticLoadout.Default,
        "default draft completes through the existing equip owner");
    defaultStore.ExternalChange(Hunter.Samus, equippedCustom);
    defaults.Reset();
    defaults.SetDefaultLoadout();
    defaultStore.ExternalChange(Hunter.Samus, equippedCustom with { ArmorEffectKey = "armor.lightning" });
    Check(!defaults.Equip() && defaultStore.EquipCalls == 1,
        "resetting defaults retains stale equipped-loadout protection");
    Exception? defaultWrongThread = null;
    Task.Run(() =>
    {
        try { defaults.SetDefaultLoadout(); }
        catch (Exception error) { defaultWrongThread = error; }
    }).GetAwaiter().GetResult();
    Check(defaultWrongThread is InvalidOperationException,
        "Reset Loadout rejects mutation away from the draft owner thread");
    defaults.Dispose();
    bool retiredDefaults = false;
    try { defaults.SetDefaultLoadout(); }
    catch (ObjectDisposedException) { retiredDefaults = true; }
    Check(retiredDefaults, "retired drafts reject delayed Reset Loadout actions");
}
// RML-31: the actual native Hunter preview bounds are in framebuffer pixels.
// Clamping is performed once here, not once in RmlUi and again in OpenTK.
Check(RmlUiPreviewViewport.TryCreate(100, 60, 600, 480, 1200, 800, out var viewport)
    && Math.Abs(viewport.Left - 1f / 12f) < 0.0001f
    && Math.Abs(viewport.Right - 7f / 12f) < 0.0001f
    && Math.Abs(viewport.Top - 0.075f) < 0.0001f
    && Math.Abs(viewport.Bottom - 0.675f) < 0.0001f,
    "native Hunter viewport transforms framebuffer top-left coordinates exactly once");
Check(RmlUiPreviewViewport.TryCreate(-80, -30, 160, 140, 1000, 600,
        out var clippedViewport) && clippedViewport.Left == 0f
    && clippedViewport.Top == 0f && clippedViewport.Width > 0f,
    "partially visible 3D Hunter viewport is clipped to framebuffer");
Check(!RmlUiPreviewViewport.TryCreate(float.NaN, 0, 640, 320, 1000, 600, out _)
    && !RmlUiPreviewViewport.TryCreate(0, 0, float.PositiveInfinity, 100, 1000, 600, out _)
    && !RmlUiPreviewViewport.TryCreate(40, 40, 8, 8, 1000, 600, out _)
    && !RmlUiPreviewViewport.TryCreate(1200, 1000, 400, 300, 1000, 600, out _)
    && !RmlUiPreviewViewport.TryCreate(0, 0, 400, 300, 0, 600, out _),
    "nonfinite, invisible, undersized and dimensionless viewports never touch native GL");
Check(RmlUiPreviewViewport.TryCreate(780, 340, 460, 460, 1200, 800,
        out var edgeViewport)
    && edgeViewport.Right == 1f && edgeViewport.Bottom == 1f,
    "oversized native Hunter viewport remains bounded to active framebuffer");
Check(RmlUiPreviewViewport.IsContained(100, 60, 600, 480, 100, 60, 600, 480)
    && RmlUiPreviewViewport.IsContained(100.25f, 60.5f, 599.5f, 479, 100, 60, 600, 480)
    && RmlUiPreviewViewport.IsContained(-20, -10, 10, 5, -20, -10, 40, 20),
    "preview containment accepts exact clip edges, fractional pixels and consistent negative origins");
foreach (var box in new[] { (X: 99f, Y: 60f, Width: 600f, Height: 480f),
    (X: 100f, Y: 59f, Width: 600f, Height: 480f),
    (X: 100f, Y: 60f, Width: 601f, Height: 480f),
    (X: 100f, Y: 60f, Width: 600f, Height: 481f),
    (X: 800f, Y: 60f, Width: 20f, Height: 20f) })
    Check(!RmlUiPreviewViewport.IsContained(box.X, box.Y, box.Width, box.Height, 100, 60, 600, 480),
        "partially scrolled or disjoint preview never escapes its clip rectangle");
for (int parameter = 0; parameter < 8; parameter++)
{
    foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
    {
        float[] rectangle = { 100, 60, 600, 480, 100, 60, 600, 480 };
        rectangle[parameter] = invalid;
        Check(!RmlUiPreviewViewport.IsContained(rectangle[0], rectangle[1], rectangle[2], rectangle[3],
            rectangle[4], rectangle[5], rectangle[6], rectangle[7]),
            "nonfinite preview or clip parameter is rejected at index " + parameter);
    }
}
foreach (int parameter in new[] { 2, 3, 6, 7 })
{
    foreach (float invalid in new[] { 0f, -1f })
    {
        float[] rectangle = { 100, 60, 600, 480, 100, 60, 600, 480 };
        rectangle[parameter] = invalid;
        Check(!RmlUiPreviewViewport.IsContained(rectangle[0], rectangle[1], rectangle[2], rectangle[3],
            rectangle[4], rectangle[5], rectangle[6], rectangle[7]),
            "zero or negative preview/clip extent is rejected at index " + parameter);
    }
}

// Preview controls own no persisted identity, account or engine-global state.
var enginePreviewBefore = (CosmeticPreview.Mode, CosmeticPreview.Yaw, CosmeticPreview.Zoom,
    CosmeticPreview.LoopDeath, CosmeticPreview.DeathRequest);
int savedIdentityWrites = store.Writes;
using (var preview = new LauncherHunterPreviewController())
using (var separatePreview = new LauncherHunterPreviewController())
{
    var initialPreview = preview.Current();
    Check(initialPreview.IsValid && initialPreview.Mode == SkinContext.Biped
        && initialPreview.Yaw == 0 && initialPreview.Zoom == 1
        && !initialPreview.LoopDeath && initialPreview.DeathRequest == 0,
        "local preview begins in valid biped view with neutral camera");
    Check(preview.SelectMode(1) && preview.Current().Mode == SkinContext.ViewModel
        && preview.Current().IsValid,
        "weapon preview uses the existing native ViewModel context without persisting a loadout");
    Check(preview.SelectMode(2) && preview.Current().Mode == SkinContext.AltForm
        && !preview.SelectMode(3) && !preview.SelectMode(-1),
        "preview accepts the original three contexts and rejects undefined modes");
    Check(preview.Rotate(0) && preview.Current().Yaw < 0 && preview.Rotate(1)
        && Math.Abs(preview.Current().Yaw) < 0.00001f
        && !preview.Rotate(-1) && !preview.Rotate(2),
        "rotate controls move in opposite directions and reject invalid arguments");
    bool rotationStayedValid = true;
    for (int i = 0; i < 1000; i++)
    {
        preview.Rotate(1);
        rotationStayedValid &= preview.Current().IsValid;
    }
    Check(rotationStayedValid, "preview yaw remains finite and bounded through repeated rotations");
    for (int i = 0; i < 10; i++) preview.Zoom(1);
    Check(preview.Current().Zoom == 1.2f && !preview.Zoom(1),
        "preview zoom clamps at HunterStand upper limit");
    for (int i = 0; i < 10; i++) preview.Zoom(0);
    Check(preview.Current().Zoom == 0.8f && !preview.Zoom(0)
        && !preview.Zoom(-1) && !preview.Zoom(2),
        "preview zoom clamps at HunterStand lower limit and rejects invalid arguments");
    Check(preview.PreviewDeath() && preview.Current().Mode == SkinContext.Biped
        && preview.Current().DeathRequest == 1 && preview.PreviewDeath()
        && preview.Current().DeathRequest == 2,
        "death preview forces biped and issues a new local animation request for each click");
    Check(preview.SelectMode(2) && preview.ToggleLoopDeath()
        && preview.Current().LoopDeath && preview.Current().Mode == SkinContext.Biped
        && preview.Current().DeathRequest == 1,
        "enabling death loop starts a biped death preview like the existing customization owner");
    preview.Rotate(1);
    preview.Zoom(1);
    Check(preview.SelectMode(1) && preview.Current().Mode == SkinContext.ViewModel
        && preview.Current().Yaw == 0 && preview.Current().Zoom == 1
        && preview.Current().DeathRequest == 0 && preview.Current().LoopDeath,
        "switching to weapon preview resets camera and stale death request while preserving loop preference");
    preview.SelectMode(2);
    Check(preview.Reset() && preview.Current().Yaw == 0 && preview.Current().Zoom == 1
        && preview.Current().DeathRequest == 0 && preview.Current().LoopDeath
        && preview.Current().Mode == SkinContext.AltForm,
        "Reset matches HunterStand camera and death reset while preserving form and loop preference");
    Check(preview.ToggleLoopDeath() && !preview.Current().LoopDeath
        && separatePreview.Current() == initialPreview,
        "death loop toggles off and independent preview owners never share state");
    preview.ToggleLoopDeath();
    preview.Rotate(1);
    preview.Zoom(1);
    Check(preview.ToggleCompareNative() && preview.Current().CompareNative
        && preview.Current().Yaw == 0 && preview.Current().Zoom == 1
        && preview.Current().DeathRequest == 0 && preview.Current().LoopDeath
        && preview.Current().Mode == SkinContext.Biped
        && separatePreview.Current() == initialPreview,
        "Compare Native resets camera and death while preserving form, loop and independent owners");
    Check(preview.Reset() && preview.Current().CompareNative
        && preview.ToggleCompareNative() && !preview.Current().CompareNative,
        "camera Reset preserves comparison and a second Compare restores the cosmetic preview");
    preview.ToggleCompareNative();
    Check(preview.SelectMode(2) && !preview.Current().CompareNative,
        "changing preview form clears native comparison");
    preview.ToggleCompareNative();
    preview.ClearComparison();
    Check(!preview.Current().CompareNative && preview.Current().Mode == SkinContext.AltForm
        && preview.Current().LoopDeath,
        "draft owners can clear comparison without changing form or loop preference");
    preview.ToggleCompareNative();
    Check(preview.PreviewDeath() && !preview.Current().CompareNative,
        "death preview restores draft cosmetics when returning to biped");
    Exception? previewWrongThread = null;
    Task.Run(() =>
    {
        try { preview.ToggleCompareNative(); }
        catch (Exception error) { previewWrongThread = error; }
    }).GetAwaiter().GetResult();
    Check(previewWrongThread is InvalidOperationException,
        "local preview controls reject non-owner mutation");
    preview.Dispose();
    bool retiredPreview = false;
    try { preview.ClearComparison(); }
    catch (ObjectDisposedException) { retiredPreview = true; }
    Check(retiredPreview, "retired preview owner rejects delayed visual actions");
}
Check(store.Writes == savedIdentityWrites
    && enginePreviewBefore == (CosmeticPreview.Mode, CosmeticPreview.Yaw, CosmeticPreview.Zoom,
        CosmeticPreview.LoopDeath, CosmeticPreview.DeathRequest),
    "portable preview actions neither persist Hunter identity nor publish engine-global preview state");
var validPreview = new LauncherHunterPreviewSnapshot(SkinContext.Biped, 0, 1, false, 0);
Check(!(validPreview with { Yaw = float.NaN }).IsValid
    && !(validPreview with { Zoom = float.PositiveInfinity }).IsValid
    && !(validPreview with { Yaw = MathF.PI }).IsValid
    && !(validPreview with { Zoom = 1.3f }).IsValid
    && !(validPreview with { Mode = (SkinContext)3 }).IsValid
    && !(validPreview with { DeathRequest = -1 }).IsValid,
    "renderer preview contract rejects malformed nonfinite or unsupported visual state");

Console.WriteLine("RML local Hunter identity and cosmetic contracts complete.");

sealed class Fake : ILauncherHunterChoiceStore
{
    public LauncherHunterChoice Current = new(Hunter.Samus, 0);
    public bool ThrowOnSave;
    public int Writes;
    public LauncherHunterChoice Capture() => Current;
    public void Save(LauncherHunterChoice choice)
    {
        if (ThrowOnSave) throw new InvalidOperationException("simulated permission issue");
        Current = choice; Writes++;
    }
}


sealed class CosmeticFake : ILauncherCosmeticEquipService
{
    private readonly CosmeticLoadout[] _loads = new CosmeticLoadout[7];
    private readonly bool[] _pending = new bool[7];
    private TaskCompletionSource<string>? _reply;
    private CancellationToken _token;
    public int EquipCalls;
    public bool Cancelled => _token.IsCancellationRequested;
    public CosmeticFake()
    {
        Array.Fill(_loads, CosmeticLoadout.Default);
    }
    public CosmeticLoadout Capture(Hunter hunter) => _loads[(int)hunter];
    public bool PendingSync(Hunter hunter) => _pending[(int)hunter];
    public Task<string> EquipAsync(Hunter hunter, CosmeticLoadout loadout, CancellationToken token)
    {
        EquipCalls++;
        _loads[(int)hunter] = CosmeticCatalog.Resolve(hunter, loadout);
        _pending[(int)hunter] = true;
        _token = token;
        _reply = new(TaskCreationOptions.RunContinuationsAsynchronously);
        return _reply.Task;
    }
    public void Finish(string message) => _reply?.TrySetResult(message);
    public void ExternalChange(Hunter hunter, CosmeticLoadout changed)
        => _loads[(int)hunter] = CosmeticCatalog.Resolve(hunter, changed);
}
