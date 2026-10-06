using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using MphRead.Formats;
using MphRead.Formats.Collision;

namespace MphRead.Mods.Diagnostics;

internal static class CollisionCandidatePoolCheck
{
    internal static void Run(Action<bool, string> check)
    {
        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
        var type = typeof(CollisionDetection);
        var idle = (Queue<CollisionCandidate>)type.GetField("_inactiveItems", flags)!.GetValue(null)!;
        var active = (List<CollisionCandidate>)type.GetField("_activeItems", flags)!.GetValue(null)!;
        var temporary = (Stack<CollisionCandidate>)type.GetField("_tempItems", flags)!.GetValue(null)!;
        var originalIdle = idle.ToArray(); var originalActive = active.ToArray(); var originalTemporary = temporary.ToArray();
        idle.Clear(); active.Clear(); temporary.Clear();
        try
        {
            for (int i = 0; i < 100; i++) CollisionDetection.Init();
            check(idle.Count == 2048, "repeated private-scene loads do not grow the collision seed");
            MethodInfo rent = type.GetMethod("RentCandidate", flags)!, clear = type.GetMethod("ClearCandidates", flags)!;
            var borrowed = new List<CollisionCandidate>();
            var collision = (CollisionInstance)RuntimeHelpers.GetUninitializedObject(typeof(CollisionInstance));
            var entity = (MphRead.Formats.Collision.EntityCollision)RuntimeHelpers.GetUninitializedObject(typeof(MphRead.Formats.Collision.EntityCollision));
            for (int i = 0; i < 4096; i++)
            {
                var item = (CollisionCandidate)rent.Invoke(null, null)!;
                item.Collision = collision; item.EntityCollision = entity;
                active.Add(item); borrowed.Add(item);
            }
            CollisionDetection.Init();
            check(idle.Count == 0 && active.SequenceEqual(borrowed), "seeding preserves active collision candidate order");
            clear.Invoke(null, null);
            check(active.Count == 0 && idle.Count == 2048, "oversized collision queries return only the bounded idle capacity");
            check(borrowed.All(item => item.Collision == null && item.EntityCollision == null && item.Entry.Equals(default(MphRead.Formats.Collision.CollisionGridEntry))),
                "returned and discarded candidates release collision/entity references");
        }
        finally
        {
            idle.Clear(); foreach (var item in originalIdle) idle.Enqueue(item);
            active.Clear(); active.AddRange(originalActive);
            temporary.Clear(); foreach (var item in originalTemporary.Reverse()) temporary.Push(item);
        }
    }
}
