using System.Diagnostics;
using OpenTK.Graphics.OpenGL;

namespace MphRead;

public partial class Scene
{
    private void InitShaders()
    {
        try { InitShadersCore(); }
        catch
        {
            // A later program, target or variant can fail after earlier programs
            // have transferred ownership. Roll the whole initialization back.
            ReleaseSceneShaderInitialization();
            throw;
        }
    }

    private static int CompileSceneShader(ShaderType type, string source, string stage)
    {
        int shader = GL.CreateShader(type);
        try
        {
            GL.ShaderSource(shader, source);
            Mods.Render.OpenGlFailureInjection.ShaderObject(stage, shader);
            GL.CompileShader(shader);
            Mods.Render.OpenGlFailureInjection.ShaderObject(stage + "-compiled", shader);
            GL.GetShader(shader, ShaderParameter.CompileStatus, out int compiled);
            string log = compiled == 0 || Debugger.IsAttached ? GL.GetShaderInfoLog(shader) : "";
            if (Debugger.IsAttached && log != "") Debugger.Break();
            if (compiled == 0) throw new ProgramException($"Failed to compile {stage} shader. {log}");
            return shader;
        }
        catch { GL.DeleteShader(shader); throw; }
    }

    private static int LinkSceneProgram(int vertex, int fragment, string stage)
    {
        int program = GL.CreateProgram();
        try
        {
            GL.AttachShader(program, vertex);
            GL.AttachShader(program, fragment);
            Mods.Render.OpenGlFailureInjection.ShaderObject(stage, program);
            GL.LinkProgram(program);
            Mods.Render.OpenGlFailureInjection.ShaderObject(stage + "-linked", program);
            GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int linked);
            if (linked == 0) throw new ProgramException($"Failed to link {stage}. {GL.GetProgramInfoLog(program)}");
            GL.DetachShader(program, vertex);
            GL.DetachShader(program, fragment);
            return program;
        }
        catch { GL.DeleteProgram(program); throw; }
    }

    private static void DeleteSceneShader(ref int shader)
    {
        if (shader == 0) return;
        GL.DeleteShader(shader);
        shader = 0;
    }

    private void ReleaseSceneShaderInitialization()
    {
        // A deleted program remains live while bound. Only unbind this scene's
        // program; another foreground/replica/editor owner's binding survives.
        int current = GL.GetInteger(GetPName.CurrentProgram);
        if (current != 0 && (current == _shaderProgramId || current == _rttShaderProgramId
            || current == _shiftShaderProgramId || current == _celShaderProgramId
            || current == _fullWorldProgram?.Program || current == _nativeWorldProgram?.Program
            || current == _plainWorldProgram?.Program || current == _unlitWorldProgram?.Program))
            GL.UseProgram(0);
        // The cel target also owns a reference to _screenTexture. Release
        // it before deleting that texture in the shell's persistent context.
        if (_celFrameBuffer != 0)
        {
            GL.DeleteFramebuffer(_celFrameBuffer);
            _celFrameBuffer = 0;
        }
        _celFrameBufferColor = 0;
        if (_frameBuffer != 0)
        {
            GL.DeleteFramebuffer(_frameBuffer);
            _frameBuffer = 0;
        }
        if (_renderBuffer != 0)
        {
            GL.DeleteRenderbuffer(_renderBuffer);
            _renderBuffer = 0;
        }
        DeleteTexture(ref _screenTexture);
        DeleteTexture(ref _celTexture);
        DeleteTexture(ref _depthTexture);
        ReleaseWorldShaderVariants();
        DeleteProgram(ref _shaderProgramId);
        DeleteProgram(ref _rttShaderProgramId);
        DeleteProgram(ref _shiftShaderProgramId);
        DeleteProgram(ref _celShaderProgramId);
    }
}
