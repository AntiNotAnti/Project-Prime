using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using MphRead.Mods;
using MphRead.Mods.Launcher;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead
{
    internal readonly record struct ImpactDecalRenderItem(
        ImpactDecalDescriptor Decal,
        ImpactDecalTextureAsset Texture);

    /// <summary>
    /// Presentation-only bridge from copied static collision facts to the
    /// bounded decal pool. No entity or collision object is retained.
    /// </summary>
    internal sealed class ImpactDecalPresentationState
    {
        private readonly ImpactDecalProfileCatalog _catalog;
        private readonly ImpactDecalPool _pool;
        private readonly Dictionary<ulong, ImpactDecalTextureAsset> _textures
            = new();
        private readonly List<ulong> _staleTextureKeys = new();
        private int _roomId = -1;
        private ulong? _lastTick;
        private CombatActor _life = CombatActor.None;
        private bool _lifeObserved;
        private bool _enabled;

        public ImpactDecalPresentationState(ImpactDecalProfileCatalog catalog,
            int globalCapacity, int perRegionCapacity)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _pool = new ImpactDecalPool(globalCapacity, perRegionCapacity);
        }

        internal int ActiveCount => _pool.Items.Count;

        public void ConfigureRoom(int roomId)
        {
            _roomId = roomId;
            ResetVisuals();
            _lifeObserved = false;
        }

        public bool TryObserve(in StaticBeamImpactPresentation impact,
            GraphicsPreset preset, CombatActor life)
        {
            ObserveContext(preset, life);
            if (!_enabled || !impact.IsValid || impact.RoomId != _roomId
                || !WeaponBeamVisualProfiles.TryGet(impact.Beam,
                    out BeamVisualProfile beamProfile)
                || !_catalog.TryGetProfile(beamProfile.ImpactStyle,
                    out ImpactDecalProfile? profile))
            {
                return false;
            }

            ObserveTick(impact.EventTick);
            TimeSpan spawnedAt = EnvironmentalParticlePresentationClock
                .FromSimulationTick(impact.EventTick);
            ulong stableKey = ImpactDecalPresentationKey.ForImpact(impact,
                beamProfile.ImpactStyle);
            var decal = new ImpactDecalDescriptor(stableKey,
                ImpactDecalPresentationKey.ForRegion(impact), profile!.Kind,
                impact.Position, impact.Normal, profile.Radius, profile.Color,
                spawnedAt, profile.Lifetime);
            if (!_pool.TryAdd(decal, spawnedAt)) return false;
            _textures.Add(stableKey, profile.Texture);
            RemoveStaleTextures();
            return true;
        }

        public ImpactDecalRenderItem[] Prepare(GraphicsPreset preset,
            ulong presentationTick, CombatActor life)
        {
            ObserveContext(preset, life);
            if (!_enabled) return Array.Empty<ImpactDecalRenderItem>();
            ObserveTick(presentationTick);
            _pool.Advance(EnvironmentalParticlePresentationClock
                .FromSimulationTick(presentationTick));
            RemoveStaleTextures();
            if (_pool.Items.Count == 0)
                return Array.Empty<ImpactDecalRenderItem>();
            var result = new ImpactDecalRenderItem[_pool.Items.Count];
            for (int i = 0; i < result.Length; i++)
            {
                ImpactDecalDescriptor decal = _pool.Items[i];
                result[i] = new ImpactDecalRenderItem(decal,
                    _textures[decal.StableKey]);
            }
            return result;
        }

        public void Reset()
        {
            ResetVisuals();
            _life = CombatActor.None;
            _lifeObserved = false;
            _enabled = false;
        }

        private void ObserveContext(GraphicsPreset preset, CombatActor life)
        {
            bool enabled = preset == GraphicsPreset.Enhanced;
            if (_enabled != enabled)
            {
                ResetVisuals();
                _enabled = enabled;
            }
            if (!_lifeObserved)
            {
                _life = life;
                _lifeObserved = true;
            }
            else if (_life != life)
            {
                ResetVisuals();
                _life = life;
            }
        }

        private void ObserveTick(ulong tick)
        {
            if (_lastTick.HasValue && tick < _lastTick.Value) ResetVisuals();
            _lastTick = tick;
        }

        private void ResetVisuals()
        {
            _pool.Reset();
            _textures.Clear();
            _staleTextureKeys.Clear();
            _lastTick = null;
        }

        private void RemoveStaleTextures()
        {
            _staleTextureKeys.Clear();
            foreach (ulong key in _textures.Keys)
            {
                bool found = false;
                for (int i = 0; i < _pool.Items.Count; i++)
                {
                    if (_pool.Items[i].StableKey == key)
                    {
                        found = true;
                        break;
                    }
                }
                if (!found) _staleTextureKeys.Add(key);
            }
            for (int i = 0; i < _staleTextureKeys.Count; i++)
                _textures.Remove(_staleTextureKeys[i]);
        }
    }

    internal static class ImpactDecalPresentationKey
    {
        public static ulong ForImpact(in StaticBeamImpactPresentation impact,
            BeamImpactStyle style)
        {
            ulong hash = Start(0x494D504143544445UL);
            hash = Add(hash, impact.SourceIdentity);
            hash = Add(hash, impact.SourceGeneration);
            hash = Add(hash, impact.EventTick);
            hash = Add(hash, (byte)style);
            hash = Add(hash, unchecked((uint)impact.RoomId));
            hash = AddNode(hash, impact.NodeRef);
            hash = AddVector(hash, impact.Position);
            return NonZero(hash);
        }

        public static ulong ForRegion(in StaticBeamImpactPresentation impact)
        {
            ulong hash = Start(0x444543414C524547UL);
            hash = Add(hash, unchecked((uint)impact.RoomId));
            hash = AddNode(hash, impact.NodeRef);
            return NonZero(hash);
        }

        private const ulong Prime = 1099511628211UL;
        private static ulong Start(ulong domain)
            => Add(14695981039346656037UL, domain);
        private static ulong Add(ulong hash, byte value)
            => (hash ^ value) * Prime;
        private static ulong Add(ulong hash, uint value)
        {
            hash = Add(hash, (byte)value);
            hash = Add(hash, (byte)(value >> 8));
            hash = Add(hash, (byte)(value >> 16));
            return Add(hash, (byte)(value >> 24));
        }
        private static ulong Add(ulong hash, ulong value)
        {
            hash = Add(hash, (uint)value);
            return Add(hash, (uint)(value >> 32));
        }
        private static ulong Add(ulong hash, string? value)
        {
            if (value == null) return Add(hash, UInt32.MaxValue);
            hash = Add(hash, (uint)value.Length);
            for (int i = 0; i < value.Length; i++)
                hash = Add(hash, value[i]);
            return hash;
        }
        private static ulong AddNode(ulong hash,
            Formats.Culling.NodeRef nodeRef)
        {
            hash = Add(hash, nodeRef.RoomName);
            hash = Add(hash, unchecked((uint)nodeRef.PartIndex));
            hash = Add(hash, unchecked((uint)nodeRef.NodeIndex));
            return Add(hash, unchecked((uint)nodeRef.ModelIndex));
        }
        private static ulong AddVector(ulong hash, Vector3 value)
        {
            hash = Add(hash, BitConverter.SingleToUInt32Bits(value.X));
            hash = Add(hash, BitConverter.SingleToUInt32Bits(value.Y));
            return Add(hash, BitConverter.SingleToUInt32Bits(value.Z));
        }
        private static ulong NonZero(ulong hash) => hash == 0 ? 1 : hash;
    }

    public partial class ScenePresentation
    {
        internal const int MaximumImpactDecalSubmissions = 256;
        internal const int MaximumImpactDecalsPerRegion = 32;
        internal const float ImpactDecalSurfaceBias = 1f / 1024f;

        private readonly ImpactDecalPresentationState _impactDecalPresentation
            = new(LoadDefaultImpactDecalProfiles(),
                MaximumImpactDecalSubmissions, MaximumImpactDecalsPerRegion);
        private ImpactDecalRenderItem[] _impactDecalSnapshot
            = Array.Empty<ImpactDecalRenderItem>();

        private static ImpactDecalProfileCatalog LoadDefaultImpactDecalProfiles()
        {
            string root = Path.Combine(LauncherPrefs.Directory,
                "enhancements", "default");
            if (!File.Exists(Path.Combine(root,
                ImpactDecalProfilePackLoader.ManifestFileName)))
                return ImpactDecalProfileCatalog.Empty;
            ImpactDecalProfilePackLoadResult loaded
                = ImpactDecalProfilePackLoader.Load(root);
            if (loaded.Issues.Count != 0)
                Console.WriteLine($"[render] impact decals loaded with "
                    + $"{loaded.Issues.Count} ignored issue(s).");
            return loaded.Catalog;
        }

        public void ObserveStaticBeamImpact(
            in StaticBeamImpactPresentation impact)
        {
            if (Mods.Network.DemoPlayback.IsSeeking) return;
            _impactDecalPresentation.TryObserve(impact,
                Mods.RenderOptions.GraphicsPreset, CombatFeedback.Local);
        }

        private void ConfigureImpactDecals(RoomMetadata metadata)
        {
            _impactDecalSnapshot = Array.Empty<ImpactDecalRenderItem>();
            _impactDecalPresentation.ConfigureRoom(World.RoomId);
        }

        private void ResetImpactDecalPresentation()
        {
            _impactDecalSnapshot = Array.Empty<ImpactDecalRenderItem>();
            _impactDecalPresentation.Reset();
        }

        private void PrepareImpactDecals(RenderQualitySnapshot quality,
            ulong presentationTick)
        {
            _impactDecalSnapshot = _impactDecalPresentation.Prepare(
                quality.GraphicsPreset, presentationTick,
                CombatFeedback.Local);
        }

        private void SubmitImpactDecals()
        {
            int available = Math.Max(0,
                _renderFrame.MaximumCapacity - _renderFrame.Count);
            int count = Math.Min(available, _impactDecalSnapshot.Length);
            int first = _impactDecalSnapshot.Length - count;
            for (int i = first; i < _impactDecalSnapshot.Length; i++)
            {
                ImpactDecalRenderItem item = _impactDecalSnapshot[i];
                Vector3[] points = ArrayPool<Vector3>.Shared.Rent(8);
                BuildImpactDecalQuad(item.Decal, points);
                DrawSubmission submission = GetRenderItem();
                submission.Primitive = RenderPrimitive.Particle;
                submission.PolygonId = GetNextPolygonId();
                submission.Alpha = item.Decal.Tint.W;
                submission.PolygonMode = PolygonMode.Modulate;
                submission.RenderMode = RenderMode.Decal;
                submission.CullingMode = CullingMode.Neither;
                submission.BillboardMode = BillboardMode.None;
                submission.Wireframe = false;
                submission.Lighting = false;
                submission.NoLines = true;
                submission.Diffuse = item.Decal.Tint.Xyz;
                submission.Ambient = Vector3.Zero;
                submission.Specular = Vector3.Zero;
                submission.Emission = Vector3.Zero;
                submission.LightInfo = LightInfo.Zero;
                submission.TexgenMode = TexgenMode.None;
                submission.XRepeat = RepeatMode.Clamp;
                submission.YRepeat = RepeatMode.Clamp;
                submission.HasTexture = true;
                submission.TextureIdentity = item.Texture.TextureIdentity;
                SetLegacyTexture(submission, 0);
                submission.TexcoordMatrix = Matrix4.Identity;
                submission.Transform = Matrix4.Identity;
                submission.MatrixStackCount = 0;
                submission.Points = points;
                submission.ItemCount = 4;
                submission.ScaleS = 1;
                submission.ScaleT = 1;
                AddRenderItem(submission);
                _renderFrame.CaptureTexture(item.Texture.Pixels);
            }
        }

        internal static void BuildImpactDecalQuad(ImpactDecalDescriptor decal,
            Span<Vector3> destination)
        {
            if (destination.Length < 8)
                throw new ArgumentException(
                    "A textured impact decal requires eight values.",
                    nameof(destination));
            Vector3 normal = decal.Normal;
            Vector3 axis = MathF.Abs(normal.Y) < .999f
                ? Vector3.UnitY : Vector3.UnitX;
            Vector3 right = Vector3.Cross(axis, normal).Normalized()
                * decal.Radius;
            Vector3 up = Vector3.Cross(normal, right).Normalized()
                * decal.Radius;
            Vector3 center = decal.Position + normal * ImpactDecalSurfaceBias;
            destination[0] = Vector3.Zero;
            destination[1] = center - right - up;
            destination[2] = Vector3.UnitX;
            destination[3] = center + right - up;
            destination[4] = new Vector3(1, 1, 0);
            destination[5] = center + right + up;
            destination[6] = Vector3.UnitY;
            destination[7] = center - right + up;
        }
    }
}
