using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;

namespace MphRead.Mods.Input;

// Primary controls keep their deterministic enum+1 identity and legacy records.
// Additional instances have independent IDs, action, visibility and geometry.
public readonly record struct TouchButtonInstance(int Id, TouchControl Action,
    bool Enabled, float X, float Y, float Scale);

public static partial class TouchSettings
{
    public const int MaximumExtraInstances = 11; // 21 primary + 11 = 32 targets
    public const int FirstExtraInstanceId = 1000;
    public const string InstancesSettingKey = "touch_instances_v1";
    private static TouchButtonInstance[] _instances = Array.Empty<TouchButtonInstance>();
    private static bool _instancesEdited;
    private static int _instanceRevision;
    public static IReadOnlyList<TouchButtonInstance> ExtraInstances => Volatile.Read(ref _instances);
    public static int InstanceRevision => Volatile.Read(ref _instanceRevision);
    public static TouchButtonInstance[] CopyInstances() => (TouchButtonInstance[])_instances.Clone();
    public static void ReplaceInstances(IEnumerable<TouchButtonInstance> instances)
    {
        var copy = instances.Take(MaximumExtraInstances + 1).ToArray();
        if (copy.Length > MaximumExtraInstances || copy.Select(x => x.Id).Distinct().Count() != copy.Length
            || copy.Any(x => !ValidInstance(x))) throw new ArgumentException("Invalid touch button instances.");
        Array.Sort(copy, (a, b) => a.Id.CompareTo(b.Id));
        Volatile.Write(ref _instances, copy); _instancesEdited = true;
        Interlocked.Increment(ref _instanceRevision);
    }
    public static bool ValidInstance(TouchButtonInstance value)
        => value.Id >= FirstExtraInstanceId && value.Id < FirstExtraInstanceId + MaximumExtraInstances && Enum.IsDefined(value.Action)
            && float.IsFinite(value.X) && value.X is >= 0 and <= 1
            && float.IsFinite(value.Y) && value.Y is >= 0 and <= 1
            && float.IsFinite(value.Scale) && value.Scale is >= MinIndividualScale and <= MaxIndividualScale;
    public static int NextInstanceId(IEnumerable<TouchButtonInstance> instances)
    {
        var ids = instances.Select(x => x.Id).ToHashSet();
        if (ids.Count >= MaximumExtraInstances) return -1;
        for (int id = FirstExtraInstanceId; id < FirstExtraInstanceId + MaximumExtraInstances; id++) if (!ids.Contains(id)) return id;
        return -1;
    }
    public static bool TryParseInstances(string text, out TouchButtonInstance[] instances)
    {
        instances = Array.Empty<TouchButtonInstance>();
        if (text == "none") return true;
        if (text.Length is 0 or > 4096) return false;
        string[] records = text.Split(';');
        if (records.Length > MaximumExtraInstances) return false;
        var values = new List<TouchButtonInstance>(records.Length);
        foreach (string record in records)
        {
            string[] fields = record.Split(',');
            if (fields.Length != 6 || !int.TryParse(fields[0], out int id)
                || !Enum.TryParse<TouchControl>(fields[1], false, out var action)
                || !bool.TryParse(fields[2], out bool enabled)
                || !float.TryParse(fields[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)
                || !float.TryParse(fields[4], NumberStyles.Float, CultureInfo.InvariantCulture, out float y)
                || !float.TryParse(fields[5], NumberStyles.Float, CultureInfo.InvariantCulture, out float scale)) return false;
            var value = new TouchButtonInstance(id, action, enabled, x, y, scale);
            if (!ValidInstance(value) || values.Any(v => v.Id == id)) return false;
            values.Add(value);
        }
        instances = values.OrderBy(v => v.Id).ToArray(); return true;
    }
    public static string SerializeInstances(IEnumerable<TouchButtonInstance> instances)
    {
        var records = instances.OrderBy(v => v.Id).Select(v => FormattableString.Invariant(
            $"{v.Id},{v.Action},{v.Enabled},{v.X:R},{v.Y:R},{v.Scale:R}")).ToArray();
        return records.Length == 0 ? "none" : string.Join(';', records);
    }
    public static TouchButtonGeometry InstanceGeometry(TouchButtonInstance value, float width, float height, float buttonScale)
    {
        float w = Math.Max(1, width), h = Math.Max(1, height);
        float r = Math.Clamp(DefaultGeometry(value.Action, w, h).Radius * value.Scale * buttonScale,
            1, Math.Max(1, Math.Min(w, h) * .24f));
        return new(Math.Clamp(value.X * w, Math.Min(r, w / 2), Math.Max(w / 2, w - r)),
            Math.Clamp(value.Y * h, Math.Min(r, h / 2), Math.Max(h / 2, h - r)), r);
    }
}
