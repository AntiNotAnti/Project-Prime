#if !MPHREAD_SERVER
using System;
using System.Collections.Generic;

namespace MphRead.Mods.Render
{
    internal static class RetainedRenderGraphCheck
    {
        internal static int Run()
        {
            int failures = 0;
            void Check(bool condition, string name)
            {
                if (condition) Console.WriteLine("[rendergraphcheck] PASS: " + name);
                else
                {
                    Console.WriteLine("[rendergraphcheck] FAIL: " + name);
                    failures++;
                }
            }

            Check(WorldRenderGraph.Validate(out string error),
                "six-pass graph validates" + (error.Length == 0 ? "" : ": " + error));

            var opaqueA = new RenderItem { ListId = 11, TextureBindingId = 21 };
            var opaqueB = new RenderItem { ListId = 12, TextureBindingId = 22 };
            var decal = new RenderItem { ListId = 13, RenderMode = RenderMode.Decal };
            var translucent = new RenderItem
            {
                ListId = 14,
                RenderMode = RenderMode.Translucent,
                Alpha = 0.5f
            };

            var world = new RetainedRenderWorld();
            world.Capture(
                new List<RenderItem> { opaqueA, opaqueB, translucent },
                new List<RenderItem> { decal },
                new List<RenderItem> { translucent });

            Check(world.Opaque.Count == 3 && world.Decals.Count == 1
                && world.Translucent.Count == 1, "packet classes preserve membership");
            Check(ReferenceEquals(world.Opaque[0].Item, opaqueA)
                && ReferenceEquals(world.Opaque[1].Item, opaqueB),
                "capture preserves submission order");
            Check(world.Opaque[0].Sequence == 0 && world.Opaque[1].Sequence == 1,
                "sequence is retained explicitly");

            ulong firstKey = world.Opaque[0].StateKey;
            world.Capture(
                new List<RenderItem> { opaqueA, opaqueB },
                Array.Empty<RenderItem>(),
                Array.Empty<RenderItem>());
            Check(world.FrameRevision == 2, "frame revision advances");
            Check(world.Opaque[0].StateKey == firstKey, "state key is deterministic");
            Check(world.PacketCount == 2, "capture reuses and clears packet lists");

            Console.WriteLine(failures == 0
                ? "[rendergraphcheck] PASS"
                : $"[rendergraphcheck] FAIL: {failures} check(s)");
            return failures == 0 ? 0 : 1;
        }
    }
}
#endif
