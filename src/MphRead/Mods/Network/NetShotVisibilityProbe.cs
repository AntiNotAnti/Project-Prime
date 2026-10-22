using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MphRead.Effects;
using MphRead.Entities;
using MphRead.Formats;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using ReFuel.Stb;

namespace MphRead.Mods.Network
{
    // A render owner is a diagnostic value, never an entity/packet/payment owner.
    internal readonly record struct NcxDrawIdentity(ShotKey Key, uint Component, uint Phase,
        byte Kind, byte Victim = 255, ushort VictimGeneration = 0, ushort VictimLife = 0,
        ushort DamageEvent = 0, byte Weapon = 255, byte Provenance = 0)
    {
        internal bool Valid => Key.AuthorityEpoch != 0 && Key.MatchId != 0 && Key.ShotId != 0
            && Key.Generation != 0 && Key.LifeId != 0 && (uint)Key.ShooterSlot < 8
            && (Kind == 0 && Component != 0 || Kind == 1 && DamageEvent != 0
                && Victim < 8 && VictimGeneration != 0 && VictimLife != 0);
        internal string Token => Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(
            string.Create(CultureInfo.InvariantCulture,
                $"ncx-shot-v1:{Key.AuthorityEpoch}:{Key.MatchId}:{Key.ShooterSlot}:{Key.Generation}:{Key.LifeId}:{Key.ShotId}")))).ToLowerInvariant();
    }

    internal readonly record struct NcxPixelDifference(int Pixels, int MinX, int MinY, int MaxX, int MaxY);
    internal static class NetShotVisibilityProbe
    {
        internal static readonly string? Output = Environment.GetEnvironmentVariable("PRIME_NCX_VISIBILITY");
        internal static bool Enabled => !string.IsNullOrEmpty(Output) && NetCampaignEvidence.TimelineEnabled
            && NetSession.IsClient;

        // RGB is bottom-up, exactly the detached array subsequently encoded as PNG.
        internal static NcxPixelDifference Difference(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b,
            int width, int height, Span<byte> mask)
        {
            int length = checked(width * height * 3);
            if (width <= 0 || height <= 0 || a.Length != length || b.Length != length || mask.Length != length)
                throw new ArgumentException("RGB dimensions do not match.");
            int count = 0, minX = width, minY = height, maxX = -1, maxY = -1;
            for (int p = 0; p < width * height; p++)
            {
                int i = p * 3;
                bool changed = a[i] != b[i] || a[i + 1] != b[i + 1] || a[i + 2] != b[i + 2];
                mask[i] = mask[i + 1] = mask[i + 2] = changed ? (byte)255 : (byte)0;
                if (!changed) continue;
                count++; int x = p % width, y = height - 1 - p / width;
                minX = Math.Min(minX, x); minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y);
            }
            return new(count, count == 0 ? -1 : minX, count == 0 ? -1 : minY, maxX, maxY);
        }
    }
}

namespace MphRead
{
    public partial class Scene
    {
        private const int NcxPixelCapacity = 64, NcxItemCapacity = 8192;
        private sealed class NcxPixelState
        {
            internal readonly (string Sha256, string Kind) Engine = Mods.Diagnostics.RuntimeApplicationImage.Capture(typeof(Scene).Assembly);
            internal readonly Dictionary<RenderItem, Mods.Network.NcxDrawIdentity> Items = new();
            internal readonly Dictionary<SingleParticle, Mods.Network.NcxDrawIdentity> Singles = new();
            internal readonly Dictionary<Mods.Network.NcxDrawIdentity, int> Contributions = new();
            internal readonly Dictionary<Mods.Network.NcxDrawIdentity, int> Attempts = new();
            internal readonly List<object> Rows = new();
            internal long Overflow;
        }
        private NcxPixelState? _ncxPixels;
        private Mods.Network.NcxDrawIdentity _ncxDrawOwner, _ncxExcludedOwner;
        private bool _ncxPreparedRedraw;

        internal readonly struct NcxDrawScope : IDisposable
        {
            private readonly Scene? _scene;
            private readonly Mods.Network.NcxDrawIdentity _previous;
            internal NcxDrawScope(Scene scene, Mods.Network.NcxDrawIdentity identity)
            { _scene = scene; _previous = scene._ncxDrawOwner; scene._ncxDrawOwner = identity; }
            public void Dispose() { if (_scene != null) _scene._ncxDrawOwner = _previous; }
        }
        internal NcxDrawScope NcxBeamItems(BeamProjectileEntity beam) => NcxSourceItems(
            // Narrow flight proof: native Power Beam Draw00 consists of its direct
            // trail and a deferred SingleParticle. Persistent effects are unsupported.
            beam.Beam == BeamType.PowerBeam && beam.DrawFuncId == 0 && beam.ModCustomDefinition == null
                ? new(beam.ModLaunchKey, beam.ModComponentId, 0, 0, Weapon: (byte)beam.Beam) : default);
        internal NcxDrawScope NcxSourceItems(Mods.Network.NcxDrawIdentity identity)
        {
            if (!Mods.Network.NetShotVisibilityProbe.Enabled || !identity.Valid || _ncxPixels == null)
                return NcxOwnedItems(default);
            if (_ncxPixels.Contributions.Count >= NcxItemCapacity)
            { _ncxPixels.Overflow++; return NcxOwnedItems(default); }
            _ncxPixels.Contributions[identity] = _ncxPixels.Contributions.GetValueOrDefault(identity) + 1;
            return NcxOwnedItems(identity);
        }
        internal NcxDrawScope NcxOwnedItems(Mods.Network.NcxDrawIdentity identity)
            => Mods.Network.NetShotVisibilityProbe.Enabled ? new(this, identity) : default;
        private void NcxBeginDrawItems()
        {
            if (!Mods.Network.NetShotVisibilityProbe.Enabled) return;
            _ncxPixels ??= new();
            _ncxPixels.Items.Clear(); _ncxPixels.Singles.Clear(); _ncxPixels.Contributions.Clear(); _ncxDrawOwner = default;
        }
        private void NcxTagItem(RenderItem item)
        {
            if (!_ncxDrawOwner.Valid || _ncxPixels == null) return;
            if (_ncxPixels.Items.Count == NcxItemCapacity) { _ncxPixels.Overflow++; return; }
            _ncxPixels.Items[item] = _ncxDrawOwner;
        }
        private void NcxTagSingle(SingleParticle single)
        {
            if (!_ncxDrawOwner.Valid || _ncxPixels == null) return;
            if (_ncxPixels.Singles.Count == NcxItemCapacity) { _ncxPixels.Overflow++; return; }
            _ncxPixels.Singles[single] = _ncxDrawOwner;
        }
        private NcxDrawScope NcxSingleItems(SingleParticle single) => NcxOwnedItems(
            _ncxPixels != null && _ncxPixels.Singles.TryGetValue(single, out var owner) ? owner : default);
        private bool NcxExcludeItem(RenderItem item) => _ncxPreparedRedraw && _ncxExcludedOwner.Valid
            && _ncxPixels != null && _ncxPixels.Items.TryGetValue(item, out var owner) && owner == _ncxExcludedOwner;
        private bool NcxExcludeBatch(IReadOnlyList<Mods.Render.RetainedDrawPacket> packets,
            Mods.Render.RetainedDrawBatch batch)
        {
            if (!_ncxExcludedOwner.Valid) return false;
            for (int i = 0; i < batch.Count; i++)
                if (NcxExcludeItem(packets[batch.Start + i].Item)) return true;
            return false;
        }

        private string NcxGuardState()
        {
            // Explicit projections complement the existing gameplay state hash.
            // No reflection, state reconstruction, network pump or entity draw occurs.
            var lives = Enumerable.Range(0, 8).Select(i => new { Slot = i,
                Generation = Mods.Network.NetPlayerLifecycle.Generation(i), Life = Mods.Network.NetPlayerLifecycle.Get(i) }).ToArray();
            return JsonSerializer.Serialize(new
            {
                Gameplay = Mods.Network.ReplayStateHash.Compute(this, Mods.Network.NetSession.NetFrame),
                FrameCount, Mods.Network.NetSession.NetFrame, Mods.Network.NetSession.AppliedSnapshotFrame,
                Mods.Network.NetSession.LastSnapshotFrame, Mods.Network.NetSession.AuthorityEpoch,
                Mods.Network.NetSession.CurrentMatchId, Lives = lives,
                Random.Rng1, Random.Rng2, GlobalRng1 = Rng.Rng1, GlobalRng2 = Rng.Rng2,
                Alpha = Mods.Render.FrameTiming.PresentationAlpha,
                Camera = new[] { _cameraPosition.X, _cameraPosition.Y, _cameraPosition.Z,
                    _cameraFacing.X, _cameraFacing.Y, _cameraFacing.Z, _cameraRight.X, _cameraRight.Y, _cameraRight.Z,
                    _cameraUp.X, _cameraUp.Y, _cameraUp.Z, _cameraFov, _viewModelFov },
                View = MatrixValues(_viewMatrix), Projection = MatrixValues(_perspectiveMatrix),
                Effects = _activeElements.Count, _effectFrame, _pendingEffectSteps,
                Entities = Entities.Count, _fadePercent, _fadeType, _fadeColor, _fadeIn
            });
        }

        private static float[] MatrixValues(Matrix4 m) => new[] {
            m.M11, m.M12, m.M13, m.M14, m.M21, m.M22, m.M23, m.M24,
            m.M31, m.M32, m.M33, m.M34, m.M41, m.M42, m.M43, m.M44 };

        private byte[] NcxReadPixels(out int width, out int height)
        {
            // Readback restores BOTH framebuffer bindings, the source's read buffer,
            // and pack alignment. In particular it never changes the actual camera.
            int read = GL.GetInteger(GetPName.ReadFramebufferBinding);
            int draw = GL.GetInteger(GetPName.DrawFramebufferBinding);
            int pack = GL.GetInteger(GetPName.PackAlignment);
            GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, GraphicsReadFramebuffer());
            int buffer = GL.GetInteger(GetPName.ReadBuffer);
            try
            {
                width = _targetSize.X; height = _targetSize.Y;
                if (_frameBuffer == 0 || width <= 0 || height <= 0) throw new IOException("No scene target.");
                var rgb = new byte[checked(width * height * 3)];
                GL.ReadBuffer(ReadBufferMode.ColorAttachment0);
                GL.PixelStore(PixelStoreParameter.PackAlignment, 1);
                GL.ReadPixels(0, 0, width, height, PixelFormat.Rgb, PixelType.UnsignedByte, rgb);
                CheckGlError("NCX readback");
                return rgb;
            }
            finally
            {
                GL.ReadBuffer((ReadBufferMode)buffer);
                GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, read);
                GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, draw);
                GL.PixelStore(PixelStoreParameter.PackAlignment, pack);
            }
        }

        private void NcxObservePreparedPixels(bool drawGameHud)
        {
            if (!Mods.Network.NetShotVisibilityProbe.Enabled || _ncxPixels == null || _ncxPreparedRedraw
                || Services.IsReplica || Mods.KillCam.Presentation(this) != null
                || Mods.Network.DemoPlayback.Presentation(this) != null) return;
            var state = _ncxPixels;
            // Each exact owner gets at most three attempts. Bounded capture is a
            // diagnostic workload; its elapsed time must never enter CPU comparisons.
            foreach (var owner in state.Items.Values.Distinct().ToArray())
            {
                if (state.Rows.Count >= NcxPixelCapacity) break;
                int attempt = state.Attempts.GetValueOrDefault(owner);
                if (attempt >= 3) continue;
                state.Attempts[owner] = attempt + 1;
                long started = Stopwatch.GetTimestamp();
                string guard = NcxGuardState();
                int errors = RenderGlErrorCount;
                byte[] before = NcxReadPixels(out int width, out int height);
                byte[]? excluded = null, restored = null;
                string? failure = null;
                try
                {
                    _ncxPreparedRedraw = true; _ncxExcludedOwner = owner;
                    BeginWorldPass();
                    if (!ExecuteCoreFrameRenderGraph(drawGameHud)) throw new IOException("Excluded render exited.");
                    excluded = NcxReadPixels(out int ew, out int eh);
                    if (ew != width || eh != height) throw new IOException("Target resized.");
                }
                catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
                { failure = ex.GetType().Name; }
                finally
                {
                    // Always restore the original prepared packet set before the
                    // ordinary HUD/presentation continues. Never call OnDrawFrame,
                    // OnUpdateFrame, CountFrame, replay camera staging or UpdateFade.
                    _ncxExcludedOwner = default;
                    try
                    {
                        BeginWorldPass();
                        if (!ExecuteCoreFrameRenderGraph(drawGameHud)) throw new IOException("Restore render exited.");
                        restored = NcxReadPixels(out int rw, out int rh);
                        if (rw != width || rh != height) throw new IOException("Restore target resized.");
                    }
                    finally { _ncxPreparedRedraw = false; }
                }
                bool unchanged = guard == NcxGuardState();
                byte[] mask = new byte[before.Length];
                var changed = excluded == null ? default : Mods.Network.NetShotVisibilityProbe.Difference(before, excluded, width, height, mask);
                int unstable = restored == null ? -1 : Mods.Network.NetShotVisibilityProbe.Difference(before, restored, width, height, new byte[before.Length]).Pixels;
                int contributors = state.Contributions.GetValueOrDefault(owner);
                bool complete = failure == null && unchanged && unstable == 0 && state.Overflow == 0
                    && errors == RenderGlErrorCount && contributors == 1;
                string status = !complete ? "INCOMPLETE" : changed.Pixels > 0 ? "VISIBLE" : "NO_TARGET_PIXELS";
                string directory = Path.GetFullPath(Mods.Network.NetShotVisibilityProbe.Output! + ".frames");
                Directory.CreateDirectory(directory);
                string prefix = Path.Combine(directory, $"sample-{state.Rows.Count:000}");
                object Save(string suffix, byte[] rgb)
                {
                    string path = prefix + "-" + suffix + ".png";
                    if (Mods.ScreenCapture.PngWriter is { } writer) writer(rgb, width, height, path);
                    else
                    {
                        using var stream = new FileStream(path, FileMode.CreateNew);
                        StbImage.FlipVerticallyOnSave = true;
                        Mods.PngEncoder.Write<byte>(rgb, width, height, StbiImageFormat.Rgb, stream);
                    }
                    return new { File = Path.GetFileName(path), RgbSha256 = Convert.ToHexString(SHA256.HashData(rgb)),
                        PngSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) };
                }
                var images = new List<object> { Save("actual", before), Save("mask", mask) };
                if (excluded != null) images.Add(Save("excluded", excluded));
                if (restored != null) images.Add(Save("restored", restored));
                state.Rows.Add(new { Shot = owner.Token, owner.Component, owner.Phase, owner.Kind,
                    owner.Victim, owner.VictimGeneration, owner.VictimLife, Attempt = attempt + 1,
                    owner.DamageEvent, owner.Weapon, owner.Provenance, Contributors = contributors,
                    LocalSlot = Mods.Network.NetSession.LocalSlot, NetFrame = Mods.Network.NetSession.NetFrame,
                    Spectating = Players.Main.Flags2.TestFlag(PlayerFlags2.Spectating),
                    ObserverHarness = Mods.Network.HitRig.NcxObserverRole,
                    FrameCount, Timestamp = started, ClockFrequency = Stopwatch.Frequency,
                    Width = width, Height = height, Status = status, Changed = changed,
                    UnstablePixels = unstable, GameplayAndCameraUnchanged = unchanged,
                    GlErrors = RenderGlErrorCount - errors, Failure = failure,
                    CaptureTicks = Stopwatch.GetTimestamp() - started, Images = images });
                if (complete && changed.Pixels > 0) state.Attempts[owner] = 3;
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(Mods.Network.NetShotVisibilityProbe.Output!))!);
                File.WriteAllText(Mods.Network.NetShotVisibilityProbe.Output!, JsonSerializer.Serialize(new
                {
                    Schema = 1, Scope = "Actual camera; same prepared native Power Beam trail/SingleParticle or exact confirmed cue. World target before 2D HUD. Flight is not authority confirmation. Missing pixels are not a missed legal hit. No persistent-effect or shadow attribution.",
                    EngineSha256 = state.Engine.Sha256, EngineImageKind = state.Engine.Kind,
                    state.Overflow, Samples = state.Rows, Capacity = NcxPixelCapacity
                }, new JsonSerializerOptions { WriteIndented = true }));
            }
        }
    }
}
