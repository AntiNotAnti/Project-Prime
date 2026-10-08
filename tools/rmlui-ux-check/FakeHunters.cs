using System.Collections.Immutable;
using MphRead;
using MphRead.Mods.Cosmetics;
using MphRead.Mods.Launcher.Core;
sealed class FakeHunters : IHunterSelectionBackend
{
    private readonly Dictionary<Hunter, CosmeticLoadout> _equipped = new();
    private readonly HashSet<Hunter> _pending = new();
    private TaskCompletionSource<HunterEquipResult>? _work;
    private Hunter _savingHunter;
    public Hunter PreferredHunter { get; set; } = Hunter.Samus;
    public byte PreferredColor => 0;
    public Hunter SavedHunter;
    public byte SavedColor;
    public string Blocked = "";
    public void SaveIdentity(Hunter hunter, byte color) { SavedHunter = hunter; SavedColor = color; }
    public HunterSelectionProfile Capture(Hunter hunter) => new(
        _equipped.GetValueOrDefault(hunter, CosmeticLoadout.Default), _pending.Contains(hunter),
        ImmutableArray.Create(SkinContext.Biped, SkinContext.ViewModel), true, "Installed pack enabled.", "Effects visible.");
    public bool IsUnlocked(Hunter hunter, CosmeticDefinition definition, out string reason)
    { reason = definition.Key == Blocked ? "This cosmetic is locked by the test authority." : ""; return reason.Length == 0; }
    public Task<HunterEquipResult> EquipAsync(Hunter hunter, CosmeticLoadout loadout, CancellationToken cancellationToken)
    {
        _equipped[hunter] = loadout; _pending.Add(hunter); _savingHunter = hunter;
        _work = new(); return _work.Task;
    }
    public void Complete(bool synced)
    {
        if (synced) _pending.Remove(_savingHunter);
        _work!.SetResult(new(synced, synced ? "EQUIPPED / SYNCED" : "LOCAL / NOT SYNCED — server unavailable"));
    }
}
