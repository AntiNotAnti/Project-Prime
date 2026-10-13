using System;
using System.Collections.Generic;

namespace MphRead.Mods.Render;

// Only the immutable ID sequence survives a world execution. Pose/light/merge
// proofs are renewed for every execution, after shadow collection has finished.
internal sealed class RetainedRoomBatchPlan
{
    internal const int MaxIds = 1024;
    internal const int MaxPlans = 256;
    private ulong _revision;
    private bool _prepared;
    private int _membershipCount;
    internal ulong MembershipRevision { get; private set; }
    internal object? DrawToken { get; set; }
    internal bool Mergeable { get; private set; }
    internal int[] Ids { get; private set; } = Array.Empty<int>();
    internal int Count { get; private set; }
    internal ulong Hash { get; private set; }

    internal bool Prepare(ulong revision, IReadOnlyList<RetainedDrawPacket> packets,
        RetainedDrawBatch batch)
    {
        if (_prepared && _revision == revision)
        {
            OpenGlPassProfiler.Count(OpenGlFrameCounter.RoomBatchProofReuses);
            return Mergeable;
        }
        _prepared = true;
        _revision = revision;
        Mergeable = false;
        Count = 0;
        OpenGlPassProfiler.Count(OpenGlFrameCounter.RoomBatchProofs);
        if (batch.Count < 2 || batch.Count > MaxIds) return false;
        RetainedDrawPacket first = packets[batch.Start];
        bool changed = _membershipCount != batch.Count;
        // Prove current pose/light compatibility and membership in one traversal.
        // Material identity avoids repeated full descriptor comparisons.
        for (int offset = 0; offset < batch.Count; offset++)
        {
            var packet = packets[batch.Start + offset];
            if (offset > 0 && !RetainedOpaqueBatchPolicy.CanMerge(first, packet)) return false;
            if (!changed && Ids[offset] != packet.Mesh.ListId) changed = true;
        }
        if (changed)
        {
            unchecked { MembershipRevision++; } DrawToken = null;
            _membershipCount = batch.Count;
            if (Ids.Length < batch.Count) Ids = new int[batch.Count];
            for (int offset = 0; offset < batch.Count; offset++)
                Ids[offset] = packets[batch.Start + offset].Mesh.ListId;
            Hash = HashIds(Ids, batch.Count);
        }
        Count = batch.Count;
        Mergeable = true;
        return true;
    }

    internal static int CheckMembershipPolicy()
    {
        int failures = 0;
        void Check(bool value, string name)
        { Console.WriteLine($"[roombatchmembershipcheck] {(value ? "PASS" : "FAIL")}: {name}"); if (!value) failures++; }
        RetainedDrawPacket Packet(int id)
        {
            var item = new RenderItem { Type = RenderItemType.Mesh, ListId = id, RetainedRoomOwned = true,
                Alpha = 1f, PolygonMode = PolygonMode.Modulate, RenderMode = RenderMode.Normal };
            return new(item, id, new RetainedMeshDescriptor(RetainedMeshDescriptor.KeyOf(item)));
        }
        var a = Packet(1); var b = Packet(2); var packets = new[] { a, b };
        var plan = new RetainedRoomBatchPlan(); var batch = new RetainedDrawBatch(0, 2, a.BatchState);
        Check(plan.Prepare(1, packets, batch), "membership fixture proves current merge compatibility");
        ulong membership = plan.MembershipRevision; object token = new(); plan.DrawToken = token;
        Check(plan.Prepare(2, packets, batch) && plan.MembershipRevision == membership && ReferenceEquals(plan.DrawToken, token),
            "new frame with identical ordered IDs preserves membership and token");
        var reversed = new[] { b, a };
        Check(plan.Prepare(3, reversed, batch) && plan.MembershipRevision != membership && plan.DrawToken == null,
            "changed ordered IDs invalidate token even with same count");
        membership = plan.MembershipRevision; token = new(); plan.DrawToken = token;
        a.Item.Transform = OpenTK.Mathematics.Matrix4.CreateTranslation(1f, 0f, 0f);
        Check(!plan.Prepare(4, reversed, batch) && !plan.Mergeable, "current pose change still rejects merge regardless of stored token");
        a.Item.Transform = default;
        Check(plan.Prepare(5, reversed, batch) && plan.MembershipRevision == membership && ReferenceEquals(plan.DrawToken, token),
            "restored proof retains unchanged membership after a failed frame");
        var three = new[] { b, a, Packet(3) };
        Check(plan.Prepare(6, three, new RetainedDrawBatch(0, 3, a.BatchState)) && plan.MembershipRevision != membership && plan.DrawToken == null,
            "changed exact membership count invalidates token");
        return failures;
    }

    internal static ulong HashIds(int[] ids, int count)
    {
        unchecked
        {
            ulong hash = 14695981039346656037UL;
            for (int i = 0; i < count; i++)
                hash = (hash ^ (uint)ids[i]) * 1099511628211UL;
            return (hash ^ (uint)count) * 1099511628211UL;
        }
    }
}
