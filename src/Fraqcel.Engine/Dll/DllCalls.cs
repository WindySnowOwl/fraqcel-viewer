// Part of the unofficial Fraqcel host prototype.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using FraqcelLib;

namespace Fraqcel.Engine.Dll;

/// <summary>
/// Typed calls into the stock DLL's <c>MainClass</c>, with named arguments, so the two different
/// box-bound orders (perturbation: rr1, rr2, cc1, cc2; multi-precision: rr1, cc1, rr2, cc2) can't
/// be mixed up. Nothing here may run before <see cref="FraqcelDll.Load"/>.
/// </summary>
internal static class DllCalls
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static MainClass Main()
    {
        FraqcelDll.EnsureLoaded();
        return new MainClass();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Dbl(ref double[,] imageData, string typeOption, double eqnParameter,
        bool flagSmooth, bool flagSac, bool flagFixedRange, double sDen, int flagDllOption,
        double zr, double zi, double scaleTerm, int res, int maxIter, int refreshType, int prevMaxIter,
        int r1, int c1, int navSize, int rSize, int cSize)
    {
        var m = Main();
        if (!flagSac)
            m.fraqcelDbl(ref imageData, typeOption, eqnParameter, flagSmooth, flagDllOption, zr, zi, scaleTerm,
                res, maxIter, refreshType, prevMaxIter, r1, c1, navSize, rSize, cSize);
        else
            m.fraqcelDbl_SAC(ref imageData, typeOption, eqnParameter, flagFixedRange, sDen, flagDllOption, zr, zi,
                scaleTerm, res, maxIter, refreshType, prevMaxIter, r1, c1, navSize, rSize, cSize);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void MP(ref double[,] imageData, string typeOption, double eqnParameter,
        bool flagSmooth, bool flagSac, bool flagFixedRange, double sDen, int flagDllOption,
        string zr, string zi, string pixelStep, int res, int maxIter, int refreshType, int prevMaxIter,
        int r1, int c1, int navSize, int rr1, int cc1, int rr2, int cc2, ref int numPointsMP, int dgMx)
    {
        var m = Main();
        if (!flagSac)
            m.fraqcel_MP(ref imageData, typeOption, eqnParameter, flagSmooth, flagDllOption, zr, zi, pixelStep,
                res, maxIter, refreshType, prevMaxIter, r1, c1, navSize,
                rr1: rr1, cc1: cc1, rr2: rr2, cc2: cc2, numPointsMP: ref numPointsMP, DgMx: dgMx);
        else
            m.fraqcel_MP_SAC(ref imageData, typeOption, eqnParameter, flagFixedRange, sDen, flagDllOption, zr, zi,
                pixelStep, res, maxIter, refreshType, prevMaxIter, r1, c1, navSize,
                rr1: rr1, cc1: cc1, rr2: rr2, cc2: cc2, numPointsMP: ref numPointsMP, DgMx: dgMx);
    }

    /// <summary>Perturbation (series off) or series approximation (SD or HF), one pass.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Pert(bool series, bool flagHF, ref double[,] imageData, ref int[,] glitchFlag, ref bool flagGlitch,
        string typeOption, double eqnParameter, ref int numSkip,
        bool flagSmooth, bool flagSac, bool flagFixedRange, double sDen, int flagDllOption,
        string x0, string y0, double scaleTerm, int res, int maxIter, int refreshType, int prevMaxIter,
        int dr, int dc, int navSize, int rr1, int rr2, int cc1, int cc2, int rRef, int cRef, ref int numPointsMP,
        bool flagRebasing, double seriesTolerance, double divThreshold, bool flagDebugGlitch, int maxPastRef,
        double glitchThreshold, int dgMx, int[,]? prevGlitchFlag, int prevGlitchInfo2)
    {
        var m = Main();
        if (!series && !flagSac)
            m.fraqcelPert(ref imageData, ref glitchFlag, ref flagGlitch, typeOption, eqnParameter, flagSmooth,
                flagDllOption, x0, y0, scaleTerm, res, maxIter, refreshType, prevMaxIter, dr, dc, navSize,
                rr1: rr1, rr2: rr2, cc1: cc1, cc2: cc2, rRef: rRef, cRef: cRef, numPointsMP: ref numPointsMP,
                flagRebasing: flagRebasing, divThreshold: divThreshold, flagDebugGlitch: flagDebugGlitch,
                maxPastRef: maxPastRef, glitchThreshold: glitchThreshold, DgMx: dgMx, prevGlitchFlag: prevGlitchFlag);
        else if (!series)
            m.fraqcelPert_SAC(ref imageData, ref glitchFlag, ref flagGlitch, typeOption, eqnParameter, flagFixedRange,
                sDen, flagDllOption, x0, y0, scaleTerm, res, maxIter, refreshType, prevMaxIter, dr, dc, navSize,
                rr1: rr1, rr2: rr2, cc1: cc1, cc2: cc2, rRef: rRef, cRef: cRef, numPointsMP: ref numPointsMP,
                flagRebasing: flagRebasing, divThreshold: divThreshold, flagDebugGlitch: flagDebugGlitch,
                maxPastRef: maxPastRef, glitchThreshold: glitchThreshold, DgMx: dgMx, prevGlitchFlag: prevGlitchFlag);
        else if (!flagSac)
            m.fraqcelSeries(ref imageData, ref glitchFlag, ref flagGlitch, typeOption, eqnParameter, ref numSkip, flagHF,
                flagSmooth, flagDllOption, x0, y0, scaleTerm, res, maxIter, refreshType, prevMaxIter, dr, dc, navSize,
                rr1: rr1, rr2: rr2, cc1: cc1, cc2: cc2, rRef: rRef, cRef: cRef, numPointsMP: ref numPointsMP,
                flagRebasing: flagRebasing, seriesTolerance: seriesTolerance, divThreshold: divThreshold,
                flagDebugGlitch: flagDebugGlitch, maxPastRef: maxPastRef, glitchThreshold: glitchThreshold, DgMx: dgMx,
                prevGlitchFlag: prevGlitchFlag, prevGlitchInfo2: prevGlitchInfo2);
        else
            m.fraqcelSeries_SAC(ref imageData, ref glitchFlag, ref flagGlitch, typeOption, eqnParameter, ref numSkip, flagHF,
                flagFixedRange, sDen, flagDllOption, x0, y0, scaleTerm, res, maxIter, refreshType, prevMaxIter, dr, dc,
                navSize, rr1: rr1, rr2: rr2, cc1: cc1, cc2: cc2, rRef: rRef, cRef: cRef, numPointsMP: ref numPointsMP,
                flagRebasing: flagRebasing, seriesTolerance: seriesTolerance, divThreshold: divThreshold,
                flagDebugGlitch: flagDebugGlitch, maxPastRef: maxPastRef, glitchThreshold: glitchThreshold, DgMx: dgMx,
                prevGlitchFlag: prevGlitchFlag, prevGlitchInfo2: prevGlitchInfo2);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void BmpPixels(double[,] imageData, ref byte[] pixels, int[,] paletteRgb, int numColors,
        bool flagSmooth, bool flagBump, bool flagFixedRange, int maxIter, int rSize, int cSize, int res,
        int rowPadding, double divideIter, double lx, double ly, double lz, double nz2, double nzLz, double sD, double sL)
    {
        Main().getBmpPixels(imageData, ref pixels, paletteRgb, numColors, flagSmooth, flagBump, flagFixedRange, maxIter,
            rSize, cSize, res, borderSize: 0, rowPadding: rowPadding, divideIter: divideIter,
            Lx: lx, Ly: ly, Lz: lz, Nz2: nz2, NzLz: nzLz, sD: sD, sL: sL);
    }

    /// <summary>
    /// Routes the DLL's progress text to <paramref name="onMessage"/> for the duration of one call,
    /// and records when each message arrived. The DLL calls the sink on the calling thread while
    /// its worker threads wait, so the sink only records.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static IDisposable CaptureProgress(Action<string>? onMessage, List<(double Ms, string Text)> log)
    {
        var sw = Stopwatch.StartNew();
        HostStatus.Sink = text =>
        {
            log.Add((sw.Elapsed.TotalMilliseconds, text));
            onMessage?.Invoke(text);
        };
        return new SinkReset();
    }

    private sealed class SinkReset : IDisposable
    {
        public void Dispose() => ClearSink();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ClearSink() => HostStatus.Sink = null!;
}
