// Part of the unofficial Fraqcel host prototype. Ported from Fraqcel 2.15.0's VBA
// (coreCode.bas isXnumNeeded, Xnumb.bas SetDgMx), Copyright (c) 2026 Kevin Orloske, BSD-2-Clause.

using System;

namespace Fraqcel.Engine;

/// <summary>Fraqcel's <c>flagMP</c> values.</summary>
public enum PrecisionMode
{
    Double = 0,
    MultiPrecision = 1,
    Perturbation = 2,
    SeriesSD = 3,
    SeriesHF = 4,
}

public static class Precision
{
    /// <summary>XNUMBERS' digit limit in Fraqcel's build.</summary>
    public const int DigitsLimit = 816;

    /// <summary><c>xDigit = |Int(log10(scaleTest))| + 4 + extra digits</c>, at least 16.</summary>
    public static int XDigit(double scaleTest, int extraDigits)
    {
        int x = Vba.CInt(Math.Abs(Vba.Int(Vba.Log10(scaleTest))) + 4 + extraDigits);
        return x < 16 ? 16 : x;
    }

    /// <summary>The digits given to the DLL: <c>SetDgMx</c> adds XNUMBERS' padding of 5.</summary>
    public static int DgMx(int xDigit)
    {
        int d = Math.Min(xDigit, DigitsLimit) + 5;
        return Math.Min(d, DigitsLimit);
    }

    /// <summary>
    /// The mode <c>isXnumNeeded</c> picks. <paramref name="manualReference"/> is "Recalc on one
    /// pixel" (refreshType 4), which forces perturbation or series whatever the other settings say.
    /// </summary>
    public static PrecisionMode Choose(double scaleTest, PrecisionSettings p, bool manualReference = false)
    {
        PrecisionMode SeriesOrPert() => p.Series switch
        {
            SeriesType.StandardDouble => PrecisionMode.SeriesSD,
            SeriesType.ExtendedDouble => PrecisionMode.SeriesHF,
            _ => PrecisionMode.Perturbation,
        };

        if (manualReference) return SeriesOrPert();
        if (p.ForceMultiPrecision || scaleTest < p.Threshold)
            return p.Perturbation ? SeriesOrPert() : PrecisionMode.MultiPrecision;
        return PrecisionMode.Double;
    }

    public static string Name(PrecisionMode m) => m switch
    {
        PrecisionMode.MultiPrecision => "multi-precision",
        PrecisionMode.Perturbation => "perturbation",
        PrecisionMode.SeriesSD => "series-sd",
        PrecisionMode.SeriesHF => "series-hf",
        _ => "double",
    };
}
