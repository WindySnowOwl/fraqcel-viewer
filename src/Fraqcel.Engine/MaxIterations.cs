// Part of the unofficial Fraqcel host prototype. Ported from Fraqcel 2.15.0's VBA
// (WS.cls iterPosBtn_Click and iterNegBtn_Click), Copyright (c) 2026 Kevin Orloske, BSD-2-Clause
// (see NOTICES.md).

using System.Globalization;

namespace Fraqcel.Engine;

/// <summary>Fraqcel's x2 and /2 buttons next to the max iterations box. They change the box, not the view.</summary>
public static class MaxIterations
{
    /// <summary>
    /// iterPosBtn_Click: the box's value doubled, clipped to 10,000,000. Text that isn't a number
    /// of at least 1 goes back to <paramref name="current"/>, the rendered view's max iterations.
    /// A value above the limit is clipped without doubling.
    /// </summary>
    public static int Double(string text, int current)
    {
        if (!TryRead(text, out double v) || v < 1) return current;
        if (v > Location.MaxIterationsLimit) return Location.MaxIterationsLimit;
        long doubled = 2L * Vba.CLng(v);
        return doubled > Location.MaxIterationsLimit ? Location.MaxIterationsLimit : (int)doubled;
    }

    /// <summary>
    /// iterNegBtn_Click: the box's value halved, rounded as VBA assigns to a Long (half to even),
    /// and kept between 1 and 10,000,000. Text that isn't a number of at least 1 goes back to
    /// <paramref name="current"/>. Fraqcel's own check here lets values up to 20,000,000 through
    /// to be halved; anything above that becomes 10,000,000.
    /// </summary>
    public static int Halve(string text, int current)
    {
        if (!TryRead(text, out double v) || v < 1) return current;
        if (v > 2.0 * Location.MaxIterationsLimit) return Location.MaxIterationsLimit;
        int halved = Vba.CLng(Vba.CLng(v) / 2.0);
        if (halved > Location.MaxIterationsLimit) halved = Location.MaxIterationsLimit;
        return halved < 1 ? 1 : halved;
    }

    private static bool TryRead(string text, out double v) =>
        double.TryParse((text ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v) &&
        !double.IsNaN(v) && !double.IsInfinity(v);
}
