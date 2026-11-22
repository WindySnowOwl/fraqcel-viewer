// Part of the unofficial Fraqcel host prototype. Behavior ported from Fraqcel 2.15.0's VBA,
// Copyright (c) 2026 Kevin Orloske, BSD-2-Clause (see NOTICES.md).

using System;
using System.Globalization;
using System.Runtime.InteropServices;

namespace Fraqcel.Engine;

/// <summary>
/// VBA's numeric conversions, so ported code rounds exactly where Fraqcel's VBA rounds.
/// </summary>
public static class Vba
{
    /// <summary>Assignment to a VBA Integer: banker's rounding, overflow outside 16 bits.</summary>
    public static int CInt(double x)
    {
        double r = Math.Round(x, MidpointRounding.ToEven);
        if (double.IsNaN(r) || r < short.MinValue || r > short.MaxValue)
            throw new OverflowException("VBA Integer overflow: " + x.ToString("R", CultureInfo.InvariantCulture));
        return (int)r;
    }

    /// <summary>Assignment to a VBA Long: banker's rounding, overflow outside 32 bits.</summary>
    public static int CLng(double x)
    {
        double r = Math.Round(x, MidpointRounding.ToEven);
        if (double.IsNaN(r) || r < int.MinValue || r > int.MaxValue)
            throw new OverflowException("VBA Long overflow: " + x.ToString("R", CultureInfo.InvariantCulture));
        return (int)r;
    }

    /// <summary>VBA's Int(): rounds toward minus infinity.</summary>
    public static double Int(double x) => Math.Floor(x);

    /// <summary>
    /// <c>Log(x) / Log(10#)</c> as the VBA writes it. Not Math.Log10: the two can differ in the
    /// last bit at exact powers of ten, which changes Int() of the result.
    /// </summary>
    public static double Log10(double x) => Math.Log(x) / Math.Log(10.0);

    // US English, ignoring any user overrides of the decimal symbol.
    private const uint LcidEnUs = 0x0409;
    private const uint LocaleNoUserOverride = 0x80000000;

    [DllImport("oleaut32.dll", ExactSpelling = true)]
    private static extern int VarBstrFromR8(double dblIn, uint lcid, uint dwFlags,
        [MarshalAs(UnmanagedType.BStr)] out string pbstrOut);

    [DllImport("oleaut32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int VarR8FromStr(string strIn, uint lcid, uint dwFlags, out double pdblOut);

    /// <summary>
    /// VBA's CStr of a Double: OLE Automation's 15-significant-digit conversion
    /// (for example 4/4.5e31 gives "8.88888888888889E-32"). This is how XNUMBERS receives doubles.
    /// </summary>
    public static string CStr(double x)
    {
        int hr = VarBstrFromR8(x, LcidEnUs, LocaleNoUserOverride, out string s);
        if (hr != 0) throw new FormatException("VarBstrFromR8 failed, HRESULT 0x" + hr.ToString("X8"));
        return s;
    }

    /// <summary>
    /// VBA's coercion of a numeric string to a Double (OLE Automation's parser). Fraqcel's
    /// double-precision path coerces the center text this way. Falls back to .NET's parser when
    /// the OLE parser refuses the text.
    /// </summary>
    public static double CDbl(string s)
    {
        int hr = VarR8FromStr(s.Trim(), LcidEnUs, LocaleNoUserOverride, out double d);
        if (hr == 0) return d;
        return double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
    }
}
