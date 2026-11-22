// Part of the unofficial Fraqcel host prototype. The presets are Fraqcel 2.15.0's (DATA!G24:M47)
// and the palette arithmetic is ported from its VBA (ST.cls colorBtn_Click, plotCode.bas createBmp),
// Copyright (c) 2026 Kevin Orloske, BSD-2-Clause (see NOTICES.md).

using System;
using System.Collections.Generic;
using System.Linq;

namespace Fraqcel.Engine;

/// <summary>A Fraqcel palette preset. Colors are Excel color values: red + 256 * green + 65536 * blue.</summary>
public sealed class PalettePreset
{
    public PalettePreset(string name, int bands, int setColor, params int[] keyColors)
    {
        Name = name;
        Bands = bands;
        SetColor = setColor;
        KeyColors = keyColors;
    }

    public string Name { get; }
    public int Bands { get; }
    public int SetColor { get; }
    public IReadOnlyList<int> KeyColors { get; }

    public override string ToString() => Name;
}

public static class PalettePresets
{
    public static readonly PalettePreset Default = new("Default", 20, 0,
        6684672, 16750950, 13434879, 13209);

    public static readonly PalettePreset PseudorandomChromaticity = new("Pseudorandom Chromaticity", 20, 0,
        5511698, 15491422, 7258820, 4127409, 16718728, 60144, 13376025, 4918088, 4679418, 3514963, 9378157,
        14018993, 6277852, 4670373, 11954536, 3514963, 10527211, 15371013, 935271, 3474645, 9734324);

    public static readonly PalettePreset Grayscale = new("Grayscale", 40, 0,
        1842204, 16316664);

    public static readonly PalettePreset DarkRainbow = new("Dark Rainbow", 20, 16777215,
        0, 6684774, 6684672, 13158, 6697728, 3342438, 13056);

    public static readonly PalettePreset BlueWaves = new("Blue Waves", 20, 0,
        15580012, 15138815, 41469, 0, 9443331, 15580012, 15138815, 32768, 0, 9443331, 15580012, 16777215,
        13369548, 0, 9443331);

    public static readonly PalettePreset HighContrast = new("High Contrast", 4, 0,
        16777215, 204, 0, 49407, 16777215, 14834432, 0, 13158, 16777215, 28672, 0, 10498160);

    public static IReadOnlyList<PalettePreset> All { get; } = new[]
    {
        Default, PseudorandomChromaticity, Grayscale, DarkRainbow, BlueWaves, HighContrast,
    };

    public static PalettePreset Find(string name) =>
        All.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)
                                || string.Equals(p.Name.Replace(" ", ""), name.Replace(" ", ""), StringComparison.OrdinalIgnoreCase))
        ?? throw new ArgumentException("Unknown palette: " + name + ". Known: " + string.Join(", ", All.Select(p => p.Name)));
}

public static class PaletteBuilder
{
    /// <summary>
    /// colorBtn_Click: the colors between each pair of key colors (wrapping from the last to the
    /// first). Copied as written: <c>numBand</c> is incremented first, then <c>d1 = j * (1 / numBand)</c>
    /// and each channel is assigned to a VBA Integer (banker's rounding).
    /// </summary>
    public static int[] Build(IReadOnlyList<int> keyColors, int bands)
    {
        if (keyColors.Count == 0) throw new ArgumentException("A palette needs at least one key color.");
        int numKey = keyColors.Count;
        if (numKey == 1) return new[] { keyColors[0] };
        int numBand = bands + 1;
        var colors = new List<int>(numKey * numBand);
        for (int i = 1; i <= numKey; i++)
        {
            int k1 = keyColors[i - 1];
            int k2 = i != numKey ? keyColors[i] : keyColors[0];
            int r1 = Red(k1), r2 = Red(k2), g1 = Green(k1), g2 = Green(k2), b1 = Blue(k1), b2 = Blue(k2);
            for (int j = 0; j <= numBand - 1; j++)
            {
                double d1 = j * (1.0 / numBand);
                int r = Vba.CInt(r2 * d1 + r1 * (1 - d1));
                int g = Vba.CInt(g2 * d1 + g1 * (1 - d1));
                int b = Vba.CInt(b2 * d1 + b1 * (1 - d1));
                colors.Add(Rgb(r, g, b));
            }
        }
        return colors.ToArray();
    }

    /// <summary>
    /// createBmp: the DLL's palette array, 1-based [1..numColors + 1, 1..3] holding red, green,
    /// blue. Applies the offset, the reversal (offset after reversing) and the set color, which
    /// goes in the last entry.
    /// </summary>
    public static int[,] ToDllPalette(IReadOnlyList<int> colors, int setColor, int offset, bool reverse, out int numColors)
    {
        numColors = colors.Count;
        bool single = numColors == 1;
        if (single) numColors = 2; // a single color is duplicated
        if (offset < 0 || offset > 32767 || single) offset = 0;
        if (offset >= numColors) offset %= numColors;

        int Color(int i) => i >= 1 && i <= colors.Count ? colors[i - 1] : 0; // ST column B, 1-based
        var palette = new int[numColors + 2];
        int j = 1;
        if (!reverse || single)
        {
            for (int i = 1 + offset; i <= numColors; i++) palette[j++] = Color(i);
            for (int i = 1; i <= offset; i++) palette[j++] = Color(i);
        }
        else
        {
            for (int i = numColors - offset; i >= 1; i--) palette[j++] = Color(i);
            for (int i = numColors; i >= numColors + 1 - offset; i--) palette[j++] = Color(i);
        }
        if (single) palette[2] = palette[1];
        palette[numColors + 1] = setColor;

        var rgb = (int[,])Array.CreateInstance(typeof(int), new[] { numColors + 1, 3 }, new[] { 1, 1 });
        for (int i = 1; i <= numColors + 1; i++)
        {
            rgb[i, 1] = Red(palette[i]);
            rgb[i, 2] = Green(palette[i]);
            rgb[i, 3] = Blue(palette[i]);
        }
        return rgb;
    }

    public static int Red(int color) => color % 256;
    public static int Green(int color) => color / 256 % 256;
    public static int Blue(int color) => color / 65536 % 256;
    public static int Rgb(int r, int g, int b) => r + 256 * g + 65536 * b;
}
