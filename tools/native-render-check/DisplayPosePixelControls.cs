using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using MphRead;
using MphRead.Effects;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Mods.Render;
using OpenTK.Graphics.OpenGL;

// Explicit -posecheck -pose-pixels controls in the existing render owner.
sealed partial class CheckWindow
{
    private readonly List<object> _posePixelDiagnostics = new();
    private object ObservePixelEffect(EffectEntry? entry)
    {
        if (entry == null) return new { present = false };
        return new { present = true, effectId = entry.EffectId, processLocalOwner = RuntimeHelpers.GetHashCode(entry),
            drawOverride = PoseGet(entry, "DrawTransformOverride") is OpenTK.Mathematics.Matrix4 matrix ? PoseMatrix(matrix) : null,
            drawUsesViewModel = PoseGet(entry, "DrawTransformUsesViewModel"),
            elements = entry.Elements.Select((element, index) => new { index, element.EffectId, element.EffectName,
                element.ElementName, flags = (uint)element.Flags, element.Expired, element.CreationTime,
                element.ExpirationTime, element.ParticleAmount, particleCount = element.Particles.Count,
                // ShouldDraw is a cached draw result. Submission additionally
                // requires this element's current DrawEnabled flag.
                cachedShouldDrawCount = element.Particles.Count(particle => particle.ShouldDraw),
                enabledVisibleCount = element.Flags.TestFlag(EffElemFlags.DrawEnabled)
                    ? element.Particles.Count(particle => particle.ShouldDraw && particle.Alpha > 0) : 0,
                ownTransform = PoseMatrix(element.OwnTransform), transform = PoseMatrix(element.Transform),
                particleSamples = element.Particles.Take(4).Select(particle => new { particle.ShouldDraw,
                    particle.Alpha, particle.Scale, particle.CreationTime, particle.ExpirationTime,
                    particle.Lifespan, particle.DrawId, particle.SetVecsId, particle.DrawNode,
                    billboard = particle.BillboardMode.ToString(), position = PoseVector(particle.Position),
                    vertex0 = PoseVector(particle.Vertex0), vertex1 = PoseVector(particle.Vertex1),
                    vertex2 = PoseVector(particle.Vertex2), vertex3 = PoseVector(particle.Vertex3),
                    ownerMatches = ReferenceEquals(particle.Owner, element) }).ToArray() }).ToArray() };
    }
    private object ObservePixelContext(EffectEntry? subject)
    {
        var main = Scene.Players.Main;
        var charge = PoseGet(main, "_chargeEffect") as EffectEntry;
        var muzzle = PoseGet(main, "_muzzleEffect") as EffectEntry;
        return new { scope = "diagnostic observations only; never canonical acceptance fields",
            hunter = main.Hunter.ToString(), weapon = main.CurrentWeapon.ToString(),
            chargeLevel = main.EquipInfo.ChargeLevel, gunAnimation = PoseGet(main, "GunAnimation")?.ToString(),
            sceneFrame = Scene.FrameCount, totalSteps = FrameTiming.TotalSteps,
            pendingEffectSimulation = PoseGet(Scene, "HasPendingEffectSimulation"),
            cameraMode = Scene.CameraMode.ToString(), Scene.IsFreeCam, cameraType = PoseGet(main, "CameraType")?.ToString(),
            firstPersonRenderPoseValid = PoseGet(main, "_fpRenderPoseValid"),
            collectingViewModelItems = PoseGet(Scene, "_collectingViewModelItems"),
            renderingViewModelItems = PoseGet(Scene, "_renderingViewModelItems"),
            flags1 = (uint)main.Flags1, flags2 = (uint)main.Flags2,
            exactSubjectIsCharge = subject != null && ReferenceEquals(subject, charge),
            exactSubjectIsMuzzle = subject != null && ReferenceEquals(subject, muzzle),
            subject = ObservePixelEffect(subject), charge = ObservePixelEffect(charge), muzzle = ObservePixelEffect(muzzle),
            nativeChargeProjection = ObserveNativeParticleProjection(subject) };
    }
    private void RequireNormalFirstPersonControl()
    {
        var main = Scene.Players.Main;
        PoseRequire(PoseGet(Scene, "HasPendingEffectSimulation") is false,
            "pixel control requires a completed canonical effect pass");
        PoseRequire(main.IsMainPlayer && main.Health > 0 && main.LoadFlags.TestFlag(LoadFlags.Spawned)
            && !main.IsAltForm && !main.IsMorphing && !main.IsUnmorphing
            && !main.Flags2.TestFlag(PlayerFlags2.HideModel), "pixel control requires live native first-person biped");
        // PlayerDraw drawBiped=false, followed by AttachedEnemy==null &&
        // !_field6D0 && Hunter!=Guardian: this selects only the native gun branch.
        PoseRequire(Scene.CameraMode == CameraMode.Player && !Scene.IsFreeCam
            && PoseGet(main, "CameraType")!.ToString() == "First" && Scene.CameraSequences.Current == null
            && Convert.ToInt32(PoseGet(main, "_camSwitchTimer")) >= main.Values.CamSwitchTime * 2
            && main.AttachedEnemy == null && !(bool)PoseGet(main, "_field6D0")!
            && main.Hunter != Hunter.Guardian, "native first-person gun branch predicate not satisfied");
    }
    private void DrawFrozenControl()
    {
        Scene.OnDrawFrame(); PoseRequire(Scene.OnRenderFrame(), "pixel-control renderer stopped");
        PoseRequire(Scene.CheckRenderInvariants() && GL.GetError() == OpenTK.Graphics.OpenGL.ErrorCode.NoError,
            "pixel-control native GL invariant failed");
    }
    private object CaptureFrozenPixelControl(string label, Action suppress, Action restore, Func<bool> restoredExactly,
        EffectEntry? subject = null)
    {
        string entryProjection = PoseProjection(); ulong tick = Scene.FrameCount; long steps = FrameTiming.TotalSteps;
        double alpha = FrameTiming.PresentationAlpha;
        byte[] enabled = PosePixels(); byte[]? hidden = null, restored = null; object? hiddenImage = null;
        object contextBefore = ObservePixelContext(subject); object? contextHidden = null, contextRestored = null;
        bool ownerAndFlagsRestored = false;
        Exception? failure = null;
        try
        {
            suppress(); DrawFrozenControl(); hidden = PosePixels(); hiddenImage = PoseImage(label + "-disabled", hidden);
            contextHidden = ObservePixelContext(subject);
        }
        catch (Exception error) { failure = error; }
        finally
        {
            try
            {
                // Restore exact primitive/reference/flag values before any draw
                // or assertion can fail. No state snapshot is discarded.
                restore();
                PoseRequire(restoredExactly(), "pixel-control exact owner/flags restoration failed");
                ownerAndFlagsRestored = true;
                DrawFrozenControl(); restored = PosePixels();
                contextRestored = ObservePixelContext(subject);
                PoseRequire(Scene.FrameCount == tick && FrameTiming.TotalSteps == steps
                    && FrameTiming.PresentationAlpha == alpha && PoseProjection() == entryProjection,
                    "pixel control changed simulation/frame/established canonical projection after restoration");
            }
            catch (Exception error) { failure = failure == null ? error : new AggregateException(failure, error); }
        }
        if (failure != null)
        {
            _posePixelDiagnostics.Add(new { label, status = "FAIL_BEFORE_PIXEL_COMPARISON", ownerAndFlagsRestored,
                entryProjection, exitProjection = PoseProjection(), contextBefore, contextHidden, contextRestored,
                error = failure.ToString() });
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
        int changed = PoseDifferent(enabled, hidden!), unstable = PoseDifferent(enabled, restored!);
        var result = new { status = changed > 0 && unstable == 0 ? "PASS" : "FAIL", label,
            changedPixels = changed, unstablePixels = unstable,
            sceneFrameBefore = tick, sceneFrameAfter = Scene.FrameCount, simulationStepsBefore = steps,
            simulationStepsAfter = FrameTiming.TotalSteps, alpha,
            entryProjection, exitProjection = PoseProjection(), exactOwnerAndFlagsRestored = true,
            enabledPixelSha256 = PoseDigest(enabled), disabledPixelSha256 = PoseDigest(hidden!),
            restoredPixelSha256 = PoseDigest(restored!), hiddenImage };
        _posePixelDiagnostics.Add(new { label, status = result.status, witness = result,
            contextBefore, contextHidden, contextRestored });
        PoseRequire(changed > 0 && unstable == 0, label + " lacks stable positive isolated native pixels");
        return result;
    }
    private object CaptureFirstPersonGunPixels(string prefix)
    {
        RequireNormalFirstPersonControl(); var main = Scene.Players.Main;
        // PlayerDraw's gun branch also owns smoke and attached emitter overrides.
        // Exclude all of them here so only actual arm-cannon geometry is omitted.
        PoseRequire(PoseGet(main, "_chargeEffect") == null && PoseGet(main, "_muzzleEffect") == null
            && !main.Flags1.TestFlag(PlayerFlags1.DrawGunSmoke), "gun pixel control would also omit fire effects/smoke");
        var field = typeof(PlayerEntity).GetField("_field6D0", PosePrivate)!;
        bool saved = (bool)field.GetValue(main)!;
        object result = CaptureFrozenPixelControl(prefix + "-gun", () => field.SetValue(main, true),
            () => field.SetValue(main, saved), () => (bool)field.GetValue(main)! == saved);
        return new { sourceBranch = "PlayerDraw first-person !drawBiped && AttachedEnemy==null && !_field6D0 && Hunter!=Guardian; no smoke/attached effects",
            savedSuppressGun = saved, result };
    }
    private object CaptureChargeEffectPixels(string prefix)
    {
        RequireNormalFirstPersonControl(); var main = Scene.Players.Main;
        var field = typeof(PlayerEntity).GetField("_chargeEffect", PosePrivate)!;
        var entry = field.GetValue(main) as EffectEntry;
        PoseRequire(entry != null && entry.Elements.Count > 0, "charge pixel control has no native attached effect");
        var elements = entry!.Elements.ToArray(); var savedFlags = elements.Select(element => element.Flags).ToArray();
        bool Restored() => ReferenceEquals(field.GetValue(main), entry) && entry.Elements.Count == elements.Length
            && entry.Elements.Select((element, index) => ReferenceEquals(element, elements[index])
                && element.Flags == savedFlags[index]).All(value => value);
        void Restore()
        {
            field.SetValue(main, entry);
            // Exact saved references remain owned even if a failing draw changed
            // collection shape. The shape check then fails closed; no simulation
            // may resume, and the original native cleanup owns all elements.
            for (int index = 0; index < elements.Length; index++) elements[index].Flags = savedFlags[index];
        }
        object result = CaptureFrozenPixelControl(prefix + "-charge-effect", () =>
        {
            // Every normal draw can re-enable player-linked emitters. Temporarily
            // detach only this exact reference and suppress its existing element
            // draw bits; never Unlink/Spawn/Clear or modify particles/lifetimes.
            field.SetValue(main, null); entry.SetDrawEnabled(false);
        }, Restore, Restored, entry);
        return new { sourceBranch = "PlayerDraw first-person _chargeEffect draw override + Scene effect DrawEnabled submission",
            effectId = entry.EffectId, elementCount = elements.Length, originalFlags = savedFlags.Select(value => (uint)value).ToArray(),
            result };
    }
}
