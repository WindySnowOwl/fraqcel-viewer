// Part of the unofficial Fraqcel host prototype. The render flow is ported from Fraqcel 2.15.0's
// VBA (coreCode.bas calcAndPlot; eq_general.bas generalDbl_C, generalXnum_C, generalPert_C,
// generalSeries_C, getXnumCoord, getRefCoord), Copyright (c) 2026 Kevin Orloske, BSD-2-Clause.

using System;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading;
using Fraqcel.Engine.Dll;
using PeterO.Numbers;

namespace Fraqcel.Engine;

/// <summary>
/// Renders iteration data with the stock DLL, the way Fraqcel's VBA drives it.
/// One render at a time per process: the DLL's progress hook is global.
/// </summary>
public sealed class Renderer
{
    private const int Res = 1; // reduced resolution (50%, 33%) is not in the prototype

    private static readonly object OneAtATime = new();

    /// <summary>
    /// Renders <paramref name="location"/>. Cancellation is checked between DLL calls, so a
    /// render stops after the glitch-correction pass that is running.
    /// </summary>
    public RenderResult Render(Location location, RenderSettings settings, Geometry geometry,
        IProgress<string>? progress = null, CancellationToken cancel = default)
    {
        FraqcelDll.EnsureLoaded();
        lock (OneAtATime)
        {
            var s = settings.Clone();
            var job = new Job(location, s, geometry, progress, cancel);
            job.Result.Warnings.AddRange(s.Normalize());
            job.Run();
            return job.Result;
        }
    }

    /// <summary>The state of one render (calcAndPlot's locals).</summary>
    private sealed class Job
    {
        private readonly Location loc;
        private readonly RenderSettings s;
        private readonly Geometry geo;
        private readonly IProgress<string>? progress;
        private readonly CancellationToken cancel;
        private readonly string type;
        private readonly double eqnParameter;
        private double[,] data;

        public Job(Location loc, RenderSettings s, Geometry geo, IProgress<string>? progress, CancellationToken cancel)
        {
            this.loc = loc;
            this.s = s;
            this.geo = geo;
            this.progress = progress;
            this.cancel = cancel;
            type = FractalTypes.Code(s.Type);
            eqnParameter = s.Type == FractalType.RotationFamily ? s.Parameter : 0;
            data = VbaArrays.Double2D(geo.RSize, geo.CSize);
            Result = new RenderResult(data, loc, s, geo);
        }

        public RenderResult Result { get; }

        private int RSize => geo.RSize;
        private int CSize => geo.CSize;
        private int MaxIter => loc.MaxIter;

        public void Run()
        {
            if (type == "r" && eqnParameter == 0)
                throw new ArgumentException("The Rotation Family parameter must not be zero.");

            var total = Stopwatch.StartNew();
            double scaleTest = loc.PlotScale / (geo.ScaleSize - 1);
            int xDigit = Precision.XDigit(scaleTest, s.Precision.ExtraDigits);
            var mode = Precision.Choose(scaleTest, s.Precision);

            // calcAndPlot: "prevent potential overflow error" (a signed comparison, as in the VBA).
            if (loc.CenterReAsDouble > 1e150 || loc.CenterImAsDouble > 1e150 || loc.PlotScale > 1e150)
                mode = PrecisionMode.Double;

            // Burning Ship, Buffalo, Celtic and Perpendicular Burning Ship have no series in the
            // DLL path: calcAndPlot sends any multi-precision mode above plain MP to generalPert_C.
            if (mode >= PrecisionMode.SeriesSD && !FractalTypes.HasSeries(s.Type))
                mode = PrecisionMode.Perturbation;

            Result.Mode = mode;
            Result.XDigit = mode == PrecisionMode.Double ? 0 : xDigit;
            Result.DgMx = mode == PrecisionMode.Double ? 0 : Precision.DgMx(xDigit);

            switch (mode)
            {
                case PrecisionMode.Double: RenderDouble(); break;
                case PrecisionMode.MultiPrecision: RenderMultiPrecision(Result.DgMx); break;
                default: RenderPerturbation(mode, Result.DgMx); break;
            }

            // Result checks (design 5.10), on the full sample buffer before any blending.
            Result.NonFinite = CountNonFinite(data);
            Result.EngineMs = total.Elapsed.TotalMilliseconds;

            if (geo.SampleFactor > 1)
            {
                var sw = Stopwatch.StartNew();
                data = Supersampling.Blend(data, geo.SampleFactor, MaxIter, s.FlagSmooth, s.FlagFixedRange);
                Result.SupersampleMs = sw.Elapsed.TotalMilliseconds;
            }
            Result.Data = data;
        }

        // ---- double precision (generalDbl_C) ------------------------------------------------

        private void RenderDouble()
        {
            double plotScale = loc.PlotScale;
            double plotScaleRe = geo.PlotScaleRe(plotScale);
            double plotScaleIm = geo.PlotScaleIm(plotScale);
            double zr = loc.CenterReAsDouble - (plotScaleRe / 2.0); // min real position (1st column)
            double zi = loc.CenterImAsDouble + (plotScaleIm / 2.0); // max imaginary position (1st row)
            double scaleTerm = plotScale / (geo.ScaleSize - 1);

            var pass = StartPass(0, 0, (2, 2, RSize + 1, CSize + 1));
            var sw = Stopwatch.StartNew();
            using (DllCalls.CaptureProgress(Report(pass), pass.Progress))
                DllCalls.Dbl(ref data, type, eqnParameter, s.FlagSmooth, s.FlagSac, s.FlagFixedRange, s.StripeDensity,
                    s.FlagDllOption, zr, zi, scaleTerm, Res, MaxIter, geo.RefreshType, prevMaxIter: 0, r1: 0, c1: 0,
                    navSize: s.NavSize, rSize: RSize, cSize: CSize);
            pass.TotalMs = sw.Elapsed.TotalMilliseconds;
        }

        // ---- plain multi-precision (generalXnum_C, getXnumCoord) -------------------------------

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void RenderMultiPrecision(int dgMx)
        {
            var x = new Xnum(dgMx);
            double plotScale = loc.PlotScale;
            EDecimal a1 = x.Cvt(geo.PlotScaleRe(plotScale));
            EDecimal a2 = x.Cvt(2);
            EDecimal a3 = x.Center(loc.CenterRe, loc.CenterReValue);
            EDecimal a4 = x.Center(loc.CenterIm, loc.CenterImValue);
            EDecimal a5 = x.Div(a1, a2);           // plotScaleRe / 2
            EDecimal a6 = x.Sub(a3, a5);           // zr = centerRe - plotScaleRe / 2
            a1 = x.Cvt(geo.PlotScaleIm(plotScale));
            a5 = x.Div(a1, a2);                    // plotScaleIm / 2
            EDecimal a7 = x.Add(a4, a5);           // zi = centerIm + plotScaleIm / 2
            a1 = x.Cvt(plotScale);
            a2 = x.Cvt(geo.ScaleSize - 1);
            EDecimal a8 = x.Div(a1, a2);           // pixelStep = plotScale / (scaleSize - 1)
            a5 = x.Cvt(Res);
            a8 = x.Mul(a8, a5);                    // pixelStep * res
            string zr = Xnum.ToStr(a6, dgMx), zi = Xnum.ToStr(a7, dgMx), pixelStep = Xnum.ToStr(a8, dgMx);

            int numPointsMP = 0;
            var pass = StartPass(0, 0, (2, 2, RSize + 1, CSize + 1));
            var sw = Stopwatch.StartNew();
            using (DllCalls.CaptureProgress(Report(pass), pass.Progress))
                DllCalls.MP(ref data, type, eqnParameter, s.FlagSmooth, s.FlagSac, s.FlagFixedRange, s.StripeDensity,
                    s.FlagDllOption, zr, zi, pixelStep, Res, MaxIter, geo.RefreshType, prevMaxIter: 0, r1: 0, c1: 0,
                    navSize: s.NavSize, rr1: 2, cc1: 2, rr2: RSize + 1, cc2: CSize + 1, numPointsMP: ref numPointsMP,
                    dgMx: dgMx);
            pass.TotalMs = sw.Elapsed.TotalMilliseconds;
            Result.References = numPointsMP;
        }

        // ---- perturbation and series, with glitch correction (generalPert_C, generalSeries_C) ---

        private void RenderPerturbation(PrecisionMode mode, int dgMx)
        {
            bool series = mode is PrecisionMode.SeriesSD or PrecisionMode.SeriesHF;
            bool flagHF = mode == PrecisionMode.SeriesHF;
            var p = s.Precision;
            double divThreshold = p.DivThreshold * p.DivThreshold;
            double glitchThreshold = p.GlitchThreshold * p.GlitchThreshold;
            int maxCorrect = Vba.CInt(p.MaxReferences);
            int maxPastRef = Vba.CLng(p.MaxPastRef); // multiplied by the reference's iteration count in the DLL
            double scaleTerm = loc.PlotScale / (geo.ScaleSize - 1);

            // The VBA recursion as a loop. r1..c2 is the box to compute, rRef/cRef the reference pixel.
            int r1 = 2, c1 = 2, r2 = RSize + 1, c2 = CSize + 1;
            int rRef = (int)Vba.Int(2 + ((RSize + 1) - 2) / 2.0);
            int cRef = (int)Vba.Int(2 + ((CSize + 1) - 2) / 2.0);
            int numPointsMP = 0, numSkip = 0;
            int[,]? prevGlitchFlag = null;
            long[]? prevGlitchInfo = null;

            while (true)
            {
                cancel.ThrowIfCancellationRequested();

                RefCoord(dgMx, ref cRef, rRef, numPointsMP, out string x0, out string y0);
                int rr1 = r1 - rRef, cc1 = c1 - cRef, rr2 = r2 - rRef, cc2 = c2 - cRef;
                int[,] glitchFlag = VbaArrays.Int2D(r1 - Res, r2, c1 - Res, c2 + Res); // column and top-row buffers
                bool flagGlitch = false;
                bool hasPrev = numPointsMP > 0; // numPointsMP is incremented in the DLL

                var pass = StartPass(rRef, cRef, (r1, c1, r2, c2));
                var sw = Stopwatch.StartNew();
                using (DllCalls.CaptureProgress(Report(pass), pass.Progress))
                    DllCalls.Pert(series, flagHF, ref data, ref glitchFlag, ref flagGlitch, type, eqnParameter, ref numSkip,
                        s.FlagSmooth, s.FlagSac, s.FlagFixedRange, s.StripeDensity, s.FlagDllOption, x0, y0, scaleTerm,
                        Res, MaxIter, geo.RefreshType, prevMaxIter: 0, dr: 0, dc: 0, navSize: s.NavSize,
                        rr1: rr1, rr2: rr2, cc1: cc1, cc2: cc2, rRef: rRef, cRef: cRef, numPointsMP: ref numPointsMP,
                        flagRebasing: p.Rebasing, seriesTolerance: p.SeriesTolerance, divThreshold: divThreshold,
                        flagDebugGlitch: p.DebugGlitches, maxPastRef: maxPastRef, glitchThreshold: glitchThreshold,
                        dgMx: dgMx, prevGlitchFlag: hasPrev ? prevGlitchFlag : null,
                        prevGlitchInfo2: hasPrev && series ? Vba.CLng(prevGlitchInfo![2]) : 0);
                pass.TotalMs = sw.Elapsed.TotalMilliseconds;
                if (pass.Progress.Count > 0) pass.RefMs = pass.Progress[0].Ms;
                pass.Glitched = CountFlagged(glitchFlag, r1, c1, r2, c2);
                Result.References = numPointsMP;
                Result.Skip = numSkip;

                // No need to find the largest glitch if no further reference points will be used.
                bool flagBlend = flagGlitch && numPointsMP == maxCorrect;

                if (!flagGlitch) break;
                if (p.DebugGlitches)
                {
                    Result.Unresolved = pass.Glitched; // "set glitches to max iteration and do not correct"
                    break;
                }

                int[] roots = GlitchRegions.RegionLabeling(glitchFlag, Res);
                GlitchRegions.GetRange(glitchFlag, Res, out r1, out c1, out r2, out c2);
                int j = roots.Length - 1;
                if (j == 0)
                {
                    Result.Warnings.Add("the DLL reported glitches but flagged no pixel");
                    break;
                }
                int k = roots[j];
                var glitchSize = new long[k + 1];
                var rRef2 = new int[k + 1];
                var cRef2 = new int[k + 1];
                long largestGlitchSize = 0;
                int largestGlitchLabel = 0;
                GlitchRegions.GetSize(glitchFlag, Res, r1, c1, r2, c2, rRef2, cRef2, glitchSize);
                var glitchInfo = new long[4];
                if (!flagBlend)
                    GlitchRegions.GetGlitchInfo(ref largestGlitchSize, ref largestGlitchLabel, glitchInfo, glitchSize, j, k);
                pass.Regions = j;
                for (int i = 1; i <= k; i++) if (glitchSize[i] > pass.LargestRegion) pass.LargestRegion = glitchSize[i];

                // Blend single-pixel glitches and stop.
                if (largestGlitchSize == 1 || flagBlend)
                {
                    for (int i = 1; i <= k; i++)
                    {
                        if (glitchSize[i] == 1)
                        {
                            try
                            {
                                GlitchRegions.AveragePixel(data, rRef2[i], cRef2[i], Res);
                                Result.Blended++;
                            }
                            catch (OverflowException)
                            {
                                // A NaN neighbor: the VBA would stop with an error here.
                                Result.Unresolved++;
                            }
                        }
                        else if (glitchSize[i] > 1)
                        {
                            Result.Unresolved += glitchSize[i];
                        }
                    }
                    break;
                }

                // Correct the largest glitch with a new reference point.
                if (numPointsMP > 1 && prevGlitchInfo![1] == glitchInfo[1] && prevGlitchInfo[2] == glitchInfo[2])
                {
                    // The last attempt made minimal to no impact: try a corner instead.
                    glitchInfo[3] = prevGlitchInfo[3] + 1;
                    GlitchRegions.GetCorner(glitchFlag, largestGlitchLabel, Res, r1, c1, r2, c2, out rRef, out cRef, glitchInfo[3]);
                }
                else
                {
                    GlitchRegions.GetCentroid(glitchFlag, largestGlitchLabel, Res, r1, c1, r2, c2, out rRef, out cRef);
                }
                prevGlitchFlag = glitchFlag;
                prevGlitchInfo = glitchInfo;
            }
        }

        /// <summary>getRefCoord: the reference pixel's coordinates as strings.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private void RefCoord(int dgMx, ref int cRef, int rRef, int numPointsMP, out string x0, out string y0)
        {
            var x = new Xnum(dgMx);
            double plotScale = loc.PlotScale;
            EDecimal a1 = x.Cvt(geo.PlotScaleRe(plotScale));
            EDecimal a2 = x.Cvt(2);
            EDecimal a3 = x.Center(loc.CenterRe, loc.CenterReValue);
            EDecimal a4 = x.Center(loc.CenterIm, loc.CenterImValue);
            if (numPointsMP == 0)
            {
                // If centerRe <= -2, shift the first reference to the right side: improves zooms into (-2, 0).
                if (Xnum.Compare(a3, x.Cvt(-2)) != 1) cRef = CSize;
            }
            EDecimal a5 = x.Div(a1, a2);
            EDecimal a6 = x.Sub(a3, a5);           // zr = centerRe - plotScaleRe / 2
            a1 = x.Cvt(geo.PlotScaleIm(plotScale));
            a5 = x.Div(a1, a2);
            EDecimal a7 = x.Add(a4, a5);           // zi = centerIm + plotScaleIm / 2
            a1 = x.Cvt(plotScale);
            a2 = x.Cvt(geo.ScaleSize - 1);
            EDecimal a8 = x.Div(a1, a2);           // pixelStep
            a1 = x.Cvt(cRef - 2);
            a2 = x.Mul(a1, a8);
            a6 = x.Add(a6, a2);                    // zr + (cRef - 2) * pixelStep
            a1 = x.Cvt(rRef - 2);
            a2 = x.Mul(a1, a8);
            a7 = x.Sub(a7, a2);                    // zi - (rRef - 2) * pixelStep
            x0 = Xnum.ToStr(a6, dgMx);
            y0 = Xnum.ToStr(a7, dgMx);
        }

        // ---- helpers ---------------------------------------------------------------------------

        private PassInfo StartPass(int rRef, int cRef, (int, int, int, int) box)
        {
            var pass = new PassInfo { Index = Result.Passes.Count + 1, RefRow = rRef, RefCol = cRef, Box = box };
            Result.Passes.Add(pass);
            return pass;
        }

        private Action<string>? Report(PassInfo pass)
        {
            if (progress == null) return null;
            int n = pass.Index;
            return text => progress.Report(n == 1 ? text : "Pass " + n.ToString(CultureInfo.InvariantCulture) + ": " + text);
        }

        private static long CountFlagged(int[,] glitchFlag, int r1, int c1, int r2, int c2)
        {
            long n = 0;
            for (int r = r1; r <= r2; r += Res)
                for (int c = c1; c <= c2; c += Res)
                    if (glitchFlag[r, c] != 0) n++;
            return n;
        }

        private static long CountNonFinite(double[,] a)
        {
            long n = 0;
            foreach (double v in a)
                if (double.IsNaN(v) || double.IsInfinity(v)) n++;
            return n;
        }
    }
}
