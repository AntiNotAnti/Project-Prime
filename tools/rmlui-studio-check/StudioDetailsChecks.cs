using System.Globalization;
using MphRead.Mods.Launcher;

internal static class StudioDetailsChecks
{
    internal static void Run(Action<bool, string> check)
    {
        foreach(var stage in Enum.GetValues<NativeStudioChangeStage>())
        {
            var change=new NativeStudioChangeSnapshot(Guid.NewGuid(),stage,"CURRENT","NEXT","/deep/path/reviewed-map.json","");
            check(change.StatusText.Length>20&&!change.StatusText.Contains(stage.ToString(),StringComparison.Ordinal)
                &&change.ReviewedFileName=="reviewed-map.json"&&change.SavePath=="/deep/path/reviewed-map.json",
                "project change presents human status and separate exact filename/full path: "+stage);
        }
        check(new NativeStudioChangeSnapshot(Guid.NewGuid(),NativeStudioChangeStage.NeedsDecision,"","","","EXACT FAILURE").StatusText=="EXACT FAILURE",
            "project decision preserves explicit owner failure text");
        using var owner = new Owner();
        var details = new NativeStudioEditorDetails(owner);
        check(details.PageCount == 2 && details.PageIndex == 0 && details.Rows.Count == 8
            && details.Rows.Select(r => r.Field.Kind).Distinct().Count() == 5,
            "details exposes eight typed schema slots and a second page");
        var pinned = owner.Capture().Ticket;
        string schema = owner.Schema;
        details.SetDraft(0, "edited"); details.SetDraft(1, "2.5"); details.SetDraft(2, "4");
        details.Cycle(3); details.Cycle(4);
        details.Page(1); details.SetDraft(0, "second"); details.Cycle(1);
        details.Page(-1);
        check(details.Rows[0].Value == "edited" && details.Rows[1].Value == "2.5"
            && details.Rows[3].Value == "true" && details.Rows[4].Value == "B",
            "typed text, number, boolean and choice drafts survive property page changes");
        details.Page(1);
        check(details.Rows[0].Value == "second" && details.Rows[1].Value == "true",
            "second property page retains its own drafts");
        check(details.Apply() && owner.ApplyCalls == 1 && owner.Commands == 1 && owner.LastPropertyTicket == new NativeStudioPropertyTicket(pinned, schema)
            && owner.LastPatch.Count == 7 && owner.LastPatch["name"] == "edited" && owner.LastPatch["extra"] == "second"
            && owner.Values["enabled"] == "true" && owner.Values["mode"] == "B" && details.PageIndex == 0,
            "all edited pages commit once using their original owner revision and schema ticket");
        details.SetDraft(0, "pending");
        owner.ExternalChange(); owner.Capture();
        string saved = owner.Values["name"];
        check(!details.Apply() && owner.Commands == 1 && owner.Values["name"] == saved && details.Rows[0].Value == "pending",
            "external owner revision rejects pinned property drafts even after a fresh owner capture");
        details.Reset();
        check(details.Rows[0].Value == saved && details.PageIndex == 0, "explicit reset discards stale drafts and reloads the current owner");
        details.SetDraft(0, "schema"); owner.Schema = "schema-2"; owner.Capture();
        check(!details.Apply() && owner.Commands == 1 && owner.Values["name"] == saved,
            "schema-only changes reject the originally pinned property schema");
        details.Reset(); details.SetDraft(0, "reset");
        check(details.Apply() && owner.LastPropertyTicket.Schema == "schema-2" && owner.Values["name"] == "reset",
            "reset reloads the new schema and permits a subsequent edit");

        int commands = owner.Commands;
        details.SetDraft(0, "valid"); details.SetDraft(1, "NaN");
        check(!details.Apply() && owner.Commands == commands && owner.Values["name"] == "reset" && owner.Values["speed"] == "2.5"
            && owner.LastPatch["speed"] == "NaN",
            "owner receives invalid numeric drafts and validates the whole patch before any command or partial write");
        details.Reset(); details.SetDraft(0, new string('x', 9));
        check(!details.Apply() && owner.Commands == commands && owner.Values["name"] == "reset" && owner.LastPatch["name"].Length == 9,
            "owner enforces schema text bounds atomically");
        details.Reset(); details.SetDraft(0, new string('x', 4097));
        check(details.Rows[0].Value == "reset", "details bounds raw draft allocation before owner validation");
        details.SetDraft(3, "false"); details.SetDraft(4, "A");
        check(details.Rows[3].Value == "true" && details.Rows[4].Value == "B", "boolean and choice fields accept typed cycling only");

        foreach (NativeStudioCreateKind kind in Enum.GetValues<NativeStudioCreateKind>())
        {
            var ticket = owner.Capture().Ticket;
            int before = owner.CreateCalls;
            check(details.Create(ticket, kind) && owner.CreateCalls == before + 1 && owner.LastCreate == (ticket, kind),
                $"details delegates {kind} creation exactly once with the supplied owner ticket");
        }
        var duplicateTicket = owner.Capture().Ticket;
        check(details.Duplicate(duplicateTicket) && owner.DuplicateCalls == 1 && owner.LastDuplicate == duplicateTicket,
            "details delegates duplicate exactly once with its supplied owner ticket");
        check(!details.Create(pinned, NativeStudioCreateKind.Box) && !details.Duplicate(pinned),
            "details lets the owner reject stale create and duplicate tickets");

        details.RequestDelete(owner.Capture().Ticket);
        var confirmation = details.Delete;
        int beforeDelete = owner.DeleteCalls;
        check(confirmation != null && details.ConfirmDelete() && !details.ConfirmDelete() && owner.DeleteCalls == beforeDelete + 1,
            "delete requires one prepared confirmation and repeated confirmation cannot execute twice");
        details.RequestDelete(owner.Capture().Ticket);
        owner.ExternalChange();
        var replacement = owner.PrepareObjectDelete(owner.Capture().Ticket);
        beforeDelete = owner.DeleteCalls; commands = owner.Commands;
        check(!details.ConfirmDelete() && owner.DeleteCalls == beforeDelete + 1 && owner.Commands == commands
            && owner.Pending == replacement && details.Delete == null,
            "stale delete confirmation cannot mutate the owner or consume a newer unrelated confirmation");
        details.RequestDelete(owner.Capture().Ticket); details.CancelDelete();
        check(details.Delete == null && !details.ConfirmDelete() && owner.Pending == null,
            "canceling delete retires the prepared confirmation without executing it");
        check(owner.CloseCalls == 0 && owner.SelectCalls == 0 && owner.ExecuteCalls == 0 && owner.DisposeCalls == 0,
            "details commands never close, dispose or invoke unrelated retained owner operations");

        FileChecks(check);
    }

    private static void FileChecks(Action<bool, string> check)
    {
        string root = Path.Combine(Path.GetTempPath(), "studio-details-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string file = Path.Combine(root, "source.json");
            File.WriteAllBytes(file, new byte[] { 1, 2, 3, 4 });
            var version = NativeStudioFileVersion.Capture(file);
            check(version.Exists && version.Length == 4 && version.Matches(), "file version matches unchanged private synthetic source bytes");
            File.WriteAllBytes(file, new byte[] { 4, 3, 2, 1 });
            File.SetLastWriteTimeUtc(file, new DateTime(version.ModifiedUtcTicks, DateTimeKind.Utc));
            var replaced = NativeStudioFileVersion.Capture(file);
            check(replaced.Length == version.Length && replaced.ModifiedUtcTicks == version.ModifiedUtcTicks
                && replaced.Sha256 != version.Sha256 && !version.Matches(),
                "file hash detects same-length same-mtime source replacement");
            string missing = Path.Combine(root, "missing.json");
            var absent = NativeStudioFileVersion.Capture(missing, allowMissing: true);
            check(!absent.Exists && absent.Matches(), "missing save target has an inspectable stable version");
            File.WriteAllBytes(missing, new byte[] { 9 });
            check(!absent.Matches(), "a newly appearing save target invalidates missing-target confirmation");
            string leaf = Path.Combine(root, "leaf.json");
            File.CreateSymbolicLink(leaf, file);
            bool rejected = false;
            try { NativeStudioFileVersion.Capture(leaf); } catch (IOException) { rejected = true; }
            check(rejected, "file version rejects a linked leaf before trusting source bytes");
            File.Delete(leaf); File.CreateSymbolicLink(leaf, Path.Combine(root, "absent-target.json")); rejected = false;
            try { NativeStudioFileVersion.Capture(leaf, allowMissing: true); } catch (IOException) { rejected = true; }
            check(rejected, "missing linked save target cannot bypass the direct-file path guard");
            string target = Path.Combine(root, "real"); Directory.CreateDirectory(target);
            string targetFile = Path.Combine(target, "project.json"); File.WriteAllBytes(targetFile, new byte[] { 5, 6 });
            string alias = Path.Combine(root, "alias"); Directory.CreateSymbolicLink(alias, target);
            string canonicalTarget = NativeStudioFileVersion.Capture(targetFile).Path;
            var viaAlias = NativeStudioFileVersion.Capture(Path.Combine(alias, "project.json"));
            check(viaAlias.Path == canonicalTarget && viaAlias.Matches(),
                "parent directory alias resolves to the safe canonical project path");
            Directory.Delete(alias);
            string other = Path.Combine(root, "other"); Directory.CreateDirectory(other);
            File.WriteAllBytes(Path.Combine(other, "project.json"), new byte[] { 7, 8 }); Directory.CreateSymbolicLink(alias, other);
            check(viaAlias.Matches() && viaAlias.Path == canonicalTarget,
                "later parent-alias retargeting cannot redirect an already captured canonical project version");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private sealed class Owner : INativeStudioEditorSession, INativeStudioEditorObjects, INativeStudioEditorProperties
    {
        private readonly Guid _lease = Guid.NewGuid(), _selection = Guid.NewGuid();
        private ulong _state;
        private long _revision;
        internal string Schema = "schema-1";
        internal readonly Dictionary<string, string> Values = new(StringComparer.Ordinal)
        { ["name"] = "initial", ["speed"] = "1", ["count"] = "2", ["enabled"] = "false", ["mode"] = "A",
          ["note1"] = "one", ["note2"] = "two", ["note3"] = "three", ["extra"] = "tail", ["visible"] = "false" };
        internal int Commands, ApplyCalls, CreateCalls, DuplicateCalls, DeleteCalls, CloseCalls, DisposeCalls, SelectCalls, ExecuteCalls;
        internal NativeStudioPropertyTicket LastPropertyTicket;
        internal Dictionary<string, string> LastPatch = new();
        internal (NativeStudioEditTicket, NativeStudioCreateKind) LastCreate;
        internal NativeStudioEditTicket LastDuplicate;
        internal NativeStudioObjectDelete? Pending;
        private NativeStudioEditTicket Ticket => new(_lease, _state, _revision, _selection);
        public NativeStudioEditorSnapshot Capture() => new(_revision, Ticket, true, "synthetic", "", Commands > 0, Commands > 0, false,
            "selected", new[] { new NativeStudioObjectRow(_selection, "fixture", false) }, "");
        internal void ExternalChange() { _state++; _revision++; }
        private NativeStudioPropertyField Field(string id, NativeStudioPropertyKind kind = NativeStudioPropertyKind.Text,
            int length = 8, double? min = null, double? max = null, IReadOnlyList<string>? choices = null)
            => new(id, id, kind, Values[id], length, min, max, choices);
        public NativeStudioPropertySnapshot ReadProperties(NativeStudioEditTicket ticket) => new(new(ticket, Schema), new[]
        {
            Field("name"), Field("speed", NativeStudioPropertyKind.Number, min: 0, max: 10),
            Field("count", NativeStudioPropertyKind.Integer, min: 0, max: 10), Field("enabled", NativeStudioPropertyKind.Boolean),
            Field("mode", NativeStudioPropertyKind.Choice, choices: new[] { "A", "B" }), Field("note1"), Field("note2"), Field("note3"),
            Field("extra"), Field("visible", NativeStudioPropertyKind.Boolean)
        });
        public NativeStudioResult ApplyProperties(NativeStudioPropertyTicket ticket, IReadOnlyDictionary<string, string> patch)
        {
            ApplyCalls++; LastPropertyTicket = ticket; LastPatch = new(patch);
            if (ticket.Owner != Ticket || ticket.Schema != Schema) return new(false, "STALE PROPERTY TICKET");
            var fields = ReadProperties(Ticket).Fields.ToDictionary(f => f.Id);
            foreach (var change in patch)
            {
                if (!fields.TryGetValue(change.Key, out var field) || change.Value.Length > field.MaxLength) return new(false, "INVALID FIELD");
                switch (field.Kind)
                {
                    case NativeStudioPropertyKind.Number:
                    case NativeStudioPropertyKind.Integer:
                        if (!double.TryParse(change.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number)
                            || !double.IsFinite(number) || number < field.Min || number > field.Max
                            || field.Kind == NativeStudioPropertyKind.Integer && number != Math.Truncate(number)) return new(false, "INVALID NUMBER");
                        break;
                    case NativeStudioPropertyKind.Boolean:
                        if (change.Value is not ("true" or "false")) return new(false, "INVALID BOOLEAN"); break;
                    case NativeStudioPropertyKind.Choice:
                        if (!field.Choices!.Contains(change.Value)) return new(false, "INVALID CHOICE"); break;
                }
            }
            if (patch.Count > 0) { foreach (var change in patch) Values[change.Key] = change.Value; Commands++; ExternalChange(); }
            return new(true, "APPLIED");
        }
        public NativeStudioResult CreateObject(NativeStudioEditTicket ticket, NativeStudioCreateKind kind)
        { CreateCalls++; LastCreate = (ticket, kind); return Command(ticket); }
        public NativeStudioResult DuplicateObjects(NativeStudioEditTicket ticket)
        { DuplicateCalls++; LastDuplicate = ticket; return Command(ticket); }
        private NativeStudioResult Command(NativeStudioEditTicket ticket)
        { if (ticket != Ticket) return new(false, "STALE OWNER"); Commands++; ExternalChange(); return new(true, "DONE"); }
        public NativeStudioObjectDelete? PrepareObjectDelete(NativeStudioEditTicket ticket)
            => ticket == Ticket ? Pending = new(ticket, Guid.NewGuid(), "fixture") : null;
        public NativeStudioResult ConfirmObjectDelete(NativeStudioObjectDelete confirmation)
        {
            DeleteCalls++;
            if (confirmation != Pending || confirmation.Owner != Ticket) return new(false, "STALE DELETE");
            Pending = null; return Command(confirmation.Owner);
        }
        public void CancelObjectDelete() => Pending = null;
        public NativeStudioResult Select(NativeStudioEditTicket ticket, Guid objectId) { SelectCalls++; return new(false, "UNUSED"); }
        public NativeStudioResult Execute(NativeStudioEditTicket ticket, NativeStudioEditorOperation operation, NativeStudioEditorValues values)
        { ExecuteCalls++; return new(false, "UNUSED"); }
        public NativeStudioResult TryClose() { CloseCalls++; return new(true, "CLOSED"); }
        public void Dispose() => DisposeCalls++;
    }
}
