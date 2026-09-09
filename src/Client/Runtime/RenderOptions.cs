using System;
using System.Collections.Generic;
using System.Globalization;

namespace MphRead.Mods
{
    /// <summary>
    /// A small, user-facing quality bundle.  The names intentionally describe
    /// the result rather than the graphics API used to produce it.
    /// </summary>
    public enum GraphicsPreset : byte
    {
        Original,
        Enhanced,
        Performance
    }

    /// <summary>Texture filtering choices exposed by the settings screen.</summary>
    public enum TextureFilteringPreset : byte
    {
        Original,
        Smooth,
        Enhanced
    }

    /// <summary>Requested anisotropic filtering strength; zero means disabled.</summary>
    public enum AnisotropyLevel : byte
    {
        Off = 0,
        X2 = 2,
        X4 = 4,
        X8 = 8,
        X16 = 16
    }

    /// <summary>Requested multisample count; zero means the single-sample path.</summary>
    public enum MsaaLevel : byte
    {
        Off = 0,
        X2 = 2,
        X4 = 4
    }

    /// <summary>
    /// The immutable quality state copied into a sealed render frame.
    ///
    /// This is deliberately independent of SDL, OpenGL, or any other backend:
    /// a backend can clamp its capabilities later without changing the state
    /// the presentation thread captured for this frame.
    /// </summary>
    public readonly record struct RenderQualitySnapshot(
        GraphicsPreset GraphicsPreset,
        TextureFilteringPreset TextureFilteringPreset,
        AnisotropyLevel Anisotropy,
        MsaaLevel Msaa,
        bool Bloom,
        bool DynamicVisualLights)
    {
        public int AnisotropySamples => (int)Anisotropy;

        /// <summary>Returns the actual sample count, where Off is one sample.</summary>
        public int MsaaSampleCount => (int)Msaa == 0 ? 1 : (int)Msaa;

        public bool UsesLinearFiltering => TextureFilteringPreset != TextureFilteringPreset.Original;
        public bool UsesMipmaps => TextureFilteringPreset != TextureFilteringPreset.Original;
        public bool UsesAnisotropicFiltering
            => TextureFilteringPreset == TextureFilteringPreset.Enhanced
                && Anisotropy != AnisotropyLevel.Off;
    }

    /// <summary>
    /// The knobs that trade picture for frame rate.
    ///
    /// The engine already had all of these; they were debug keys in the model
    /// viewer this grew out of (L for lighting, G for fog, F for filtering) and
    /// so were reachable only by someone who had read the help text, and never
    /// at all on a phone, which has no keyboard. They are the things worth
    /// turning off on a device that cannot keep 60, so they belong in the
    /// settings on every platform.
    ///
    /// <see cref="ResolutionScale"/> is the new one and the one that matters
    /// most. The scene is already drawn into an offscreen target and then put
    /// on the screen as one textured quad, so rendering that target smaller and
    /// letting the quad stretch it costs nothing to arrange and saves fill rate
    /// in proportion. The HUD, the helmet and the fade are drawn afterwards,
    /// straight to the window, so they stay sharp at any scale.
    /// </summary>
    public static class RenderOptions
    {
        private static readonly string[] _graphicsPresetLabels = { "Original", "Enhanced", "Performance" };
        private static readonly string[] _textureFilteringLabels = { "Original", "Smooth", "Enhanced" };
        private static readonly string[] _anisotropyLabels = { "Off", "2x", "4x", "8x", "16x" };
        private static readonly string[] _msaaLabels = { "Off", "2x", "4x" };

        public static IReadOnlyList<string> GraphicsPresetLabels => _graphicsPresetLabels;
        public static IReadOnlyList<string> TextureFilteringLabels => _textureFilteringLabels;
        public static IReadOnlyList<string> AnisotropyLabels => _anisotropyLabels;
        public static IReadOnlyList<string> MsaaLabels => _msaaLabels;

        private static GraphicsPreset _graphicsPreset = GraphicsPreset.Original;
        private static TextureFilteringPreset _textureFilteringPreset = TextureFilteringPreset.Original;
        private static AnisotropyLevel _anisotropy = AnisotropyLevel.Off;
        private static MsaaLevel _msaa = MsaaLevel.Off;

        /// <summary>
        /// The selected high-level bundle.  Original is intentionally the
        /// default until performance evidence justifies another default.
        /// </summary>
        public static GraphicsPreset GraphicsPreset
        {
            get => _graphicsPreset;
            set => _graphicsPreset = NormalizeGraphicsPreset(value);
        }

        public static TextureFilteringPreset TextureFilteringPreset
        {
            get => _textureFilteringPreset;
            set => _textureFilteringPreset = NormalizeTextureFilteringPreset(value);
        }

        public static AnisotropyLevel Anisotropy
        {
            get => _anisotropy;
            set => _anisotropy = NormalizeAnisotropy(value);
        }

        public static MsaaLevel Msaa
        {
            get => _msaa;
            set => _msaa = NormalizeMsaa(value);
        }

        public static bool Bloom { get; set; }
        public static bool DynamicVisualLights { get; set; }

        /// <summary>
        /// Percent of the window the 3D scene is rendered at, 25 to 100.
        /// Halving it quarters the pixels.
        /// </summary>
        public static int ResolutionScale
        {
            get => _resolutionScale;
            set => _resolutionScale = Math.Clamp(value, MinScale, 100);
        }

        private static int _resolutionScale = 100;

        public const int MinScale = 25;

        /// <summary>Per-vertex lighting. Off is flatter and cheaper.</summary>
        public static bool Lighting { get; set; } = true;

        /// <summary>
        /// Cel shading: every surface goes to flat colour and the shapes in
        /// the room are drawn around in ink.
        ///
        /// Two halves, and the first one is the one that was missing. The
        /// texture is not banded, it is *replaced*: each one is averaged to a
        /// single colour when it is uploaded and the fragment shader paints
        /// with that, keeping only the texel's alpha so cut-outs are still cut
        /// out. Banding a photograph of rubble only ever gives banded rubble.
        /// What is left -- the vertex colours and the lighting -- is then
        /// banded into <see cref="CelBands"/> steps of brightness, so a wall
        /// keeps its hue and it is the shading across it that goes to steps.
        ///
        /// The second half is <see cref="CelEdge"/>, a pass over the depth the
        /// scene left behind, which is what makes the picture read as drawn
        /// rather than merely posterised.
        /// </summary>
        public static bool CelShading { get; set; }

        /// <summary>
        /// Draw the frame rate over the game.
        ///
        /// Read every frame rather than copied when a scene is built, for the
        /// reason <see cref="Fog"/> and <see cref="Lighting"/> are: the
        /// settings window opens from the pause menu during a match, and a
        /// counter you cannot turn on while you are looking at the stutter is
        /// the wrong tool.
        /// </summary>
        public static bool ShowFps { get; set; }

        /// <summary>How many steps the shading is banded into, 2 to 8.</summary>
        public static int CelBands
        {
            get => _celBands;
            set => _celBands = Math.Clamp(value, 2, 8);
        }

        private static int _celBands = 8;

        /// <summary>
        /// How dark the ink line goes, 0 to 1. Zero is no outline at all, and
        /// the renderer then leaves the depth in the cheaper buffer that
        /// cannot be read back.
        ///
        /// Locked at 0.5 (50%) -- steps and outline strength are no longer
        /// player-configurable, only the on/off switch above is.
        /// </summary>
        public static float CelEdge
        {
            get => _celEdge;
            set => _celEdge = Math.Clamp(value, 0, 1);
        }

        private static float _celEdge = 0.5f;

        /// <summary>Distance fog, where the room asks for it.</summary>
        public static bool Fog { get; set; } = true;

        /// <summary>
        /// Linear texture filtering. The DS had none, so off is both faster and
        /// what the game looked like.
        /// </summary>
        public static bool TextureFiltering
        {
            get => TextureFilteringPreset != TextureFilteringPreset.Original;
            set => TextureFilteringPreset = value
                ? TextureFilteringPreset.Smooth
                : TextureFilteringPreset.Original;
        }

        /// <summary>
        /// Resolve persisted quality settings in one place.  A missing newer
        /// key uses the selected preset; the old filtering switch is consulted
        /// only when the new filtering key is absent.
        /// </summary>
        public static RenderQualitySnapshot ResolveQuality(MenuSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            GraphicsPreset preset = ParseGraphicsPreset(settings.GraphicsPreset, GraphicsPreset.Original);
            TextureFilteringPreset filtering = ResolveTextureFilteringPreset(
                settings.TextureFilteringPreset,
                settings.TextureFiltering,
                TextureFilteringFor(preset));
            AnisotropyLevel anisotropy = ParseAnisotropy(settings.Anisotropy, AnisotropyFor(preset));
            MsaaLevel msaa = ParseMsaa(settings.Msaa, MsaaFor(preset));
            bool bloom = ParseOnOff(settings.Bloom, BloomFor(preset));
            bool dynamicVisualLights = ParseOnOff(settings.DynamicVisualLights,
                DynamicVisualLightsFor(preset));
            return new RenderQualitySnapshot(preset, filtering, anisotropy, msaa,
                bloom, dynamicVisualLights);
        }

        public static void ApplyQuality(MenuSettings settings)
        {
            RenderQualitySnapshot quality = ResolveQuality(settings);
            GraphicsPreset = quality.GraphicsPreset;
            TextureFilteringPreset = quality.TextureFilteringPreset;
            Anisotropy = quality.Anisotropy;
            Msaa = quality.Msaa;
            Bloom = quality.Bloom;
            DynamicVisualLights = quality.DynamicVisualLights;
        }

        /// <summary>Capture quality exactly once before a frame is sealed.</summary>
        public static RenderQualitySnapshot CaptureSnapshot()
            => new(GraphicsPreset, TextureFilteringPreset, Anisotropy, Msaa,
                Bloom, DynamicVisualLights);

        public static TextureFilteringPreset TextureFilteringFor(GraphicsPreset preset)
            => NormalizeGraphicsPreset(preset) switch
            {
                GraphicsPreset.Enhanced => TextureFilteringPreset.Enhanced,
                _ => TextureFilteringPreset.Original
            };

        public static AnisotropyLevel AnisotropyFor(GraphicsPreset preset)
            => NormalizeGraphicsPreset(preset) == GraphicsPreset.Enhanced
                ? AnisotropyLevel.X4 : AnisotropyLevel.Off;

        public static MsaaLevel MsaaFor(GraphicsPreset preset)
            => NormalizeGraphicsPreset(preset) == GraphicsPreset.Enhanced
                ? MsaaLevel.X4 : MsaaLevel.Off;

        public static bool BloomFor(GraphicsPreset preset)
            => NormalizeGraphicsPreset(preset) == GraphicsPreset.Enhanced;

        public static bool DynamicVisualLightsFor(GraphicsPreset preset)
            => NormalizeGraphicsPreset(preset) == GraphicsPreset.Enhanced;

        public static GraphicsPreset ParseGraphicsPreset(string? value,
            GraphicsPreset fallback = GraphicsPreset.Original)
        {
            string token = QualityToken(value);
            return token switch
            {
                "original" => GraphicsPreset.Original,
                "enhanced" => GraphicsPreset.Enhanced,
                "performance" or "perf" => GraphicsPreset.Performance,
                _ => NormalizeGraphicsPreset(fallback)
            };
        }

        public static string FormatGraphicsPreset(GraphicsPreset value)
            => NormalizeGraphicsPreset(value) switch
            {
                GraphicsPreset.Enhanced => "enhanced",
                GraphicsPreset.Performance => "performance",
                _ => "original"
            };

        public static TextureFilteringPreset ParseTextureFilteringPreset(string? value,
            TextureFilteringPreset fallback = TextureFilteringPreset.Original)
        {
            string token = QualityToken(value);
            return token switch
            {
                "original" => TextureFilteringPreset.Original,
                "smooth" => TextureFilteringPreset.Smooth,
                "enhanced" => TextureFilteringPreset.Enhanced,
                _ => NormalizeTextureFilteringPreset(fallback)
            };
        }

        public static TextureFilteringPreset ResolveTextureFilteringPreset(string? value,
            string? legacyValue, TextureFilteringPreset fallback = TextureFilteringPreset.Original)
        {
            if (!String.IsNullOrWhiteSpace(value))
            {
                return ParseTextureFilteringPreset(value, fallback);
            }
            if (!String.IsNullOrWhiteSpace(legacyValue))
            {
                return ParseOnOff(legacyValue, fallback != TextureFilteringPreset.Original)
                    ? TextureFilteringPreset.Smooth
                    : TextureFilteringPreset.Original;
            }
            return NormalizeTextureFilteringPreset(fallback);
        }

        public static string FormatTextureFilteringPreset(TextureFilteringPreset value)
            => NormalizeTextureFilteringPreset(value) switch
            {
                TextureFilteringPreset.Smooth => "smooth",
                TextureFilteringPreset.Enhanced => "enhanced",
                _ => "original"
            };

        public static AnisotropyLevel ParseAnisotropy(string? value,
            AnisotropyLevel fallback = AnisotropyLevel.Off)
        {
            string token = QualityToken(value);
            if (token is "off" or "none") return AnisotropyLevel.Off;
            if (TryQualityInteger(token, out int requested))
            {
                return ClampAnisotropy(requested);
            }
            return NormalizeAnisotropy(fallback);
        }

        public static string FormatAnisotropy(AnisotropyLevel value)
            => NormalizeAnisotropy(value) switch
            {
                AnisotropyLevel.X2 => "2x",
                AnisotropyLevel.X4 => "4x",
                AnisotropyLevel.X8 => "8x",
                AnisotropyLevel.X16 => "16x",
                _ => "off"
            };

        public static AnisotropyLevel AnisotropyAtIndex(int index)
            => Math.Clamp(index, 0, _anisotropyLabels.Length - 1) switch
            {
                1 => AnisotropyLevel.X2,
                2 => AnisotropyLevel.X4,
                3 => AnisotropyLevel.X8,
                4 => AnisotropyLevel.X16,
                _ => AnisotropyLevel.Off
            };

        public static int AnisotropyIndex(AnisotropyLevel value)
            => NormalizeAnisotropy(value) switch
            {
                AnisotropyLevel.X2 => 1,
                AnisotropyLevel.X4 => 2,
                AnisotropyLevel.X8 => 3,
                AnisotropyLevel.X16 => 4,
                _ => 0
            };

        public static MsaaLevel ParseMsaa(string? value, MsaaLevel fallback = MsaaLevel.Off)
        {
            string token = QualityToken(value);
            if (token is "off" or "none") return MsaaLevel.Off;
            if (TryQualityInteger(token, out int requested))
            {
                return ClampMsaa(requested);
            }
            return NormalizeMsaa(fallback);
        }

        public static string FormatMsaa(MsaaLevel value)
            => NormalizeMsaa(value) switch
            {
                MsaaLevel.X2 => "2x",
                MsaaLevel.X4 => "4x",
                _ => "off"
            };

        public static MsaaLevel MsaaAtIndex(int index)
            => Math.Clamp(index, 0, _msaaLabels.Length - 1) switch
            {
                1 => MsaaLevel.X2,
                2 => MsaaLevel.X4,
                _ => MsaaLevel.Off
            };

        public static int MsaaIndex(MsaaLevel value)
            => NormalizeMsaa(value) switch
            {
                MsaaLevel.X2 => 1,
                MsaaLevel.X4 => 2,
                _ => 0
            };

        private static string QualityToken(string? value)
            => (value ?? String.Empty).Trim().ToLowerInvariant()
                .Replace(" ", String.Empty, StringComparison.Ordinal)
                .Replace("_", String.Empty, StringComparison.Ordinal)
                .Replace("-", String.Empty, StringComparison.Ordinal);

        private static bool TryQualityInteger(string token, out int value)
        {
            string number = token.EndsWith('x') ? token[..^1] : token;
            return Int32.TryParse(number, NumberStyles.Integer,
                CultureInfo.InvariantCulture, out value);
        }

        private static GraphicsPreset NormalizeGraphicsPreset(GraphicsPreset value)
            => Enum.IsDefined(value) ? value : GraphicsPreset.Original;

        private static TextureFilteringPreset NormalizeTextureFilteringPreset(TextureFilteringPreset value)
            => Enum.IsDefined(value) ? value : TextureFilteringPreset.Original;

        private static AnisotropyLevel NormalizeAnisotropy(AnisotropyLevel value)
            => value switch
            {
                AnisotropyLevel.X2 or AnisotropyLevel.X4 or AnisotropyLevel.X8
                    or AnisotropyLevel.X16 => value,
                _ => AnisotropyLevel.Off
            };

        private static MsaaLevel NormalizeMsaa(MsaaLevel value)
            => value switch
            {
                MsaaLevel.X2 or MsaaLevel.X4 => value,
                _ => MsaaLevel.Off
            };

        private static AnisotropyLevel ClampAnisotropy(int value)
            => value <= 0 ? AnisotropyLevel.Off
                : value <= 2 ? AnisotropyLevel.X2
                : value <= 4 ? AnisotropyLevel.X4
                : value <= 8 ? AnisotropyLevel.X8
                : AnisotropyLevel.X16;

        private static MsaaLevel ClampMsaa(int value)
            => value <= 0 ? MsaaLevel.Off
                : value <= 2 ? MsaaLevel.X2
                : MsaaLevel.X4;

        /// <summary>Apply a scale to one dimension, never below one pixel.</summary>
        public static int Scaled(int pixels)
        {
            if (_resolutionScale >= 100)
            {
                return pixels;
            }
            return Math.Max(1, pixels * _resolutionScale / 100);
        }

        public static bool ParseOnOff(string? value, bool fallback)
        {
            if (value == null)
            {
                return fallback;
            }
            string text = value.Trim().ToLowerInvariant();
            if (text == "on" || text == "true" || text == "yes")
            {
                return true;
            }
            if (text == "off" || text == "false" || text == "no")
            {
                return false;
            }
            return fallback;
        }

        public static string OnOff(bool value) => value ? "on" : "off";

        public static int ParseScale(string? value, int fallback)
        {
            if (value != null && Int32.TryParse(value.Trim().TrimEnd('%'),
                NumberStyles.Integer, CultureInfo.InvariantCulture, out int percent))
            {
                return Math.Clamp(percent, MinScale, 100);
            }
            return fallback;
        }

        /// <summary>Unclamped; the properties do their own clamping.</summary>
        public static int ParseInt(string? value, int fallback)
        {
            if (value != null && Int32.TryParse(value.Trim().TrimEnd('%'),
                NumberStyles.Integer, CultureInfo.InvariantCulture, out int number))
            {
                return number;
            }
            return fallback;
        }
    }
}
