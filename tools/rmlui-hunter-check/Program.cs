using System;
using System.Threading.Tasks;
using System.Threading;
using MphRead.Mods.Cosmetics;
using MphRead;
using MphRead.Mods.Launcher;

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
    Check(cosmetics.NextSkin() && cosmetics.NextArmor() && cosmetics.NextDeath()
        && cosmetics.Current().Dirty && cosmeticStore.EquipCalls == 0
        && cosmeticStore.Capture(Hunter.Samus) == CosmeticLoadout.Default,
        "skin, armor and death controls only stage a local draft");
    Check(cosmetics.Current().Draft.SkinKey == "skin.samus.obsidian"
        && cosmetics.Current().Draft.ArmorEffectKey == "armor.lightning"
        && cosmetics.Current().Draft.DeathEffectKey == "death.inferno_burst",
        "native cosmetic controls cycle current catalog keys, never guessed wire IDs");
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
