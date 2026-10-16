using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using OpenTK.Mathematics;

namespace MphRead.Mods.MapGen;

/// <summary>
/// Bounded Quake shader-script source representation. Source archives are
/// author-provided assets; only approved stage instructions become native
/// material motion, and fog/multipass declarations remain explicit pending
/// native volume/depth integration.
/// </summary>
internal sealed record Q3ShaderStage(
    string Texture, float ScrollS, float ScrollT, float RotateDegrees,
    float ScaleS, float ScaleT, bool HasBlend, bool HasWave, bool Unsupported);

internal sealed record Q3ShaderProgram(
    string Name, IReadOnlyList<Q3ShaderStage> Stages,
    bool HasFog, Vector3 FogColor, float FogDistance)
{
    internal bool RequiresFullShader => HasFog || Stages.Count(s => !s.Texture.Equals("$lightmap",
        StringComparison.OrdinalIgnoreCase)) > 1
        || Stages.Any(s => s.HasBlend || s.HasWave || s.Unsupported);

    internal bool TryNativeUv(out MapMaterialAnimation animation)
    {
        animation = null!;
        var stages = Stages.Where(s => !s.Texture.Equals("$lightmap",
            StringComparison.OrdinalIgnoreCase)).ToArray();
        if (stages.Length != 1 || HasFog || stages[0].HasWave || stages[0].Unsupported
            || stages[0].HasBlend || Stages.Any(s => s.Unsupported))
            return false;
        var stage = stages[0];
        if (MathF.Abs(stage.ScrollS) > MapUvAnimation.MaxScrollSpeed
            || MathF.Abs(stage.ScrollT) > MapUvAnimation.MaxScrollSpeed
            || MathF.Abs(stage.RotateDegrees) > MapUvAnimation.MaxRotationSpeed
            || stage.ScaleS <= 0 || stage.ScaleT <= 0
            || stage.ScaleS > MapUvAnimation.MaxScale
            || stage.ScaleT > MapUvAnimation.MaxScale)
            return false;
        int length = 0;
        for (int frames = MapUvAnimation.MinLoopFrames;
            frames <= MapUvAnimation.MaxLoopFrames; frames += MapUvAnimation.NativeFramesPerSecond)
        {
            if (MapUvAnimation.IsSeamless(stage.ScrollS, frames)
                && MapUvAnimation.IsSeamless(stage.ScrollT, frames)
                && MapUvAnimation.IsRotationSeamless(stage.RotateDegrees, frames))
            { length = frames; break; }
        }
        if (length == 0) return false;
        animation = new MapMaterialAnimation {
            UvScroll = new[] { stage.ScrollS, stage.ScrollT },
            UvRotationDegreesPerSecond = stage.RotateDegrees,
            UvScale = new[] { stage.ScaleS, stage.ScaleT }, LoopFrames = length
        };
        return true;
    }
}

internal static class Q3ShaderStageParser
{
    internal const int MaxScriptChars = 4 * 1024 * 1024;
    private const int MaxTokens = 100000;
    private const int MaxPrograms = 4096;
    private const int MaxStages = 8;

    private static bool Number(IReadOnlyList<string> tokens, ref int at, out float value,
        float min = -100000f, float max = 100000f)
    {
        value = 0;
        if (at >= tokens.Count || !float.TryParse(tokens[at], NumberStyles.Float,
                CultureInfo.InvariantCulture, out float parsed)
            || !float.IsFinite(parsed) || parsed < min || parsed > max)
            return false;
        value = parsed; at++; return true;
    }

    private static List<string> Tokens(string source)
    {
        var output = new List<string>();
        using var reader = new StringReader(source);
        string? line;
        bool commentBlock = false;
        while ((line = reader.ReadLine()) != null && output.Count < MaxTokens)
        {
            var token = new StringBuilder();
            void Flush()
            {
                if (token.Length == 0) return;
                output.Add(token.ToString());
                token.Clear();
            }
            for (int i = 0; i < line.Length; i++)
            {
                char ch = line[i];
                if (commentBlock)
                {
                    if (ch == '*' && i + 1 < line.Length && line[i + 1] == '/')
                    { commentBlock = false; i++; }
                    continue;
                }
                if (ch == '/' && i + 1 < line.Length && line[i + 1] == '*')
                { Flush(); commentBlock = true; i++; continue; }
                if (ch == '/' && i + 1 < line.Length && line[i + 1] == '/')
                { Flush(); break; }
                if (char.IsWhiteSpace(ch)) { Flush(); continue; }
                if (ch is '{' or '}' or '(' or ')')
                { Flush(); output.Add(ch.ToString()); continue; }
                if (token.Length < 256) token.Append(ch);
            }
            Flush();
        }
        return output;
    }

    internal static IReadOnlyDictionary<string, Q3ShaderProgram> Parse(string source)
    {
        var result = new Dictionary<string, Q3ShaderProgram>(StringComparer.OrdinalIgnoreCase);
        if (source.Length > MaxScriptChars) return result;
        var tokens = Tokens(source);
        int i = 0;
        while (i + 1 < tokens.Count && result.Count < MaxPrograms)
        {
            string name = tokens[i++].Trim('"').TrimStart('/');
            if (name is "{" or "}" or "(" or ")" || name.Length == 0
                || i >= tokens.Count || tokens[i++] != "{") continue;
            int depth = 1;
            var stages = new List<Q3ShaderStage>();
            bool fog = false;
            Vector3 fogColor = default; float fogDistance = 0;
            string texture = ""; float scrollS = 0, scrollT = 0, rotate = 0, scaleS = 1, scaleT = 1;
            bool blend = false, wave = false, unsupported = false;
            while (i < tokens.Count && depth > 0)
            {
                string key = tokens[i++].ToLowerInvariant();
                if (key == "{")
                {
                    depth++;
                    if (depth == 2)
                    { texture = ""; scrollS = scrollT = rotate = 0; scaleS = scaleT = 1;
                      blend = wave = unsupported = false; }
                    continue;
                }
                if (key == "}")
                {
                    if (depth == 2 && stages.Count < MaxStages)
                        stages.Add(new(texture, scrollS, scrollT, rotate, scaleS, scaleT,
                            blend, wave, unsupported));
                    depth--;
                    continue;
                }
                if (depth == 1 && key == "fogparms")
                {
                    int start = i;
                    if (i < tokens.Count && tokens[i] == "(") i++;
                    if (Number(tokens, ref i, out float r, 0, 1)
                        && Number(tokens, ref i, out float g, 0, 1)
                        && Number(tokens, ref i, out float b, 0, 1))
                    {
                        if (i < tokens.Count && tokens[i] == ")") i++;
                        if (Number(tokens, ref i, out float distance, 0.001f, 100000))
                        { fog = true; fogColor = new(r, g, b); fogDistance = distance; }
                    }
                    else i = start;
                    continue;
                }
                if (depth != 2) continue;
                if (key is "map" or "clampmap")
                {
                    if (i < tokens.Count) texture = tokens[i++].Trim('"');
                }
                else if (key == "tcmod" && i < tokens.Count)
                {
                    string op = tokens[i++].ToLowerInvariant();
                    if (op == "scroll")
                    {
                        if (Number(tokens, ref i, out float u) && Number(tokens, ref i, out float v))
                        { scrollS += u; scrollT += v; }
                        else unsupported = true;
                    }
                    else if (op == "rotate")
                    {
                        if (Number(tokens, ref i, out float angle)) rotate += angle;
                        else unsupported = true;
                    }
                    else if (op == "scale")
                    {
                        if (Number(tokens, ref i, out float u, 0.001f, 8)
                            && Number(tokens, ref i, out float v, 0.001f, 8))
                        { scaleS *= u; scaleT *= v; }
                        else unsupported = true;
                    }
                    else unsupported = true;
                }
                else if (key == "blendfunc")
                {
                    blend = true;
                    if (i < tokens.Count)
                    {
                        string first = tokens[i++].ToLowerInvariant();
                        if (first is not ("add" or "blend" or "filter") && i < tokens.Count) i++;
                    }
                }
                else if (key is "rgbgen" or "alphagen" or "alphafunc"
                    or "tcgen" or "depthfunc" or "depthwrite"
                    or "animmap" or "clampmap")
                    { wave = true; unsupported = true; }
            }
            if (depth == 0 && stages.Count > 0 && !result.ContainsKey(name))
                result.Add(name, new(name, stages, fog, fogColor, fogDistance));
        }
        return result;
    }

    /// <summary>Read only the local source author's shader scripts. No download.</summary>
    internal static IReadOnlyDictionary<string, Q3ShaderProgram> ReadArchives(
        IReadOnlyList<string> archivePaths)
    {
        var programs = new Dictionary<string, Q3ShaderProgram>(StringComparer.OrdinalIgnoreCase);
        int scripts = 0;
        foreach (string path in archivePaths)
        {
            if (!File.Exists(path)) continue;
            try
            {
                using var archive = ZipFile.OpenRead(path);
                foreach (var entry in archive.Entries)
                {
                    if (!entry.FullName.EndsWith(".shader", StringComparison.OrdinalIgnoreCase)
                        || entry.Length > MaxScriptChars || ++scripts > 512) continue;
                    using var reader = new StreamReader(entry.Open(), Encoding.UTF8, true);
                    foreach (var pair in Parse(reader.ReadToEnd()))
                        if (!programs.ContainsKey(pair.Key))
                            programs.Add(pair.Key, pair.Value);
                }
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException
                or UnauthorizedAccessException or NotSupportedException) { }
        }
        return programs;
    }
}
