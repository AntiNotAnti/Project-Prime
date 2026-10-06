using System.Reflection;
using System.Security.Cryptography;
using MphRead.Mods.Network;
using MphRead.Mods.Replay;
using MphRead.Mods.StudioReplay;

internal static partial class Program
{
    private static void CheckReplaySeekCancellation(StudioReplayPlayer owner,
        IReadOnlyDictionary<uint, StudioReplayWorldSnapshot> references)
    {
        var player = (PassiveReplayPlayer)owner.GetType().GetField("_player", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;
        var checkpoints = (SortedDictionary<uint, ReplayWorldCheckpoint>)player.GetType()
            .GetField("_checkpoints", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(player)!;
        string[] CheckpointBytes() => checkpoints.Select(pair => pair.Key + ":" + Convert.ToHexString(SHA256.HashData(pair.Value.Bytes))).ToArray();
        float originalRate = owner.Status.Rate; owner.SetRate(1); owner.Advance(TimeSpan.Zero);
        var original = player.Current; StudioReplayWorldSnapshot before = owner.Snapshot();
        string[] bytes = CheckpointBytes(); int count = owner.Status.CheckpointCount; long size = owner.Status.CheckpointBytes;
        Check(before.Frame > 60 && count > 0 && size > 0,
            "seek cancellation fixture starts from a real simulated world with retained checkpoint payloads");

        Task? abandoned = null;
        HoldPreparation(() =>
        {
            long request = owner.RequestSeek(0); owner.Advance(TimeSpan.Zero);
            Check(owner.Status.Preparing && !owner.Status.Ready && owner.AppliedSeekRequestId == request
                && ReferenceEquals(player.Current, original),
                "backward seek stays pending in detached preparation while its presented world is retained");
            abandoned = ((ReplayPreparationJob)player.GetType().GetField("_preparation", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(player)!).Completion;
            owner.CancelSeek(request); owner.Advance(TimeSpan.Zero);
            Check(owner.Status is { Ready: true, Preparing: false, State: "Paused" } && ReferenceEquals(player.Current, original)
                && SameReplayWorld(before, owner.Snapshot()) && owner.Status.CheckpointCount == count
                && owner.Status.CheckpointBytes == size && bytes.SequenceEqual(CheckpointBytes()),
                "canceling pending backward preparation preserves the same world and every gameplay, presentation, graph and checkpoint byte");
        });
        try { abandoned!.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult(); }
        catch (OperationCanceledException) { }
        owner.Advance(TimeSpan.Zero);
        Check(abandoned!.IsCompleted && ReferenceEquals(player.Current, original) && SameReplayWorld(before, owner.Snapshot())
            && bytes.SequenceEqual(CheckpointBytes()), "abandoned preparation completion cannot publish a world after cancellation");

        long queuedRebuild = owner.RequestSeek(0); owner.CancelSeek(queuedRebuild); owner.Advance(TimeSpan.Zero);
        Check(owner.Status is { Ready: true, Preparing: false, State: "Paused" } && !player.Transport.RequestedSeekTarget.HasValue
            && ReferenceEquals(player.Current, original) && SameReplayWorld(before, owner.Snapshot()) && bytes.SequenceEqual(CheckpointBytes()),
            "canceling a seek in the same owner queue clears its unconsumed rebuild request before any worker or world replacement");

        Task? olderAbandoned = null, newerAbandoned = null;
        HoldPreparation(() =>
        {
            long older = owner.RequestSeek(0); owner.Advance(TimeSpan.Zero);
            Check(owner.Status.Preparing && owner.AppliedSeekRequestId == older,
                "older seek owns a pending canonical worker before it is superseded");
            olderAbandoned = ((ReplayPreparationJob)player.GetType().GetField("_preparation", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(player)!).Completion;
            long newer = owner.RequestSeek(60); owner.CancelSeek(older); owner.Advance(TimeSpan.Zero);
            // TakeRebuild transfers a backward target from Transport into the
            // private preparation state before its worker completes.
            uint pendingTarget = (uint)player.GetType().GetField("_preparingTarget", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(player)!;
            Check(owner.Status.Preparing && owner.AppliedSeekRequestId == newer && pendingTarget == 60
                && ReferenceEquals(player.Current, original),
                "canceling an older queued seek preserves the newer exact target and preparation");
            newerAbandoned = ((ReplayPreparationJob)player.GetType().GetField("_preparation", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(player)!).Completion;
            owner.CancelSeek(newer); owner.Advance(TimeSpan.Zero);
            Check(owner.Status.Ready && SameReplayWorld(before, owner.Snapshot()) && bytes.SequenceEqual(CheckpointBytes()),
                "the current newer seek remains cancellable without changing presented world or retained checkpoints");
        });
        try { Task.WhenAll(olderAbandoned!, newerAbandoned!).WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult(); }
        catch (OperationCanceledException) { }
        Check(olderAbandoned!.IsCompleted && newerAbandoned!.IsCompleted && ReferenceEquals(player.Current, original)
            && SameReplayWorld(before, owner.Snapshot()) && bytes.SequenceEqual(CheckpointBytes()),
            "both superseded and canceled workers settle without stale world publication or checkpoint mutation");

        uint candidateTarget = Enumerable.Range(2, (int)Math.Min(before.Frame - 2, 58)).Select(frame => (uint)frame)
            .LastOrDefault(frame => !checkpoints.Keys.Any(saved => saved <= frame && frame - saved < 2)
                && !player.Current.Session.DurableCheckpoints.Any(saved =>
                { uint visible = player.Current.Session.CheckpointVisibleFrame(saved); return visible <= frame && frame - visible < 2; }));
        Check(candidateTarget > 1, "canonical candidate cancellation has a target beyond its retained checkpoint baseline");
        player.Seek(candidateTarget); player.Update(1, 0);
        if (player.GetType().GetField("_preparation", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(player) is ReplayPreparationJob candidatePreparation)
            candidatePreparation.Completion.WaitAsync(TimeSpan.FromSeconds(30)).GetAwaiter().GetResult();
        player.Update(1, 0);
        var candidate = player.GetType().GetField("_candidate", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(player) as PassiveReplayScene;
        Check(candidate is not null
            && player.Preparing && ReferenceEquals(player.Current, original),
            "a genuinely allocated and partially simulated private candidate remains unpublished before cancellation");
        Check(player.CancelPendingSeek(), "canonical owner cancels an allocated private candidate"); owner.Advance(TimeSpan.Zero);
        Check(candidate is { Session.IsActive: false } && player.GetType().GetField("_candidate", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(player) is null
            && owner.Status.Ready && ReferenceEquals(player.Current, original) && SameReplayWorld(before, owner.Snapshot())
            && owner.Status.CheckpointCount == count && owner.Status.CheckpointBytes == size && bytes.SequenceEqual(CheckpointBytes()),
            "candidate cancellation releases the private candidate and preserves all current world and retained checkpoint bytes");

        long settled = owner.RequestSeek(60); WaitReplayReady(owner);
        Check(owner.SettledSeekRequestId == settled && SameReplayWorld(references[60], owner.Snapshot()),
            "a subsequent seek after cancellation reaches exact reference gameplay, presentation and full graph");
        owner.TogglePause(); owner.CancelSeek(settled); owner.Advance(TimeSpan.Zero);
        Check(owner.Status is { State: "Playing", Frame: 60 }, "late cancellation of an already settled seek cannot pause resumed playback");
        // The accumulator accepts an actual elapsed duration. Round the input up
        // to a representable TimeSpan tick so it contains one full 1/60 step.
        owner.Advance(TimeSpan.FromTicks((TimeSpan.TicksPerSecond + 59) / 60));
        Check(owner.Status is { State: "Playing", Frame: 61 }, "resumed playback advances normally after an ignored settled cancellation: "
            + System.Text.Json.JsonSerializer.Serialize(owner.Status));
        owner.Pause(); owner.RequestSeek(before.Frame); WaitReplayReady(owner);
        owner.SetRate(originalRate); owner.Advance(TimeSpan.Zero);
        Check(SameReplayWorld(before, owner.Snapshot()) && owner.Status.Rate == originalRate,
            "seek cancellation leaves future exact restoration and the caller's playback rate usable");

        void HoldPreparation(Action test)
        {
            ThreadPool.GetMinThreads(out int minimum, out int minimumIo); ThreadPool.GetMaxThreads(out int maximum, out int maximumIo);
            using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
            Task? blocker = null;
            try
            {
                Check(ThreadPool.SetMinThreads(1, minimumIo) && ThreadPool.SetMaxThreads(1, maximumIo),
                    "owned fixture can pause the canonical detached seek worker before source preparation");
                blocker = Task.Run(() => { entered.Set(); release.Wait(); });
                Check(entered.Wait(TimeSpan.FromSeconds(3)), "detached seek worker admission barrier is active");
                test();
            }
            finally
            {
                release.Set(); blocker?.GetAwaiter().GetResult();
                ThreadPool.SetMaxThreads(maximum, maximumIo); ThreadPool.SetMinThreads(minimum, minimumIo);
            }
        }
    }
}
