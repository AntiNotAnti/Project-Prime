using System;
using System.Buffers;
using System.IO;
using MphRead.Formats;
using MphRead.Mods;
using MphRead.Mods.Launcher;
using OpenTK.Mathematics;

namespace MphRead
{
    public partial class ScenePresentation
    {
        internal const int MaximumEnvironmentalParticleSubmissions = 128;

        private readonly EnvironmentalParticlePresentationState
            _environmentalParticlePresentation = new(
                LoadDefaultEnvironmentalParticleCatalog(),
                MaximumEnvironmentalParticleSubmissions);
        private EnvironmentalParticleSnapshot? _environmentalParticleSnapshot;

        private static EnvironmentalParticleCatalog
            LoadDefaultEnvironmentalParticleCatalog()
        {
            string root = Path.Combine(LauncherPrefs.Directory,
                "enhancements", "default");
            if (!File.Exists(Path.Combine(root,
                EnvironmentalParticleManifestLoader.ManifestFileName)))
                return EnvironmentalParticleCatalog.Empty;
            EnvironmentalParticleCatalogLoadResult loaded
                = EnvironmentalParticleManifestLoader.Load(root);
            if (loaded.Issues.Count != 0)
                Console.WriteLine($"[render] environmental particles loaded with "
                    + $"{loaded.Issues.Count} ignored issue(s).");
            return loaded.Catalog;
        }

        private void ConfigureEnvironmentalParticles(RoomMetadata metadata)
        {
            _environmentalParticleSnapshot = null;
            _environmentalParticlePresentation.ConfigureRoom(metadata.Archive);
        }

        private void ResetEnvironmentalParticlePresentation()
        {
            _environmentalParticleSnapshot = null;
            _environmentalParticlePresentation.Reset();
        }

        private void PrepareEnvironmentalParticles(RenderQualitySnapshot quality,
            ulong capturedPresentationTick, float capturedRenderFraction)
        {
            _environmentalParticleSnapshot = _environmentalParticlePresentation.Prepare(
                quality.GraphicsPreset, capturedPresentationTick,
                capturedRenderFraction);
        }

        private void SubmitEnvironmentalParticles()
        {
            if (_environmentalParticleSnapshot == null) return;
            int available = Math.Max(0, _renderFrame.MaximumCapacity - _renderFrame.Count);
            int count = Math.Min(available,
                Math.Min(MaximumEnvironmentalParticleSubmissions,
                    _environmentalParticleSnapshot.Items.Count));
            int first = _environmentalParticleSnapshot.Items.Count - count;
            for (int i = first; i < _environmentalParticleSnapshot.Items.Count; i++)
            {
                EnvironmentalParticleSample sample
                    = _environmentalParticleSnapshot.Items[i];
                Vector3[] vertices = ArrayPool<Vector3>.Shared.Rent(4);
                BuildEnvironmentalParticleQuad(sample, _cameraRight, _cameraUp,
                    vertices);
                AddRenderItem(CullingMode.Neither, GetNextPolygonId(),
                    sample.Particle.Tint, RenderPrimitive.Quad, vertices,
                    noLines: true,
                    bloomStrength: sample.Particle.EmissiveStrength
                        / EnvironmentalParticleLayerDescriptor.MaximumEmissiveStrength);
            }
        }

        internal static void BuildEnvironmentalParticleQuad(
            EnvironmentalParticleSample sample, Vector3 cameraRight,
            Vector3 cameraUp, Span<Vector3> destination)
        {
            if (destination.Length < 4)
                throw new ArgumentException("A particle quad requires four vertices.",
                    nameof(destination));
            if (!IsFinite(cameraRight) || !IsFinite(cameraUp))
                throw new ArgumentOutOfRangeException(nameof(cameraRight));
            float halfSize = sample.Particle.Size * .5f;
            Vector3 right = cameraRight * halfSize;
            Vector3 up = cameraUp * halfSize;
            destination[0] = Corner(sample.Position, right, up, -1, 1);
            destination[1] = Corner(sample.Position, right, up, 1, 1);
            destination[2] = Corner(sample.Position, right, up, 1, -1);
            destination[3] = Corner(sample.Position, right, up, -1, -1);
        }

        private static Vector3 Corner(Vector3 position, Vector3 right, Vector3 up,
            int rightSign, int upSign)
            => new(
                FiniteCoordinate(position.X, right.X, up.X, rightSign, upSign),
                FiniteCoordinate(position.Y, right.Y, up.Y, rightSign, upSign),
                FiniteCoordinate(position.Z, right.Z, up.Z, rightSign, upSign));

        private static float FiniteCoordinate(float position, float right, float up,
            int rightSign, int upSign)
        {
            double value = position + rightSign * (double)right
                + upSign * (double)up;
            if (value >= float.MaxValue) return float.MaxValue;
            if (value <= -float.MaxValue) return -float.MaxValue;
            return (float)value;
        }

        private static bool IsFinite(Vector3 value)
            => float.IsFinite(value.X) && float.IsFinite(value.Y)
                && float.IsFinite(value.Z);
    }
}
