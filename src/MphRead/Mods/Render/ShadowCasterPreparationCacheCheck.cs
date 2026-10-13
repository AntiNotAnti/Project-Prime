using System;
using OpenTK.Mathematics;

namespace MphRead.Mods.Render;

internal static class ShadowCasterPreparationCacheCheck
{
    internal static int Run()
    {
        int failures = 0;
        void Check(bool value, string name)
        { Console.WriteLine($"[shadowprepcheck] {(value ? "PASS" : "FAIL")}: {name}"); if (!value) failures++; }
        var cache = new ShadowCasterPreparationCache();
        var item = new RenderItem { Type = RenderItemType.Mesh, RetainedRoomOwned = true, Alpha = 1f,
            Transform = Matrix4.Identity };
        Check(cache.Prepare(item) == default, "first static caster applies all preparation");
        Check(cache.Prepare(item) == new ShadowCasterPreparationCache.Reuse(true, true), "exact adjacent zero-palette caster shares preparation");
        item.Transform = Matrix4.CreateTranslation(1, 0, 0);
        Check(cache.Prepare(item) == new ShadowCasterPreparationCache.Reuse(true, false), "current pose mutation restores object state without stale snapshot");
        item.LightInfo = new LightInfo(Vector3.UnitX, Vector3.One, Vector3.UnitY, Vector3.One);
        Check(cache.Prepare(item) == new ShadowCasterPreparationCache.Reuse(true, false), "current lighting mutation restores object state");
        item.EmissiveIntensity = .5f;
        Check(cache.Prepare(item) == default, "emissive intensity change restores material state");
        item.TexcoordMatrix = Matrix4.CreateScale(.5f);
        Check(cache.Prepare(item) == default, "animated texture coordinates restore material state");
        item.MatrixStackCount = 1; item.MatrixPaletteVersion = 0;
        cache.Prepare(item);
        Check(cache.Prepare(item) == new ShadowCasterPreparationCache.Reuse(true, false), "unknown palette always uploads current object state");
        item.MatrixPaletteSource = new object(); item.MatrixPaletteVersion = 1;
        cache.Prepare(item);
        Check(cache.Prepare(item).Object == MatrixPaletteRevision.Enabled, "known source/version uses existing palette policy");
        item.MatrixPaletteVersion++;
        Check(!cache.Prepare(item).Object, "changed palette revision cannot reuse stale pose");
        item.MatrixPaletteSource = new object();
        Check(!cache.Prepare(item).Object, "different palette source invalidates object reuse");
        item.MatrixPaletteScale = 2;
        Check(!cache.Prepare(item).Object, "changed palette scale invalidates object reuse");
        item.OverrideColor = Vector4.One;
        Check(cache.Prepare(item) == default, "override caster is a preparation barrier"); item.OverrideColor = null;
        Check(cache.Prepare(item) == default, "barrier invalidates prior shared state");
        item.RetainedRoomOwned = false;
        Check(cache.Prepare(item) == default, "moving entity always follows full preparation");
        item.RetainedRoomOwned = true; item.MatrixStackCount = 0;
        cache.Prepare(item); cache.Clear();
        Check(cache.Prepare(item) == default, "new pass and scene cleanup cannot inherit prior cache");
        return failures;
    }
}
