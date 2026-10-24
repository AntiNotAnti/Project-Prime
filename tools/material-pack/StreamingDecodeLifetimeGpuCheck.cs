using System;
using System.Collections;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using MphRead;
using MphRead.Mods;
using MphRead.Mods.Input;
using MphRead.Mods.Render;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;

internal static class StreamingDecodeLifetimeGpuCheck
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    internal static void Run()
    {
        bool replacements = RenderOptions.TextureReplacements;
        try
        {
            RenderOptions.TextureReplacements = false;
            foreach (bool unload in new[] { false, true })
            {
                var scene = new Scene(new Vector2i(32, 32), SyntheticInput.CreateKeyboard(), SyntheticInput.CreateMouse(),
                    _ => { }, () => { }, initializeRuntime: false);
                using var entered = new ManualResetEventSlim();
                using var finish = new ManualResetEventSlim();
                if (!TextureDecodeBudget.Shared.TryAcquire(TextureDecodeBudget.Shared.LimitBytes, out var reservation))
                    throw new Exception("Scene lifetime fixture requires idle shared decode admission.");
                using var job = new TextureDecodeJob<PreparedTextureAsset>(reservation, _ =>
                {
                    entered.Set(); finish.Wait(); return new FixtureAsset();
                });
                try
                {
                    Check(entered.Wait(TimeSpan.FromSeconds(10)), "injected production decode job entered codec");
                    Type requestType = typeof(Scene).GetNestedType("StreamingTextureRequest", BindingFlags.NonPublic)!;
                    ConstructorInfo constructor = requestType.GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
                    object request = constructor.Invoke(constructor.GetParameters()
                        .Select(p => p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null).ToArray());
                    Type pendingType = typeof(Scene).GetNestedType("StreamingTextureDecode", BindingFlags.NonPublic)!;
                    var pending = (IList)typeof(Scene).GetField("_streamingTextureDecodes", Hidden)!.GetValue(scene)!;
                    pending.Add(Activator.CreateInstance(pendingType, request, job)!);
                    long start = Stopwatch.GetTimestamp();
                    if (unload) scene.UnloadGl();
                    else typeof(Scene).GetMethod("PumpModernTextureStreaming", Hidden)!.Invoke(scene, null);
                    Check(Stopwatch.GetElapsedTime(start).TotalSeconds < 1 && !job.Completion.IsCompleted,
                        unload ? "actual Scene unload never waits for codec" : "actual stale pump retires before codec completion");
                    Check(pending.Count == 0 && TextureDecodeBudget.Shared.ReservedBytes == TextureDecodeBudget.Shared.LimitBytes,
                        "Scene list clears while retired process-shared reservation remains");
                    Check(!TextureDecodeBudget.Shared.TryAcquire(1, out _), "new Scene admission remains blocked by retired Scene");
                    finish.Set();
                    Check(SpinWait.SpinUntil(() => job.Completion.IsCompleted && TextureDecodeBudget.Shared.ActiveJobs == 0,
                        TimeSpan.FromSeconds(10)), "retired Scene codec really finishes before reservation release");
                    Check(job.TakeCompleted() == null, "actual retired Scene drops late prepared result");
                    Check(TextureDecodeBudget.Shared.ReservedBytes == 0 && GL.GetError() == ErrorCode.NoError,
                        "no stale result upload or GL error after retirement");
                }
                finally { finish.Set(); scene.UnloadGl(); }
            }
            Console.WriteLine("STREAMING DECODE LIFETIME GPU PASS: actual Scene stale-pump/unload retirement, shared transition bound, no late publication");
        }
        finally { RenderOptions.TextureReplacements = replacements; }
    }
    private static void Check(bool condition, string name) { if (!condition) throw new Exception(name); }
    private sealed class FixtureAsset() : PreparedTextureAsset("retired-fixture", TextureAssetClass.Hunter, TextureAssetChannel.Albedo, 1, 1)
    {
        internal override PreparedTextureAsset WithSemantics(TextureAssetSemantics semantics) => this;
        public override long EstimateGpuBytes(bool mipmaps) => 4;
    }
}
