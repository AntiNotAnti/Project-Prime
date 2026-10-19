using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.MapGen;

/// <summary>
/// Dedicated headless process only. Exercises the real publication lock with
/// in-memory definitions and held builders, without package files or user data.
/// </summary>
internal static class MapRegistryPublicationAcceptance
{
    private static readonly TimeSpan Watchdog = TimeSpan.FromSeconds(10);

    internal static int Run()
    {
        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
        FieldInfo definitions = typeof(CustomRooms).GetField("_definitions", flags)!;
        FieldInfo installed = typeof(CustomRooms).GetField("_installed", flags)!;
        object gate = typeof(CustomRooms).GetField("_lock", flags)!.GetValue(null)!;
        object? originalDefinitions, originalInstalled;
        lock (gate)
        {
            originalDefinitions = definitions.GetValue(null);
            originalInstalled = installed.GetValue(null);
        }
        int checks = 0;
        try
        {
            SourceReplacement(); ConcurrentReaders();
            Console.WriteLine($"MAP REGISTRY PUBLICATION HEADLESS PASS checks={checks} scope=in-memory snapshots / actual publication lock / no GL or filesystem");
            return 0;
        }
        finally
        {
            // Every worker is joined by its case before restoring the process snapshot.
            lock (gate)
            {
                definitions.SetValue(null, originalDefinitions);
                installed.SetValue(null, originalInstalled);
            }
        }

        void SourceReplacement()
        {
            var first = new[] { Definition("OLD") };
            Reset(first);
            using var entered = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            var stale = new InstalledMapRegistry();
            var current = new InstalledMapRegistry();
            IReadOnlyList<MapDefinition>? retrySource = null;
            int calls = 0;
            Task<InstalledMapRegistry> reader = Worker(() => CustomRooms.ReadInstalled(source =>
            {
                int call = Interlocked.Increment(ref calls);
                if (call == 1)
                {
                    Check(ReferenceEquals(source, first), "cold builder captures the initial snapshot");
                    entered.Set();
                    Await(release, "release initial builder");
                    return stale;
                }
                if (call != 2) throw new InvalidOperationException("Unexpected registry construction retry.");
                retrySource = source;
                lock (gate) Check(installed.GetValue(null) == null,
                    "discarded stale candidate is never published while the retry builds");
                return current;
            }));
            Task? publisher = null;
            try
            {
                Await(entered, "cold builder enters held construction");
                publisher = Worker(() => { CustomRooms.InstallSnapshot(Definition("NEW")); return true; });
                Join(publisher, "InstallSnapshot completes while registry construction is held");
                Check(!reader.IsCompleted && !release.IsSet,
                    "snapshot publication does not wait for the held builder");
                Check(CustomRooms.Definitions.Select(d => d.Name).SequenceEqual(new[] { "OLD", "NEW" }),
                    "actual InstallSnapshot publishes the replacement definition snapshot");
                lock (gate) Check(installed.GetValue(null) == null,
                    "source replacement has no stale cached registry");
                release.Set();
                Join(reader, "cold reader retries after source replacement");
                Check(calls == 2 && retrySource != null && !ReferenceEquals(retrySource, first)
                    && retrySource.Select(d => d.Name).SequenceEqual(new[] { "OLD", "NEW" }),
                    "retry builds from the newest source reference");
                Check(ReferenceEquals(reader.Result, current) && !ReferenceEquals(reader.Result, stale),
                    "stale constructor result never becomes the returned registry");
                lock (gate) Check(ReferenceEquals(installed.GetValue(null), current),
                    "only the current source registry is cached");
                Cached(current);
            }
            finally
            {
                release.Set();
                Drain(reader, publisher);
            }
        }

        void ConcurrentReaders()
        {
            var source = new[] { Definition("SIMULTANEOUS") };
            Reset(source);
            using var entered = new CountdownEvent(2);
            using var release = new ManualResetEventSlim();
            var candidates = new[] { new InstalledMapRegistry(), new InstalledMapRegistry() };
            int calls = 0;
            InstalledMapRegistry Build(IReadOnlyList<MapDefinition> captured)
            {
                if (!ReferenceEquals(captured, source)) throw new InvalidOperationException("Wrong simultaneous source snapshot.");
                int index = Interlocked.Increment(ref calls) - 1;
                if (index >= candidates.Length) throw new InvalidOperationException("Unexpected repeated construction.");
                entered.Signal();
                Await(release, "release simultaneous builders");
                return candidates[index];
            }
            var a = Worker(() => CustomRooms.ReadInstalled(Build));
            var b = Worker(() => CustomRooms.ReadInstalled(Build));
            try
            {
                Check(entered.Wait(Watchdog), "two cold constructors can enter without holding the publication lock");
                release.Set();
                Join(a, "first simultaneous reader completes");
                Join(b, "second simultaneous reader completes");
                Check(calls == 2 && !ReferenceEquals(candidates[0], candidates[1]),
                    "simultaneous readers build distinct candidate registries");
                Check(ReferenceEquals(a.Result, b.Result) && candidates.Contains(a.Result),
                    "simultaneous constructors converge on one published current result");
                lock (gate) Check(ReferenceEquals(installed.GetValue(null), a.Result),
                    "converged result matches the cache");
                Cached(a.Result);
            }
            finally
            {
                release.Set();
                Drain(a, b);
            }
        }

        void Cached(InstalledMapRegistry expected)
        {
            int builds = 0;
            var cached = CustomRooms.ReadInstalled(_ =>
            {
                builds++;
                throw new InvalidOperationException("Cached registry unexpectedly rebuilt.");
            });
            Check(builds == 0 && ReferenceEquals(cached, expected),
                "cached read returns the existing registry without invoking construction");
            Check(ReferenceEquals(CustomRooms.Installed, expected),
                "public Installed property uses the same current cache");
        }

        void Reset(IReadOnlyList<MapDefinition> snapshot)
        {
            lock (gate)
            {
                definitions.SetValue(null, snapshot);
                installed.SetValue(null, null);
            }
        }
        void Check(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException(name);
            Interlocked.Increment(ref checks);
            Console.WriteLine("MAP REGISTRY PASS " + name);
        }
    }

    private static MapDefinition Definition(string name) => new() { Name = name, MapId = Guid.NewGuid() };
    private static Task<T> Worker<T>(Func<T> work) => Task.Factory.StartNew(work,
        CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    private static void Await(ManualResetEventSlim signal, string name)
    {
        if (!signal.Wait(Watchdog)) throw new TimeoutException(name);
    }
    private static void Join(Task task, string name)
    {
        if (!task.Wait(Watchdog)) throw new TimeoutException(name);
        task.GetAwaiter().GetResult();
    }
    private static void Drain(params Task?[] tasks)
    {
        // Observe failures without obscuring the original assertion. Each factory
        // also has its own watchdog, so a failed check cannot leave a held worker.
        foreach (Task? task in tasks)
        {
            if (task == null) continue;
            try { Join(task, "registry fixture worker cleanup"); }
            catch (AggregateException) { }
        }
    }
}
