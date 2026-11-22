// Part of the unofficial Fraqcel host prototype. Ported from Fraqcel 2.15.0's VBA
// (globals.bas setSize and setScale, WS.cls checkPublishDim),
// Copyright (c) 2026 Kevin Orloske, BSD-2-Clause.

using System;

namespace Fraqcel.Engine;

/// <summary>
/// The size of a render. Fraqcel has two kinds: the square navigation image (800 or 596) and
/// the published image (any size within its rules, optionally supersampled).
/// </summary>
public sealed class Geometry
{
    private Geometry(int outRows, int outCols, int samplesPerPixel, bool scaleOverLarger, bool isNavigation)
    {
        OutRSize = outRows;
        OutCSize = outCols;
        SamplesPerPixel = samplesPerPixel;
        SampleFactor = (int)Math.Round(Math.Sqrt(samplesPerPixel));
        IsNavigation = isNavigation;

        // setSize: the plot scale spans the smaller dimension unless "plot scale over larger dimension" is set.
        int scale;
        if (outRows > outCols) scale = scaleOverLarger ? outRows : outCols;
        else scale = scaleOverLarger ? outCols : outRows;

        // calcAndPlot multiplies all three by sqrt(samples) for a publish render.
        RSize = outRows * SampleFactor;
        CSize = outCols * SampleFactor;
        ScaleSize = scale * SampleFactor;
    }

    /// <summary>The navigation image: square, 800 or 596 (the only sizes the DLL's resolution grid and quick pan accept).</summary>
    public static Geometry Navigation(int navSize)
    {
        if (navSize != 800 && navSize != 596)
            throw new ArgumentOutOfRangeException(nameof(navSize), "The navigation size must be 800 or 596.");
        return new Geometry(navSize, navSize, 1, false, true);
    }

    /// <summary>A published image, checked against Fraqcel's own rules (WS.cls checkPublishDim).</summary>
    public static Geometry Publish(int width, int height, int samplesPerPixel = 1, bool scaleOverLarger = false)
    {
        if (width < 17 || width > 16383) throw new ArgumentOutOfRangeException(nameof(width), "Width must be 17 to 16,383.");
        if (height < 17 || height > 32765) throw new ArgumentOutOfRangeException(nameof(height), "Height must be 17 to 32,765.");
        if (samplesPerPixel is not (1 or 4 or 9 or 16 or 25))
            throw new ArgumentOutOfRangeException(nameof(samplesPerPixel), "Samples per pixel must be 1, 4, 9, 16 or 25.");
        double s = Math.Sqrt(samplesPerPixel);
        if (width * s > 32765) throw new ArgumentOutOfRangeException(nameof(width), "Width is too large for the selected samples per pixel.");
        if (height * s > 32765) throw new ArgumentOutOfRangeException(nameof(height), "Height is too large for the selected samples per pixel.");
        return new Geometry(height, width, samplesPerPixel, scaleOverLarger, false);
    }

    /// <summary>Rows computed (supersampled).</summary>
    public int RSize { get; }

    /// <summary>Columns computed (supersampled).</summary>
    public int CSize { get; }

    /// <summary>The computed dimension the plot scale spans.</summary>
    public int ScaleSize { get; }

    /// <summary>Rows of the output image.</summary>
    public int OutRSize { get; }

    /// <summary>Columns of the output image.</summary>
    public int OutCSize { get; }

    public int SamplesPerPixel { get; }

    /// <summary>sqrt(samples per pixel): 1 to 5.</summary>
    public int SampleFactor { get; }

    public bool IsNavigation { get; }

    /// <summary>The refreshType Fraqcel passes the DLL for this kind of render.</summary>
    public int RefreshType => IsNavigation ? 0 : -2;

    /// <summary>setScale: the plot scale along the real axis.</summary>
    public double PlotScaleRe(double plotScale) =>
        ScaleSize == CSize ? plotScale : plotScale * ((double)CSize / RSize);

    /// <summary>setScale: the plot scale along the imaginary axis.</summary>
    public double PlotScaleIm(double plotScale) =>
        ScaleSize == CSize ? plotScale * ((double)RSize / CSize) : plotScale;

    /// <summary>The step between samples: Fraqcel spans edge sample to edge sample.</summary>
    public double PixelStep(double plotScale) => plotScale / (ScaleSize - 1);

    public override string ToString() => OutCSize + "x" + OutRSize + (SamplesPerPixel > 1 ? " x" + SamplesPerPixel + " samples" : "");
}
