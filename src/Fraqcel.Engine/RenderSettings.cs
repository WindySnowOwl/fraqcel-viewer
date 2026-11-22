// Part of the unofficial Fraqcel host prototype. Defaults and input rules ported from
// Fraqcel 2.15.0's VBA (globals.bas, isXnumNeeded), Copyright (c) 2026 Kevin Orloske, BSD-2-Clause.

using System;
using System.Collections.Generic;

namespace Fraqcel.Engine;

public enum FractalType
{
    Mandelbrot,
    BurningShip,
    Buffalo,
    Celtic,
    RotationFamily,
    Beetle,
    PerpendicularBurningShip,
}

public static class FractalTypes
{
    /// <summary>The DLL's <c>typeOption</c> letter.</summary>
    public static string Code(FractalType t) => t switch
    {
        FractalType.BurningShip => "b",
        FractalType.Buffalo => "f",
        FractalType.Celtic => "c",
        FractalType.RotationFamily => "r",
        FractalType.Beetle => "t",
        FractalType.PerpendicularBurningShip => "p",
        _ => "m",
    };

    public static string DisplayName(FractalType t) => t switch
    {
        FractalType.BurningShip => "Burning Ship",
        FractalType.Buffalo => "Buffalo",
        FractalType.Celtic => "Celtic",
        FractalType.RotationFamily => "Rotation Family",
        FractalType.Beetle => "Beetle",
        FractalType.PerpendicularBurningShip => "Perpendicular Burning Ship",
        _ => "Mandelbrot",
    };

    /// <summary>The DLL's series routines exist for Mandelbrot, Rotation Family and Beetle only.</summary>
    public static bool HasSeries(FractalType t) =>
        t is FractalType.Mandelbrot or FractalType.RotationFamily or FractalType.Beetle;

    public static FractalType Parse(string s)
    {
        foreach (FractalType t in Enum.GetValues(typeof(FractalType)))
            if (string.Equals(s, Code(t), StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s, DisplayName(t), StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s, t.ToString(), StringComparison.OrdinalIgnoreCase))
                return t;
        throw new ArgumentException("Unknown fractal type: " + s);
    }
}

public enum ColoringMode
{
    /// <summary>Whole iteration counts; escape radius 2.</summary>
    Discrete,
    /// <summary>Smooth (fractional) counts; escape radius 1024.</summary>
    Smooth,
    /// <summary>Stripe average coloring, fixed range.</summary>
    StripeFixedRange,
    /// <summary>Stripe average coloring, iteration-based range.</summary>
    StripeIterationRange,
}

public enum SeriesType
{
    Off,
    /// <summary>Adaptive 3rd/5th order, standard doubles (Fraqcel's default).</summary>
    StandardDouble,
    /// <summary>5th order, extended doubles (HugeFloat).</summary>
    ExtendedDouble,
}

/// <summary>Fraqcel's multi-precision options (SETTINGS J33:J46), with its defaults.</summary>
public sealed class PrecisionSettings
{
    public const double DefaultThreshold = 1e-13;
    public const double DefaultDivThreshold = 2e-5;
    public const double DefaultGlitchThreshold = 0.002;
    public const int DefaultMaxReferences = 30;
    public const double DefaultMaxPastRef = 0;
    public const double DefaultSeriesTolerance = 0.001;

    public double Threshold { get; set; } = DefaultThreshold;
    public int ExtraDigits { get; set; }
    public bool ForceMultiPrecision { get; set; }
    public bool Perturbation { get; set; } = true;
    public double DivThreshold { get; set; } = DefaultDivThreshold;
    public double GlitchThreshold { get; set; } = DefaultGlitchThreshold;
    public int MaxReferences { get; set; } = DefaultMaxReferences;
    public double MaxPastRef { get; set; } = DefaultMaxPastRef;
    public bool DebugGlitches { get; set; }
    public bool Rebasing { get; set; }
    public SeriesType Series { get; set; } = SeriesType.StandardDouble;
    public double SeriesTolerance { get; set; } = DefaultSeriesTolerance;

    public PrecisionSettings Clone() => (PrecisionSettings)MemberwiseClone();

    /// <summary>
    /// Rewrites invalid values the way <c>isXnumNeeded</c> does, and returns a warning for each.
    /// </summary>
    public List<string> Normalize()
    {
        var w = new List<string>();
        if (!(Threshold >= 0)) { w.Add("threshold reset to default"); Threshold = DefaultThreshold; }
        if (!(DivThreshold >= 0)) { w.Add("divergence threshold reset to default"); DivThreshold = DefaultDivThreshold; }
        if (!(GlitchThreshold >= 0)) { w.Add("glitch threshold reset to default"); GlitchThreshold = DefaultGlitchThreshold; }
        if (MaxReferences < 1) { w.Add("max references reset to default"); MaxReferences = DefaultMaxReferences; }
        if (MaxPastRef == 0) { /* zero disables the check */ }
        else if (!(MaxPastRef >= 1)) { w.Add("iterations beyond reference reset to default"); MaxPastRef = DefaultMaxPastRef; }
        else if (MaxPastRef > 100) { w.Add("iterations beyond reference capped at 100"); MaxPastRef = 100; }
        if (!(SeriesTolerance >= 0)) { w.Add("series tolerance reset to default"); SeriesTolerance = DefaultSeriesTolerance; }
        return w;
    }
}

/// <summary>Everything about a render apart from the location and the image size.</summary>
public sealed class RenderSettings
{
    public FractalType Type { get; set; } = FractalType.Mandelbrot;

    /// <summary>The Rotation Family parameter; ignored for other types.</summary>
    public double Parameter { get; set; } = 3;

    /// <summary>Fraqcel ships with discrete coloring.</summary>
    public ColoringMode Coloring { get; set; } = ColoringMode.Discrete;

    public double StripeDensity { get; set; } = 3;

    public PrecisionSettings Precision { get; set; } = new();

    /// <summary>
    /// Threads to use; null means no limit (Fraqcel's default, passed to the DLL as -1).
    /// A number N reserves ProcessorCount - N processors; N = ProcessorCount is a cap, not "no limit".
    /// </summary>
    public int? Threads { get; set; }

    /// <summary>Navigation size (800 or 596). The DLL uses it for quick pan only.</summary>
    public int NavSize { get; set; } = 800;

    public RenderSettings Clone()
    {
        var c = (RenderSettings)MemberwiseClone();
        c.Precision = Precision.Clone();
        return c;
    }

    public bool FlagSmooth => Coloring != ColoringMode.Discrete;
    public bool FlagFixedRange => Coloring == ColoringMode.StripeFixedRange;
    public bool FlagSac => Coloring is ColoringMode.StripeFixedRange or ColoringMode.StripeIterationRange;

    /// <summary>The DLL's <c>flagDllOption</c>: reserved processors, or -1 for no limit.</summary>
    public int FlagDllOption
    {
        get
        {
            if (Threads is not int n) return -1;
            if (n < 1 || n > Environment.ProcessorCount)
                throw new ArgumentOutOfRangeException(nameof(Threads), "Threads must be 1 to " + Environment.ProcessorCount + ".");
            return Environment.ProcessorCount - n;
        }
    }

    public List<string> Normalize()
    {
        var w = Precision.Normalize();
        if (!(StripeDensity > 0)) { w.Add("stripe density reset to 3"); StripeDensity = 3; }
        if (NavSize != 800 && NavSize != 596) { w.Add("navigation size reset to 800"); NavSize = 800; }
        return w;
    }
}
