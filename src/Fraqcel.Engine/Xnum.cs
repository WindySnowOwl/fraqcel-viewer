// Part of the unofficial Fraqcel host prototype.

using System;
using System.Runtime.CompilerServices;
using PeterO.Numbers;

namespace Fraqcel.Engine;

/// <summary>
/// PROTOTYPE STAND-IN for Fraqcel's XNUMBERS arithmetic.
/// <para>
/// It uses the DLL's own EDecimal at DgMx digits with half-up rounding, and enters doubles as
/// VBA's 15-digit text, as XNUMBERS does. That is close to XNUMBERS but not proven identical:
/// XNUMBERS has its own guard-digit rules per operation, so the last digit can differ
/// (design 5.3). The exact port, gated on identical strings, is milestone M0 work.
/// </para>
/// </summary>
internal sealed class Xnum
{
    private readonly EContext ctx;

    public Xnum(int dgMx)
    {
        DgMx = dgMx;
        ctx = EContext.ForPrecision(dgMx).WithRounding(ERounding.HalfUp);
    }

    public int DgMx { get; }

    /// <summary>Cvt2xNum of a Double: whole numbers exactly, anything else through VBA's CStr.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public EDecimal Cvt(double d)
    {
        if (d == Math.Floor(d) && Math.Abs(d) < 1e7) return EDecimal.FromInt64((long)d);
        return EDecimal.FromString(Vba.CStr(d), ctx);
    }

    /// <summary>Cvt2xNum of a VBA Integer or Long.</summary>
    public EDecimal Cvt(int i) => EDecimal.FromInt32(i);

    /// <summary>Cvt2xNum of a String: rounded half-up to DgMx digits on entry.</summary>
    public EDecimal Cvt(string s) => EDecimal.FromString(s, ctx);

    /// <summary>A center coordinate: a stored Double goes through CStr, text is parsed.</summary>
    public EDecimal Center(string text, double? value) => value is double d ? Cvt(d) : Cvt(text);

    public EDecimal Add(EDecimal a, EDecimal b) => a.Add(b, ctx);
    public EDecimal Sub(EDecimal a, EDecimal b) => a.Subtract(b, ctx);
    public EDecimal Mul(EDecimal a, EDecimal b) => a.Multiply(b, ctx);
    public EDecimal Div(EDecimal a, EDecimal b) => a.Divide(b, ctx);

    /// <summary>
    /// CvtxNum2str: the value as text, rounded half-up to <paramref name="digits"/> significant
    /// digits, without trailing zeros.
    /// </summary>
    public static string ToStr(EDecimal x, int digits)
    {
        var ctx = EContext.ForPrecision(digits).WithRounding(ERounding.HalfUp);
        return x.RoundToPrecision(ctx).Reduce(ctx).ToString();
    }

    /// <summary>xComp_: the sign of a - b.</summary>
    public static int Compare(EDecimal a, EDecimal b) => a.CompareTo(b);
}
