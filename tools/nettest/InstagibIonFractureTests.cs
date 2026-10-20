using System;
using System.Linq;
using MphRead.Mods.Render;
using MphRead.Mods.Cosmetics;
using MphRead.Mods.Sound;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

// Runs without assets or a GL context. Render-path/occlusion acceptance is
// deliberately a separate integration gate, not implied by these checks.
internal static class InstagibIonFractureTests
{
    private static int _checks;
    private static void Check(bool ok, string description)
    {
        _checks++;
        if (!ok) throw new InvalidOperationException("ION FRACTURE: " + description);
    }

    internal static int Run()
    {
        InstagibAudioOwnershipTests.Run(Check);
        Check(InstagibIonFracture.Capacity > 0 && InstagibIonFracture.Capacity <= 64,
            "bounded world effect capacity");
        Check(InstagibIonFracture.LifeSeconds > 0.5f &&
            InstagibIonFracture.LifeSeconds <= 1.0f, "short presentation lifetime");

        Vector3[] palette = new Vector3[8];
        for (int slot = 0; slot < palette.Length; slot++)
        {
            Vector3 color = InstagibIonFracture.ColorForSlot(slot);
            Check(float.IsFinite(color.X) && float.IsFinite(color.Y) && float.IsFinite(color.Z)
                && color.X >= 0 && color.X <= 1
                && color.Y >= 0 && color.Y <= 1
                && color.Z >= 0 && color.Z <= 1, "normalized finite slot color " + slot);
            for (int other = 0; other < slot; other++)
                Check((palette[other] - color).LengthSquared > 0.02f,
                    "distinct colors " + other + "/" + slot);
            palette[slot] = color;
        }
        Check(InstagibIonFracture.ColorForSlot(8) == palette[0],
            "deterministic eight-player wrap");
        Check(InstagibIonFracture.ColorForSlot(-1) == palette[0],
            "invalid slot safe fallback");
        Check(InstagibIonFracture.FadeAt(-1) == 1f
            && InstagibIonFracture.FadeAt(0) == 1f
            && InstagibIonFracture.FadeAt(1) == 0f
            && InstagibIonFracture.FadeAt(2) == 0f,
            "bounded fade endpoints");
        float previous = 1;
        for (int step = 1; step <= 100; step++)
        {
            float current = InstagibIonFracture.FadeAt(step / 100f);
            Check(current >= 0 && current <= previous, "monotone fade " + step);
            previous = current;
        }
        var waveIds = new System.Collections.Generic.HashSet<uint>();
        foreach (var beam in CosmeticCatalog.BeamEffects)
        {
            byte[] pcm = InstagibBeamAudio.BuildWave(beam.Style);
            byte[] impactPcm = InstagibBeamAudio.BuildWave(beam.Style, impact: true);
            Check(pcm.Length > 1024 && pcm.Length < 16000 && pcm[0] == 'R' && pcm[1] == 'I',
                "authored short firing synthesis emits a bounded RIFF/WAV for each beam");
            Check(impactPcm.Length > 1024 && impactPcm.Length < pcm.Length
                && impactPcm[0] == 'R' && impactPcm[1] == 'I',
                "distinct collision synthesis emits a shorter valid RIFF/WAV");
            uint hash = 2166136261;
            foreach (byte sample in pcm) hash = (hash ^ sample) * 16777619;
            Check(waveIds.Add(hash), "all beam firing signatures are acoustically distinct by PCM");
            uint impactHash = 2166136261;
            foreach (byte sample in impactPcm) impactHash = (impactHash ^ sample) * 16777619;
            Check(waveIds.Add(impactHash), "every impact signature differs from firing and other styles");

            Check(beam.WireId == (ushort)beam.Style && beam.WireId < 16,
                "style identity fits the reserved cosmetic nibble");
            for (byte tone = 0; tone < BeamPalette.Names.Count; tone++)
            {
                var color = BeamPalette.Resolve(tone, beam.PrimaryColor);
                Check(float.IsFinite(color.X) && float.IsFinite(color.Y) && float.IsFinite(color.Z)
                    && color.X >= 0 && color.Y >= 0 && color.Z >= 0
                    && color.X <= 1 && color.Y <= 1 && color.Z <= 1,
                    "beam palette always yields finite normalized RGB");
            }
        }
        Check(InstagibIonFracture.DetailQualityForRange(CosmeticEffectQuality.High, 5f, 3)
            == CosmeticEffectQuality.High, "near shot retains full High detail");
        Check(InstagibIonFracture.DetailQualityForRange(CosmeticEffectQuality.High, 23f, 3)
            == CosmeticEffectQuality.Medium, "mid-distance shot scales to Medium detail");
        Check(InstagibIonFracture.DetailQualityForRange(CosmeticEffectQuality.High, 45f, 3)
            == CosmeticEffectQuality.Low, "far shot scales to Low detail");
        Check(InstagibIonFracture.DetailQualityForRange(CosmeticEffectQuality.High, 80f, 3)
            == CosmeticEffectQuality.Off, "very distant shot retains only core");
        Check(InstagibIonFracture.DetailQualityForRange(CosmeticEffectQuality.High, 5f, 0)
            == CosmeticEffectQuality.Off, "exhausted multi-player accent budget preserves core");
        Check(InstagibIonFracture.DetailQualityForRange(CosmeticEffectQuality.High,
            float.NaN, 3) == CosmeticEffectQuality.Off, "nonfinite distance safely disables accents");
        // Insta-Gib now uses the vanilla Imperialist sniperBeam model's X/Z
        // cross-section instead of the earlier Quake-sized 0.24-unit halo.
        // All nine styles keep their pure animation curves, only their final
        // world-space ribbons are scaled/clipped to the visual envelope.
        const float oldRadius = InstagibIonFracture.OriginalRailHaloHalfWidth;
        float compact = InstagibIonFracture.ImperialistWidthScale(
            InstagibIonFracture.FallbackImperialistHalfWidth);
        Check(oldRadius == 0.24f && compact > 0f && compact < 0.35f,
            "narrow Imperialist beam is not the old oversized rail");
        Check(MathF.Abs(oldRadius * compact
                - InstagibIonFracture.FallbackImperialistHalfWidth) < 0.0001f,
            "reference halo matches original Imperialist fallback width");
        float normalWidth = InstagibIonFracture.AuthoredImperialistHalfWidth(
            new Vector3(-0.045f, -1f, -0.037f),
            new Vector3(0.045f, 1f, 0.037f), Vector3.One);
        Check(MathF.Abs(normalWidth - 0.045f) < 0.000001f,
            "canonical X/Z model radius ignores longitudinal Y extent");
        Check(MathF.Abs(InstagibIonFracture.AuthoredImperialistHalfWidth(
                new Vector3(-0.1f, -1f, -0.075f),
                new Vector3(0.1f, 1f, 0.075f),
                new Vector3(0.5f, 3f, 0.5f)) - 0.05f) < 0.000001f,
            "original beam mesh node transverse scale is respected");
        Check(InstagibIonFracture.AuthoredImperialistHalfWidth(
                Vector3.Zero, new Vector3(float.NaN, 1f, 1f),
                Vector3.One) == 0f,
            "bad source geometry cannot enlarge the rail");
        Check(InstagibIonFracture.ImperialistHalfWidth(0f)
                == InstagibIonFracture.FallbackImperialistHalfWidth
            && InstagibIonFracture.ImperialistHalfWidth(float.NaN)
                == InstagibIonFracture.FallbackImperialistHalfWidth,
            "missing or corrupt model uses a safe narrow fallback");
        Check(InstagibIonFracture.ImperialistHalfWidth(1.5f)
                == InstagibIonFracture.MaxImperialistHalfWidth
            && InstagibIonFracture.ImperialistHalfWidth(0.0001f)
                == InstagibIonFracture.MinImperialistHalfWidth,
            "oversized or tiny authored models remain within competitive bounds");
        foreach (var beam in CosmeticCatalog.BeamEffects)
        {
            for (int strand = 0; strand < 2; strand++)
            {
                float radius = InstagibIonFracture.FallbackImperialistHalfWidth;
                float scale = InstagibIonFracture.ImperialistWidthScale(radius);
                float halfWidth = beam.Style == BeamFxStyle.IonFracture
                    ? 0.031f * scale : InstagibIonFracture.SignatureFor(beam.Style).Width * scale;
                Vector3 original = beam.Style == BeamFxStyle.IonFracture
                    ? InstagibIonFracture.HelixDisplacement(
                        Vector3.UnitX, Vector3.UnitY, 8f, 0.36f, 0.4f, strand)
                    : InstagibIonFracture.SignatureOffset(
                        beam.Style, 0.48f, 0.36f, strand, Vector3.UnitX, Vector3.UnitY);
                Vector3 fit = InstagibIonFracture.FitMotionToImperialist(
                    original, scale, radius, halfWidth);
                Check(fit.Length + halfWidth <= radius + 0.00001f,
                    "all animated beam styles stay within Imperialist shaft envelope");
                Check(InstagibIonFracture.FitMotionToImperialist(
                    original, scale, radius, halfWidth) == fit,
                    "size-constrained strand preserves deterministic replay presentation");
            }
        }
        var born = InstagibIonFracture.LookAt(0f);
        var expired = InstagibIonFracture.LookAt(1f);
        Check(born.Core == 1f && Math.Abs(born.Glow - 0.75f) < 0.001f
            && Math.Abs(born.Spiral - 0.95f) < 0.001f
            && Math.Abs(born.Radius - 0.11f) < 0.001f,
            "reference rail launches bright core, soft halo and full twin filament");
        Check(expired.Core == 0f && expired.Glow == 0f && expired.Spiral == 0f,
            "all textured rail layers fade to zero");
        Check(InstagibIonFracture.TaperAt(0f, 0.24f, 9f) < 0.04f
            && Math.Abs(InstagibIonFracture.TaperAt(20f, 0.24f, 9f) - 0.24f) < 0.001f,
            "halo narrows at muzzle and reaches authored full width");
        var offset0 = InstagibIonFracture.HelixDisplacement(Vector3.UnitX, Vector3.UnitY,
            0f, 0.5f, 0f, 0);
        var strandA = InstagibIonFracture.HelixDisplacement(Vector3.UnitX, Vector3.UnitY,
            8f, 0.25f, 0.45f, 0);
        var strandB = InstagibIonFracture.HelixDisplacement(Vector3.UnitX, Vector3.UnitY,
            8f, 0.25f, 0.45f, 1);
        Check(offset0.LengthSquared < 0.000001f,
            "twin filaments begin at exact muzzle, without large first-person halo");
        Check((strandA + strandB).LengthSquared < 0.000001f
            && strandA.Length > 0.1f && strandA.Length < 0.5f,
            "filaments oppose each other and stay bounded around shot axis");
        Check((InstagibIonFracture.HelixDisplacement(Vector3.UnitX, Vector3.UnitY,
            8f, 0.25f, 0.45f, 0) - strandA).LengthSquared == 0f,
            "helix is deterministic and independent of draw frame RNG");
        // Geometry and animation must remain distinct after the textured core
        // is shared across all nine styles. Tests must fail if effects become
        // mere recolors or if an edit accidentally freezes their movement.
        var nonDefault = CosmeticCatalog.BeamEffects
            .Where(b => b.Style != BeamFxStyle.IonFracture).ToArray();
        Check(nonDefault.Length == 8, "all eight non-default motion identities are defined");
        var signatures = new System.Collections.Generic.List<float[]>();
        var right = Vector3.UnitX;
        var up = Vector3.UnitY;
        foreach (var beam in nonDefault)
        {
            var profile = InstagibIonFracture.SignatureFor(beam.Style);
            Check(profile.Width > 0.02f && profile.Width <= 0.10f
                && profile.Opacity > 0.1f && profile.Opacity <= 1f
                && profile.Reach >= 25f && profile.Reach <= 60f,
                "signature budgets are bounded and visible");
            var fingerprint = new System.Collections.Generic.List<float>();
            bool animated = false;
            foreach (float u in new[] { 0.13f, 0.31f, 0.48f, 0.67f, 0.83f })
            {
                Vector3 first = InstagibIonFracture.SignatureOffset(
                    beam.Style, u, 0.12f, 0, right, up);
                Vector3 second = InstagibIonFracture.SignatureOffset(
                    beam.Style, u, 0.55f, 0, right, up);
                Vector3 opposite = InstagibIonFracture.SignatureOffset(
                    beam.Style, u, 0.12f, 1, right, up);
                Check(float.IsFinite(first.X) && float.IsFinite(first.Y) && float.IsFinite(first.Z)
                    && first.Length <= 0.6f && opposite.Length <= 0.6f,
                    "signature motion stays finite and inside visual half-width");
                Check((first - InstagibIonFracture.SignatureOffset(
                    beam.Style, u, 0.12f, 0, right, up)).LengthSquared == 0f,
                    "signature is pure and deterministic without frame RNG");
                animated |= (first - second).LengthSquared > 0.00002f;
                fingerprint.Add(first.X); fingerprint.Add(first.Y);
                fingerprint.Add(second.X); fingerprint.Add(second.Y);
                fingerprint.Add(opposite.X); fingerprint.Add(opposite.Y);
            }
            Check(animated, beam.DisplayName + " animates over lifetime");
            signatures.Add(fingerprint.ToArray());
        }
        for (int i = 0; i < signatures.Count; i++)
            for (int j = i + 1; j < signatures.Count; j++)
            {
                float distinct = 0f;
                for (int k = 0; k < signatures[i].Length; k++)
                {
                    float delta = signatures[i][k] - signatures[j][k];
                    distinct += delta * delta;
                }
                Check(distinct > 0.004f, "beam " + i + " and " + j
                    + " have different geometric motion, not just colors");
            }
        var world = new InstagibIonFracture();
        Check(world.Count == 0, "new scene owns empty pool");
        world.Clear();
        Check(world.Count == 0, "clear is idempotent");

        Console.WriteLine("ION FRACTURE PASS " + _checks + " asset-free policy assertions");
        return 0;
    }
}
