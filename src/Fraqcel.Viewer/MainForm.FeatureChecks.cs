// Part of the unofficial Fraqcel host prototype.

using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using Fraqcel.Engine;

namespace Fraqcel.Viewer;

/// <summary>
/// Self-test checks for the controls added on 2026-11-21. They run through the same handlers
/// the controls use, and write files only to the self-test's folder.
/// </summary>
internal sealed partial class MainForm
{
    /// <summary>At the starting view: x2 and /2, statistics, set color, the bump light, Save view, the wheel and the shortcuts.</summary>
    private async Task CheckStartViewFeaturesAsync(string dir, Action<bool, string> expect)
    {
        // x2 and /2 change the box only, as Fraqcel's buttons do.
        txtIter.Text = "500";
        btnIterDouble.PerformClick();
        expect(txtIter.Text == "1000", "×2 doubles the box: " + txtIter.Text);
        btnIterHalve.PerformClick();
        btnIterHalve.PerformClick();
        expect(txtIter.Text == "250", "÷2 twice halves it twice: " + txtIter.Text);
        txtIter.Text = "not a number";
        btnIterDouble.PerformClick();
        expect(txtIter.Text == CurrentMaxIter.ToString(Ci), "×2 on text that isn't a number puts back the view's max iterations: " + txtIter.Text);
        txtIter.Text = CurrentMaxIter.ToString(Ci);

        // Statistics: the line matches the data, and the full view's max is its max iterations (the set is in view).
        var data = navResult!.Data.Cast<double>().ToArray();
        string want = "iterations  total " + data.Sum().ToString("0.00E+00", Ci) + ", max " + Iteration(data.Max()) + ", min " + Iteration(data.Min());
        expect(InfoLine("iterations") == want && data.Max() == CurrentMaxIter,
               "the statistics line reads \"" + InfoLine("iterations") + "\" (expected \"" + want + "\")");
        int coloring = cmbColoring.SelectedIndex;
        cmbColoring.SelectedIndex = (int)ColoringMode.StripeFixedRange;
        await WaitIdle();
        expect(InfoLine("iterations").Contains("total --"), "stripe coloring shows -- for the totals, as Fraqcel does: " + InfoLine("iterations"));
        cmbColoring.SelectedIndex = coloring;
        await WaitIdle();

        // Set color: exactly the in-set pixels take it, and the preset's own color puts the image back.
        int inSet = navResult!.Data.Cast<double>().Count(v => v >= navResult.MaxIter);
        int presetColor = setColor;
        using (var before = CopyCanvas())
        {
            SetSetColor(PaletteBuilder.Rgb(255, 0, 0));
            await RecolorAsync();
            using (var red = CopyCanvas())
            {
                var mid = red.GetPixel(red.Width / 2, red.Height / 2);
                int changed = CountDiffering(before, red);
                expect(mid.ToArgb() == Color.Red.ToArgb() && changed == inSet,
                       "the set takes the set color: the middle is " + mid + ", " + changed + " pixels changed, " + inSet + " are in the set");
            }
            SetSetColor(presetColor);
            await RecolorAsync();
            using var back = CopyCanvas();
            expect(CountDiffering(before, back) == 0, "the preset's set color gives back the same image");
        }

        // The bump light: bump mapping and each slider change the shading; bump off gives back the plain image.
        using (var plain = CopyCanvas())
        {
            expect(!trkBumpDirection.Enabled, "the light sliders are off while bump mapping is");
            chkBump.Checked = true;
            await RecolorAsync();
            expect(trkBumpDirection.Enabled, "the light sliders turn on with bump mapping");
            var previous = CopyCanvas();
            expect(CountDiffering(plain, previous) > 1000, "bump mapping shades the image: " + CountDiffering(plain, previous) + " pixels changed");
            foreach (var (bar, value, name) in new[] { (trkBumpDirection, 90, "direction"), (trkBumpElevation, 45, "elevation"), (trkBumpStrength, 90, "strength") })
            {
                bar.Value = value;
                await RecolorAsync();
                var next = CopyCanvas();
                int changed = CountDiffering(previous, next);
                expect(changed > 1000, "the light " + name + " slider changes the shading: " + changed + " pixels changed");
                previous.Dispose();
                previous = next;
            }
            previous.Dispose();
            // A picture of the panel's coloring section, to look at the new rows.
            side.ScrollControlIntoView(trkBumpStrength);
            await Task.Delay(100);
            SaveWindow(this, Path.Combine(dir, "8-panel-coloring.png"));
            side.ScrollControlIntoView(txtRe);
            trkBumpDirection.Value = 0;
            trkBumpElevation.Value = 0;
            trkBumpStrength.Value = 50;
            chkBump.Checked = false;
            await RecolorAsync();
            using var off = CopyCanvas();
            expect(CountDiffering(plain, off) == 0, "bump mapping off gives back the plain image");
        }

        // Save view writes the image on screen.
        string viewPng = Path.Combine(dir, "7-saved-view.png");
        expect(SaveViewTo(viewPng) && ImageWriter.EndsWithIend(viewPng) && SameAsCanvas(viewPng), "Save view writes the image on screen to " + viewPng);

        // The wheel keeps the sample under the pointer where it is; a step back out returns.
        double zoomFactor = ParseDouble(txtZoomFactor.Text, "Zoom factor");
        var w0 = current!;
        OnWheelStep(200, 600, +1);
        await WaitIdle();
        var w1 = current!;
        expect(w1.PlotScale == w0.PlotScale / zoomFactor && DriftPixels(w0, w1, 200, 600) < 1e-6,
               "a wheel step in keeps pixel (200, 600) in place: drift " + DriftPixels(w0, w1, 200, 600) + " pixels, " + w1);
        // CONTROL: the drift measure does see a pixel the zoom didn't hold (one pixel left moves 3 pixels).
        expect(DriftPixels(w0, w1, 200, 599) > 0.5, "control: pixel (200, 599) moved " + DriftPixels(w0, w1, 200, 599) + " pixels");
        OnWheelStep(200, 600, -1);
        await WaitIdle();
        expect(Math.Abs(current!.PlotScale - w0.PlotScale) <= 1e-15 * w0.PlotScale && DriftPixels(w0, current, 200, 600) < 1e-6,
               "a wheel step out returns to the same view: " + current);

        // Ctrl+Shift+Z, X and C, as Fraqcel's shortcuts, through the same handler as the keyboard.
        var precision = BuildSettings().Precision;
        expect(!HandleShortcut(Keys.Control | Keys.Z, null), "Ctrl+Z alone is not one of the shortcuts");
        var k0 = current!;
        HandleShortcut(Keys.Control | Keys.Shift | Keys.Z, null);
        await WaitIdle();
        expect(SameView(current!, Navigator.Move(k0, 800, CellSelection.Pixel(399, 399), ZoomDirection.In, zoomFactor, precision)),
               "Ctrl+Shift+Z with the pointer off the image zooms in on the middle: " + current);
        HandleShortcut(Keys.Control | Keys.Shift | Keys.X, null);
        await WaitIdle();
        expect(Math.Abs(current!.PlotScale - k0.PlotScale) <= 1e-15 * k0.PlotScale, "Ctrl+Shift+X zooms back out: " + current);
        var k1 = current!;
        HandleShortcut(Keys.Control | Keys.Shift | Keys.C, new Point(600, 200));
        await WaitIdle();
        expect(SameView(current!, Navigator.Move(k1, 800, CellSelection.Pixel(200, 600), ZoomDirection.Pan, zoomFactor, precision)),
               "Ctrl+Shift+C centers on the pixel under the pointer: " + current);

        expect(await RenderNavAsync(Engine.Location.FullView, false), "back to the starting view");
    }

    /// <summary>At the deep sample: saved locations and the session, in the self-test's own settings files.</summary>
    private async Task CheckSavedAndSessionAsync(string dir, Action<bool, string> expect)
    {
        string file = LocationsFile; // DIR\session.ini in a self-test
        File.Delete(file);
        var deep = current!;
        string defaultName = SavedLocation.DefaultName(deep);
        cmbSaved.Text = "Self-test spot";
        expect(SaveLocation(cmbSaved.Text) && HostSettings.Load(file).Locations.Count == 1 && cmbSaved.Items.Contains("Self-test spot"),
               "Save keeps the view under the typed name");
        expect(SaveLocation("") && HostSettings.Load(file).Locations.Any(l => l.Name == defaultName),
               "Save with an empty name uses the zoom and iterations: " + defaultName);
        expect(await RenderNavAsync(Engine.Location.FullView, true), "go somewhere else");
        LoadLocation("SELF-TEST spot");
        await WaitIdle();
        expect(current!.CenterRe == deep.CenterRe && current.CenterIm == deep.CenterIm && current.PlotScale == deep.PlotScale &&
               current.MaxIter == deep.MaxIter && current.CenterReValue == null,
               "Load returns to the saved view, matching the name in any case: " + current);
        var still = current;
        LoadLocation("no such place");
        expect(!busy && ReferenceEquals(current, still), "control: loading a name that isn't saved changes nothing");
        expect(DeleteLocation("Self-test spot") && HostSettings.Load(file).Locations.Select(l => l.Name).SequenceEqual(new[] { defaultName }),
               "Delete removes only that location");

        // The session: distinctive values saved, the controls reset, then read back.
        cmbPalette.SelectedIndex = 4; // Blue Waves
        SetSetColor(PaletteBuilder.Rgb(10, 200, 30));
        numOffset.Value = 7;
        trkBumpStrength.Value = 77;
        txtZoomFactor.Text = "6";
        numWidth.Value = 1234;
        cmbSeries.SelectedIndex = 2;
        chkRebasing.Checked = true;
        txtThreshold.Text = "1E-12";
        cmbSamples.SelectedIndex = 2; // 9
        var state = new HostSettings();
        SaveSession(state);
        string statePath = Path.Combine(dir, "session-state.ini");
        state.Save(statePath);
        var read = HostSettings.Load(statePath);
        ResetSessionControls();
        var (view, viewMs) = RestoreSession(read);
        string got = "palette " + cmbPalette.Text + ", set color " + setColor + ", offset " + numOffset.Value + ", strength " + trkBumpStrength.Value +
                     ", zoom factor " + txtZoomFactor.Text + ", width " + numWidth.Value + ", series " + cmbSeries.SelectedIndex +
                     ", rebasing " + chkRebasing.Checked + ", threshold " + txtThreshold.Text + ", samples " + cmbSamples.Text;
        expect(cmbPalette.SelectedIndex == 4 && setColor == PaletteBuilder.Rgb(10, 200, 30) && numOffset.Value == 7 &&
               trkBumpStrength.Value == 77 && txtZoomFactor.Text == "6" && numWidth.Value == 1234 && cmbSeries.SelectedIndex == 2 &&
               chkRebasing.Checked && txtThreshold.Text == "1E-12" && cmbSamples.Text == "9",
               "the session gives back every control: " + got);
        expect(view != null && view.CenterRe == current!.CenterRe && view.CenterIm == current.CenterIm && view.PlotScale == current.PlotScale &&
               viewMs is double ms && Math.Abs(ms - navResult!.EngineMs) < 1,
               "the session gives back the last view and its render time: " + view + ", " + viewMs + " ms");
        // CONTROL: an unreadable value leaves its control alone.
        read.Set("viewer.offset", "lots");
        read.Set("viewer.palette", "No Such Palette");
        numOffset.Value = 3;
        cmbPalette.SelectedIndex = 1;
        RestoreSession(read);
        expect(numOffset.Value == 3 && cmbPalette.SelectedIndex == 1, "control: unreadable values keep their controls");
        ResetSessionControls();
        await WaitIdle();
    }

    /// <summary>The controls the session check changes, back at their starting values.</summary>
    private void ResetSessionControls()
    {
        cmbPalette.SelectedIndex = 0; // also brings back the preset's set color
        numOffset.Value = 0;
        trkBumpStrength.Value = 50;
        txtZoomFactor.Text = "4";
        numWidth.Value = 1920;
        cmbSeries.SelectedIndex = 1;
        chkRebasing.Checked = false;
        txtThreshold.Text = "1E-13";
        cmbSamples.SelectedIndex = 0;
    }

    private async Task WaitIdle()
    {
        while (busy) await Task.Delay(50);
    }

    private string InfoLine(string start) => txtInfo.Lines.FirstOrDefault(l => l.StartsWith(start, StringComparison.Ordinal)) ?? "";

    private Bitmap CopyCanvas() => new(canvas.Image!);

    private bool SameAsCanvas(string png)
    {
        using var file = new Bitmap(png);
        using var shown = CopyCanvas();
        return CountDiffering(file, shown) == 0;
    }

    private static bool SameView(Location a, Location b) =>
        a.CenterRe == b.CenterRe && a.CenterIm == b.CenterIm && a.PlotScale == b.PlotScale && a.MaxIter == b.MaxIter;

    /// <summary>How far pixel (row, col)'s sample moved between two double-precision views, in the second view's pixel steps.</summary>
    private static double DriftPixels(Location a, Location b, int row, int col, int n = 800)
    {
        double stepA = a.PlotScale / (n - 1), stepB = b.PlotScale / (n - 1);
        double reA = a.CenterReAsDouble - a.PlotScale / 2 + col * stepA, reB = b.CenterReAsDouble - b.PlotScale / 2 + col * stepB;
        double imA = a.CenterImAsDouble + a.PlotScale / 2 - row * stepA, imB = b.CenterImAsDouble + b.PlotScale / 2 - row * stepB;
        return Math.Max(Math.Abs(reB - reA), Math.Abs(imB - imA)) / stepB;
    }

    /// <summary>The number of pixels that differ; images of different sizes count as entirely different.</summary>
    private static int CountDiffering(Bitmap a, Bitmap b)
    {
        if (a.Size != b.Size) return int.MaxValue;
        var rect = new Rectangle(Point.Empty, a.Size);
        var da = a.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var db = b.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var pa = new int[a.Width];
            var pb = new int[a.Width];
            int count = 0;
            for (int y = 0; y < a.Height; y++)
            {
                Marshal.Copy(da.Scan0 + y * da.Stride, pa, 0, a.Width);
                Marshal.Copy(db.Scan0 + y * db.Stride, pb, 0, a.Width);
                for (int x = 0; x < a.Width; x++) if (pa[x] != pb[x]) count++;
            }
            return count;
        }
        finally
        {
            a.UnlockBits(da);
            b.UnlockBits(db);
        }
    }
}
