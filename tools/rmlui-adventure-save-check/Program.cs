using MphRead;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.RmlUi.Core;

int checks = 0;
void Check(bool ok, string label) { if (!ok) throw new InvalidOperationException(label); checks++; }
string cwd = Directory.GetCurrentDirectory();
string root = Path.Combine(Path.GetTempPath(), "prime-native-save-check-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(Path.Combine(root, "Savedata"));
try
{
    Directory.SetCurrentDirectory(root);
    string PathFor(int slot) => Path.Combine("Savedata", $"save{slot:000}.json");
    byte[] first = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray(), second = { 0, 255, 61, 48, 13, 10 };
    File.WriteAllBytes(PathFor(1), first); File.WriteAllBytes(PathFor(2), second); Menu.SaveSlot = 3;
    string one = AdventureSave.ContentFingerprint(1), two = AdventureSave.ContentFingerprint(2);
    Check(one.Length == 64 && two.Length == 64 && AdventureSave.ContentFingerprint(3) == "EMPTY", "exact identities include arbitrary unreadable bytes and empty slots");
    Check(!AdventureSave.TryCopy(1, 1, one, one, out _) && !AdventureSave.TryCopy(0, 2, one, two, out _), "copy-to-self and invalid slot rejected");
    Check(!AdventureSave.TryCopy(1, 2, "UNAVAILABLE", two, out _) && !AdventureSave.TryArchive(1, "EMPTY", out _), "unknown/empty source cannot authorize a mutation");
    Check(!AdventureSave.TryCopy(1, 2, one, "EMPTY", out _) && File.ReadAllBytes(PathFor(2)).SequenceEqual(second), "changed occupied destination rejects empty confirmation without touching bytes");
    Check(!AdventureSave.TryCopy(1, 2, two, two, out _) && !AdventureSave.TryArchive(1, two, out _), "changed source rejects copy/archive without backup or write");
    Check(Directory.GetFiles("Savedata", "*.bak*").Length == 0, "stale confirmations make no backup changes");
    Check(AdventureSave.TryCopy(1, 2, one, two, out string copied) && copied.Contains("VERIFIED"), "guarded occupied copy verifies publication");
    Check(File.ReadAllBytes(PathFor(1)).SequenceEqual(first) && File.ReadAllBytes(PathFor(2)).SequenceEqual(first), "guarded copy retains every source byte");
    Check(File.ReadAllBytes(PathFor(2) + ".bak").SequenceEqual(second), "guarded copy reserves exact previous destination without clobber");
    Check(AdventureSave.TryCopy(1, 2, one, one, out _) && File.ReadAllBytes(PathFor(2) + ".bak").SequenceEqual(second)
        && File.ReadAllBytes(PathFor(2) + ".bak001").SequenceEqual(first), "repeat guarded copy preserves every earlier backup");
    Check(AdventureSave.TryCopy(1, 3, one, "EMPTY", out _) && !File.Exists(PathFor(3) + ".bak"), "empty destination admission creates no spurious archive");
    Check(AdventureSave.TryArchive(2, one, out _) && !File.Exists(PathFor(2))
        && File.ReadAllBytes(PathFor(2) + ".bak002").SequenceEqual(first), "archive removes slot only into a unique exact-byte backup");
    Check(!AdventureSave.TryArchive(2, one, out _) && AdventureSave.ContentFingerprint(2) == "EMPTY", "duplicate archive confirmation is refused");
    Directory.CreateDirectory(PathFor(2));
    Check(AdventureSave.ContentFingerprint(2) == "UNAVAILABLE", "directory at slot path is unavailable rather than empty");
    Check(!AdventureSave.TryCopy(1, 2, one, "EMPTY", out _) && Directory.Exists(PathFor(2)), "blocked destination is never overwritten or removed");
    Directory.Delete(PathFor(2));
    for (int i = 0; i < 1000; i++)
    {
        string backup = PathFor(3) + (i == 0 ? ".bak" : $".bak{i:000}");
        File.WriteAllBytes(backup, second);
    }
    Check(!AdventureSave.TryCopy(1, 3, one, one, out _) && File.ReadAllBytes(PathFor(3)).SequenceEqual(first), "exhausted backup names leave occupied destination unchanged");
    Check(!AdventureSave.TryArchive(3, one, out _) && File.ReadAllBytes(PathFor(3)).SequenceEqual(first), "exhausted archive names leave source unchanged");
    Check(Directory.GetFiles("Savedata", "*.copy-*").Length == 0 && Menu.SaveSlot == 3, "failed copies clean only own staging and never redirect gameplay slot");

    RmlUiAdventureSlot[] Slots() => new[]
    {
        new RmlUiAdventureSlot(1, true, false, "Celestial Archives", 2, 79, 99, one),
        new RmlUiAdventureSlot(2, false, false, "", 0, 0, 0, "EMPTY"),
        new RmlUiAdventureSlot(3, false, true, "", 0, 0, 0, two)
    };
    var draft = new RmlUiAdventureDraft(Slots(), Hunter.Samus);
    Check(draft.CanCopy && draft.CanArchive && draft.CopyDestination == 2, "readable source offers copy and recoverable archive");
    Check(!draft.SelectCopyDestination(1) && !draft.SelectCopyDestination(0) && !draft.SelectCopyDestination(4), "destination cannot equal source or escape three slots");
    Check(draft.SelectCopyDestination(3) && draft.RequestSaveChange(RmlUiAdventureSaveAction.Copy), "unreadable occupied destination may be explicitly backed up and replaced");
    var frozen = draft.PendingSaveChange!.Value;
    Check(frozen.Source == Slots()[0] && frozen.Destination == Slots()[2], "copy confirmation captures exact source/destination metadata and hashes");
    Check(!draft.Select(2) && !draft.SelectCopyDestination(2) && !draft.NextHunter() && !draft.CanResume && !draft.CanStart, "confirmation fences other draft and launch actions");
    Check(draft.ConfirmSaveChange(RmlUiAdventureSaveAction.Archive) == null && draft.PendingSaveChange.HasValue, "wrong confirm action cannot consume a copy proposal");
    Check(draft.CancelConfirmation() && draft.PendingSaveChange == null && draft.CanCopy, "cancel discards proposal without affecting bytes");
    Check(draft.RequestSaveChange(RmlUiAdventureSaveAction.Copy) && draft.Refresh(Slots()) && draft.PendingSaveChange == null
        && draft.ConfirmSaveChange(RmlUiAdventureSaveAction.Copy) == null, "explicit refresh retires old copy confirmation before new data");
    Check(draft.RequestSaveChange(RmlUiAdventureSaveAction.Copy) && draft.ConfirmSaveChange(RmlUiAdventureSaveAction.Copy) == frozen
        && draft.ConfirmSaveChange(RmlUiAdventureSaveAction.Copy) == null, "exact mutation command is emitted once");
    Check(draft.Select(3) && !draft.CanCopy && draft.CanArchive, "unreadable bytes can be archived but cannot masquerade as a readable source");
    Check(draft.RequestSaveChange(RmlUiAdventureSaveAction.Archive)
        && draft.PendingSaveChange?.Destination == null && draft.PendingSaveChange?.Source.Fingerprint == two,
        "unreadable archive captures real byte identity without decoding the save");
    Check(draft.CancelConfirmation() && draft.Select(2) && !draft.CanCopy && !draft.CanArchive, "empty slot cannot be copied or archived");
    var missing = new RmlUiAdventureDraft(new[] { Slots()[0], Slots()[0] }, Hunter.Samus);
    Check(!missing.CanCopy && !missing.CanArchive, "duplicate/missing service slots have unavailable authority");
    var unavailable = new RmlUiAdventureDraft(new[] { Slots()[0] with { Fingerprint = "UNAVAILABLE" }, Slots()[1], Slots()[2] }, Hunter.Samus);
    Check(!unavailable.CanCopy && !unavailable.CanArchive, "unavailable fingerprint never permits management");
    Check(File.ReadAllBytes(PathFor(1)).SequenceEqual(first) && Menu.SaveSlot == 3, "all pure draft operations preserve raw source and active slot");
    Console.WriteLine($"PASS {checks} native Adventure save checks (actual byte owner in temp tree; synthetic unrelated engine symbols; no user saves or serializer).");
}
finally { Directory.SetCurrentDirectory(cwd); Directory.Delete(root, true); }
