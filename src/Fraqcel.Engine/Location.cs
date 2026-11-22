// Part of the unofficial Fraqcel host prototype.

using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Fraqcel.Engine;

/// <summary>
/// A place in the complex plane, the way Fraqcel stores one: the center as decimal text (any
/// number of digits), the plot scale as a double, and max iterations.
/// </summary>
/// <remarks>
/// After a zoom in double precision, Fraqcel stores the new center as a VBA Double, not as text.
/// That matters later: a Double enters multi-precision through VBA's 15-digit CStr (design 5.3).
/// <see cref="CenterReValue"/> and <see cref="CenterImValue"/> carry that Double when there is one.
/// </remarks>
public sealed class Location
{
    private static readonly Regex Number = new(@"^[+-]?(\d+\.?\d*|\.\d+)([eE][+-]?\d+)?$", RegexOptions.CultureInvariant);

    public const int MaxIterationsLimit = 10_000_000;

    public Location(string centerRe, string centerIm, double plotScale, int maxIter)
        : this(centerRe, centerIm, null, null, plotScale, maxIter) { }

    private Location(string centerRe, string centerIm, double? reValue, double? imValue, double plotScale, int maxIter)
    {
        CenterRe = CheckNumber(centerRe, nameof(centerRe));
        CenterIm = CheckNumber(centerIm, nameof(centerIm));
        if (double.IsNaN(plotScale) || double.IsInfinity(plotScale) || plotScale <= 0)
            throw new ArgumentOutOfRangeException(nameof(plotScale), "The plot scale must be a positive number.");
        if (maxIter < 1 || maxIter > MaxIterationsLimit)
            throw new ArgumentOutOfRangeException(nameof(maxIter), "Max iterations must be 1 to 10,000,000.");
        CenterReValue = reValue;
        CenterImValue = imValue;
        PlotScale = plotScale;
        MaxIter = maxIter;
    }

    /// <summary>A center that Fraqcel would hold as VBA Doubles (after a double-precision zoom).</summary>
    public static Location FromDoubles(double centerRe, double centerIm, double plotScale, int maxIter) =>
        new(centerRe.ToString("R", CultureInfo.InvariantCulture), centerIm.ToString("R", CultureInfo.InvariantCulture),
            centerRe, centerIm, plotScale, maxIter);

    /// <summary>Fraqcel's starting view (DATA!B2:B5).</summary>
    public static Location FullView => new("-0.5", "0", 4, 500);

    /// <summary>The location in saved slot 20 of the shipped workbook: the bench kit's scene 62.</summary>
    public static Location FraqcelSuggested => new("-1.99909517044828534214814164792614097",
        "6.16911258817465896937280471817351021E-7", 8.81751911989855E-32, 2000);

    /// <summary>
    /// A bench-kit scene: the short side spans 4/Z, so Fraqcel's plot scale is 4/Z
    /// over the smaller dimension (design 5.1).
    /// </summary>
    public static Location FromKitZoom(string re, string im, string zoomText, int maxIter)
    {
        double zoom;
        try
        {
            zoom = double.Parse(zoomText.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture);
        }
        catch (OverflowException)
        {
            throw new ArgumentOutOfRangeException(nameof(zoomText), "Zoom " + zoomText + " is beyond the range of a double; Fraqcel's plot scale cannot represent it.");
        }
        if (double.IsInfinity(zoom) || zoom <= 0)
            throw new ArgumentOutOfRangeException(nameof(zoomText), "Zoom " + zoomText + " is not usable.");
        double plotScale = 4.0 / zoom;
        if (plotScale == 0 || double.IsInfinity(plotScale))
            throw new ArgumentOutOfRangeException(nameof(zoomText), "Zoom " + zoomText + " gives a plot scale outside the range of a double.");
        return new Location(re, im, plotScale, maxIter);
    }

    public string CenterRe { get; }
    public string CenterIm { get; }
    public double? CenterReValue { get; }
    public double? CenterImValue { get; }
    public double PlotScale { get; }
    public int MaxIter { get; }

    /// <summary>The kit-style zoom: 4 / plot scale.</summary>
    public double Zoom => 4.0 / PlotScale;

    /// <summary>The center real part as VBA would coerce it to a Double.</summary>
    public double CenterReAsDouble => CenterReValue ?? Vba.CDbl(CenterRe);

    /// <summary>The center imaginary part as VBA would coerce it to a Double.</summary>
    public double CenterImAsDouble => CenterImValue ?? Vba.CDbl(CenterIm);

    public Location WithMaxIter(int maxIter) => new(CenterRe, CenterIm, CenterReValue, CenterImValue, PlotScale, maxIter);

    public override string ToString() =>
        "Re " + CenterRe + "  Im " + CenterIm + "  scale " + PlotScale.ToString("R", CultureInfo.InvariantCulture) +
        "  iter " + MaxIter.ToString(CultureInfo.InvariantCulture);

    private static string CheckNumber(string s, string name)
    {
        string t = (s ?? "").Trim();
        if (!Number.IsMatch(t)) throw new ArgumentException("Not a decimal number: \"" + s + "\"", name);
        return t;
    }
}
