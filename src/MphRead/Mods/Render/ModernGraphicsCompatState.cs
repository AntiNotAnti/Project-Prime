#if !MPHREAD_SERVER
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;

namespace MphRead.Mods.Render
{
    internal enum ModernProgramKind
    {
        Unknown,
        Clear,
        FixedFunction,
        World,
        Rtt,
        Shift,
        Cel,
        PlayerOutline,
        ToneMap,
        Backdrop,
        DeferredPbr,
        PostProcess
    }

    /// <summary>
    /// OpenGL-style shader/program/uniform bookkeeping with no OpenGL objects.
    /// ModernGraphicsCompat consumes this state and turns it into WebGPU bind
    /// groups/uniform buffers. Keeping locations and program IDs here lets the
    /// historical renderer keep its existing GL.Uniform* call sites.
    /// </summary>
    internal sealed class ModernGraphicsCompatState
    {
        private sealed class ShaderRecord
        {
            internal ShaderType Type;
            internal string Source = "";
            internal bool Compiled;
        }

        internal sealed class ProgramRecord
        {
            internal readonly HashSet<int> AttachedShaders = new();
            internal readonly Dictionary<string, int> Locations = new(StringComparer.Ordinal);
            internal readonly Dictionary<string, UniformValue> Uniforms = new(StringComparer.Ordinal);
            internal readonly HashSet<string> DeclaredUniforms = new(StringComparer.Ordinal);
            internal bool Linked;
            internal string VertexSource = "";
            internal string FragmentSource = "";
            internal ModernProgramKind Kind;
        }

        internal readonly struct UniformValue
        {
            internal UniformValue(int intValue)
            {
                IntValue = intValue;
                Data = null;
            }

            internal UniformValue(float[] data)
            {
                IntValue = 0;
                Data = data;
            }

            internal int IntValue { get; }
            internal float[]? Data { get; }
            internal bool IsInteger => Data == null;
        }

        private readonly Dictionary<int, ShaderRecord> _shaders = new();
        private readonly Dictionary<int, ProgramRecord> _programs = new();
        private readonly Dictionary<int, (int Program, string Name)> _locations = new();
        private int _nextShader = 1;
        private int _nextProgram = 1;
        private int _nextLocation = 1;

        internal int CurrentProgram { get; private set; }

        internal int CreateShader(ShaderType type)
        {
            int id = _nextShader++;
            _shaders.Add(id, new ShaderRecord { Type = type });
            return id;
        }

        internal void ShaderSource(int shader, string source)
        {
            RequireShader(shader).Source = source ?? "";
        }

        internal void CompileShader(int shader)
        {
            ShaderRecord record = RequireShader(shader);
            record.Compiled = record.Source.Length != 0;
        }

        internal void GetShader(int shader, ShaderParameter pname, out int value)
        {
            ShaderRecord record = RequireShader(shader);
            value = pname == ShaderParameter.CompileStatus && record.Compiled ? 1 : 0;
        }

        internal string GetShaderInfoLog(int shader)
        {
            return RequireShader(shader).Compiled ? "" : "No shader source was supplied.";
        }

        internal void DeleteShader(int shader)
        {
            _shaders.Remove(shader);
        }

        internal int CreateProgram()
        {
            int id = _nextProgram++;
            _programs.Add(id, new ProgramRecord());
            return id;
        }

        internal void AttachShader(int program, int shader)
        {
            RequireShader(shader);
            RequireProgram(program).AttachedShaders.Add(shader);
        }

        internal void DetachShader(int program, int shader)
        {
            RequireProgram(program).AttachedShaders.Remove(shader);
        }

        internal void LinkProgram(int program)
        {
            ProgramRecord record = RequireProgram(program);
            record.DeclaredUniforms.Clear();
            record.VertexSource = "";
            record.FragmentSource = "";

            foreach (int shaderId in record.AttachedShaders)
            {
                ShaderRecord shader = RequireShader(shaderId);
                if (!shader.Compiled)
                {
                    record.Linked = false;
                    return;
                }
                if (shader.Type == ShaderType.VertexShader)
                {
                    record.VertexSource = shader.Source;
                }
                else if (shader.Type == ShaderType.FragmentShader)
                {
                    record.FragmentSource = shader.Source;
                }
                ExtractUniforms(shader.Source, record.DeclaredUniforms);
            }

            record.Linked = record.VertexSource.Length != 0 && record.FragmentSource.Length != 0;
            record.Kind = Identify(record.VertexSource, record.FragmentSource);
        }

        internal void GetProgram(int program, GetProgramParameterName pname, out int value)
        {
            ProgramRecord record = RequireProgram(program);
            value = pname == GetProgramParameterName.LinkStatus && record.Linked ? 1 : 0;
        }

        internal string GetProgramInfoLog(int program)
        {
            return RequireProgram(program).Linked ? "" : "Program is missing a compiled vertex or fragment shader.";
        }

        internal void DeleteProgram(int program)
        {
            if (CurrentProgram == program) CurrentProgram = 0;
            if (!_programs.Remove(program, out ProgramRecord? record)) return;
            foreach (int location in record.Locations.Values)
            {
                _locations.Remove(location);
            }
        }

        internal void UseProgram(int program)
        {
            if (program != 0 && !RequireProgram(program).Linked)
            {
                throw new InvalidOperationException($"Program {program} has not linked.");
            }
            CurrentProgram = program;
        }

        internal int GetUniformLocation(int program, string name)
        {
            ProgramRecord record = RequireProgram(program);
            if (!record.Linked || !record.DeclaredUniforms.Contains(name))
            {
                return -1;
            }
            if (record.Locations.TryGetValue(name, out int location))
            {
                return location;
            }

            location = _nextLocation++;
            record.Locations.Add(name, location);
            _locations.Add(location, (program, name));
            return location;
        }

        internal void Uniform1(int location, int value)
        {
            Set(location, new UniformValue(value));
        }

        internal void Uniform1(int location, float value)
        {
            SetFloats(location, stackalloc float[] { value });
        }

        internal void Uniform1(int location, int count, float[] values)
        {
            SetFloats(location, values.AsSpan(0, Math.Min(values.Length, count)));
        }

        internal void Uniform2(int location, float x, float y)
        {
            SetFloats(location, stackalloc float[] { x, y });
        }

        internal void Uniform3(int location, Vector3 value)
        {
            SetFloats(location, stackalloc float[] { value.X, value.Y, value.Z });
        }

        internal void Uniform3(int location, int count, float[] values)
        {
            SetFloats(location, values.AsSpan(0, Math.Min(values.Length, checked(count * 3))));
        }

        internal void Uniform4(int location, Vector4 value)
        {
            SetFloats(location, stackalloc float[] { value.X, value.Y, value.Z, value.W });
        }

        internal void Uniform4(int location, float x, float y, float z, float w)
        {
            SetFloats(location, stackalloc float[] { x, y, z, w });
        }

        internal void Uniform4(int location, int x, int y, int z, int w)
        {
            SetFloats(location, stackalloc float[] { x, y, z, w });
        }

        internal void UniformMatrix4(int location, bool transpose, Matrix4 value)
        {
            Span<float> data = stackalloc float[]
            {
                value.M11, value.M12, value.M13, value.M14,
                value.M21, value.M22, value.M23, value.M24,
                value.M31, value.M32, value.M33, value.M34,
                value.M41, value.M42, value.M43, value.M44
            };
            if (transpose) TransposeInPlace(data, 1);
            SetFloats(location, data);
        }

        internal void UniformMatrix4(int location, int count, bool transpose, float[] values)
        {
            if (!transpose)
            {
                SetFloats(location, values.AsSpan(0, Math.Min(values.Length, checked(count * 16))));
                return;
            }
            float[] data = Copy(values, checked(count * 16));
            TransposeInPlace(data, count);
            SetFloats(location, data);
        }

        internal void GetUniform(int program, int location, out int value)
        {
            value = 0;
            if (!_locations.TryGetValue(location, out var slot) || slot.Program != program) return;
            ProgramRecord record = RequireProgram(program);
            if (!record.Uniforms.TryGetValue(slot.Name, out UniformValue uniform)) return;
            value = uniform.IsInteger
                ? uniform.IntValue
                : uniform.Data is { Length: > 0 } data ? (int)data[0] : 0;
        }

        internal ProgramRecord Program(int id) => RequireProgram(id);

        private void SetFloats(int location, ReadOnlySpan<float> values)
        {
            if (location < 0) return;
            if (!_locations.TryGetValue(location, out var slot) || CurrentProgram != slot.Program)
                throw new InvalidOperationException($"Invalid uniform location {location} for program {CurrentProgram}.");
            var record = RequireProgram(slot.Program);
            if (record.Uniforms.TryGetValue(slot.Name, out var existing) && existing.Data?.Length == values.Length)
                values.CopyTo(existing.Data);
            else record.Uniforms[slot.Name] = new UniformValue(values.ToArray());
        }

        private void Set(int location, UniformValue value)
        {
            if (location < 0) return; // OpenGL silently ignores location -1.
            if (!_locations.TryGetValue(location, out var slot))
            {
                throw new InvalidOperationException($"Unknown uniform location {location}.");
            }
            if (CurrentProgram != slot.Program)
            {
                throw new InvalidOperationException(
                    $"Uniform location {location} belongs to program {slot.Program}, current program is {CurrentProgram}.");
            }
            RequireProgram(slot.Program).Uniforms[slot.Name] = value;
        }

        private ShaderRecord RequireShader(int id)
        {
            return _shaders.TryGetValue(id, out ShaderRecord? record)
                ? record
                : throw new InvalidOperationException($"Unknown shader {id}.");
        }

        private ProgramRecord RequireProgram(int id)
        {
            return _programs.TryGetValue(id, out ProgramRecord? record)
                ? record
                : throw new InvalidOperationException($"Unknown program {id}.");
        }

        private static float[] Copy(float[] values, int length)
        {
            length = Math.Clamp(length, 0, values.Length);
            var result = new float[length];
            Array.Copy(values, result, length);
            return result;
        }

        private static void TransposeInPlace(Span<float> values, int matrices)
        {
            for (int m = 0; m < matrices; m++)
            {
                int b = m * 16;
                Swap(values, b + 1, b + 4);
                Swap(values, b + 2, b + 8);
                Swap(values, b + 3, b + 12);
                Swap(values, b + 6, b + 9);
                Swap(values, b + 7, b + 13);
                Swap(values, b + 11, b + 14);
            }
        }

        private static void Swap(Span<float> values, int a, int b)
        {
            (values[a], values[b]) = (values[b], values[a]);
        }

        private static void ExtractUniforms(string source, HashSet<string> output)
        {
            string withoutComments = Regex.Replace(source,
                @"//.*?$|/\*.*?\*/", "", RegexOptions.Multiline | RegexOptions.Singleline);
            foreach (Match match in Regex.Matches(withoutComments,
                @"\buniform\s+(?:highp\s+)?\w+(?:\[(\d+)\])?\s+(\w+)(?:\[(\d+)\])?\s*;"))
            {
                string name = match.Groups[2].Value;
                output.Add(name);
                string countText = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[3].Value;
                if (int.TryParse(countText, out int count))
                    for (int i = 0; i < count; i++) output.Add($"{name}[{i}]");
            }
        }

        private static ModernProgramKind Identify(string vertex, string fragment)
        {
            if (vertex == DeferredPbrShader.VertexSource && fragment == DeferredPbrShader.FragmentSource)
                return ModernProgramKind.DeferredPbr;
            if (vertex == GraphicsPipelineShader.VertexSource && fragment == GraphicsPipelineShader.FragmentSource)
                return ModernProgramKind.PostProcess;
            if (vertex == Shaders.VertexShader && fragment == Shaders.FragmentShader)
                return ModernProgramKind.World;
            if (vertex == Shaders.RttVertexShader && fragment == Shaders.RttFragmentShader)
                return ModernProgramKind.Rtt;
            if (vertex == Shaders.RttVertexShader && fragment == Shaders.ShiftFragmentShader)
                return ModernProgramKind.Shift;
            if (vertex == Shaders.RttVertexShader && fragment == Shaders.CelFragmentShader)
                return ModernProgramKind.Cel;
            if (vertex == PlayerOutlineShader.VertexSource && fragment == PlayerOutlineShader.Source)
                return ModernProgramKind.PlayerOutline;
            if (vertex == GraphicsToneMapShader.VertexSource && fragment == GraphicsToneMapShader.FragmentSource)
                return ModernProgramKind.ToneMap;
            if (vertex == Shaders.BackdropVertexShader && fragment == Shaders.BackdropFragmentShader)
                return ModernProgramKind.Backdrop;
            return ModernProgramKind.Unknown;
        }
    }
}
#endif
