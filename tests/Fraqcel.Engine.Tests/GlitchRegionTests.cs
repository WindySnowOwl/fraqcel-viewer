using Fraqcel.Engine.Dll;
using Xunit;

namespace Fraqcel.Engine.Tests;

public class GlitchRegionTests
{
    /// <summary>A glitch-flag array the way the driver allocates it for a rows x cols image at resolution 1.</summary>
    private static int[,] Flags(params string[] rows)
    {
        int r1 = 2, r2 = rows.Length + 1, c1 = 2, c2 = rows[0].Length + 1;
        var f = VbaArrays.Int2D(r1 - 1, r2, c1 - 1, c2 + 1);
        for (int r = 0; r < rows.Length; r++)
            for (int c = 0; c < rows[r].Length; c++)
                if (rows[r][c] == '#') f[r + 2, c + 2] = 1;
        return f;
    }

    [Fact]
    public void Labeling_uses_8_connectivity()
    {
        var f = Flags(
            "#.....",
            ".#..##",
            "....#.",
            "......");
        int[] roots = GlitchRegions.RegionLabeling(f, 1);
        Assert.Equal(2, roots.Length - 1); // the diagonal pair is one region
        Assert.Equal(f[2, 2], f[3, 3]);
        Assert.Equal(f[3, 6], f[4, 6]);
        Assert.NotEqual(f[2, 2], f[3, 6]);
    }

    [Fact]
    public void Range_size_and_largest_region()
    {
        var f = Flags(
            "##....",
            "##...#",
            ".....#",
            "......");
        int[] roots = GlitchRegions.RegionLabeling(f, 1);
        GlitchRegions.GetRange(f, 1, out int r1, out int c1, out int r2, out int c2);
        Assert.Equal((2, 2, 4, 7), (r1, c1, r2, c2));

        int j = roots.Length - 1, k = roots[j];
        var size = new long[k + 1];
        var rr = new int[k + 1];
        var cc = new int[k + 1];
        GlitchRegions.GetSize(f, 1, r1, c1, r2, c2, rr, cc, size);
        long largest = 0;
        int label = 0;
        var info = new long[4];
        GlitchRegions.GetGlitchInfo(ref largest, ref label, info, size, j, k);
        Assert.Equal(4, largest);
        Assert.Equal(f[2, 2], label);
        Assert.Equal(new long[] { 0, 2, 4, 0 }, info);
    }

    [Fact]
    public void Equal_sizes_pick_the_lowest_label()
    {
        var f = Flags(
            "##..##",
            "......");
        int[] roots = GlitchRegions.RegionLabeling(f, 1);
        int j = roots.Length - 1, k = roots[j];
        var size = new long[k + 1];
        GlitchRegions.GetSize(f, 1, 2, 2, 3, 7, new int[k + 1], new int[k + 1], size);
        long largest = 0;
        int label = 0;
        GlitchRegions.GetGlitchInfo(ref largest, ref label, new long[4], size, j, k);
        Assert.Equal(f[2, 2], label);
    }

    [Fact]
    public void Centroid_rounds_half_to_even()
    {
        // A diagonal pair at flag coordinates (2,3) and (3,4): rows average 2.5 -> 2, columns 3.5 -> 4.
        var f = Flags(
            ".#....",
            "..#...",
            "......");
        GlitchRegions.RegionLabeling(f, 1);
        int label = f[2, 3];
        GlitchRegions.GetCentroid(f, label, 1, 2, 2, 4, 7, out int r, out int c);
        Assert.Equal(2, r);
        Assert.Equal(4, c);
    }

    [Fact]
    public void Corner_index_past_4_means_corner_4()
    {
        var f = Flags(
            "......",
            ".###..",
            ".###..",
            "......");
        GlitchRegions.RegionLabeling(f, 1);
        int label = f[3, 3];
        GlitchRegions.GetCorner(f, label, 1, 2, 2, 5, 7, out int r4, out int c4, 4);
        GlitchRegions.GetCorner(f, label, 1, 2, 2, 5, 7, out int r9, out int c9, 9);
        Assert.Equal((4, 3), (r4, c4)); // bottom left
        Assert.Equal((r4, c4), (r9, c9));
        GlitchRegions.GetCorner(f, label, 1, 2, 2, 5, 7, out int r1, out int c1, 1);
        Assert.Equal((3, 3), (r1, c1)); // top left
    }

    [Fact]
    public void AveragePixel_rounds_like_the_VBA_Long_arithmetic()
    {
        var img = VbaArrays.Double2D(3, 3);
        img[1, 2] = 10.4; // above
        img[3, 2] = 11.7; // below
        img[2, 1] = 12.2; // left
        img[2, 3] = 13.6; // right
        // CLng(10.4 + 11.7) = 22; CLng(22 + 13.6 + 12.2) = 48; CLng(48 / 4) = 12.
        GlitchRegions.AveragePixel(img, 3, 3, 1); // glitch-flag coordinates are one more than image indices
        Assert.Equal(12, img[2, 2]);
    }
}
