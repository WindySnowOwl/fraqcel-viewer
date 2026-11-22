// Part of the unofficial Fraqcel host prototype. Ported line by line from Fraqcel 2.15.0's
// VBA (pertUtilities.bas), Copyright (c) 2026 Kevin Orloske, BSD-2-Clause (see NOTICES.md).
// Each routine keeps its VBA name and behavior, including the details that decide which
// reference point comes next (design 5.5).

using System;

namespace Fraqcel.Engine;

internal static class GlitchRegions
{
    /// <summary>
    /// regionLabeling: two-pass connected-component labeling with 8-connectivity. Relabels
    /// <paramref name="glitchFlag"/> in place with root labels and returns the root labels
    /// (1-based, index 0 unused), so <c>roots.Length - 1</c> is the number of regions.
    /// </summary>
    public static int[] RegionLabeling(int[,] glitchFlag, int res)
    {
        int r1 = glitchFlag.GetLowerBound(0) + res;
        int c1 = glitchFlag.GetLowerBound(1) + res;
        int r2 = glitchFlag.GetUpperBound(0);
        int c2 = glitchFlag.GetUpperBound(1) - res;

        // Disjoint-set: negative values are roots, their magnitude the tree size.
        int maxLabel = 1000;
        int[] labelEqv = NewEqv(maxLabel);
        int newLabel = 0;

        // First pass
        for (int r = r1; r <= r2; r += res)
        {
            for (int c = c1; c <= c2; c += res)
            {
                if (glitchFlag[r, c] == 0) continue;
                int ne = glitchFlag[r - res, c + res], n = glitchFlag[r - res, c],
                    nw = glitchFlag[r - res, c - res], w = glitchFlag[r, c - res];
                if (ne > 0 || n > 0 || nw > 0 || w > 0)
                {
                    int minLabel = int.MaxValue;
                    if (ne > 0 && minLabel > ne) minLabel = ne;
                    if (n > 0 && minLabel > n) minLabel = n;
                    if (nw > 0 && minLabel > nw) minLabel = nw;
                    if (w > 0 && minLabel > w) minLabel = w;
                    glitchFlag[r, c] = minLabel;
                    if (ne > 0) DsetUnion(labelEqv, minLabel, ne);
                    if (n > 0) DsetUnion(labelEqv, minLabel, n);
                    if (nw > 0) DsetUnion(labelEqv, minLabel, nw);
                    if (w > 0) DsetUnion(labelEqv, minLabel, w);
                }
                else
                {
                    newLabel++;
                    if (newLabel > maxLabel)
                    {
                        int[] grown = NewEqv(maxLabel + maxLabel);
                        Array.Copy(labelEqv, grown, maxLabel + 1);
                        labelEqv = grown;
                        maxLabel += maxLabel;
                    }
                    glitchFlag[r, c] = newLabel;
                }
            }
        }

        // Second pass
        for (int r = r1; r <= r2; r += res)
            for (int c = c1; c <= c2; c += res)
                if (glitchFlag[r, c] != 0)
                    glitchFlag[r, c] = DsetFind(labelEqv, glitchFlag[r, c]);

        // The list of root labels
        int j = 0;
        for (int i = 1; i <= newLabel; i++)
            if (labelEqv[i] < 0) labelEqv[++j] = i;
        var roots = new int[j + 1];
        Array.Copy(labelEqv, roots, j + 1);
        return roots;
    }

    private static int[] NewEqv(int maxLabel)
    {
        var a = new int[maxLabel + 1];
        for (int i = 1; i <= maxLabel; i++) a[i] = -1;
        return a;
    }

    /// <summary>dsetFind, with the VBA's path compression.</summary>
    private static int DsetFind(int[] x, int i)
    {
        if (x[i] < 0) return i;
        x[i] = DsetFind(x, x[i]);
        return DsetFind(x, x[i]);
    }

    /// <summary>dsetUnion: union by size, ties to <paramref name="i"/>'s root.</summary>
    private static void DsetUnion(int[] x, int i, int j)
    {
        int iRoot = DsetFind(x, i);
        int jRoot = DsetFind(x, j);
        if (iRoot == jRoot) return;
        if (x[iRoot] > x[jRoot])
        {
            x[jRoot] += x[iRoot];
            x[iRoot] = jRoot;
        }
        else
        {
            x[iRoot] += x[jRoot];
            x[jRoot] = iRoot;
        }
    }

    /// <summary>getSize: region sizes, and for each region the last pixel seen (its location when the size is 1).</summary>
    public static void GetSize(int[,] glitchFlag, int res, int r1, int c1, int r2, int c2,
        int[] rRef2, int[] cRef2, long[] glitchSize)
    {
        for (int r = r1; r <= r2; r += res)
            for (int c = c1; c <= c2; c += res)
            {
                int l = glitchFlag[r, c];
                if (l == 0) continue;
                glitchSize[l]++;
                rRef2[l] = r;
                cRef2[l] = c;
            }
    }

    /// <summary>getCentroid: the region's mean row and column, assigned to Integer (banker's rounding).</summary>
    public static void GetCentroid(int[,] glitchFlag, int label, int res, int r1, int c1, int r2, int c2,
        out int rRef, out int cRef)
    {
        long size = 0;
        double rSum = 0, cSum = 0;
        for (int r = r1; r <= r2; r += res)
            for (int c = c1; c <= c2; c += res)
                if (glitchFlag[r, c] == label)
                {
                    size++;
                    rSum = r + rSum;
                    cSum = c + cSum;
                }
        rRef = Vba.CInt(rSum / size);
        cRef = Vba.CInt(cSum / size);
        SnapToResGrid(ref rRef, ref cRef, res);
    }

    /// <summary>
    /// getCorner: a corner of the region's bounding box. 1 = top left, 2 = top right,
    /// 3 = bottom right, anything else = bottom left (the index is never bounded).
    /// </summary>
    public static void GetCorner(int[,] glitchFlag, int label, int res, int r1, int c1, int r2, int c2,
        out int rRef, out int cRef, long whichCorner)
    {
        if (whichCorner == 1) { rRef = 32767; cRef = 32767; }
        else if (whichCorner == 2) { rRef = 32767; cRef = 0; }
        else if (whichCorner == 3) { rRef = 0; cRef = 0; }
        else { rRef = 0; cRef = 32767; }

        for (int r = r1; r <= r2; r += res)
            for (int c = c1; c <= c2; c += res)
            {
                if (glitchFlag[r, c] != label) continue;
                if (whichCorner == 1) { if (r < rRef) rRef = r; if (c < cRef) cRef = c; }
                else if (whichCorner == 2) { if (r < rRef) rRef = r; if (c > cRef) cRef = c; }
                else if (whichCorner == 3) { if (r > rRef) rRef = r; if (c > cRef) cRef = c; }
                else { if (r > rRef) rRef = r; if (c < cRef) cRef = c; }
            }
    }

    /// <summary>getRange: the bounding box of every glitch.</summary>
    public static void GetRange(int[,] glitchFlag, int res, out int r1, out int c1, out int r2, out int c2)
    {
        int rr1 = glitchFlag.GetLowerBound(0) + res;
        int cc1 = glitchFlag.GetLowerBound(1) + res;
        int rr2 = glitchFlag.GetUpperBound(0);
        int cc2 = glitchFlag.GetUpperBound(1) - res;
        r1 = rr2; r2 = rr1; c1 = cc2; c2 = cc1;
        for (int r = rr1; r <= rr2; r += res)
            for (int c = cc1; c <= cc2; c += res)
            {
                if (glitchFlag[r, c] == 0) continue;
                if (r < r1) r1 = r;
                if (c < c1) c1 = c;
                if (r > r2) r2 = r;
                if (c > c2) c2 = c;
            }
    }

    /// <summary>getGlitchInfo: the largest region (the lowest label among equal sizes) and the pass summary.</summary>
    public static void GetGlitchInfo(ref long largestGlitchSize, ref int largestGlitchLabel, long[] glitchInfo,
        long[] glitchSize, int j, int k)
    {
        for (int i = 1; i <= k; i++)
            if (glitchSize[i] > largestGlitchSize)
            {
                largestGlitchSize = glitchSize[i];
                largestGlitchLabel = i;
            }
        glitchInfo[1] = j;                 // number of glitches
        glitchInfo[2] = largestGlitchSize; // size of largest glitch
        glitchInfo[3] = 0;                 // type of reference point to use (center or corner)
    }

    /// <summary>
    /// averagePixel: replaces a 1-pixel glitch with the whole-number average of its neighbors.
    /// <paramref name="r"/> and <paramref name="c"/> are glitch-flag coordinates (one more than
    /// the image index). Rounds three times, as the VBA's Long arithmetic does.
    /// </summary>
    public static void AveragePixel(double[,] imageData, int r, int c, int res)
    {
        if (res != 1) throw new NotSupportedException("Reduced resolution is not in the prototype.");
        r -= 1;
        c -= 1;
        int r1 = imageData.GetLowerBound(0), c1 = imageData.GetLowerBound(1);
        int r2 = imageData.GetUpperBound(0), c2 = imageData.GetUpperBound(1);
        long iterSum;
        int cnt;
        if (r == r1) { iterSum = Vba.CLng(imageData[r + res, c]); cnt = 1; }
        else if (r == r2) { iterSum = Vba.CLng(imageData[r - res, c]); cnt = 1; }
        else if (res > 1 && r == r2 - 1) { iterSum = Vba.CLng(imageData[r - res, c]); cnt = 1; }
        else { iterSum = Vba.CLng(imageData[r + res, c] + imageData[r - res, c]); cnt = 2; }

        if (c == c1) { iterSum = Vba.CLng(iterSum + imageData[r, c + res]); cnt++; }
        else if (c == c2) { iterSum = Vba.CLng(iterSum + imageData[r, c - res]); cnt++; }
        else if (res > 1 && c == c2 - 1) { iterSum = Vba.CLng(iterSum + imageData[r, c - res]); cnt++; }
        else { iterSum = Vba.CLng(iterSum + imageData[r, c + res] + imageData[r, c - res]); cnt += 2; }

        imageData[r, c] = Vba.CLng((double)iterSum / cnt);
    }

    /// <summary>snapToResGrid (coreCode.bas).</summary>
    public static void SnapToResGrid(ref int r, ref int c, int res)
    {
        if (res == 2)
        {
            if (r % 2 != 0) r--;
            if (c % 2 != 0) c--;
        }
        else if (res == 3)
        {
            if (r % 3 == 0) r--;
            if (r % 3 == 1) r -= 2;
            if (c % 3 == 0) c--;
            if (c % 3 == 1) c -= 2;
        }
    }
}
