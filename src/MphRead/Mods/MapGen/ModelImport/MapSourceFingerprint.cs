using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace MphRead.Mods.MapGen;

public static class MapSourceFingerprint
{
    public static List<MapSourceDependency> Capture(IEnumerable<string> paths)=>paths.Select(Path.GetFullPath).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)
        .Select(path=>new MapSourceDependency(path,File.Exists(path)?MapHash256.HashFile(path).ToString():"missing")).ToList();
    public static string Hash(IEnumerable<string> paths)
        =>Hash(Capture(paths));
    public static string Hash(IReadOnlyList<MapSourceDependency> captured)
        =>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n",captured.OrderBy(d=>d.Path,StringComparer.Ordinal).Select(d=>d.Path+"\0"+d.Hash))))).ToLowerInvariant();
}
