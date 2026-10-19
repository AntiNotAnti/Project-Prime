using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using MphRead;
using MphRead.Effects;
using MphRead.Formats;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;

// Explicit -pose-pixels observations of completed native submission/depth; no render or
// simulation mutation, acceptance substitution, or new renderer hook.
sealed partial class CheckWindow
{
    private object? ObserveNativeParticleProjection(EffectEntry? entry)
    {
        if (entry == null) return null;
        int width = Scene.RenderSize.X, height = Scene.RenderSize.Y;
        float[] depth = new float[checked(width * height)];
        int priorRead = GL.GetInteger(GetPName.ReadFramebufferBinding);
        int framebuffer = (int)PoseGet(Scene, "_frameBuffer")!;
        try
        {
            GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, framebuffer);
            GL.ReadPixels(0, 0, width, height, PixelFormat.DepthComponent, PixelType.Float, depth);
        }
        finally { GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, priorRead); }
        PoseRequire(depth.All(d => float.IsFinite(d) && d >= 0 && d <= 1), "native depth observation invalid");
        var view = (Matrix4)PoseGet(Scene, "_viewMatrix")!;
        var worldProjection = (Matrix4)PoseGet(Scene, "_perspectiveMatrix")!;
        var gunProjection = (Matrix4)PoseGet(Scene, "_viewModelPerspectiveMatrix")!;
        var invSphere = (Matrix4)PoseGet(Scene, "_viewInvRotMatrix")!;
        var invCylinder = (Matrix4)PoseGet(Scene, "_viewInvRotYMatrix")!;
        var active = ((IEnumerable)PoseGet(Scene, "_activeElements")!).Cast<EffectElementEntry>().ToArray();
        var submissions = ((IEnumerable)PoseGet(Scene, "_nonDecalItems")!).Cast<RenderItem>().ToArray();
        var observations = new List<object>();
        Vector3 actualEye = view.Inverted().Row3.Xyz;
        foreach (var element in entry.Elements)
        foreach (var particle in element.Particles.Take(16))
        {
            if (particle.DrawNode) { observations.Add(new { status = "node geometry not covered" }); continue; }
            var vertices = new[] { particle.Vertex0, particle.Vertex1, particle.Vertex2, particle.Vertex3 };
            var uv = new[] { particle.Texcoord0, particle.Texcoord1, particle.Texcoord2, particle.Texcoord3 };
            Matrix4 presentation = (Matrix4)PoseGet(element, "PresentationTransform")!;
            Matrix4 transform;
            if (element.Flags.TestFlag(EffElemFlags.UseTransform))
            {
                if (particle.BillboardMode != BillboardMode.None)
                {
                    Vector3 position = Matrix.Vec3MultMtx4(particle.Position, presentation.ClearTranslation());
                    transform = Matrix4.CreateTranslation(position + presentation.Row3.Xyz);
                }
                else transform = Matrix4.CreateTranslation(particle.Position) * presentation;
            }
            else transform = Matrix4.CreateTranslation(particle.Position);
            int texture = particle.ParticleId >= 0 && particle.ParticleId < element.TextureBindingIds.Count
                ? element.TextureBindingIds[particle.ParticleId] : -1;
            bool Match(RenderItem item) => item.Type == RenderItemType.Particle && item.Points.Length >= 8
                && item.Transform == transform && item.TextureBindingId == texture && item.Alpha == particle.Alpha
                && item.BillboardMode == particle.BillboardMode
                && Enumerable.Range(0, 4).All(i => item.Points[i * 2 + 1] == vertices[i]
                    && item.Points[i * 2].Xy == uv[i]);
            var matches = submissions.Where(Match).ToArray();
            bool expectedViewModel = PoseGet(entry, "DrawTransformOverride") is Matrix4
                && PoseGet(entry, "DrawTransformUsesViewModel") is true;
            Matrix4 projection = expectedViewModel ? gunProjection : worldProjection;
            Matrix4 billboard = particle.BillboardMode == BillboardMode.Sphere ? invSphere
                : particle.BillboardMode == BillboardMode.Cylinder ? invCylinder : Matrix4.Identity;
            // GLSL column expression: projection * view * transform * billboard * vertex.
            // UniformMatrix4(transpose:false) uploads OpenTK rows as GLSL columns.
            Matrix4 combined = billboard * transform * view * projection;
            Vector4 Clip(Vector3 p) => new Vector4(
                p.X * combined.M11 + p.Y * combined.M21 + p.Z * combined.M31 + combined.M41,
                p.X * combined.M12 + p.Y * combined.M22 + p.Z * combined.M32 + combined.M42,
                p.X * combined.M13 + p.Y * combined.M23 + p.Z * combined.M33 + combined.M43,
                p.X * combined.M14 + p.Y * combined.M24 + p.Z * combined.M34 + combined.M44);
            var clip = vertices.Select(Clip).ToArray();
            var ndc = clip.Select(c => new Vector3(c.X, c.Y, c.Z) / c.W).ToArray();
            bool finite = ndc.All(p => float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z));
            bool allInFront = clip.All(c => c.W > 0);
            int x0 = finite ? (int)MathF.Floor((ndc.Min(p => p.X) + 1) * .5f * width) : -1;
            int x1 = finite ? (int)MathF.Ceiling((ndc.Max(p => p.X) + 1) * .5f * width) : -1;
            int y0 = finite ? (int)MathF.Floor((ndc.Min(p => p.Y) + 1) * .5f * height) : -1;
            int y1 = finite ? (int)MathF.Ceiling((ndc.Max(p => p.Y) + 1) * .5f * height) : -1;
            int left = Math.Clamp(x0, 0, width), right = Math.Clamp(x1, 0, width);
            int bottom = Math.Clamp(y0, 0, height), top = Math.Clamp(y1, 0, height);
            float minDepth = finite ? (ndc.Min(p => p.Z) + 1) * .5f : 0;
            float maxDepth = finite ? (ndc.Max(p => p.Z) + 1) * .5f : 0;
            float observedMin = 1, observedMax = 0; int samples = 0, nearerThanQuadMin = 0;
            for (int y = bottom; y < top; y++) for (int x = left; x < right; x++)
            {
                float d = depth[y * width + x]; observedMin = MathF.Min(observedMin, d); observedMax = MathF.Max(observedMax, d);
                samples++; if (d < minDepth - 1e-6f) nearerThanQuadMin++;
            }
            object ObserveRoomRay(Vector3 point)
            {
                CollisionResult hit = default;
                bool blocked = CollisionDetection.CheckBetweenPoints(actualEye, point, TestFlags.Beams, Scene, ref hit);
                return new { blocked, endpoint = PoseVector(point), hitPosition = PoseVector(hit.Position),
                    hitFraction = hit.Distance, flags = hit.Flags.ToString(),
                    kind = !blocked ? "NO_WORLD_COLLISION" : hit.EntityCollision == null ? "ROOM_COLLISION" : "ENTITY_COLLISION" };
            }
            var worldCorners = vertices.Select(vertex => Matrix.Vec3MultMtx4(vertex, billboard * transform)).ToArray();
            observations.Add(new { element.ElementName, particle.ParticleId, particle.MaterialId,
                activeElement = active.Contains(element), linkedEntry = ReferenceEquals(element.EffectEntry, entry),
                particleOwnerMatches = ReferenceEquals(particle.Owner, element), particle.ShouldDraw,
                drawEnabled = element.Flags.TestFlag(EffElemFlags.DrawEnabled), particle.Alpha,
                materialValid = particle.MaterialId >= 0 && particle.MaterialId < element.Model.Materials.Count,
                textureBinding = texture, expectedViewModel, matchingSubmittedItems = matches.Length,
                actualSubmittedViewModel = matches.Select(m => m.ViewModel).ToArray(),
                actualSubmittedPolygonIds = matches.Select(m => m.PolygonId).ToArray(),
                actualEye = PoseVector(actualEye), centerWorldRay = ObserveRoomRay(transform.Row3.Xyz),
                cornerWorldRays = worldCorners.Select(ObserveRoomRay).ToArray(),
                allInFront, finite, clip = clip.Select(c => new[] { c.X, c.Y, c.Z, c.W }).ToArray(),
                ndc = ndc.Select(PoseVector).ToArray(), pixelBoundsBottomLeft = new[] { x0, y0, x1, y1 },
                clampedPixelBoundsBottomLeft = new[] { left, bottom, right, top },
                minDepth, maxDepth, sceneDepthSamples = samples, sceneDepthMinimum = observedMin,
                sceneDepthMaximum = observedMax, nearerThanQuadMin,
                entireBoundsDepthNearer = allInFront && samples > 0 && nearerThanQuadMin == samples });
        }
        return new { scope = "Native submission, room collision and geometric depth observations only; no positive-pixel acceptance claim",
            framebuffer, width, height, priorReadBindingRestored = GL.GetInteger(GetPName.ReadFramebufferBinding) == priorRead,
            view = PoseMatrix(view), worldProjection = PoseMatrix(worldProjection), viewModelProjection = PoseMatrix(gunProjection),
            particles = observations };
    }
}
