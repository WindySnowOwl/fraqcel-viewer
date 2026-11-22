// Part of the unofficial Fraqcel host prototype. Coloring parameters ported from Fraqcel 2.15.0's
// VBA (plotCode.bas createBmp), Copyright (c) 2026 Kevin Orloske, BSD-2-Clause.

using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Fraqcel.Engine.Dll;

namespace Fraqcel.Engine;

/// <summary>How iteration data is turned into colors. Changing these never needs a new render.</summary>
public sealed class ColorSettings
{
    public PalettePreset Palette { get; set; } = PalettePresets.Default;

    /// <summary>Overrides the preset's bands between key colors when set.</summary>
    public int? Bands { get; set; }

    /// <summary>Palette offset (SETTINGS F6).</summary>
    public int Offset { get; set; }

    public bool Reverse { get; set; }

    /// <summary>Divide iteration (SETTINGS F7), 1 to 10,000,000.</summary>
    public double DivideIteration { get; set; } = 1;

    /// <summary>The color of the set (SETTINGS F8), as an Excel color value. Null uses the palette preset's.</summary>
    public int? SetColor { get; set; }

    /// <summary>Bump mapping. It works with every coloring type (Fraqcel's author, 2026-11-13).</summary>
    public bool Bump { get; set; }

    /// <summary>Light direction, -180 to 180 degrees (DATA K3).</summary>
    public double BumpDirection { get; set; }

    /// <summary>Light elevation, -90 to 90 degrees (DATA K4).</summary>
    public double BumpElevation { get; set; }

    /// <summary>Bump strength, 1 to 100 (DATA K5).</summary>
    public double BumpStrength { get; set; } = 50;

    public ColorSettings Clone() => (ColorSettings)MemberwiseClone();
}

public static class Colorizer
{
    private const double Pi = 3.14159265358979 + 3.10862E-15; // globals.bas

    /// <summary>Colors a render with the DLL's <c>getBmpPixels</c> and returns a 24-bit bitmap.</summary>
    public static Bitmap ToBitmap(RenderResult result, ColorSettings colors, out double milliseconds)
    {
        FraqcelDll.EnsureLoaded();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        int rSize = result.Rows, cSize = result.Cols;
        var s = result.Settings;

        int[] palette = PaletteBuilder.Build(colors.Palette.KeyColors, colors.Bands ?? colors.Palette.Bands);
        int[,] rgb = PaletteBuilder.ToDllPalette(palette, colors.SetColor ?? colors.Palette.SetColor, colors.Offset, colors.Reverse, out int numColors);

        double divideIter = colors.DivideIteration;
        if (!(divideIter >= 1) || divideIter > 10_000_000) divideIter = 1;

        bool flagBump = colors.Bump;
        double lx = 0, ly = 0, lz = 0, nz2 = 0, nzLz = 0, sD = 0, sL = 0;
        if (flagBump)
        {
            double azimuth = colors.BumpDirection * (Pi / 180) * -1;
            double elevation = colors.BumpElevation * (Pi / 180) * -1;
            double nz = Math.Pow(10, -3 * colors.BumpStrength / 99 + (102.0 / 99));
            lx = Math.Cos(azimuth) * Math.Cos(elevation);
            ly = Math.Sin(azimuth) * Math.Cos(elevation);
            lz = Math.Sin(elevation);
            nz2 = nz * nz;
            nzLz = nz * lz;
            sD = 0.2; // degree of darkening
            sL = 0.4; // degree of brightening
        }

        // BMP rows are padded to a multiple of 4 bytes.
        int rowPadding = 3 * cSize % 4;
        if (rowPadding != 0) rowPadding = 4 - rowPadding;
        int stride = 3 * cSize + rowPadding;
        var pixels = new byte[3 * (cSize + rowPadding) * rSize]; // the VBA's (generous) allocation

        DllCalls.BmpPixels(result.Data, ref pixels, rgb, numColors, s.FlagSmooth, flagBump, s.FlagFixedRange,
            result.MaxIter, rSize, cSize, res: 1, rowPadding, divideIter, lx, ly, lz, nz2, nzLz, sD, sL);

        var bmp = new Bitmap(cSize, rSize, PixelFormat.Format24bppRgb);
        var bits = bmp.LockBits(new Rectangle(0, 0, cSize, rSize), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
        try
        {
            for (int r = 0; r < rSize; r++)
                Marshal.Copy(pixels, r * stride, bits.Scan0 + r * bits.Stride, 3 * cSize);
        }
        finally
        {
            bmp.UnlockBits(bits);
        }
        milliseconds = sw.Elapsed.TotalMilliseconds;
        return bmp;
    }
}
