using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using MphRead.Entities;
using MphRead.Mods;
using MphRead.Mods.Input;
using MphRead.Mods.Network;
using MphRead.Mods.Replay;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

// Actual Scene registry, metadata only. No Initialize/OnLoad, assets, world,
// rendering, audio device or transport. Run in its own process.
internal static class SceneEntityIndexTests
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
    private static int _checks, _failures, _observed;
    private static void Check(bool value, string label)
    {
        _checks++; if (!value) _failures++;
        Console.WriteLine($"[scene-entity-index] {(value ? "PASS" : "FAIL")} {label}");
    }
    private sealed class MetadataEntity : EntityBase
    {
        internal MetadataEntity(EntityType type, int id, Scene scene) : base(type, scene) { Id = id; }
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static long LookupSample(Dictionary<EntityType, LinkedListNode<EntityBase>?> index)
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        int found = 0;
        for (int i = 0; i < 1000; i++) if (index[EntityType.ItemInstance] != null) found++;
        _observed = found;
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static long IteratorSample(Scene scene)
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        int found = 0;
        for (int i = 0; i < 1000; i++) foreach (var item in scene.GetItemInstanceEntities()) found++;
        _observed = found;
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }
    internal static int Run()
    {
        var oldState = GameState.Current; var oldPlayers = PlayerEntity.LegacyRegistry; var oldRandom = Rng.Current;
        try
        {
            Headless.Enter();
            var scene = new Scene(new Vector2i(32, 32), SyntheticInput.CreateKeyboard(), SyntheticInput.CreateMouse(),
                _ => { }, () => { }, initializeRuntime: false);
            var index = (Dictionary<EntityType, LinkedListNode<EntityBase>?>)typeof(Scene)
                .GetField("_entityNodesByType", Instance)!.GetValue(scene)!;
            EntityType[] originalKeys = index.Keys.ToArray();
            // Frozen pre-comparer registry order: 0..19, 21..26, 100..117.
            EntityType[] expectedKeys = Enumerable.Range(0, 20).Concat(Enumerable.Range(21, 6))
                .Concat(Enumerable.Range(100, 18)).Select(n => (EntityType)n).ToArray();
            Check(originalKeys.SequenceEqual(expectedKeys) && index.Values.All(n => n == null),
                "actual Scene initializes the exact historical 44 keys in insertion order with null heads");
            var comparer = index.Comparer;
            var prior = EqualityComparer<EntityType>.Default;
            bool domain = true;
            // Every possible underlying value, including every undefined enum.
            // Compare self and a distinct partner across the full ushort domain;
            // additionally cover the complete defined-value cross product.
            for (int i = 0; i <= ushort.MaxValue; i++)
            {
                var value = (EntityType)i; var distinct = (EntityType)(i ^ 0x8000);
                domain &= comparer.GetHashCode(value) == prior.GetHashCode(value)
                    && comparer.Equals(value, value) == prior.Equals(value, value)
                    && comparer.Equals(value, distinct) == prior.Equals(value, distinct)
                    && comparer.Equals(distinct, value) == prior.Equals(distinct, value);
            }
            var defined = Enum.GetValues<EntityType>();
            foreach (var left in defined) foreach (var right in defined)
                domain &= comparer.Equals(left, right) == prior.Equals(left, right);
            Check(domain, "typed equality/hash preserve default semantics across all 65,536 values and all defined pairs");
            var a = new MetadataEntity(EntityType.Door, 8101, scene);
            var b = new MetadataEntity(EntityType.Door, 8102, scene);
            var c = new MetadataEntity(EntityType.ItemInstance, 8103, scene);
            var d = new MetadataEntity(EntityType.Platform, 8104, scene);
            scene.InsertEntity(c); scene.InsertEntity(a); scene.InsertEntity(b); scene.InsertEntity(d);
            Check(index[EntityType.Door]?.Value == a && index[EntityType.ItemInstance]?.Value == c
                && index[EntityType.Platform]?.Value == d && index[EntityType.Door]?.Next?.Value == b,
                "actual out-of-order InsertEntity routes first heads and contiguous same-type nodes");
            var actualOrder = new List<int>();
            foreach (var entity in scene.Entities) actualOrder.Add(entity.Id);
            Check(actualOrder.SequenceEqual(new[] { 8104, 8101, 8102, 8103 })
                && scene.TryGetEntity(8103, out var mapped) && ReferenceEquals(mapped, c),
                "actual insertion preserves entity type order and ID map");
            scene.RemoveEntity(a);
            Check(index[EntityType.Door]?.Value == b && !scene.TryGetEntity(8101, out _),
                "actual first removal advances only its type head and removes its ID");
            scene.RemoveEntity(b); scene.RemoveEntity(c); scene.RemoveEntity(d);
            bool listEmpty = true;
            foreach (var entity in scene.Entities) listEmpty = false;
            Check(index.Values.All(n => n == null) && listEmpty
                && !scene.TryGetEntity(8102, out _) && !scene.TryGetEntity(8103, out _) && !scene.TryGetEntity(8104, out _),
                "actual final removals clear heads/list/ID map");
            Check(index.Keys.SequenceEqual(originalKeys), "insert/remove preserve registry key membership and insertion order");
            const string contract = "A4512223EC0CC49516DB00DD3733168DA6969DEA059E9DDE84B31062ADEA675A";
            Check(ReplayWorldCheckpoint.ComputeContract(stable: true) == contract && ReplayCheckpointAccessors.Contract == contract,
                "frozen canonical world schema and generated accessor fingerprint remain exact");
            var fields = (Dictionary<Type, FieldInfo[]>)typeof(ReplayWorldCheckpoint).GetField("Fields", Static)!.GetValue(null)!;
            var types = (Dictionary<string, Type>)typeof(ReplayWorldCheckpoint).GetField("Types", Static)!.GetValue(null)!;
            Check(fields.Values.SelectMany(v => v).All(f => f.Name != "_entityNodesByType")
                && !types.Values.Any(t => t == comparer.GetType() || t == index.GetType()),
                "Scene entity index and comparer remain outside canonical field/type closure");
            long firstLookup = LookupSample(index), firstIterator = IteratorSample(scene);
            for (int i = 0; i < 64; i++) { LookupSample(index); IteratorSample(scene); }
            Console.WriteLine($"[scene-entity-index] fixed helper warmup initial lookup={firstLookup}, iterator={firstIterator}; 64 additional warmups");
            for (int i = 0; i < 5; i++)
            {
                long lookup = LookupSample(index), iterator = IteratorSample(scene);
                Check(lookup == 0, $"warmed actual registry lookup {i + 1}/5 allocates zero bytes for 1000 calls (actual {lookup})");
                Check(iterator == 0, $"warmed actual production empty item iterator {i + 1}/5 allocates zero bytes for 1000 calls (actual {iterator})");
            }
            Check(_observed == 0, "measured production iterator stays genuinely empty after removals");
            Console.WriteLine($"[scene-entity-index] {_checks} checks; failures={_failures}. Content-free actual Scene registry only; no world/frame-time acceptance.");
            return _failures == 0 ? 0 : 1;
        }
        catch (Exception ex) { Console.WriteLine("[scene-entity-index] EXCEPTION " + ex); return 1; }
        finally { GameState.Current = oldState; PlayerEntity.LegacyRegistry = oldPlayers; Rng.Current = oldRandom; }
    }
}
