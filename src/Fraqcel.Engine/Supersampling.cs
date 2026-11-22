// Part of the unofficial Fraqcel host prototype. Ported from Fraqcel 2.15.0's VBA
// (coreCode.bas blendSamples), Copyright (c) 2026 Kevin Orloske, BSD-2-Clause.

using System;
using Fraqcel.Engine.Dll;

namespace Fraqcel.Engine;

internal static class Supersampling
{
    /// <summary>
    /// blendSamples: if more than half the samples are in the set the pixel is in the set;
    /// otherwise it gets the mean of the escaped samples, rounded to 3 decimals for smooth
    /// and stripe-iteration coloring, 0 for discrete, and not rounded for stripe fixed range.
    /// </summary>
    /// <remarks>VBA's Round is approximated with Math.Round(x, k, ToEven).</remarks>
    public static double[,] Blend(double[,] samples, int s, int maxIter, bool flagSmooth, bool flagFixedRange)
    {
        int rSize = samples.GetLength(0), cSize = samples.GetLength(1);
        int sampleSize = s * s;
        var result = VbaArrays.Double2D(rSize / s, cSize / s);
        int k = flagSmooth ? 3 : 0;
        int i = 0;
        for (int r = 1; r <= rSize; r += s)
        {
            i++;
            int j = 0;
            for (int c = 1; c <= cSize; c += s)
            {
                j++;
                int inSet = 0;
                double iterSum = 0;
                for (int r0 = r; r0 <= r + s - 1; r0++)
                    for (int c0 = c; c0 <= c + s - 1; c0++)
                    {
                        double v = samples[r0, c0];
                        if (v == maxIter) inSet++;
                        else iterSum += v;
                    }
                double value = inSet > sampleSize / 2.0 ? maxIter : iterSum / (sampleSize - inSet);
                result[i, j] = flagFixedRange ? value : Math.Round(value, k, MidpointRounding.ToEven);
            }
        }
        return result;
    }
}
