using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace MphRead.Mods.MapGen;

/// <summary>
/// NetRadiant's layer extension is a stream of bounded definitions followed by
/// a current-layer index shared by entities and primitives. The native source
/// retains its hierarchy and colors; canonical geometry stores the full path.
/// </summary>
internal sealed class RadiantLayerInterchange
{
    internal const string DefaultLayer = "Architecture";
    internal const int MaximumLayers = 256;
    internal const int MaximumHierarchyDepth = 32;
    internal const int MaximumPathCharacters = 1024;
    private const string Marker = "//@$&";
    private sealed record Layer(string Name, int Parent, int Red, int Green, int Blue);
    private static readonly Regex Definition = new(
        "^layerdef\\s+\"(?<name>[^\"]*)\"\\s+(?<parent>[-+]?\\d+)\\s+(?<red>[-+]?\\d+)\\s+(?<green>[-+]?\\d+)\\s+(?<blue>[-+]?\\d+)$",
        RegexOptions.CultureInvariant);
    private static readonly Regex Assignment = new("^layer\\s+(?<index>[-+]?\\d+)$", RegexOptions.CultureInvariant);
    private readonly List<Layer> _layers = new();
    private string[]? _paths;
    private bool _sealed;
    private bool _hasAssignments;

    internal bool HasMetadata { get; private set; }
    // Stock Radiant adds this lone implicit default when saving older Prime
    // source that predates native layers. It carries no reassignment evidence;
    // retain the existing payload/adjunct layers in that ambiguous case.
    internal bool HasCanonicalAssignments => HasMetadata && (_hasAssignments
        || _layers.Count != 1 || _layers[0].Name != "0" || _layers[0].Parent != -1);
    internal int CurrentIndex { get; private set; }

    internal bool Read(string sourceLine, int depth)
    {
        string line = sourceLine;
        bool marked = line.StartsWith(Marker, StringComparison.Ordinal);
        if (marked)
        {
            if (line.Length == Marker.Length || !char.IsWhiteSpace(line[Marker.Length]))
                throw new InvalidDataException("Malformed native layer extension marker.");
            line = line[Marker.Length..].Trim();
        }
        bool definition = Word(line, "layerdef"), assignment = Word(line, "layer");
        if (!definition && !assignment)
        {
            if (marked) throw new InvalidDataException("Unsupported native layer extension directive.");
            return false;
        }
        if (definition)
        {
            if (depth != 0 || _sealed)
                throw new InvalidDataException("Native layer definitions must precede all entities and layer assignments.");
            if (_layers.Count >= MaximumLayers)
                throw new InvalidDataException("Native layer definitions exceed the 256-layer budget.");
            Match match = Definition.Match(line);
            if (!match.Success)
                throw new InvalidDataException("Expected a quoted native layer name, parent index and three RGB integers.");
            string name = match.Groups["name"].Value;
            RequireName(name);
            int parent = Integer(match, "parent"), red = Integer(match, "red"),
                green = Integer(match, "green"), blue = Integer(match, "blue");
            if (parent < -1 || parent >= MaximumLayers || red is < 0 or > 255
                || green is < 0 or > 255 || blue is < 0 or > 255)
                throw new InvalidDataException("Native layer parent or RGB color is out of range.");
            _layers.Add(new(name, parent, red, green, blue));
        }
        else
        {
            if (depth is not (0 or 1))
                throw new InvalidDataException("Native layer assignments are allowed only before entities or primitives.");
            Match match = Assignment.Match(line);
            if (!match.Success)
                throw new InvalidDataException("Expected exactly one native layer index.");
            Seal();
            int index = Integer(match, "index");
            if (index < 0 || index >= _paths!.Length)
                throw new InvalidDataException("Native layer assignment refers to an unknown layer index.");
            CurrentIndex = index;
            _hasAssignments = true;
        }
        HasMetadata = true;
        return true;
    }

    internal void Seal()
    {
        if (_sealed) return;
        if (_layers.Count == 0)
        {
            _paths = new[] { DefaultLayer };
            _sealed = true;
            return;
        }
        var paths = new string[_layers.Count];
        var states = new byte[_layers.Count];
        var names = new HashSet<(int Parent, string Name)>();
        foreach (Layer layer in _layers)
            if (layer.Parent >= _layers.Count || !names.Add((layer.Parent, layer.Name)))
                throw new InvalidDataException("Native layers have an unknown parent or duplicate sibling name.");
        string Resolve(int index, int depth)
        {
            if (depth > MaximumHierarchyDepth)
                throw new InvalidDataException("Native layer hierarchy exceeds 32 levels.");
            if (states[index] == 1)
                throw new InvalidDataException("Native layer parents contain a cycle.");
            if (states[index] == 2) return paths[index];
            states[index] = 1;
            Layer layer = _layers[index];
            string path = layer.Parent == -1 ? layer.Name : Resolve(layer.Parent, depth + 1) + "/" + layer.Name;
            if (index == 0 && layer.Parent == -1 && path == "0") path = DefaultLayer;
            if (path.Length > MaximumPathCharacters)
                throw new InvalidDataException("Canonical native layer path exceeds 1024 characters.");
            states[index] = 2;
            return paths[index] = path;
        }
        for (int i = 0; i < paths.Length; i++) Resolve(i, 1);
        // Depth checks must not depend on which ancestors were cached first.
        for (int i = 0; i < _layers.Count; i++)
        {
            int depth = 1;
            for (int parent = _layers[i].Parent; parent != -1; parent = _layers[parent].Parent)
                if (++depth > MaximumHierarchyDepth)
                    throw new InvalidDataException("Native layer hierarchy exceeds 32 levels.");
        }
        if (paths.Distinct(StringComparer.Ordinal).Count() != paths.Length)
            throw new InvalidDataException("Native layer names resolve to ambiguous canonical paths.");
        _paths = paths;
        _sealed = true;
    }

    internal string Path(int index)
    {
        Seal();
        if (index < 0 || index >= _paths!.Length)
            throw new InvalidDataException("Native primitive refers to an unknown layer index.");
        return _paths[index];
    }

    internal static IReadOnlyDictionary<string, int> WriteDefinitions(IEnumerable<MapGeometry> geometry, StringBuilder output)
    {
        var layers = new List<Layer> { new(DefaultLayer, -1, 0, 0, 0) };
        var indices = new Dictionary<string, int>(StringComparer.Ordinal) { [DefaultLayer] = 0 };
        foreach (MapGeometry value in geometry)
        {
            string path = value.Layer;
            if (path == null || path.Length > MaximumPathCharacters)
                throw new InvalidDataException("Canonical native layer path is missing or exceeds 1024 characters.");
            string[] segments = path.Split('/');
            if (segments.Length > MaximumHierarchyDepth)
                throw new InvalidDataException("Canonical native layer hierarchy exceeds 32 levels.");
            string current = "";
            int parent = -1;
            foreach (string segment in segments)
            {
                RequireName(segment);
                current = current.Length == 0 ? segment : current + "/" + segment;
                if (!indices.TryGetValue(current, out int index))
                {
                    if (layers.Count >= MaximumLayers)
                        throw new InvalidDataException("Canonical native layers exceed the 256-layer budget.");
                    index = layers.Count;
                    indices.Add(current, index);
                    layers.Add(new(segment, parent, 0, 0, 0));
                }
                parent = index;
            }
        }
        foreach (Layer layer in layers)
            output.AppendLine(Marker + " layerdef \"" + layer.Name + "\" "
                + layer.Parent.ToString(CultureInfo.InvariantCulture) + " 0 0 0");
        return indices;
    }

    internal static void WriteAssignment(int index, StringBuilder output)
        => output.AppendLine(Marker + " layer " + index.ToString(CultureInfo.InvariantCulture));

    private static bool Word(string line, string word) => line == word
        || (line.StartsWith(word, StringComparison.Ordinal) && line.Length > word.Length && char.IsWhiteSpace(line[word.Length]));

    private static int Integer(Match match, string group)
        => int.TryParse(match.Groups[group].Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int value)
            ? value : throw new InvalidDataException("Native layer index or color exceeds the integer budget.");

    private static void RequireName(string name)
    {
        // Native map tokens have a 1024-byte budget. Do not permit escapes or
        // separators whose interpretation would change on a native save.
        if (string.IsNullOrWhiteSpace(name) || name.Length > 256 || Encoding.UTF8.GetByteCount(name) > 768
            || name.Any(c => char.IsControl(c) || c is '"' or '\\' or '/'))
            throw new InvalidDataException("Native layer names must be nonempty, bounded portable path segments without quotes, slashes or control characters.");
    }
}
