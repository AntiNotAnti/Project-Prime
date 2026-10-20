using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MphRead.Mods.Campaign;
using Source = MphRead.Mods.Campaign.CampaignCreditsSource;
using Duration = MphRead.Mods.Campaign.CampaignCreditsAnimationDuration;
using Timeline = MphRead.Mods.Campaign.CampaignCreditsTimeline;

// Opt-in, metadata-only acceptance against a separately owned AMHE1 extraction.
// No model payloads, game text, source bytes, images or saves are exported.
internal static class OwnedTimelineProbe
{
    private const string ExpectedMarm = "1140e510b420a9da5f4547154f0c164511a697ccaf5a249c0652625806a9d059";
    private const string ExpectedText = "742e6819be3e9f8b55be65e66cc8a4d9a8aefc0a93d2f1e4813bb663d262d114";
    private sealed record AnimationPin(string Path, string Sha256, int Bytes, int Frames);
    private static readonly AnimationPin[] Pins =
    {
        new("gameoptions/ok_Hidden_Idle_Anim.bin", "193971d218b7342e45052fcabd0910d0d66095fb34d9b4a995a758f7ce114b9e", 304, 7),
        new("gameoptions/ok_Idle_Anim.bin", "998d81b6885fac0cf8e553677db59d5f3814202ce2ae6de0f91a026e738ddd0c", 296, 1),
        new("gameoptions/ok_Idle_Hidden_Anim.bin", "175bb330503edbaa513a99cbb37007b705dd93b73efa0f5deed6b4ede3caea29", 300, 5),
        new("gameoptions/ok_Selected_Anim.bin", "998d81b6885fac0cf8e553677db59d5f3814202ce2ae6de0f91a026e738ddd0c", 296, 1),
        new("gameoptions/ok_Selected_Hidden_Anim.bin", "a0dabc9b0e7152f4aada1ba5bb3dc3f92b3170b7c1f115812bbbef222b01a443", 320, 24),
        new("logo_screen/MAYA/flare2_Hidden_Idle_Anim.bin", "a1c9af11acafd6ed560f05938cf927cba21f5d388335d8885db70f53872d3d8a", 1352, 191),
        new("logo_screen/MAYA/flare2_Idle_Hidden_Anim.bin", "9fd3cbdb70b524ea11c70f4443e11e51a7fa65715ab86ba42cebbf6f3b52cc3d", 400, 1),
        new("logo_screen/MAYA/flare3_Hidden_Idle_Anim.bin", "f3dca70097f7fe550734df84e3153337c9b99b090fc4762d47a20de7700f6ca7", 1352, 191),
        new("logo_screen/MAYA/flare3_Idle_Hidden_Anim.bin", "728d246aa884c3403dd3e2dea60206bd34c1db5197a17b6c108737be73a3e361", 400, 1),
        new("logo_screen/MAYA/logo2_Hidden_Idle_Anim.bin", "1d87b65e1bd1cf5a111a7e26bbb6254f391cdde44d7eb6b1ac45f1bc581d7e10", 2400, 191),
        new("logo_screen/MAYA/logo2_Idle_Hidden_Anim.bin", "f34f6366534cbc119a418902e221d99819b9a9e49c910ce79b74d06dc8369376", 1636, 1),
        new("logo_screen/MAYA/logo3_Hidden_Idle_Anim.bin", "eddd200c2f864ebfe1fc751986e023b9940f61f7ac8dd6cb82533b63e55927f4", 2400, 191),
        new("logo_screen/MAYA/logo3_Idle_Hidden_Anim.bin", "0c33ea91069518c193563deca2d44dd88c4e2d632d09eb3377620f2077d83af8", 1636, 1),
        new("main menu/toplogoR_Hidden_Idle_Anim.bin", "fd3ae105e15af2b57e672664454f79b4d35c3b3785a34b43559cedf1f1f6e21e", 352, 55),
        new("main menu/toplogoR_Idle_Anim.bin", "99f3e05d430d4526dd4f2e37ab10db0b6d076629233028de2e5c6ab865c1af49", 296, 1),
        new("main menu/toplogoR_Idle_Hidden_Anim.bin", "09c4d9edfdd3c33f4feaba369ed2433b097a7b3be6ee33129eff4430cab192bc", 344, 47),
        new("tostart/logofinal_Hidden_Idle_Anim.bin", "58b44907bf0731b67d7299b8227f215140745e72517a2ff853aa91510e30b06b", 4692, 246),
        new("tostart/logofinal_Idle_Anim.bin", "922ba68c9412dd4a56289aeed390c6644274743f5cbc76ebb96b693baf85b72f", 1256, 1),
    };
    private sealed record Milestone(int Tick, string Kind, int Page, int? Destination = null);
    private sealed class InputProof
    {
        internal required string Path;
        internal required string Role;
        internal int Id;
        internal int Maximum;
        internal long Bytes;
        internal required string Before;
        internal string? After;
    }

    internal static int Run(string ownedRoot)
    {
        string stage = "Initialize";
        int checks = 0;
        var inputs = new List<InputProof>();
        var branches = new List<object>();
        var animations = new Dictionary<string, Duration.Snapshot>(StringComparer.Ordinal);
        Source.Document? document = null;
        CampaignCreditsFrameTable? table = null;
        string assemblyPath = typeof(Timeline).Assembly.Location;
        string assemblyBefore = HashFile(assemblyPath), assemblyAfter = assemblyBefore;
        string? errorType = null, errorMessageSha256 = null;
        try
        {
            string root = Path.GetFullPath(ownedRoot);
            stage = "ReadFrontend";
            byte[] marm = ReadTracked(Resolve(root, "frontend/metroidhunters.bin"), "Marm", 0, Source.MaxInputBytes);
            byte[] text = ReadTracked(Resolve(root, "frontend/metroidhunters_text_en.bin"), "Text", 0, Source.MaxInputBytes);
            Check(marm.Length == 512232 && text.Length == 30604 && Hash(marm) == ExpectedMarm
                && Hash(text) == ExpectedText, "Pinned AMHE1 frontend identities differ.");
            document = Source.Read(marm, text);
            Check(document.MarmSha256 == ExpectedMarm && document.TextSha256 == ExpectedText
                && document.GlobalPageCount == 67 && document.GlobalWidgetCount == 4699 && document.GlobalStringCount == 629,
                "Admitted frontend identity differs.");
            Check(document.Pages.Select(p => p.Index).SequenceEqual(new[] { 6, 7, 8, 9, 10, 11, 12, 49, 50 })
                && document.Pages.Sum(p => p.Items.Count) == 74 && document.Widgets.Count == 39 && document.Strings.Count == 48,
                "Selected frontend inventory differs.");
            string[] paths = document.Widgets.Values.Select(w => w.Files.Count == 2 ? w.Files[1].Path : null)
                .Where(p => p != null).Select(p => p!).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            Check(paths.SequenceEqual(Pins.Select(p => p.Path)), "Required animation path inventory differs.");
            stage = "ReadAnimationDurations";
            for (int i = 0; i < Pins.Length; i++)
            {
                AnimationPin pin = Pins[i];
                byte[] bytes = ReadTracked(Resolve(root, pin.Path), "Animation", i, Duration.MaxInputBytes);
                Duration.Snapshot duration = Duration.Read(bytes);
                Check(bytes.Length == pin.Bytes && duration.Sha256 == pin.Sha256 && duration.ByteLength == pin.Bytes
                    && duration.SlotCount == 1 && duration.NodeFrames == pin.Frames && duration.MaterialFrames == pin.Frames
                    && duration.TexcoordFrames == 0 && duration.TextureFrames == 0 && duration.WidgetFrames == pin.Frames,
                    "Pinned animation duration metadata differs.");
                animations.Add(pin.Path, duration);
            }
            stage = "BindFrameTable";
            table = CampaignCreditsFrameTable.Bind(document, animations);
            Check(table.Frames.Count == 39 && table.Animations.Count == 18
                && table.MarmSha256 == ExpectedMarm && table.TextSha256 == ExpectedText,
                "Frame table binding differs.");
            foreach (var widget in document.Widgets)
            {
                Check(widget.Value.Files.Count == 2 && widget.Value.Files[1].Path is { } path
                    && table.Frames[widget.Key] == animations[path].WidgetFrames,
                    "Widget duration did not retain its exact admitted animation binding.");
            }
            // Each independent timeline shares immutable admitted values, never native resources.
            foreach (bool good in new[] { false, true })
            {
                stage = good ? "GoodTimeline" : "BadTimeline";
                branches.Add(VerifyBranch(document, table, good));
            }
        }
        catch (Exception ex)
        {
            errorType = ex.GetType().Name;
            errorMessageSha256 = Hash(Encoding.UTF8.GetBytes(ex.Message));
        }
        finally
        {
            foreach (InputProof input in inputs)
            {
                try { input.After = HashFile(input.Path, input.Maximum); }
                catch (Exception ex)
                {
                    errorType ??= ex.GetType().Name;
                    errorMessageSha256 ??= Hash(Encoding.UTF8.GetBytes(ex.Message));
                }
            }
            try { assemblyAfter = HashFile(assemblyPath); }
            catch (Exception ex)
            {
                errorType ??= ex.GetType().Name;
                errorMessageSha256 ??= Hash(Encoding.UTF8.GetBytes(ex.Message));
            }
        }
        bool unchanged = inputs.Count == 20 && inputs.All(i => i.Before == i.After);
        bool passed = errorType == null && unchanged && assemblyBefore == assemblyAfter && branches.Count == 2;
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            Schema = 1, Passed = passed, Stage = stage, ErrorType = errorType, ErrorMessageSha256 = errorMessageSha256,
            Checks = checks, PureTimelineVerificationOnly = true, RenderingActivated = false, SceneCreated = false,
            ModelPayloadsRead = false, UiAudioOrSaveTouched = false, SourcePayloadsWritten = false,
            GameTextExported = false, SourceUnchanged = unchanged, AssemblySha256Before = assemblyBefore,
            AssemblySha256After = assemblyAfter, AssemblyUnchanged = assemblyBefore == assemblyAfter,
            MarmSha256 = document?.MarmSha256, TextSha256 = document?.TextSha256,
            ReferencedWidgetCount = table?.Frames.Count ?? 0, UniqueAnimationCount = animations.Count,
            AnimationBytes = inputs.Where(i => i.Role == "Animation").Sum(i => i.Bytes),
            SourceInputs = inputs.Select(i => new { i.Role, i.Id, i.Bytes, Sha256Before = i.Before, Sha256After = i.After }).ToArray(),
            WidgetFrames = table?.Frames.OrderBy(p => p.Key).Select(p => new { WidgetId = p.Key, Frames = p.Value }).ToArray(),
            Branches = branches
        }, new JsonSerializerOptions { WriteIndented = true }));
        return passed ? 0 : 1;

        byte[] ReadTracked(string path, string role, int id, int maximum)
        {
            string before = HashFile(path, maximum);
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            long length = stream.Length;
            if (length < 1 || length > maximum) throw new InvalidDataException("Owned input exceeds its bounded extent.");
            inputs.Add(new() { Path = path, Role = role, Id = id, Maximum = maximum, Bytes = length, Before = before });
            byte[] bytes = new byte[(int)length];
            stream.ReadExactly(bytes);
            if (stream.ReadByte() != -1 || stream.Length != length || Hash(bytes) != before)
                throw new InvalidDataException("Owned input changed during admission.");
            return bytes;
        }
        void Check(bool condition, string message)
        {
            checks++;
            if (!condition) throw new InvalidDataException(message);
        }
        object VerifyBranch(Source.Document admitted, CampaignCreditsFrameTable frames, bool good)
        {
            using var timeline = new Timeline(admitted, frames, good);
            using var retirement = new Timeline(admitted, frames, good);
            var events = new List<Milestone>();
            var previous = timeline.Snapshot();
            Check(previous is { Page: 6, PendingPage: null, PageFrame: 0, SettledFrame: -1, Closed: false, Failure: null },
                "Initial timeline state differs.");
            events.Add(new(0, "Enter", 6));
            for (int tick = 1; tick <= 2740; tick++)
            {
                Check(timeline.Tick() && retirement.Tick(), "Timeline failed before results settlement.");
                var snapshot = timeline.Snapshot();
                Check(!snapshot.Closed && snapshot.Failure == null, "Timeline unexpectedly closed or failed.");
                if (snapshot.Page != previous.Page) events.Add(new(tick, "Enter", snapshot.Page));
                if (snapshot.SettledFrame == 0 && (snapshot.Page != previous.Page || previous.SettledFrame < 0))
                    events.Add(new(tick, "Settled", snapshot.Page));
                if (snapshot.PendingPage is { } next && snapshot.PendingPage != previous.PendingPage)
                    events.Add(new(tick, "ExitRequested", snapshot.Page, next));
                previous = snapshot;
            }
            int resultPage = good ? 49 : 50;
            Check(events.SequenceEqual(ExpectedMilestones(resultPage)), "Owned source timeline milestones differ from independent reference simulation.");
            Check(previous.Page == resultPage && previous.PendingPage == null && previous.SettledFrame == 0
                && previous.PageFrame == 130 && !previous.Closed && !timeline.TryTakeReturn(),
                "Results were not settled at the independently measured tick.");
            Source.Page page = admitted.Pages.Single(p => p.Index == resultPage);
            Source.Item button = page.Items.Single(i => i.Actions.Any(a => a.Calls.Any(c => c.A == 0 && c.B == 206)));
            int expectedItem = good ? 12 : 11;
            Check(button.Index == expectedItem && button.Actions.Count == 1 && button.Actions[0].Kind == 1,
                "Results acknowledgment action identity differs.");
            Check(!timeline.InvokeItemAction(good ? 50 : 49, expectedItem, 0)
                && !timeline.InvokeItemAction(12, 3, 0) && !timeline.TryTakeReturn(),
                "Stale or credits item intent was accepted.");
            Check(timeline.InvokeItemAction(resultPage, expectedItem, 0), "Exact results item intent was refused.");
            Check(timeline.Snapshot() is { Closed: true, Failure: null } && !timeline.Tick()
                && !timeline.InvokeItemAction(resultPage, expectedItem, 0), "Closed timeline accepted more work.");
            Check(timeline.TryTakeReturn() && !timeline.TryTakeReturn(), "Results acknowledgment was not exactly once.");
            timeline.Retire();
            Check(!timeline.Tick() && !timeline.TryTakeReturn(), "Consumed/retired timeline retained a stale intent.");
            Check(retirement.Snapshot() is { Closed: false, Failure: null } retireState
                && retireState.Page == resultPage && retireState.SettledFrame == 0
                && retirement.InvokeItemAction(resultPage, expectedItem, 0), "Retirement witness did not reach pending acknowledgment.");
            retirement.Retire();
            Check(retirement.Snapshot() is { Closed: true, Failure: null } && !retirement.Tick()
                && !retirement.InvokeItemAction(resultPage, expectedItem, 0) && !retirement.TryTakeReturn(),
                "Retirement did not discard an unconsumed return intent.");
            return new
            {
                GoodEnding = good, ResultsPage = resultPage, Ticks = 2740, Milestones = events.ToArray(),
                AcknowledgmentItem = expectedItem, AcknowledgmentAction = 0, ReturnConsumedExactlyOnce = true,
                ClosedTimelineRefusedFurtherWork = true, RetirementDiscardedUnconsumedIntent = true,
                IndependentRetirementReplayTicks = 2740
            };
        }
    }

    private static IEnumerable<Milestone> ExpectedMilestones(int resultPage)
    {
        int[] pages = { 6, 7, 8, 9, 10, 11, 12, resultPage };
        int[] enter = { 0, 421, 772, 1123, 1474, 1855, 2206, 2610 };
        int[] settled = { 261, 612, 963, 1314, 1665, 2046, 2500, 2740 };
        int[] exit = { 401, 752, 1103, 1454, 1835, 2186, 2560 };
        for (int i = 0; i < pages.Length; i++)
        {
            yield return new(enter[i], "Enter", pages[i]);
            yield return new(settled[i], "Settled", pages[i]);
            if (i < exit.Length) yield return new(exit[i], "ExitRequested", pages[i], pages[i + 1]);
        }
    }
    private static string Resolve(string root, string relative)
    {
        string full = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!full.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, comparison))
            throw new InvalidDataException("Owned source path escaped its supplied root.");
        return full;
    }
    private static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static string HashFile(string path, int maximum = 256 * 1024 * 1024)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length < 1 || stream.Length > maximum)
            throw new InvalidDataException("Owned hash input exceeds its bounded extent.");
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }
}
