// Part of the unofficial Fraqcel host prototype.

using System;

namespace Fraqcel.Engine.Dll;

/// <summary>Arrays shaped the way Fraqcel's VBA builds them (Option Base 1 and explicit bounds).</summary>
public static class VbaArrays
{
    /// <summary>A <c>double[1..rows, 1..cols]</c> array, like VBA's <c>ReDim a(rows, cols)</c> under Option Base 1.</summary>
    public static double[,] Double2D(int rows, int cols) =>
        (double[,])Array.CreateInstance(typeof(double), new[] { rows, cols }, new[] { 1, 1 });

    /// <summary>An <c>int[r1..r2, c1..c2]</c> array, like VBA's <c>ReDim a(r1 To r2, c1 To c2)</c>.</summary>
    public static int[,] Int2D(int r1, int r2, int c1, int c2) =>
        (int[,])Array.CreateInstance(typeof(int), new[] { r2 - r1 + 1, c2 - c1 + 1 }, new[] { r1, c1 });
}
