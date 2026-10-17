using System;
using System.Threading.Tasks;
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
Console.WriteLine("RML local Hunter selection contracts complete.");

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
