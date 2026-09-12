namespace MphRead
{
    /// <summary>
    /// A render submission's primitive identity.  This is intentionally a
    /// client concern: the game assembly owns the source model instructions,
    /// while the client owns how those instructions are presented.
    /// </summary>
    public enum RenderPrimitive
    {
        // Box/cylinder/sphere retain their historical 1/2/3 identities.
        Mesh = 0,
        Box = 1,
        Cylinder = 2,
        Sphere = 3,
        Quad = 4,
        Ngon = 5,
        Particle = 6,
        TrailSingle = 7,
        TrailMulti = 8,
        TrailStack = 9
    }

    /// <summary>
    /// The six fixed-function scene passes are kept explicit while the
    /// modern backend is being introduced.  Transparent submissions are
    /// visited by the stencil mark, depth rebuild, behind, and front passes;
    /// they must not be collapsed into one blend pass.
    /// </summary>
    public enum RenderPassKind
    {
        Opaque,
        Decal,
        TransparentStencil,
        // Name used by the pass-order documentation; same physical pass.
        TransparentMark = TransparentStencil,
        DepthRebuild,
        TransparentBehind,
        TransparentFront,
        Overlay,
        Hud
    }

    /// <summary>
    /// Renderer selection. SDL is the desktop runtime backend. Gles is an
    /// Android runtime backend and is not a command-line option.
    /// </summary>
    public enum RenderBackendKind
    {
        Sdl,
        Gles
    }

    public static class RenderBackendSelection
    {
        public static RenderBackendKind Current { get; private set; } = DefaultForCurrentPlatform();

        /// <summary>
        /// Returns the platform runtime default without changing global
        /// selection state. The boolean overload keeps this decision pure and
        /// testable without spoofing the process platform.
        /// </summary>
        public static RenderBackendKind DefaultForPlatform(bool isAndroid)
            => isAndroid ? RenderBackendKind.Gles : RenderBackendKind.Sdl;

        public static RenderBackendKind DefaultForCurrentPlatform()
            => DefaultForPlatform(System.OperatingSystem.IsAndroid());

        /// <summary>True for backends that consume portable CPU meshes.</summary>
        public static bool UsesPortableMeshPreparation(RenderBackendKind backend)
            => backend is RenderBackendKind.Sdl or RenderBackendKind.Gles;

        public static bool IsGles(RenderBackendKind backend)
            => backend == RenderBackendKind.Gles;

        /// <summary>The canonical command-line spelling for the selected backend.</summary>
        public static string CurrentCliValue => ToCliValue(Current);

        public static string ToCliValue(RenderBackendKind backend) => backend switch
        {
            RenderBackendKind.Sdl => "sdl",
            // Diagnostic/logging spelling only. TryParse deliberately does not
            // accept this value because --renderer exposes sdl only.
            RenderBackendKind.Gles => "gles",
            _ => throw new System.ArgumentOutOfRangeException(nameof(backend), backend, null)
        };

        public static bool TryParse(string? value, out RenderBackendKind backend)
        {
            if (string.Equals(value?.Trim(), "sdl", System.StringComparison.OrdinalIgnoreCase))
            {
                backend = RenderBackendKind.Sdl;
                return true;
            }
            // Keep a valid desktop default for callers that inspect the out
            // value after a failed parse; ApplyArguments leaves Current alone.
            backend = RenderBackendKind.Sdl;
            return false;
        }

        /// <summary>
        /// Parses the internal <c>--renderer=sdl</c> switch. Unknown values are
        /// rejected without changing the current selection, so a typo cannot
        /// silently select a backend. Gles is selected by the Android platform
        /// default and intentionally is not exposed as a CLI value.
        /// </summary>
        public static bool ApplyArguments(System.Collections.Generic.IEnumerable<string> args,
            out string? error)
        {
            error = null;
            foreach (string raw in args)
            {
                if (!raw.StartsWith("--renderer=", System.StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                string value = raw["--renderer=".Length..];
                if (!TryParse(value, out RenderBackendKind backend))
                {
                    error = $"Unknown renderer '{value}'. Expected sdl.";
                    return false;
                }
                Current = backend;
            }
            return true;
        }

        public static void ResetForTests() => Current = DefaultForCurrentPlatform();
    }
}
