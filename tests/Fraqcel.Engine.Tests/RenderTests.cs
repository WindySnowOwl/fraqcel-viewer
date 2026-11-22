using System;
using System.IO;
using System.Security.Cryptography;
using Fraqcel.Engine.Dll;
using Xunit;

namespace Fraqcel.Engine.Tests;

/// <summary>Loads the stock DLL once for every test that needs it.</summary>
internal static class TestDll
{
    static TestDll() => FraqcelDll.Load();
    public static void Ensure() { }
}

public class NavigatorTests
{
    public NavigatorTests() => TestDll.Ensure();

    [Fact]
    public void Clicking_the_center_pixel_keeps_the_center()
    {
        // The 800-px image's center sits between pixels 399 and 400; a click on 399 centers half a pixel past it.
        var next = Navigator.Move(Location.FullView, 800, CellSelection.Pixel(399, 399), ZoomDirection.In, 4, new PrecisionSettings());
        Assert.Equal(-0.5, next.CenterReAsDouble, 15);
        Assert.Equal(0.0, next.CenterImAsDouble, 15);
        Assert.Equal(1.0, next.PlotScale);
        Assert.NotNull(next.CenterReValue); // double mode: Fraqcel stores a Double
    }

    [Fact]
    public void A_box_zooms_to_its_larger_side()
    {
        var next = Navigator.Move(Location.FullView, 800, CellSelection.Box(360, 330, 400, 350), ZoomDirection.In, 4, new PrecisionSettings());
        Assert.Equal(4.0 * 40 / 799, next.PlotScale, 15);
    }

    [Fact]
    public void Zoom_out_with_a_click_divides_by_the_factor()
    {
        var next = Navigator.Move(Location.FullView, 800, CellSelection.Pixel(399, 399), ZoomDirection.Out, 4, new PrecisionSettings());
        Assert.Equal(16.0, next.PlotScale);
    }

    [Fact]
    public void Deep_zoom_keeps_the_center_in_multi_precision()
    {
        var deep = Location.FraqcelSuggested;
        var next = Navigator.Move(deep, 800, CellSelection.Pixel(399, 399), ZoomDirection.In, 4, new PrecisionSettings());
        Assert.Null(next.CenterReValue); // multi-precision: a string, rounded to xDigit digits
        Assert.Equal(deep.CenterRe, next.CenterRe);
        Assert.Equal(deep.CenterIm, next.CenterIm);
    }

    [Fact]
    public void Kit_zoom_past_the_double_range_is_refused()
    {
        var e = Assert.Throws<ArgumentOutOfRangeException>(() => Location.FromKitZoom("-1", "0", "4.6e1105", 1000));
        Assert.Equal("zoomText", e.ParamName);
    }
}

public class RenderTests
{
    public RenderTests() => TestDll.Ensure();

    private static string Sha256(double[,] data)
    {
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
            for (int r = 1; r <= data.GetLength(0); r++)
                for (int c = 1; c <= data.GetLength(1); c++)
                    w.Write(data[r, c]);
        using var sha = SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(ms.ToArray())).Replace("-", "").ToLowerInvariant();
    }

    [Fact]
    public void Full_view_matches_an_independent_direct_DLL_call()
    {
        // The same view rendered earlier by a throwaway host that called fraqcelDbl directly.
        var r = new Renderer().Render(Location.FullView, new RenderSettings(), Geometry.Navigation(800));
        Assert.Equal(PrecisionMode.Double, r.Mode);
        Assert.Equal("daa2a013f7e35cf47156b62b1928570924112d11f1dba95e290e13af10f0b6ea", Sha256(r.Data));
        Assert.Equal(500, r.Data[401, 401]); // (-0.5, 0) is in the set
    }

    [Fact]
    public void Deep_sample_is_corrected_until_clean()
    {
        var s = new RenderSettings { Coloring = ColoringMode.Smooth };
        var r = new Renderer().Render(Location.FraqcelSuggested, s, Geometry.Navigation(800));
        Assert.Equal(PrecisionMode.SeriesSD, r.Mode);
        Assert.True(r.Passes.Count > 1);
        Assert.True(r.Passes[0].Glitched > 10000);
        Assert.True(r.IsClean, r.Summary());
    }

    [Fact]
    public void Uncorrected_glitches_are_reported_not_hidden()
    {
        var s = new RenderSettings { Coloring = ColoringMode.Smooth };
        s.Precision.DebugGlitches = true;
        var r = new Renderer().Render(Location.FraqcelSuggested, s, Geometry.Navigation(596));
        Assert.False(r.IsClean);
        Assert.Equal(r.Passes[0].Glitched, r.Unresolved);
    }

    [Fact]
    public void Renders_are_deterministic()
    {
        var s = new RenderSettings { Coloring = ColoringMode.Smooth };
        var g = Geometry.Publish(320, 180);
        string a = Sha256(new Renderer().Render(Location.FraqcelSuggested, s, g).Data);
        string b = Sha256(new Renderer().Render(Location.FraqcelSuggested, s, g).Data);
        Assert.Equal(a, b);
    }

    [Fact]
    public void Supersampling_blends_to_the_output_size()
    {
        var r = new Renderer().Render(Location.FullView, new RenderSettings { Coloring = ColoringMode.Smooth }, Geometry.Publish(64, 36, 4));
        Assert.Equal(36, r.Rows);
        Assert.Equal(64, r.Cols);
        using var bmp = Colorizer.ToBitmap(r, new ColorSettings(), out _);
        Assert.Equal(64, bmp.Width);
        Assert.Equal(36, bmp.Height);
    }

    [Fact]
    public void Publish_sizes_follow_Fraqcels_rules()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Geometry.Publish(16, 100));
        Assert.Throws<ArgumentOutOfRangeException>(() => Geometry.Publish(16384, 100));
        Assert.Throws<ArgumentOutOfRangeException>(() => Geometry.Publish(10000, 100, 16)); // 4 x 10000 > 32765
        Assert.Throws<ArgumentOutOfRangeException>(() => Geometry.Navigation(640));
    }
}
