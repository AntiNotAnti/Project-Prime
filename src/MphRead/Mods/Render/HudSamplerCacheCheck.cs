#if !MPHREAD_SERVER
using System;
using OpenTK.Graphics.OpenGL;

namespace MphRead.Mods.Render;

internal static class HudSamplerCacheCheck
{
    internal static int Run()
    {
        int failures = 0;
        void Check(bool ok, string name)
        {
            Console.WriteLine($"[hudsamplercheck] {(ok ? "PASS" : "FAIL")}: {name}");
            if (!ok) failures++;
        }
        var cache = new HudSamplerCache();
        bool Set(int value) => cache.ShouldSet(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, value);
        Check(Set(9728) && Set(9728), "disabled cache always submits");
        cache.Begin();
        cache.Bind(TextureTarget.Texture2D, 10);
        Check(Set(9728) && Set(9728), "unknown active unit never suppresses sampler writes");
        cache.Activate(TextureUnit.Texture0); cache.Bind(TextureTarget.Texture2D, 10);
        Check(Set(9728) && !Set(9728), "known identical sampler suppresses repeated writes");
        Check(Set(9729) && !Set(9729) && Set(9728), "nearest and smooth transitions submit exact changed values");
        Check(cache.ShouldSet(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, 33071)
            && !cache.ShouldSet(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, 33071),
            "parameters are independently tracked");
        cache.Bind(TextureTarget.Texture2D, 11);
        Check(Set(9728), "different texture requires first sampler write");
        cache.Bind(TextureTarget.Texture2D, 10);
        Check(!Set(9728), "previous texture retains observed parameters within the pass");
        cache.Activate(TextureUnit.Texture1); cache.Bind(TextureTarget.Texture2D, 12);
        Check(Set(9729), "texture units have independent bindings");
        cache.Activate(TextureUnit.Texture0);
        Check(!Set(9728), "return to prior texture unit uses correct texture");
        cache.Bind(TextureTarget.Texture2D, 0);
        Check(Set(9728) && Set(9728), "default texture never cached");
        cache.Activate(TextureUnit.Texture0); cache.Bind(TextureTarget.Texture2D, 10);
        cache.Invalidate();
        Check(Set(9728) && Set(9728), "unknown list and deletion barriers clear state");
        cache.Activate(TextureUnit.Texture0); cache.Bind(TextureTarget.Texture2D, 10);
        Check(Set(9728), "ID reuse after invalidation starts unknown");
        cache.End(); cache.Begin(); cache.Activate(TextureUnit.Texture0); cache.Bind(TextureTarget.Texture2D, 10);
        Check(Set(9728), "new pass cannot inherit prior sampler state");
        cache.BeginList();
        cache.Activate(TextureUnit.Texture0); cache.Bind(TextureTarget.Texture2D, 10);
        Check(Set(9728) && Set(9728), "list compilation always records repeated sampler commands");
        cache.EndList();
        Check(Set(9728) && Set(9728), "list compilation leaves runtime bindings unknown");
        cache.Activate(TextureUnit.Texture0); cache.Bind(TextureTarget.Texture2D, 10);
        Check(Set(9728) && !Set(9728), "ordinary sampling resumes after list compilation");
        cache.End();
        Check(!cache.Active && Set(9728) && Set(9728), "scope end disables all suppression");
        return failures == 0 ? 0 : 1;
    }
}
#endif
