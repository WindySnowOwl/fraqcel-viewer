using System;
using Xunit;

namespace Fraqcel.Engine.Tests;

public class VbaTests
{
    [Theory]
    [InlineData(0.5, 0)]
    [InlineData(1.5, 2)]
    [InlineData(2.5, 2)]
    [InlineData(-2.5, -2)]
    [InlineData(7.4999999999, 7)]
    public void CInt_rounds_half_to_even(double x, int expected) => Assert.Equal(expected, Vba.CInt(x));

    [Fact]
    public void CInt_overflows_outside_16_bits() => Assert.Throws<OverflowException>(() => Vba.CInt(32768));

    [Theory]
    [InlineData(4 / 4.5e31, "8.88888888888889E-32")] // how plotScale enters XNUMBERS (design 5.3)
    [InlineData(399.5, "399.5")]
    [InlineData(0.001, "0.001")]
    [InlineData(-0.5, "-0.5")]
    public void CStr_is_VBAs_15_digit_text(double x, string expected) => Assert.Equal(expected, Vba.CStr(x));

    [Fact]
    public void CDbl_parses_with_a_decimal_point() => Assert.Equal(0.5, Vba.CDbl("0.5"));

    [Theory]
    [InlineData(4.0 / 799, 0, 16)]         // the full view: at least 16
    [InlineData(8.81751911989855E-32 / 799, 0, 38)]
    [InlineData(8.81751911989855E-32 / 799, 3, 41)]
    public void XDigit_follows_isXnumNeeded(double scaleTest, int extra, int expected) =>
        Assert.Equal(expected, Precision.XDigit(scaleTest, extra));

    [Fact]
    public void Precision_mode_follows_isXnumNeeded()
    {
        var p = new PrecisionSettings();
        Assert.Equal(PrecisionMode.Double, Precision.Choose(1e-12, p));
        Assert.Equal(PrecisionMode.SeriesSD, Precision.Choose(1e-14, p));
        p.Series = SeriesType.Off;
        Assert.Equal(PrecisionMode.Perturbation, Precision.Choose(1e-14, p));
        p.Perturbation = false;
        Assert.Equal(PrecisionMode.MultiPrecision, Precision.Choose(1e-14, p));
        p.ForceMultiPrecision = true;
        Assert.Equal(PrecisionMode.MultiPrecision, Precision.Choose(1, p));
        Assert.Equal(PrecisionMode.Perturbation, Precision.Choose(1, p, manualReference: true));
    }

    [Fact]
    public void Invalid_precision_settings_are_rewritten_like_isXnumNeeded()
    {
        var p = new PrecisionSettings { Threshold = -1, MaxReferences = 0, MaxPastRef = 250, SeriesTolerance = double.NaN };
        var warnings = p.Normalize();
        Assert.Equal(PrecisionSettings.DefaultThreshold, p.Threshold);
        Assert.Equal(30, p.MaxReferences);
        Assert.Equal(100, p.MaxPastRef);
        Assert.Equal(PrecisionSettings.DefaultSeriesTolerance, p.SeriesTolerance);
        Assert.Equal(4, warnings.Count);
    }
}

public class PaletteTests
{
    /// <summary>SETTINGS column B of the user's workbook: Fraqcel's Default preset as built by colorBtn_Click.</summary>
    private static readonly int[] WorkbookDefault =
    {
        6684672, 7145221, 7671562, 8132111, 8592659, 9053208, 9579549, 10040098, 10500647, 11026988, 11487537,
        11948085, 12408634, 12934975, 13395524, 13856073, 14382414, 14842963, 15303511, 15764060, 16290401, 16750950,
        16621165, 16425845, 16296060, 16100483, 15970698, 15775378, 15645593, 15515808, 15320488, 15190703, 14995126,
        14865341, 14670021, 14540236, 14410451, 14215131, 14085346, 13889769, 13759984, 13564664, 13434879, 12776954,
        12184821, 11526896, 10868972, 10211047, 9618914, 8960989, 8303064, 7710931, 7053006, 6395082, 5737157,
        5145024, 4487099, 3829174, 3237041, 2579116, 1921192, 1263267, 671134, 13209, 340370, 667274, 994435, 1255804,
        1582965, 1909869, 2237030, 2564191, 2891095, 3218256, 3479625, 3806786, 4133690, 4460851, 4788012, 5114916,
        5442077, 5703446, 6030607, 6357511,
    };

    [Fact]
    public void Default_preset_matches_the_workbook() =>
        Assert.Equal(WorkbookDefault, PaletteBuilder.Build(PalettePresets.Default.KeyColors, PalettePresets.Default.Bands));

    [Fact]
    public void Band_expression_is_copied_as_written()
    {
        // 5 bands, red from 0 to 9: j = 5 gives 9 * (5 * (1/6)) = 7.4999... -> 7.
        // Revision 1's j / (numBand + 1) would give 7.5 -> 8 (design 5.7).
        int[] colors = PaletteBuilder.Build(new[] { PaletteBuilder.Rgb(0, 0, 0), PaletteBuilder.Rgb(9, 0, 0) }, 5);
        Assert.Equal(12, colors.Length);
        Assert.Equal(7, PaletteBuilder.Red(colors[5]));
    }

    [Fact]
    public void Offset_reverse_and_set_color_follow_createBmp()
    {
        int[] colors = { 1, 2, 3, 4, 5 };
        int[,] rgb = PaletteBuilder.ToDllPalette(colors, setColor: 99, offset: 2, reverse: false, out int n);
        Assert.Equal(5, n);
        Assert.Equal(new[] { 3, 4, 5, 1, 2, 99 }, Reds(rgb, n));

        rgb = PaletteBuilder.ToDllPalette(colors, setColor: 99, offset: 2, reverse: true, out n);
        Assert.Equal(new[] { 3, 2, 1, 5, 4, 99 }, Reds(rgb, n));

        rgb = PaletteBuilder.ToDllPalette(new[] { 7 }, setColor: 99, offset: 3, reverse: true, out n);
        Assert.Equal(2, n); // a single color is duplicated, with no offset or reversal
        Assert.Equal(new[] { 7, 7, 99 }, Reds(rgb, n));
    }

    private static int[] Reds(int[,] rgb, int n)
    {
        var r = new int[n + 1];
        for (int i = 1; i <= n + 1; i++) r[i - 1] = rgb[i, 1];
        return r;
    }
}
