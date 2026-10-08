using MphRead.Mods.Launcher.Core;

int checks = 0;
void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); checks++; }
double clock = 1;
var backend = new FakeSettings();
var settings = new SettingsController(backend, () => clock);
Check(!settings.Dirty && settings.Snapshot().PageCount == 2, "complete category is paged");
settings.MovePage(1); Check(settings.Snapshot().Fields.Count == 2, "last page contains remaining fields");
Check(!settings.Set("scale", "NaN") && !settings.Set("scale", "Infinity"), "nonfinite scales rejected");
Check(!settings.Set("scale", "801") && !settings.Set("scale", "24"), "video range enforced");
Check(!settings.Set("unregistered", "true"), "unknown setting rejected");
settings.Set("scale", "150"); Check(settings.Dirty && backend.Values["scale"] == "100", "draft does not mutate runtime");
Check(settings.Apply() && settings.PendingVideoConfirmation && backend.Values["scale"] == "150" && backend.Writes == 0, "video preview does not persist");
Check(!settings.KeepVideo(), "keep requires a presented usable frame");
Check(!settings.Set("flag", "true"), "preview holds draft edits");
settings.ObservePresentedFrame(true); Check(settings.KeepVideo() && backend.Writes == 1 && !settings.Dirty, "usable video confirmation commits draft");
settings.Set("scale", "200"); settings.Apply(); clock += 16; settings.Tick();
Check(!settings.PendingVideoConfirmation && backend.Values["scale"] == "150" && backend.Writes == 1 && !settings.Dirty, "timeout restores last saved runtime without writes");
settings.Set("scale", "175"); settings.Apply(); settings.ObservePresentedFrame(false);
Check(backend.Values["scale"] == "150" && !settings.Dirty, "unusable frame immediately restores last good video");
settings.Set("flag", "true"); settings.Discard(); Check(!settings.Dirty && backend.Values["flag"] == "false", "discard restores full baseline");
settings.SelectCategory(SettingsCategory.Controls); settings.Set("binding", "Mouse:Unknown:Right"); settings.RevertCategory();
Check(settings.Draft["binding"] == "Mouse:W:Right", "category rollback preserves exact unused binding fields");
settings.Set("binding", "Key:Space:Right"); backend.FailWrite = true;
Check(!settings.Apply() && settings.Dirty && backend.Values["binding"] == "Mouse:W:Right", "failed persistence preserves dirty draft and runtime baseline");
backend.FailWrite = false; Check(settings.Apply() && !settings.Dirty, "retry saves unchanged draft");
settings.SelectCategory(SettingsCategory.Display); settings.Set("scale", "250"); backend.FailPreview = true;
Check(!settings.Apply() && backend.Values["scale"] == "150" && !settings.PendingVideoConfirmation, "partially failed preview restores runtime before releasing rollback");
backend.FailPreview = false; settings.Discard();
backend.Restart = true; Check(!settings.Set("flag", "true") && !settings.Apply(), "archive restart fence rejects stale writes");
backend.Restart = false; settings.Set("flag", "true"); settings.Apply();
Check(backend.Values["untouched-new-key"] == "extension-value", "forward settings remain intact");
settings.Search("field11");Check(settings.Snapshot().Fields.Count==1&&settings.Snapshot().Fields[0].Definition.Id=="field11","search filters the current category without changing draft values");
settings.Search("");Check(settings.Snapshot().PageCount==2,"clearing search restores category pages");
Check(!settings.StageSnapshot(new Dictionary<string,string>{{"unknown","x"}})&&!settings.Dirty,"unregistered profile carrier cannot alter the draft");
Check(!settings.StageSnapshot(new Dictionary<string,string>{{"flag","false"},{"field0","7"}})&&settings.Draft["flag"]=="true","invalid profile staging leaves the complete previous draft intact");
Console.WriteLine($"PASS {checks} settings draft, range, persistence, exact binding and last-good-video checks");

sealed class FakeSettings : ISettingsBackend
{
    public Dictionary<string, string> Values = new() { ["scale"] = "100", ["flag"] = "false", ["binding"] = "Mouse:W:Right", ["untouched-new-key"] = "extension-value" };
    public bool Restart, FailWrite, FailPreview; public int Writes;
    public bool RestartRequired => Restart;
    public IReadOnlyList<SettingsFieldDefinition> Definitions { get; }
    public FakeSettings()
    {
        var fields = new List<SettingsFieldDefinition> {
            new("scale", SettingsCategory.Display, "Render scale", SettingsValueKind.Number, "25 to 800", Array.Empty<string>(), v => SettingsValueValidation.Integer(v,25,800), Video:true),
            new("flag", SettingsCategory.Display, "Flag", SettingsValueKind.Boolean, "", Array.Empty<string>(), SettingsValueValidation.Boolean),
            new("binding", SettingsCategory.Controls, "Binding", SettingsValueKind.Text, "", Array.Empty<string>(), SettingsValueValidation.Text) };
        for (int i=0;i<12;i++) { string id="field"+i; Values[id]="0"; fields.Add(new(id,SettingsCategory.Display,id,SettingsValueKind.Number,"",Array.Empty<string>(),v=>SettingsValueValidation.Integer(v,0,1))); }
        Definitions=fields;
    }
    public IReadOnlyDictionary<string,string> Capture() => new Dictionary<string,string>(Values);
    public void Apply(IReadOnlyDictionary<string,string> values,bool persist)
    {
        if (persist && FailWrite) throw new IOException("simulated read-only store");
        Values=new(values); if (!persist && FailPreview && Values["scale"]=="250") throw new InvalidOperationException("simulated preview failure after mutation");
        if (persist) Writes++;
    }
}
