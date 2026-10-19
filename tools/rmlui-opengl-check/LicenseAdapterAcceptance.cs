using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.RmlUi.Pages.License;

internal static class LicenseAdapterAcceptance
{
    internal static void Run()
    {
        Guid firstAccount = Guid.Parse("10000000-0000-0000-0000-000000000001");
        Guid secondAccount = Guid.Parse("20000000-0000-0000-0000-000000000002");
        var fixture = new Fixture();
        var adapter = new CurrentHunterLicenseOverviewReader(fixture.Read, fixture.Hide);
        var firstTask = adapter.LoadAsync(CancellationToken.None);
        fixture.TakeRead().SetResult(Snapshot(firstAccount));
        NativeLicenseOverview first = Await(firstTask);
        Check(first.Verified && first.PrivacyLease > 0 && first.PublicVisibility == "PUBLIC",
            "production adapter projects verified career with an opaque privacy lease");

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        int readsBeforeCancelled = fixture.ReadCalls;
        bool cancellationObserved = false;
        try { Await(adapter.LoadAsync(cancelled.Token)); }
        catch (OperationCanceledException) { cancellationObserved = true; }
        Check(cancellationObserved && fixture.ReadCalls == readsBeforeCancelled
            && fixture.Reads.Count == 0,
            "pre-cancelled older worker never invokes loader or allocates a newer lease");
        Check(Await(adapter.HidePublicProfileAsync(first.PrivacyLease, CancellationToken.None))
            && fixture.TakeWrite() == firstAccount,
            "pre-cancelled older worker preserves the current lease and captured owner");

        var nextTask = adapter.LoadAsync(CancellationToken.None);
        Check(!Await(adapter.HidePublicProfileAsync(first.PrivacyLease, CancellationToken.None))
            && fixture.Writes.Count == 0,
            "fresh load immediately retires old privacy admission before completing");
        fixture.TakeRead().SetResult(Snapshot(secondAccount));
        NativeLicenseOverview second = Await(nextTask);
        Check(second.PrivacyLease > first.PrivacyLease
            && !Await(adapter.HidePublicProfileAsync(first.PrivacyLease, CancellationToken.None))
            && Await(adapter.HidePublicProfileAsync(second.PrivacyLease, CancellationToken.None))
            && fixture.TakeWrite() == secondAccount && fixture.Writes.Count == 0,
            "fresh successful load retires old lease and captures only the new owner");

        foreach (HunterLicenseSnapshot invalid in new[]
            { Snapshot(firstAccount, connected: false), Snapshot(Guid.Empty), InvalidId() })
        {
            var task = adapter.LoadAsync(CancellationToken.None);
            fixture.TakeRead().SetResult(invalid);
            NativeLicenseOverview result = Await(task);
            Check(result.PrivacyLease == 0
                && (invalid.Connected || !result.Verified)
                && !Await(adapter.HidePublicProfileAsync(result.PrivacyLease, CancellationToken.None))
                && !Await(adapter.HidePublicProfileAsync(second.PrivacyLease, CancellationToken.None))
                && fixture.Writes.Count == 0,
                "disconnected, empty and malformed account IDs cannot acquire writable leases");
        }

        var oldTask = adapter.LoadAsync(CancellationToken.None);
        var oldRead = fixture.TakeRead();
        var currentTask = adapter.LoadAsync(CancellationToken.None);
        var currentRead = fixture.TakeRead();
        currentRead.SetResult(Snapshot(secondAccount));
        NativeLicenseOverview current = Await(currentTask);
        oldRead.SetResult(Snapshot(firstAccount));
        NativeLicenseOverview stale = Await(oldTask);
        Check(stale.PrivacyLease == 0 && current.PrivacyLease > 0
            && !Await(adapter.HidePublicProfileAsync(stale.PrivacyLease, CancellationToken.None))
            && Await(adapter.HidePublicProfileAsync(current.PrivacyLease, CancellationToken.None))
            && fixture.TakeWrite() == secondAccount && fixture.Writes.Count == 0,
            "late superseded load cannot grant a lease or replace the current owner");

        var presentation = new NativeLicenseOverviewController.Snapshot(1, false, first,
            "SYNTHETIC FIXTURE", NativeLicenseFace.Overview, 0, 1, first.History.Count,
            first.History, true, false);
        string projected = JsonSerializer.Serialize(first) + String.Join(" ",
            NativeLicenseOverviewBindings.Create(presentation).Values.Select(value => value.Text));
        Check(!projected.Contains(firstAccount.ToString()) && !projected.Contains(secondAccount.ToString())
            && !projected.Contains("30000000-0000-0000-0000-000000000003")
            && !projected.Contains("private-fixture@example.test"),
            "production adapter DTO and RML bindings omit account UUID, match UUID and email");
        AccountAndPublicLeases(firstAccount, secondAccount);
        AuthSessionLeases(firstAccount);
    }

    private static void AuthSessionLeases(Guid owner)
    {
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;
        FieldInfo current = typeof(HunterLicenseClient).GetField("_currentSession", flags)!;
        Type sessionType = typeof(HunterLicenseClient).GetNestedType("AuthSession", BindingFlags.NonPublic)!;
        object? previous = current.GetValue(null);
        object Session()
        {
            object session = Activator.CreateInstance(sessionType, nonPublic: true)!;
            sessionType.GetProperty("ExpiresAt")!.SetValue(session, DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 3600);
            sessionType.GetProperty("AccessToken")!.SetValue(session, "SYNTHETIC-LICENSE-ACCESS-SECRET");
            sessionType.GetProperty("RefreshToken")!.SetValue(session, "SYNTHETIC-LICENSE-REFRESH-SECRET");
            return session;
        }
        try
        {
            object first = Session(), replacement = Session();
            current.SetValue(null, first);
            var fixture = new Fixture();
            var pendingWrite = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int pendingCalls = 0;
            var adapter = new CurrentHunterLicenseOverviewReader(fixture.Read,
                (_, _) => { ++pendingCalls; return pendingWrite.Task; }, fixture.Show, fixture.AccountAction,
                HunterLicenseClient.IsNativeAccountLeaseCurrent);
            NativeLicenseOverview Load(object? witness)
            {
                var loading = adapter.LoadAsync(CancellationToken.None);
                var source = Snapshot(owner); source.AccountWitness = witness;
                fixture.TakeRead().SetResult(source);
                return Await(loading);
            }
            var firstRecord = Load(first);
            Check(adapter.IsCurrent(firstRecord), "production adapter associates numeric license lease with the exact real auth session witness");
            var sourceProjection = Snapshot(owner); sourceProjection.AccountWitness = first;
            string encoded = JsonSerializer.Serialize(sourceProjection) + JsonSerializer.Serialize(firstRecord);
            Check(!encoded.Contains("SYNTHETIC-LICENSE-ACCESS-SECRET")
                && !encoded.Contains("SYNTHETIC-LICENSE-REFRESH-SECRET") && !encoded.Contains("AccountWitness")
                && !typeof(NativeLicenseOverview).GetProperties().Any(p => p.PropertyType == typeof(object)),
                "service witness is ignored by serialization and safe overview gains no credential object carrier");

            current.SetValue(null, replacement);
            Check(!adapter.IsCurrent(firstRecord)
                && !Await(adapter.HidePublicProfileAsync(firstRecord.PrivacyLease, CancellationToken.None))
                && !Await(adapter.ShowPublicProfileAsync(firstRecord.PrivacyLease, CancellationToken.None))
                && !Await(adapter.ExecuteAccountAsync(firstRecord.PrivacyLease, NativeLicenseAccountAction.LinkGoogle, "", "", CancellationToken.None))
                && pendingCalls == 0 && fixture.PublicWrites.Count == 0 && fixture.AccountWrites.Count == 0,
                "same-UUID session replacement rejects stale privacy/public/account writes before any delegate");
            Check(!Load(first).Verified && !Load(null).Verified,
                "old-session and missing-witness reads cannot grant verified production authority");

            var fresh = Load(replacement);
            Task<bool> writing = adapter.HidePublicProfileAsync(fresh.PrivacyLease, CancellationToken.None);
            Check(pendingCalls == 1 && !writing.IsCompleted,
                "visibility operation captures its auth witness through the pending server readback");
            current.SetValue(null, first); pendingWrite.SetResult(true);
            Check(!Await(writing), "same-UUID account replacement during successful server readback cannot confirm a visibility write");

            // A same-session reader refresh changes numeric admission, but the
            // visibility readback must still test the captured session itself.
            pendingWrite = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var sameSession = Load(first);
            Task<bool> sameWrite = adapter.HidePublicProfileAsync(sameSession.PrivacyLease, CancellationToken.None);
            var advanced = Load(first); pendingWrite.SetResult(true);
            Check(advanced.PrivacyLease != sameSession.PrivacyLease && Await(sameWrite)
                && adapter.IsCurrent(advanced) && adapter.IsCurrent(sameSession)
                && !Await(adapter.ShowPublicProfileAsync(sameSession.PrivacyLease, CancellationToken.None)),
                "numeric readback advancement cannot substitute for or falsely invalidate the captured same-session auth witness");
            sessionType.GetProperty("ExpiresAt")!.SetValue(first, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            Check(!adapter.IsCurrent(advanced)
                && !Await(adapter.ShowPublicProfileAsync(advanced.PrivacyLease, CancellationToken.None))
                && fixture.PublicWrites.Count == 0,
                "real auth expiry synchronously retires production overview authority and write admission");
        }
        finally { current.SetValue(null, previous); }
        Check(ReferenceEquals(current.GetValue(null), previous),
            "License auth reflection fixture restores the exact previous session without touching stored credentials");
    }

    private static void AccountAndPublicLeases(Guid firstAccount, Guid secondAccount)
    {
        var fixture = new Fixture();
        var adapter = new CurrentHunterLicenseOverviewReader(fixture.Read, fixture.Hide,
            fixture.Show, fixture.AccountAction);
        HunterLicenseSnapshot firstSource = Snapshot(firstAccount);
        firstSource.Account.Providers = new() { "GOOGLE", "discord" };
        var firstTask = adapter.LoadAsync(CancellationToken.None);
        fixture.TakeRead().SetResult(firstSource);
        NativeLicenseOverview first = Await(firstTask);
        Check(first.Account == new NativeLicenseAccountInfo(false, true, true, false, true),
            "account projection exposes exact guest/email/provider booleans with case-insensitive provider names");
        Check(Await(adapter.ShowPublicProfileAsync(first.PrivacyLease, CancellationToken.None))
            && fixture.TakePublic() == firstAccount,
            "public writer receives only the privately captured loaded account UUID");
        Check(Await(adapter.ExecuteAccountAsync(first.PrivacyLease, NativeLicenseAccountAction.SetPassword,
            "submitted-only@example.test", "submitted-only-password", CancellationToken.None)),
            "production account adapter forwards selected action through its private lease");
        AccountCall captured = fixture.TakeAccount();
        Check(captured.Owner == firstAccount && captured.Action == NativeLicenseAccountAction.SetPassword
            && captured.Email == "submitted-only@example.test" && captured.Password == "submitted-only-password",
            "account writer receives captured UUID and one-shot submitted arguments without changing safe DTO");

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        foreach (Func<Task<bool>> operation in new Func<Task<bool>>[]
        {
            () => adapter.ShowPublicProfileAsync(first.PrivacyLease, cts.Token),
            () => adapter.ExecuteAccountAsync(first.PrivacyLease, NativeLicenseAccountAction.SendVerification,
                "submitted-only@example.test", "", cts.Token)
        })
        {
            bool cancelled = false;
            try { Await(operation()); } catch (OperationCanceledException) { cancelled = true; }
            Check(cancelled && fixture.PublicWrites.Count == 0 && fixture.AccountWrites.Count == 0,
                "cancelled public/account calls invoke no credential or visibility delegate");
        }

        var secondTask = adapter.LoadAsync(CancellationToken.None);
        Check(!Await(adapter.ShowPublicProfileAsync(first.PrivacyLease, CancellationToken.None))
            && !Await(adapter.ExecuteAccountAsync(first.PrivacyLease, NativeLicenseAccountAction.LinkGoogle,
                "", "", CancellationToken.None))
            && fixture.PublicWrites.Count == 0 && fixture.AccountWrites.Count == 0,
            "fresh load retires stale public and account delegates before readback completes");
        HunterLicenseSnapshot secondSource = Snapshot(secondAccount);
        secondSource.Account.IsAnonymous = false;
        secondSource.Account.Email = "";
        secondSource.Account.Providers = new() { "GitHub" };
        fixture.TakeRead().SetResult(secondSource);
        NativeLicenseOverview second = Await(secondTask);
        Check(second.Account == new NativeLicenseAccountInfo(true, false, false, true, false),
            "new owner completely replaces account flags without retaining prior email or providers");
        Check(!Await(adapter.ShowPublicProfileAsync(first.PrivacyLease, CancellationToken.None))
            && !Await(adapter.ExecuteAccountAsync(first.PrivacyLease, NativeLicenseAccountAction.LinkDiscord,
                "", "", CancellationToken.None)) && fixture.PublicWrites.Count == 0 && fixture.AccountWrites.Count == 0,
            "old completed leases never invoke account or public delegates for a newer owner");
        Check(Await(adapter.ShowPublicProfileAsync(second.PrivacyLease, CancellationToken.None))
            && fixture.TakePublic() == secondAccount
            && Await(adapter.ExecuteAccountAsync(second.PrivacyLease, NativeLicenseAccountAction.LinkGithub,
                "", "", CancellationToken.None)) && fixture.TakeAccount().Owner == secondAccount,
            "latest lease dispatches account and public actions solely for its captured owner");

        foreach (NativeLicenseOverview overview in new[] { first, second })
        {
            var state = new NativeLicenseOverviewController.Snapshot(1, false, overview,
                "SYNTHETIC FIXTURE", NativeLicenseFace.Account, 0, 1, overview.History.Count,
                overview.History, true, false);
            string safeProjection = JsonSerializer.Serialize(overview) + String.Join(" ",
                NativeLicenseOverviewBindings.Create(state).Values.Select(value => value.Text));
            Check(!safeProjection.Contains(firstAccount.ToString()) && !safeProjection.Contains(secondAccount.ToString())
                && !safeProjection.Contains("private-fixture@example.test")
                && !safeProjection.Contains("submitted-only@example.test")
                && !safeProjection.Contains("submitted-only-password"),
                "account DTO and Account-face bindings contain no private UUID, stored email or submitted credentials");
        }
    }

    private static HunterLicenseSnapshot Snapshot(Guid account, bool connected = true) => new()
    {
        Connected = connected,
        PublicVisible = true,
        Status = "GUEST LICENSE // SUPABASE CONNECTED",
        Account = new() { Email = "private-fixture@example.test", IsAnonymous = true },
        Profile = new() { PlayerId = account.ToString(), DisplayName = "ADAPTER FIXTURE", FavoriteHunter = 0 },
        Stats = new() { GamesPlayed = 1, Wins = 1, Kills = 3, Deaths = 1 },
        Matches = new()
        {
            new() { MatchId = "30000000-0000-0000-0000-000000000003", RoomKey = "",
                Mode = 0, Kills = 3, Deaths = 1, PlayedTicks = 60 }
        }
    };

    private static HunterLicenseSnapshot InvalidId()
    {
        HunterLicenseSnapshot snapshot = Snapshot(Guid.Empty);
        snapshot.Profile.PlayerId = "invalid-account-id";
        return snapshot;
    }

    private static T Await<T>(Task<T> task)
        => task.WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();

    private static void Check(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException(label);
        Console.WriteLine("RML LICENSE ADAPTER PASS " + label);
    }

    private sealed class Fixture
    {
        internal readonly BlockingCollection<TaskCompletionSource<HunterLicenseSnapshot>> Reads = new();
        internal readonly BlockingCollection<Guid> Writes = new();
        internal readonly BlockingCollection<Guid> PublicWrites = new();
        internal readonly BlockingCollection<AccountCall> AccountWrites = new();
        internal int ReadCalls;

        internal Task<HunterLicenseSnapshot> Read(CancellationToken token)
        {
            Interlocked.Increment(ref ReadCalls);
            var completion = new TaskCompletionSource<HunterLicenseSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
            Reads.Add(completion);
            return completion.Task;
        }

        internal Task<bool> Hide(Guid account, CancellationToken token)
        {
            Writes.Add(account);
            return Task.FromResult(true);
        }

        internal Task<bool> Show(Guid account, CancellationToken token)
        {
            PublicWrites.Add(account);
            return Task.FromResult(true);
        }

        internal Task<bool> AccountAction(Guid owner, NativeLicenseAccountAction action,
            string email, string password, CancellationToken token)
        {
            AccountWrites.Add(new(owner, action, email, password));
            return Task.FromResult(true);
        }

        internal TaskCompletionSource<HunterLicenseSnapshot> TakeRead()
            => Reads.TryTake(out var value, TimeSpan.FromSeconds(3)) ? value
                : throw new TimeoutException("Production adapter did not dispatch fixture read");

        internal Guid TakeWrite()
            => Writes.TryTake(out Guid value, TimeSpan.FromSeconds(3)) ? value
                : throw new TimeoutException("Production adapter did not dispatch fixture write");

        internal Guid TakePublic()
            => PublicWrites.TryTake(out Guid value, TimeSpan.FromSeconds(3)) ? value
                : throw new TimeoutException("Production adapter did not dispatch fixture public write");

        internal AccountCall TakeAccount()
            => AccountWrites.TryTake(out var value, TimeSpan.FromSeconds(3)) ? value
                : throw new TimeoutException("Production adapter did not dispatch fixture account action");
    }

    private sealed record AccountCall(Guid Owner, NativeLicenseAccountAction Action, string Email, string Password);
}
