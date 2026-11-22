using System;
using System.Linq;
using PeterO.Numbers;
using Xunit;

namespace Fraqcel.Engine.Tests;

public class MaxIterationsTests
{
    [Theory]
    [InlineData("500", 1000)]
    [InlineData(" 500 ", 1000)]
    [InlineData("1e3", 2000)]
    [InlineData("2.5", 4)]              // CLng rounds half to even: 2, then doubled
    [InlineData("6000000", 10000000)]   // doubled past the limit: clipped
    [InlineData("9999999.5", 10000000)] // CLng gives 10,000,000; doubled and clipped
    [InlineData("10000000", 10000000)]
    [InlineData("12000000", 10000000)]  // above the limit: clipped, not doubled
    [InlineData("abc", 750)]            // not a number: back to the rendered view's value
    [InlineData("", 750)]
    [InlineData("0", 750)]              // below 1: the same
    [InlineData("0.5", 750)]
    public void Times_two_follows_iterPosBtn(string text, int expected) =>
        Assert.Equal(expected, MaxIterations.Double(text, 750));

    [Theory]
    [InlineData("500", 250)]
    [InlineData("5", 2)]                // 2.5 to a Long: half to even
    [InlineData("7", 4)]                // 3.5: half to even
    [InlineData("3", 2)]                // 1.5
    [InlineData("1", 1)]                // 0.5 rounds to 0, which is clipped to 1
    [InlineData("2.5", 1)]              // CLng gives 2 first
    [InlineData("15000000", 7500000)]   // above 10,000,000 but within Fraqcel's 20,000,000 check
    [InlineData("20000000", 10000000)]
    [InlineData("25000000", 10000000)]
    [InlineData("x", 750)]
    [InlineData("-4", 750)]
    public void Divide_by_two_follows_iterNegBtn(string text, int expected) =>
        Assert.Equal(expected, MaxIterations.Halve(text, 750));
}

public class WheelZoomTests
{
    public WheelZoomTests() => TestDll.Ensure();

    private static readonly EContext Ctx = EContext.ForPrecision(80);

    /// <summary>Pixel (row, col)'s sample: Re = center - scale/2 + col step, Im = center + scale/2 - row step.</summary>
    private static (EDecimal Re, EDecimal Im, EDecimal Step) Sample(Location loc, int n, int row, int col)
    {
        EDecimal re = loc.CenterReValue is double dr ? EDecimal.FromDouble(dr) : EDecimal.FromString(loc.CenterRe);
        EDecimal im = loc.CenterImValue is double di ? EDecimal.FromDouble(di) : EDecimal.FromString(loc.CenterIm);
        EDecimal scale = EDecimal.FromDouble(loc.PlotScale);
        EDecimal half = scale.Divide(EDecimal.FromInt32(2), Ctx);
        EDecimal step = scale.Divide(EDecimal.FromInt32(n - 1), Ctx);
        return (re.Subtract(half, Ctx).Add(step.Multiply(EDecimal.FromInt32(col), Ctx), Ctx),
                im.Add(half, Ctx).Subtract(step.Multiply(EDecimal.FromInt32(row), Ctx), Ctx),
                step);
    }

    /// <summary>How far, in the new view's pixel steps, the sample under the pointer moved.</summary>
    private static double Drift(Location before, Location after, int n, int row, int col)
    {
        var a = Sample(before, n, row, col);
        var b = Sample(after, n, row, col);
        double dRe = b.Re.Subtract(a.Re, Ctx).Abs().Divide(b.Step, Ctx).ToDouble();
        double dIm = b.Im.Subtract(a.Im, Ctx).Abs().Divide(b.Step, Ctx).ToDouble();
        return Math.Max(dRe, dIm);
    }

    [Theory]
    [InlineData(200, 600, 1)]
    [InlineData(0, 0, 1)]
    [InlineData(799, 13, -1)]
    [InlineData(399, 400, 1)]
    [InlineData(123, 456, -1)]
    public void The_sample_under_the_pointer_stays_put(int row, int col, int direction)
    {
        var start = Location.FullView;
        var next = Navigator.ZoomAround(start, 800, row, col, (ZoomDirection)direction, 4, new PrecisionSettings());
        Assert.Equal(direction > 0 ? 1.0 : 16.0, next.PlotScale);
        Assert.NotNull(next.CenterReValue); // still double precision: Fraqcel holds the center as a Double
        Assert.True(Drift(start, next, 800, row, col) < 1e-6, "drift " + Drift(start, next, 800, row, col));
        // CONTROL: a neighboring pixel's sample does move (by f - 1 = 3 pixels of the new view per pixel off).
        Assert.True(Drift(start, next, 800, row, col == 0 ? 1 : col - 1) > 0.5);
    }

    [Fact]
    public void Deep_wheel_zoom_keeps_the_pointer_in_multi_precision()
    {
        var start = Location.FraqcelSuggested; // plot scale 8.8e-32: multi-precision
        foreach (var (row, col, dir) in new[] { (100, 650, 1), (700, 20, 1), (399, 399, -1) })
        {
            var next = Navigator.ZoomAround(start, 800, row, col, (ZoomDirection)dir, 10, new PrecisionSettings());
            Assert.Null(next.CenterReValue); // a decimal string, rounded to the digits the scale needs
            double drift = Drift(start, next, 800, row, col);
            Assert.True(drift < 0.01, "drift " + drift + " pixels at (" + row + ", " + col + ")");
        }
    }

    [Fact]
    public void Wheel_zoom_refuses_a_pixel_off_the_image_and_a_pan()
    {
        Assert.Throws<ArgumentException>(() => Navigator.ZoomAround(Location.FullView, 800, 800, 0, ZoomDirection.In, 4, new PrecisionSettings()));
        Assert.Throws<ArgumentException>(() => Navigator.ZoomAround(Location.FullView, 800, 5, 5, ZoomDirection.Pan, 4, new PrecisionSettings()));
        Assert.Throws<ArgumentException>(() => Navigator.ZoomAround(Location.FullView, 800, 5, 5, ZoomDirection.In, 0, new PrecisionSettings()));
    }
}

public class SavedLocationTests
{
    [Fact]
    public void A_text_center_round_trips_and_the_name_is_cleaned()
    {
        var saved = new SavedLocation("  Deep | one\n", Location.FraqcelSuggested);
        Assert.Equal("Deep / one", saved.Name);
        Assert.True(SavedLocation.TryParse(saved.ToSettingsValue(), out var back));
        Assert.Equal("Deep / one", back!.Name);
        Assert.Equal(Location.FraqcelSuggested.CenterRe, back.Location.CenterRe);
        Assert.Equal(Location.FraqcelSuggested.CenterIm, back.Location.CenterIm);
        Assert.Equal(Location.FraqcelSuggested.PlotScale, back.Location.PlotScale);
        Assert.Equal(2000, back.Location.MaxIter);
        Assert.Null(back.Location.CenterReValue);
    }

    [Fact]
    public void A_double_center_reloads_as_the_same_doubles()
    {
        double re = -0.75123456789012345, im = 0.1 + 1e-17, scale = 4.0 / 799 * 3;
        var loc = Location.FromDoubles(re, im, scale, 1000);
        Assert.True(SavedLocation.TryParse(new SavedLocation("d", loc).ToSettingsValue(), out var back));
        Assert.Equal(re, back!.Location.CenterReValue);
        Assert.Equal(im, back.Location.CenterImValue);
        Assert.Equal(scale, back.Location.PlotScale);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("a|b")]
    [InlineData("n|x|0|1|10|text")]   // center not a number
    [InlineData("n|0|0|-1|10|text")]  // negative plot scale
    [InlineData("n|0|0|NaN|10|text")]
    [InlineData("n|0|0|1|0|text")]    // max iterations below 1
    [InlineData("n|0|0|1|10|other")]  // unknown kind
    [InlineData("n|NaN|0|1|10|double")]
    public void Unreadable_values_are_refused(string? value) =>
        Assert.False(SavedLocation.TryParse(value, out _));

    [Fact]
    public void Fields_past_the_sixth_are_ignored() =>
        Assert.True(SavedLocation.TryParse("n|0|0|1|10|text|a later field", out _));

    [Fact]
    public void The_settings_file_keeps_the_list_in_order_and_other_keys_alone()
    {
        using var tmp = new TempFolder();
        string path = tmp.File("settings.ini");
        System.IO.File.WriteAllText(path,
            "fraqcel_dll=C:\\fraqcel\\FraqcelLib2150_x64.dll\r\n" +
            "location.10=ten|0|0|1|10|text\r\n" +
            "location.2=two|0|0|2|20|text\r\n" +
            "location.3=garbage\r\n" +
            "later_tool_key=kept\r\n");
        var s = HostSettings.Load(path);
        Assert.Equal(new[] { "two", "ten" }, s.Locations.Select(l => l.Name)); // by number; the bad entry is skipped

        s.Locations = new[] { new SavedLocation("only", Location.FullView) };
        s.Save(path);
        var back = HostSettings.Load(path);
        Assert.Equal(new[] { "only" }, back.Locations.Select(l => l.Name));
        Assert.Null(back.Get("location.2"));
        Assert.Null(back.Get("location.3")); // replaced lists drop entries they couldn't read
        Assert.Null(back.Get("location.10"));
        Assert.Equal("kept", back.Get("later_tool_key"));
        Assert.Equal("C:\\fraqcel\\FraqcelLib2150_x64.dll", back.DllPath);
    }
}
