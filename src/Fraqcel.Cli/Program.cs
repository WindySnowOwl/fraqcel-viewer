// Part of the unofficial Fraqcel host prototype.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using Fraqcel.Engine;
using Fraqcel.Engine.Dll;

namespace Fraqcel.Cli;

/// <summary>
/// fraqcel-cli: renders one image with the stock Fraqcel DLL and prints a key=value result line.
/// A subset of the design's CLI (section 6); see README-dev.md.
/// </summary>
internal static class Program
{
    // Exit codes (design 6.3).
    private const int Ok = 0, BadArgs = 2, OutOfCapability = 3, Failed = 4, OutOfMemory = 5,
        OutputFailed = 7, EngineMissing = 8, NotClean = 9;

    private static int Main(string[] args)
    {
        Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;

        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            Console.WriteLine(Usage);
            return args.Length == 0 ? BadArgs : Ok;
        }

        Options o;
        try
        {
            o = Options.Parse(args);
        }
        catch (ArgumentOutOfRangeException e) when (e.ParamName == "zoomText")
        {
            Console.Error.WriteLine("fraqcel-cli: " + e.Message);
            Console.WriteLine("fraqcel status=NA-past-its-ceiling");
            return OutOfCapability;
        }
        catch (Exception e) when (e is ArgumentException or FormatException or IOException)
        {
            Console.Error.WriteLine("fraqcel-cli: " + e.Message);
            Console.WriteLine("fraqcel status=NA-command-line");
            return BadArgs;
        }

        try
        {
            FraqcelDll.Load(o.DllPath, o.AllowUnknownDll);
        }
        catch (DllLoadException e)
        {
            Console.Error.WriteLine("fraqcel-cli: " + e.Message);
            Console.Error.WriteLine("fraqcel-cli: Fraqcel's DLLs are in the \"optional DLL files\\fraqcel\" folder of Fraqcel's download, " +
                                    FraqcelDll.DownloadPage + ". Copy " + FraqcelDll.FileNameX64 + " next to fraqcel-cli.exe, or pass --dll PATH.");
            Console.WriteLine("fraqcel status=DNF-engine-missing");
            return EngineMissing;
        }

        if (o.Command == "info") return Info();
        return Render(o);
    }

    private static int Info()
    {
        Console.WriteLine("host       fraqcel-cli " + typeof(Program).Assembly.GetName().Version + " (prototype)");
        Console.WriteLine("runtime    " + Environment.Version + (Environment.Is64BitProcess ? " x64" : " x86"));
        Console.WriteLine("dll        " + FraqcelDll.LoadedPath);
        Console.WriteLine("sha256     " + FraqcelDll.LoadedSha256 + (FraqcelDll.LoadedIsKnown ? " (known 2.15.0)" : " (UNKNOWN)"));
        Console.WriteLine("processors " + Environment.ProcessorCount);
        Console.WriteLine("os         " + Environment.OSVersion);
        return Ok;
    }

    private static int Render(Options o)
    {
        var wall = Stopwatch.StartNew();
        RenderResult result;
        try
        {
            var progress = o.Quiet ? null : new ConsoleProgress();
            result = new Renderer().Render(o.Location!, o.Settings, o.Geometry!, progress);
            progress?.Done();
        }
        catch (OutOfMemoryException e)
        {
            Console.Error.WriteLine("fraqcel-cli: out of memory: " + e.Message);
            Console.WriteLine("fraqcel status=DNF-out-of-memory");
            return OutOfMemory;
        }
        catch (ArgumentException e)
        {
            Console.Error.WriteLine("fraqcel-cli: " + e.Message);
            Console.WriteLine("fraqcel status=NA-past-its-ceiling");
            return OutOfCapability;
        }
        catch (Exception e)
        {
            var inner = e is AggregateException ae && ae.InnerExceptions.Count == 1 ? ae.InnerException! : e;
            if (inner is OutOfMemoryException)
            {
                Console.Error.WriteLine("fraqcel-cli: out of memory: " + inner.Message);
                Console.WriteLine("fraqcel status=DNF-out-of-memory");
                return OutOfMemory;
            }
            Console.Error.WriteLine("fraqcel-cli: render failed: " + inner);
            Console.WriteLine("fraqcel status=DNF-failed");
            return Failed;
        }

        double colorMs = 0, encodeMs = 0;
        if (o.OutPath != null)
        {
            try
            {
                using var bmp = Colorizer.ToBitmap(result, o.Colors, out colorMs);
                encodeMs = ImageWriter.SavePng(bmp, o.OutPath);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.ExternalException)
            {
                Console.Error.WriteLine("fraqcel-cli: could not write " + o.OutPath + ": " + e.Message);
                Console.WriteLine("fraqcel status=DNF-truncated " + result.Summary());
                return OutputFailed;
            }
        }
        if (o.RawPath != null) WriteRaw(result, o.RawPath);

        string status = result.Unresolved > 0 ? "DNF-unresolved-glitch" : result.NonFinite > 0 ? "DNF-nan" : "ok";
        var ci = CultureInfo.InvariantCulture;
        Console.WriteLine("fraqcel status=" + status + " " + result.Summary() +
                          " color_ms=" + colorMs.ToString("0", ci) + " encode_ms=" + encodeMs.ToString("0", ci) +
                          " wall_ms=" + wall.Elapsed.TotalMilliseconds.ToString("0", ci) +
                          " dll=" + FraqcelDll.LoadedSha256!.Substring(0, 8) + " known=" + (FraqcelDll.LoadedIsKnown ? 1 : 0) +
                          " runtime=net48/" + Environment.Version);
        foreach (string w in result.Warnings) Console.Error.WriteLine("warning: " + w);
        return result.IsClean ? Ok : NotClean;
    }

    /// <summary>The raw buffer: row-major float64, little-endian, rows x cols (for tests and comparisons).</summary>
    private static void WriteRaw(RenderResult r, string path)
    {
        using var w = new BinaryWriter(File.Create(path));
        for (int i = 1; i <= r.Rows; i++)
            for (int j = 1; j <= r.Cols; j++)
                w.Write(r.Data[i, j]);
    }

    private sealed class ConsoleProgress : IProgress<string>
    {
        private string last = "";
        public void Report(string value)
        {
            if (value == last) return;
            last = value;
            Console.Error.Write("\r" + value.PadRight(60));
        }
        public void Done() => Console.Error.WriteLine();
    }

    private const string Usage = @"fraqcel-cli (prototype): renders with Fraqcel's stock DLL, without Excel.

  fraqcel-cli render [options]
  fraqcel-cli info

Location (pick one way):
  --kfr FILE                 bench-kit scene file (Re, Im, Zoom, Iterations)
  --re S --im S              center as decimal text
  --plot-scale S | --zoom Z  Fraqcel's plot scale, or the kit's zoom (plot scale = 4/Z)
  --iter N                   max iterations (default 500, or the scene's)
  --fraqcel-default          Fraqcel's starting view (-0.5, 0, scale 4, 500 iterations)

Image:
  --size WxH                 output size (default: the 800x800 navigation image)
  --nav 800|596              render the square navigation image instead
  --samples 1|4|9|16|25      samples per pixel (default 1)
  --scale-over larger        the plot scale spans the larger side (default: smaller)

Fractal and coloring:
  --type m|b|f|c|r|t|p       fractal type (default m); --param P for Rotation Family
  --coloring discrete|smooth|stripe-fixed|stripe-iter   (default discrete, as Fraqcel ships)
  --stripe-density D         default 3
  --palette NAME             Default, ""Pseudorandom Chromaticity"", Grayscale, ""Dark Rainbow"", ""Blue Waves"", ""High Contrast""
  --bands N  --offset N  --divide X  --reverse  --bump

Precision (Fraqcel's defaults: perturbation on, SD series, no rebasing, 30 references):
  --no-perturbation  --series off|sd|hf  --rebasing  --force-mp  --threshold T
  --extra-digits N   --max-refs N  --debug-glitches

Run and output:
  --threads all|N            default all (no limit, as Fraqcel)
  --dll PATH                 the stock DLL (default: next to fraqcel-cli.exe)
  --allow-unknown-dll        run a DLL whose hash is not the known 2.15.0 hash
  --out FILE.png             write the image
  --raw FILE.f64             write the iteration buffer (row-major float64)
  --quiet                    no progress on stderr

Exit codes: 0 ok, 2 bad arguments, 3 outside capability, 4 failed, 5 out of memory,
7 output failed, 8 DLL missing or unknown, 9 rendered with unresolved glitches or NaN.";

    private sealed class Options
    {
        public string Command = "render";
        public Location? Location;
        public Geometry? Geometry;
        public RenderSettings Settings = new();
        public ColorSettings Colors = new();
        public string? DllPath;
        public bool AllowUnknownDll;
        public string? OutPath;
        public string? RawPath;
        public bool Quiet;

        public static Options Parse(string[] args)
        {
            var o = new Options { Command = args[0] };
            if (o.Command != "render" && o.Command != "info")
                throw new ArgumentException("Unknown command: " + o.Command);

            string? re = null, im = null, plotScale = null, zoom = null, kfr = null;
            int? iter = null, nav = null;
            int width = 0, height = 0, samples = 1;
            bool scaleOverLarger = false, fraqcelDefault = false;
            var ci = CultureInfo.InvariantCulture;

            for (int i = 1; i < args.Length; i++)
            {
                string a = args[i];
                string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException(a + " needs a value");
                double NextDouble() => double.Parse(Next(), NumberStyles.Float, ci);
                int NextInt() => int.Parse(Next(), NumberStyles.Integer, ci);
                switch (a)
                {
                    case "--kfr": kfr = Next(); break;
                    case "--re": re = Next(); break;
                    case "--im": im = Next(); break;
                    case "--plot-scale": plotScale = Next(); break;
                    case "--zoom": zoom = Next(); break;
                    case "--iter": iter = NextInt(); break;
                    case "--fraqcel-default": fraqcelDefault = true; break;
                    case "--size":
                        {
                            string[] p = Next().ToLowerInvariant().Split('x');
                            if (p.Length != 2) throw new ArgumentException("--size wants WxH");
                            width = int.Parse(p[0], ci);
                            height = int.Parse(p[1], ci);
                            break;
                        }
                    case "--nav": nav = NextInt(); break;
                    case "--samples": samples = NextInt(); break;
                    case "--scale-over":
                        scaleOverLarger = Next() switch
                        {
                            "larger" => true,
                            "smaller" => false,
                            _ => throw new ArgumentException("--scale-over wants larger or smaller"),
                        };
                        break;
                    case "--type": o.Settings.Type = FractalTypes.Parse(Next()); break;
                    case "--param": o.Settings.Parameter = NextDouble(); break;
                    case "--coloring":
                        o.Settings.Coloring = Next() switch
                        {
                            "discrete" => ColoringMode.Discrete,
                            "smooth" => ColoringMode.Smooth,
                            "stripe-fixed" => ColoringMode.StripeFixedRange,
                            "stripe-iter" => ColoringMode.StripeIterationRange,
                            var v => throw new ArgumentException("Unknown coloring: " + v),
                        };
                        break;
                    case "--stripe-density": o.Settings.StripeDensity = NextDouble(); break;
                    case "--palette": o.Colors.Palette = PalettePresets.Find(Next()); break;
                    case "--bands": o.Colors.Bands = NextInt(); break;
                    case "--offset": o.Colors.Offset = NextInt(); break;
                    case "--divide": o.Colors.DivideIteration = NextDouble(); break;
                    case "--reverse": o.Colors.Reverse = true; break;
                    case "--bump": o.Colors.Bump = true; break;
                    case "--no-perturbation": o.Settings.Precision.Perturbation = false; break;
                    case "--series":
                        o.Settings.Precision.Series = Next() switch
                        {
                            "off" => SeriesType.Off,
                            "sd" => SeriesType.StandardDouble,
                            "hf" => SeriesType.ExtendedDouble,
                            var v => throw new ArgumentException("Unknown series type: " + v),
                        };
                        break;
                    case "--rebasing": o.Settings.Precision.Rebasing = true; break;
                    case "--force-mp": o.Settings.Precision.ForceMultiPrecision = true; break;
                    case "--threshold": o.Settings.Precision.Threshold = NextDouble(); break;
                    case "--extra-digits": o.Settings.Precision.ExtraDigits = NextInt(); break;
                    case "--max-refs": o.Settings.Precision.MaxReferences = NextInt(); break;
                    case "--debug-glitches": o.Settings.Precision.DebugGlitches = true; break;
                    case "--threads":
                        {
                            string v = Next();
                            if (v == "all") o.Settings.Threads = null;
                            else
                            {
                                int n = int.Parse(v, ci);
                                if (n < 1 || n > Environment.ProcessorCount)
                                    throw new ArgumentException("--threads wants all or 1 to " + Environment.ProcessorCount);
                                o.Settings.Threads = n;
                            }
                            break;
                        }
                    case "--dll": o.DllPath = Path.GetFullPath(Next()); break;
                    case "--allow-unknown-dll": o.AllowUnknownDll = true; break;
                    case "--out": o.OutPath = Path.GetFullPath(Next()); break;
                    case "--raw": o.RawPath = Path.GetFullPath(Next()); break;
                    case "--quiet": o.Quiet = true; break;
                    default: throw new ArgumentException("Unknown option: " + a);
                }
            }

            if (o.Command == "info") return o;

            if (kfr != null) o.Location = SceneFile.ReadKfr(Path.GetFullPath(kfr), iter);
            else if (fraqcelDefault) o.Location = iter is int n0 ? Location.FullView.WithMaxIter(n0) : Location.FullView;
            else
            {
                if (re == null || im == null) throw new ArgumentException("Give --kfr, --fraqcel-default, or --re and --im.");
                if ((plotScale == null) == (zoom == null)) throw new ArgumentException("Give exactly one of --plot-scale and --zoom.");
                int n = iter ?? 500;
                o.Location = plotScale != null
                    ? new Location(re, im, double.Parse(plotScale, NumberStyles.Float, ci), n)
                    : Location.FromKitZoom(re, im, zoom!, n);
            }

            if (nav != null)
            {
                if (width != 0) throw new ArgumentException("Give --nav or --size, not both.");
                o.Geometry = Geometry.Navigation(nav.Value);
                o.Settings.NavSize = nav.Value;
            }
            else if (width != 0) o.Geometry = Geometry.Publish(width, height, samples, scaleOverLarger);
            else o.Geometry = Geometry.Navigation(800);
            return o;
        }
    }
}
