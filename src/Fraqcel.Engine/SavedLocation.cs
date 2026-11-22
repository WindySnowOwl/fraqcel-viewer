// Part of the unofficial Fraqcel host prototype.

using System;
using System.Globalization;
using System.Text;

namespace Fraqcel.Engine;

/// <summary>
/// A named location, kept as one line of text in the settings file:
/// <c>name|re|im|plot scale|max iterations|double or text</c>.
/// <list type="bullet">
/// <item>The plot scale is written so that it reads back to the same double.</item>
/// <item>The last field records how the center is held. After a double-precision zoom Fraqcel
/// holds it as a VBA Double, which later enters multi-precision through VBA's 15-digit CStr
/// (<see cref="Location"/>), so a saved view reloads exactly as it was.</item>
/// <item>Fields past the sixth are ignored, so a later version can add some.</item>
/// </list>
/// </summary>
public sealed class SavedLocation
{
    private static readonly CultureInfo Ci = CultureInfo.InvariantCulture;

    public SavedLocation(string name, Location location)
    {
        Name = CleanName(name);
        Location = location ?? throw new ArgumentNullException(nameof(location));
    }

    public string Name { get; }
    public Location Location { get; }

    /// <summary>A name the one-line format can hold: no '|', no control characters, trimmed.</summary>
    public static string CleanName(string? name)
    {
        var sb = new StringBuilder();
        foreach (char ch in name ?? "") sb.Append(ch == '|' ? '/' : char.IsControl(ch) ? ' ' : ch);
        return sb.ToString().Trim();
    }

    /// <summary>A short default name: the zoom and max iterations.</summary>
    public static string DefaultName(Location loc) =>
        "Zoom " + loc.Zoom.ToString("0.###E+0", Ci) + ", " + loc.MaxIter.ToString(Ci) + " iterations";

    public string ToSettingsValue()
    {
        bool asDouble = Location.CenterReValue.HasValue && Location.CenterImValue.HasValue;
        return string.Join("|", new[]
        {
            Name,
            Location.CenterRe,
            Location.CenterIm,
            Location.PlotScale.ToString("R", Ci),
            Location.MaxIter.ToString(Ci),
            asDouble ? "double" : "text",
        });
    }

    /// <summary>Reads a value written by <see cref="ToSettingsValue"/>. Returns false for anything it can't read.</summary>
    public static bool TryParse(string? value, out SavedLocation? saved)
    {
        saved = null;
        if (value == null) return false;
        string[] f = value.Split('|');
        if (f.Length < 6) return false;
        if (!double.TryParse(f[3].Trim(), NumberStyles.Float, Ci, out double scale)) return false;
        if (!int.TryParse(f[4].Trim(), NumberStyles.Integer, Ci, out int maxIter)) return false;
        try
        {
            Location loc;
            string kind = f[5].Trim();
            if (string.Equals(kind, "double", StringComparison.OrdinalIgnoreCase))
            {
                if (!double.TryParse(f[1].Trim(), NumberStyles.Float, Ci, out double re) ||
                    !double.TryParse(f[2].Trim(), NumberStyles.Float, Ci, out double im))
                    return false;
                loc = Location.FromDoubles(re, im, scale, maxIter);
            }
            else if (string.Equals(kind, "text", StringComparison.OrdinalIgnoreCase))
            {
                loc = new Location(f[1], f[2], scale, maxIter);
            }
            else
            {
                return false;
            }
            saved = new SavedLocation(f[0], loc);
            return true;
        }
        catch (ArgumentException)
        {
            return false; // a center that isn't a number, or a scale or max iterations out of range
        }
    }

    public override string ToString() => Name;
}
