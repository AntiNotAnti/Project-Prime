using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace MphRead.Mods.MapGen;

/// <summary>
/// Exact, non-executable Quake .map patchDef2 control-grid interchange.
/// Only known patchDef2 fields are admitted, so unsupported source metadata
/// cannot vanish during a native-editable roundtrip.
/// </summary>
internal static class QuakePatchDef2
{
    private static readonly Regex Tokens = new(
        "\\(|\\)|\"[^\"]*\"|[^\\s()]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public sealed record Parsed(string Shader, MapBezierPatch Geometry);

    public static Parsed Parse(IReadOnlyList<string> body, float sourceUnits)
    {
        if (body.Count == 0 || body.Count > 512
            || !float.IsFinite(sourceUnits) || sourceUnits is < 1 or > 1024)
            throw new InvalidDataException("Missing or over-budget patchDef2 source.");
        string text = string.Join("\n", body);
        string[] tokens = Tokens.Matches(text).Select(m => m.Value).ToArray();
        if (tokens.Length > 2500)
            throw new InvalidDataException("patchDef2 contains too many tokens.");
        int next = 0;
        string Read()
        {
            if (next >= tokens.Length)
                throw new InvalidDataException("Truncated patchDef2 control grid.");
            return tokens[next++];
        }
        void Expect(string symbol)
        {
            if (Read() != symbol)
                throw new InvalidDataException("Unexpected patchDef2 structure; expected " + symbol + ".");
        }
        int Integer()
        {
            if (!int.TryParse(Read(), NumberStyles.None,
                CultureInfo.InvariantCulture, out int value))
                throw new InvalidDataException("Invalid patchDef2 dimension/metadata integer.");
            return value;
        }
        float Number()
        {
            if (!float.TryParse(Read(), NumberStyles.Float,
                CultureInfo.InvariantCulture, out float value) || !float.IsFinite(value))
                throw new InvalidDataException("Invalid patchDef2 control coordinate/UV.");
            return value;
        }

        string shader = Read();
        if (shader.Length > 1 && shader[0] == '"' && shader[^1] == '"')
            shader = shader[1..^1];
        Expect("(");
        int columns = Integer(), rows = Integer();
        if (columns is < 3 or > BezierPatchCompiler.MaximumDimension
            || rows is < 3 or > BezierPatchCompiler.MaximumDimension
            || columns % 2 == 0 || rows % 2 == 0)
            throw new InvalidDataException("patchDef2 requires odd 3-17 control dimensions.");
        for (int i = 0; i < 3; i++)
            if (Integer() != 0)
                throw new InvalidDataException("Unsupported nonzero patchDef2 header metadata.");
        Expect(")");
        Expect("(");
        var patch = new MapBezierPatch { Columns = columns, Rows = rows,
            Tessellation = 3 };
        for (int col = 0; col < columns; col++)
        {
            Expect("(");
            for (int row = 0; row < rows; row++)
            {
                Expect("(");
                float x = Number(), y = Number(), z = Number();
                float s = Number(), t = Number();
                Expect(")");
                if (Math.Abs(x) >= 65536 || Math.Abs(y) >= 65536
                    || Math.Abs(z) >= 65536 || Math.Abs(s) > 128
                    || Math.Abs(t) > 128)
                    throw new InvalidDataException("patchDef2 coordinate or UV exceeds source budget.");
                patch.ControlPoints.Add(new[]
                {
                    x/sourceUnits, z/sourceUnits, -y/sourceUnits, s, t
                });
            }
            Expect(")");
        }
        Expect(")");
        if (next != tokens.Length)
            throw new InvalidDataException("Unsupported trailing patchDef2 properties or tokens.");
        return new Parsed(shader,patch);
    }

    public static void Write(MapBezierPatch patch, string shader,
        float sourceUnits, StringBuilder sb)
    {
        if (!float.IsFinite(sourceUnits) || sourceUnits is < 1 or > 1024
            || patch.Columns is < 3 or > BezierPatchCompiler.MaximumDimension
            || patch.Rows is < 3 or > BezierPatchCompiler.MaximumDimension
            || patch.Columns % 2 == 0 || patch.Rows % 2 == 0
            || patch.ControlPoints == null
            || patch.ControlPoints.Count != patch.Columns * patch.Rows
            || patch.ControlPoints.Any(p => p is not { Length: 5 }
                || p.Any(v => !float.IsFinite(v))))
            throw new InvalidDataException("Unsupported patchDef2 export dimensions or control grid.");

        static string F(float n) => n.ToString("R", CultureInfo.InvariantCulture);
        sb.AppendLine("patchDef2");
        sb.AppendLine("{");
        sb.AppendLine(shader);
        sb.AppendLine("( " + patch.Columns.ToString(CultureInfo.InvariantCulture)
            + " " + patch.Rows.ToString(CultureInfo.InvariantCulture) + " 0 0 0 )");
        sb.AppendLine("(");
        for (int col = 0; col < patch.Columns; col++)
        {
            sb.Append("( ");
            for (int row = 0; row < patch.Rows; row++)
            {
                float[] cp = patch.ControlPoints[col * patch.Rows + row];
                float x = cp[0] * sourceUnits;
                float y = -cp[2] * sourceUnits;
                float z = cp[1] * sourceUnits;
                if (!float.IsFinite(x) || !float.IsFinite(y)
                    || !float.IsFinite(z) || Math.Abs(x) >= 65536
                    || Math.Abs(y) >= 65536 || Math.Abs(z) >= 65536
                    || Math.Abs(cp[3]) > 128 || Math.Abs(cp[4]) > 128)
                    throw new InvalidDataException("patchDef2 control point exceeds Quake source budget.");
                sb.Append("( ").Append(F(x)).Append(' ').Append(F(y)).Append(' ')
                    .Append(F(z)).Append(' ').Append(F(cp[3]))
                    .Append(' ').Append(F(cp[4])).Append(" ) ");
            }
            sb.AppendLine(")");
        }
        sb.AppendLine(")");
        sb.AppendLine("}");
    }
}
