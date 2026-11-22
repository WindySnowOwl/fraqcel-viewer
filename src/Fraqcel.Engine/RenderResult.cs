// Part of the unofficial Fraqcel host prototype.

using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Fraqcel.Engine;

/// <summary>One call into the DLL.</summary>
public sealed class PassInfo
{
    public int Index { get; set; }
    public int RefRow { get; set; }
    public int RefCol { get; set; }
    /// <summary>Box computed by this pass, in glitch-flag coordinates (r1, c1, r2, c2).</summary>
    public (int R1, int C1, int R2, int C2) Box { get; set; }
    /// <summary>Pixels the DLL flagged as glitched in this pass.</summary>
    public long Glitched { get; set; }
    public int Regions { get; set; }
    public long LargestRegion { get; set; }
    public double TotalMs { get; set; }
    /// <summary>Call start to the DLL's 0% message (string parsing, reference orbit, allocations); null when the pass has no reference.</summary>
    public double? RefMs { get; set; }
    public List<(double Ms, string Text)> Progress { get; } = new();
}

/// <summary>The iteration data of a finished render, with what happened along the way.</summary>
public sealed class RenderResult
{
    internal RenderResult(double[,] data, Location location, RenderSettings settings, Geometry geometry)
    {
        Data = data;
        Location = location;
        Settings = settings;
        Geometry = geometry;
    }

    /// <summary>Iteration data at the output size, 1-based [1..rows, 1..cols]. In-set pixels hold MaxIter.</summary>
    public double[,] Data { get; internal set; }

    public Location Location { get; }
    public RenderSettings Settings { get; }
    public Geometry Geometry { get; }

    public int Rows => Data.GetLength(0);
    public int Cols => Data.GetLength(1);
    public int MaxIter => Location.MaxIter;

    public PrecisionMode Mode { get; internal set; }
    public int XDigit { get; internal set; }
    public int DgMx { get; internal set; }

    /// <summary>numPointsMP: references used (multi-precision pixels in plain multi-precision mode).</summary>
    public int References { get; internal set; }

    /// <summary>numSkip: iterations skipped by the series approximation.</summary>
    public int Skip { get; internal set; }

    public List<PassInfo> Passes { get; } = new();

    /// <summary>1-pixel glitches replaced by the average of their neighbors.</summary>
    public long Blended { get; internal set; }

    /// <summary>Pixels still flagged as glitched at the end: horizontal smears of the left neighbor's value (design 5.10).</summary>
    public long Unresolved { get; internal set; }

    /// <summary>NaN or infinite samples in the computed buffer (design 5.10). The DLL paints them as the set.</summary>
    public long NonFinite { get; internal set; }

    /// <summary>True when the image can be trusted: no unresolved glitches and no NaN.</summary>
    public bool IsClean => Unresolved == 0 && NonFinite == 0;

    public List<string> Warnings { get; } = new();

    public double EngineMs { get; internal set; }
    public double SupersampleMs { get; internal set; }

    /// <summary>Sum, max and min of the iteration data (Fraqcel's statistics; not meaningful for stripe coloring).</summary>
    public (double Sum, double Max, double Min) Statistics()
    {
        double sum = 0, max = double.MinValue, min = double.MaxValue;
        foreach (double v in Data)
        {
            sum += v;
            if (v > max) max = v;
            if (v < min) min = v;
        }
        return (sum, max, min);
    }

    /// <summary>A one-line summary in the CLI's key=value form.</summary>
    public string Summary()
    {
        var ci = CultureInfo.InvariantCulture;
        string Ms(double v) => v.ToString("0", ci);
        return string.Join(" ", new[]
        {
            "size=" + Geometry,
            "mode=" + Precision.Name(Mode),
            "digits=" + DgMx.ToString(ci),
            "passes=" + Passes.Count.ToString(ci),
            "refs=" + References.ToString(ci),
            "glitched=" + (Passes.Count == 0 ? "0" : string.Join(",", Passes.Select(p => p.Glitched.ToString(ci)))),
            "blended=" + Blended.ToString(ci),
            "unresolved=" + Unresolved.ToString(ci),
            "nan=" + NonFinite.ToString(ci),
            "skip=" + Skip.ToString(ci),
            "engine_ms=" + Ms(EngineMs),
        });
    }
}
