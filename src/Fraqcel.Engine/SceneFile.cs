// Part of the unofficial Fraqcel host prototype.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Fraqcel.Engine;

/// <summary>Reads the bench kit's <c>.kfr</c> scene files (<c>Re:</c>, <c>Im:</c>, <c>Zoom:</c>, <c>Iterations:</c>).</summary>
public static class SceneFile
{
    public static Location ReadKfr(string path, int? maxIterOverride = null)
    {
        var keys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string line in File.ReadAllLines(path))
        {
            int colon = line.IndexOf(':');
            if (colon <= 0) continue;
            keys[line.Substring(0, colon).Trim()] = line.Substring(colon + 1).Trim();
        }
        string Need(string k) => keys.TryGetValue(k, out var v) && v.Length > 0
            ? v : throw new FormatException(path + ": missing \"" + k + ":\"");
        int iter = maxIterOverride ?? int.Parse(Need("Iterations"), NumberStyles.Integer, CultureInfo.InvariantCulture);
        return Location.FromKitZoom(Need("Re"), Need("Im"), Need("Zoom"), iter);
    }
}
