// Part of the unofficial Fraqcel host prototype. Ported from Fraqcel 2.15.0's VBA
// (WS.cls selectionMove), Copyright (c) 2026 Kevin Orloske, BSD-2-Clause (see NOTICES.md).

using System;
using System.Runtime.CompilerServices;
using PeterO.Numbers;

namespace Fraqcel.Engine;

/// <summary>
/// A selection on the navigation image in Fraqcel's cell coordinates: image pixel (row, column),
/// counted from 0, is cell (row + 2, column + 2). A single cell has <c>R2 = 0</c>, as findRange returns.
/// </summary>
public readonly struct CellSelection
{
    public CellSelection(int r1, int c1, int r2, int c2)
    {
        R1 = r1; C1 = c1; R2 = r2; C2 = c2;
    }

    public int R1 { get; }
    public int C1 { get; }
    public int R2 { get; }
    public int C2 { get; }
    public bool IsSingleCell => R2 == 0;

    /// <summary>One image pixel (0-based row and column).</summary>
    public static CellSelection Pixel(int row, int col) => new(row + 2, col + 2, 0, 0);

    /// <summary>A box between two image pixels (0-based, in any order). The same pixel twice is a single cell.</summary>
    public static CellSelection Box(int rowA, int colA, int rowB, int colB)
    {
        if (rowA == rowB && colA == colB) return Pixel(rowA, colA);
        return new(Math.Min(rowA, rowB) + 2, Math.Min(colA, colB) + 2, Math.Max(rowA, rowB) + 2, Math.Max(colA, colB) + 2);
    }
}

public enum ZoomDirection
{
    Out = -1,
    Pan = 0,
    In = 1,
}

public static class Navigator
{
    /// <summary>
    /// selectionMove: the new location after zooming into, out of, or panning to a selection
    /// on the navigation image.
    /// <list type="bullet">
    /// <item>A box zooms so that its larger side spans the image.</item>
    /// <item>A single pixel zooms by <paramref name="zoomFactor"/>.</item>
    /// <item>The new center sits half a pixel down and right of the selection's middle pixel,
    /// on the grid the navigation image's own center uses.</item>
    /// </list>
    /// </summary>
    public static Location Move(Location current, int navSize, CellSelection sel, ZoomDirection direction,
        double zoomFactor, PrecisionSettings precision)
    {
        int r1 = sel.R1, c1 = sel.C1, r2 = sel.R2, c2 = sel.C2;
        if (r1 > navSize + 1 || c1 > navSize + 1 || (r1 < 2 && r2 < 2) || (c1 < 2 && c2 < 2))
            throw new ArgumentException("Select a range of cells within the image.");
        if (r1 < 2) r1 = 2;
        if (c1 < 2) c1 = 2;
        if (r2 > navSize + 1) r2 = navSize + 1;
        if (c2 > navSize + 1) c2 = navSize + 1;

        int centerR, centerC;
        if (r2 != 0)
        {
            centerR = (int)Vba.Int(r1 + (r2 - r1) / 2.0);
            centerC = (int)Vba.Int(c1 + (c2 - c1) / 2.0);
        }
        else
        {
            centerR = r1;
            centerC = c1;
        }
        // Resolution 100% only: snapToResGrid leaves the cell as it is.
        double v1 = centerR - 1.5; // -2 + 0.5
        double v2 = centerC - 1.5;

        double plotScale0 = current.PlotScale;
        double pixelFactor;
        if (direction != ZoomDirection.Pan)
        {
            int dir = (int)direction;
            if (r2 != 0)
            {
                int maxDeltaRC = (r2 - r1) > (c2 - c1) ? r2 - r1 : c2 - c1;
                pixelFactor = Math.Pow((double)maxDeltaRC / (navSize - 1), dir);
            }
            else
            {
                if (zoomFactor == 0 || double.IsNaN(zoomFactor))
                    throw new ArgumentException("Enter a nonzero number for the zoom factor.");
                pixelFactor = Math.Pow(1 / Math.Abs(zoomFactor), dir);
            }
        }
        else
        {
            pixelFactor = 1;
        }

        return Recenter(current, navSize, v1, v2, pixelFactor * plotScale0, precision);
    }

    /// <summary>
    /// The mouse wheel. This is not a Fraqcel control: it zooms by <paramref name="zoomFactor"/>
    /// and keeps the sample at image pixel (<paramref name="row"/>, <paramref name="col"/>), counted
    /// from 0, where it is on the image. The new center can fall off Fraqcel's half-pixel grid;
    /// nothing in the prototype depends on that grid.
    /// </summary>
    public static Location ZoomAround(Location current, int navSize, int row, int col, ZoomDirection direction,
        double zoomFactor, PrecisionSettings precision)
    {
        if (row < 0 || col < 0 || row >= navSize || col >= navSize)
            throw new ArgumentException("Point at a pixel within the image.");
        if (direction == ZoomDirection.Pan) throw new ArgumentException("The wheel zooms in or out; it doesn't pan.");
        if (zoomFactor == 0 || double.IsNaN(zoomFactor))
            throw new ArgumentException("Enter a nonzero number for the zoom factor.");

        // As in Move, a factor f shrinks the plot scale by f going in and grows it by f going out.
        double f = Math.Pow(Math.Abs(zoomFactor), (int)direction);
        // Pixel (row, col)'s sample sits row and col pixel steps from the top-left sample, and the
        // center sits (navSize - 1) / 2 steps in. Bringing the center f times closer to the pixel,
        // in the old view's steps, leaves the pixel's sample where it was in the new view.
        double mid = (navSize - 1) / 2.0;
        double v1 = row + (mid - row) / f;
        double v2 = col + (mid - col) / f;
        return Recenter(current, navSize, v1, v2, current.PlotScale / f, precision);
    }

    /// <summary>
    /// The end of selectionMove: the location whose center sits <paramref name="v1"/> rows and
    /// <paramref name="v2"/> columns (in the current view's pixel steps) from the current view's
    /// top-left sample, at plot scale <paramref name="plotScale1"/>.
    /// </summary>
    private static Location Recenter(Location current, int navSize, double v1, double v2, double plotScale1,
        PrecisionSettings precision)
    {
        double plotScale0 = current.PlotScale;
        if (double.IsInfinity(plotScale1)) throw new ArgumentException("Resulting plot scale is too large.");
        double scaleTest = plotScale1 / (navSize - 1);
        if (scaleTest == 0) throw new ArgumentException("Resulting plot scale is too small.");

        int xDigit = Precision.XDigit(scaleTest, precision.ExtraDigits);
        var mode = Precision.Choose(scaleTest, precision);

        if (mode != PrecisionMode.Double)
        {
            MultiPrecisionCenter(current, navSize, plotScale0, v1, v2, xDigit, out string re, out string im);
            return new Location(re, im, plotScale1, current.MaxIter);
        }

        // Double precision: Fraqcel then stores the new center as a VBA Double.
        double centerRe0 = current.CenterReAsDouble;
        double centerIm0 = current.CenterImAsDouble;
        double pixelStep0 = plotScale0 / (navSize - 1);
        double zr0 = centerRe0 - (plotScale0 / 2); // min real position (1st column)
        double zi0 = centerIm0 + (plotScale0 / 2); // max imaginary position (1st row)
        double centerIm1 = zi0 - v1 * pixelStep0;
        double centerRe1 = zr0 + v2 * pixelStep0;
        return Location.FromDoubles(centerRe1, centerIm1, plotScale1, current.MaxIter);
    }

    /// <summary>The multi-precision branch: arithmetic at DgMx digits, result rounded to xDigit digits.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void MultiPrecisionCenter(Location current, int navSize, double plotScale0, double v1, double v2,
        int xDigit, out string centerRe1, out string centerIm1)
    {
        var x = new Xnum(Precision.DgMx(xDigit));
        EDecimal a0 = x.Cvt(plotScale0);
        EDecimal a1 = x.Cvt(navSize - 1);
        EDecimal a2 = x.Center(current.CenterRe, current.CenterReValue);
        EDecimal a3 = x.Center(current.CenterIm, current.CenterImValue);
        EDecimal a4 = x.Cvt(2);
        EDecimal a5 = x.Div(a0, a4);           // plotScale0 / 2
        EDecimal a6 = x.Sub(a2, a5);           // zr0 = centerRe0 - plotScale0 / 2
        EDecimal a7 = x.Add(a3, a5);           // zi0 = centerIm0 + plotScale0 / 2
        EDecimal a8 = x.Div(a0, a1);           // pixelStep0 = plotScale0 / (navSize - 1)
        a0 = x.Cvt(v1);
        a1 = x.Cvt(v2);
        a0 = x.Mul(a0, a8);                    // v1 * pixelStep0
        a1 = x.Mul(a1, a8);                    // v2 * pixelStep0
        a4 = x.Sub(a7, a0);                    // centerIm1 = zi0 - v1 * pixelStep0
        a5 = x.Add(a6, a1);                    // centerRe1 = zr0 + v2 * pixelStep0
        centerIm1 = Xnum.ToStr(a4, xDigit);
        centerRe1 = Xnum.ToStr(a5, xDigit);
    }
}
