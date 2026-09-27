using OpenTK.Graphics.OpenGL;
namespace MphRead.Mods.Cosmetics
{
    internal sealed class CosmeticUniforms
    {
        private readonly int _skin, _effect, _time, _primary, _secondary, _intensity, _pulse, _scroll, _dissolve;
        public CosmeticUniforms(int program)
        {
            _skin = GL.GetUniformLocation(program, "cosmetic_skin"); _effect = GL.GetUniformLocation(program, "cosmetic_effect");
            _time = GL.GetUniformLocation(program, "cosmetic_time"); _primary = GL.GetUniformLocation(program, "cosmetic_primary");
            _secondary = GL.GetUniformLocation(program, "cosmetic_secondary"); _intensity = GL.GetUniformLocation(program, "cosmetic_intensity");
            _pulse = GL.GetUniformLocation(program, "cosmetic_pulse"); _scroll = GL.GetUniformLocation(program, "cosmetic_scroll");
            _dissolve = GL.GetUniformLocation(program, "cosmetic_dissolve");
        }
        private CosmeticSurface _last;
        private bool _hasLast;
        public void Apply(CosmeticSurface surface)
        {
            // Uniform values belong to the program and survive program switches.
            // Most world/HUD meshes have no cosmetics: do not issue nine GL
            // calls per mesh just to repeatedly upload an inactive state.
            if (surface.Skin == 0 && surface.Effect == 0 && surface.Dissolve <= 0) surface = default;
            if (_hasLast && _last == surface) return;
            _last = surface; _hasLast = true;
            GL.Uniform1(_skin, surface.Skin); GL.Uniform1(_effect, surface.Effect); GL.Uniform1(_time, surface.Time);
            GL.Uniform3(_primary, surface.Primary); GL.Uniform3(_secondary, surface.Secondary);
            GL.Uniform1(_intensity, surface.Intensity); GL.Uniform1(_pulse, surface.Pulse); GL.Uniform1(_scroll, surface.Scroll);
            GL.Uniform1(_dissolve, surface.Dissolve);
        }
    }
}
