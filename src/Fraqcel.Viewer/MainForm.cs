// Part of the unofficial Fraqcel host prototype.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Fraqcel.Engine;
using Fraqcel.Engine.Dll;

namespace Fraqcel.Viewer;

/// <summary>
/// The prototype viewer: pick a location, zoom with the mouse, render an image.
/// Renders run on a background thread; Cancel stops after the glitch-correction pass in progress.
/// </summary>
internal sealed partial class MainForm : Form
{
    private static readonly CultureInfo Ci = CultureInfo.InvariantCulture;

    private readonly Renderer renderer = new();
    private readonly ImageCanvas canvas = new() { Dock = DockStyle.Fill };

    // Location
    private readonly TextBox txtRe = Box(), txtIm = Box(), txtScale = Box(), txtIter = Box();
    private readonly TextBox txtZoomFactor = Box("4");
    private readonly Button btnIterDouble = SmallButton("×2");
    private readonly Button btnIterHalve = SmallButton("÷2");
    private readonly TableLayoutPanel side = new()
    {
        Dock = DockStyle.Right, Width = 430, ColumnCount = 2, AutoScroll = true,
        Padding = new Padding(8, 6, 18, 6),
    };

    // Fractal
    private readonly ComboBox cmbType = Combo(Enum.GetValues(typeof(FractalType)).Cast<FractalType>().Select(FractalTypes.DisplayName).ToArray());
    private readonly TextBox txtParam = Box("3");
    private readonly ComboBox cmbNav = Combo("800", "596");

    // Coloring
    private readonly ComboBox cmbColoring = Combo("Discrete", "Smooth", "Stripe (fixed range)", "Stripe (iteration range)");
    private readonly TextBox txtStripe = Box("3");
    private readonly ComboBox cmbPalette = Combo(PalettePresets.All.Select(p => p.Name).ToArray());
    private readonly NumericUpDown numOffset = new() { Minimum = 0, Maximum = 32767, Anchor = AnchorStyles.Left };
    private readonly TextBox txtDivide = Box("1");
    private readonly CheckBox chkReverse = Check("Reverse palette");
    // Bump mapping works with every coloring type (Fraqcel's author, 2026-11-13).
    private readonly CheckBox chkBump = Check("Bump mapping");
    // The light, as Fraqcel's scroll bars set it (DATA K3:K5); the shipped values are 0, 0 and 50.
    private readonly TrackBar trkBumpDirection = Slider(-180, 180, 0);
    private readonly TrackBar trkBumpElevation = Slider(-90, 90, 0);
    private readonly TrackBar trkBumpStrength = Slider(1, 100, 50);
    private readonly Label lblBumpDirection = ValueLabel(), lblBumpElevation = ValueLabel(), lblBumpStrength = ValueLabel();
    // The color of the set (SETTINGS F8), an Excel color value. Choosing a palette preset sets it to the preset's.
    private int setColor;
    private readonly Panel pnlSetColor = new() { Size = new Size(44, 22), BorderStyle = BorderStyle.FixedSingle, Cursor = Cursors.Hand, Margin = new Padding(3, 4, 3, 3) };

    // Saved locations (Fraqcel's Save and Load slots), kept in the settings file.
    private readonly ComboBox cmbSaved = new() { DropDownStyle = ComboBoxStyle.DropDown, Anchor = AnchorStyles.Left | AnchorStyles.Right };

    // Precision
    private readonly CheckBox chkPert = Check("Use perturbation theory", true);
    private readonly ComboBox cmbSeries = Combo("Off", "O(5-3) double (SD)", "O(5) extended (HF)");
    private readonly CheckBox chkRebasing = Check("Use rebasing");
    private readonly CheckBox chkForceMP = Check("Ignore threshold, force multi-precision");
    private readonly TextBox txtThreshold = Box("1E-13");
    private readonly NumericUpDown numMaxRefs = new() { Minimum = 1, Maximum = 10000, Value = 30, Anchor = AnchorStyles.Left };

    // Render image
    private readonly NumericUpDown numWidth = new() { Minimum = 17, Maximum = 16383, Value = 1920, Anchor = AnchorStyles.Left };
    private readonly NumericUpDown numHeight = new() { Minimum = 17, Maximum = 32765, Value = 1080, Anchor = AnchorStyles.Left };
    private readonly ComboBox cmbSamples = Combo("1", "4", "9", "16", "25");
    private readonly Button btnRenderImage = new() { Text = "Render image…", AutoSize = true };
    private readonly Button btnSaveView = new() { Text = "Save view…", AutoSize = true };

    private readonly TextBox txtInfo = new()
    {
        Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Height = 170,
        Anchor = AnchorStyles.Left | AnchorStyles.Right, Font = new Font(FontFamily.GenericMonospace, 8.25f),
    };

    // Status bar
    private readonly ToolStripStatusLabel lblStatus = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly ToolStripStatusLabel lblPixel = new() { AutoSize = false, Width = 140 };
    private readonly ToolStripProgressBar progressBar = new() { Width = 160, Minimum = 0, Maximum = 100 };
    private readonly ToolStripButton btnCancel = new("Cancel") { Enabled = false };
    private readonly ToolStripButton btnOpenImage = new("Open last image") { Enabled = false };

    private Location? current;
    private RenderResult? navResult;
    private readonly Stack<Location> history = new();
    private bool busy;
    private CancellationTokenSource? cts;
    private (Location Loc, bool Push)? pendingNav;
    private string? lastImagePath;
    private readonly string? startKfr;
    private readonly string? selfTestDir;
    private readonly string settingsPath;
    private readonly Label lblDll = Hint("");
    private Point? hoveredPixel; // x = column, y = row
    private int recolorSeq;

    public int ExitCode { get; private set; }

    /// <summary>
    /// The version in the title bar: &lt;Version&gt; in Directory.Build.props, without the
    /// "+commit" that newer SDKs append to the informational version inside a git repository.
    /// </summary>
    internal static string DisplayVersion
    {
        get
        {
            string v = Application.ProductVersion;
            int plus = v.IndexOf('+');
            return plus < 0 ? v : v.Substring(0, plus);
        }
    }

    public MainForm(string? startKfr, string? selfTestDir, string? monitor, string settingsPath)
    {
        this.startKfr = startKfr;
        this.selfTestDir = selfTestDir;
        this.settingsPath = settingsPath;

        SuspendLayout();
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = "Fraqcel viewer " + DisplayVersion + " (unofficial prototype)";
        ClientSize = new Size(1320, 880);
        StartPosition = FormStartPosition.WindowsDefaultLocation;
        if (WindowPlacement.Named(monitor) is Screen named) WindowPlacement.CreateOn(this, named);
        KeyPreview = true;

        cmbType.SelectedIndex = 0;
        cmbNav.SelectedIndex = 0;
        cmbColoring.SelectedIndex = 1; // smooth: the default for looking; Fraqcel ships discrete
        cmbPalette.SelectedIndex = 0;
        cmbSeries.SelectedIndex = 1;
        cmbSamples.SelectedIndex = 0;

        side.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        side.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        Header(side, "Location");
        Row(side, "Re", txtRe);
        Row(side, "Im", txtIm);
        Row(side, "Plot scale", txtScale);
        Row(side, "Max iterations", Inline(txtIter, btnIterDouble, btnIterHalve));
        btnIterDouble.Click += (_, _) => txtIter.Text = MaxIterations.Double(txtIter.Text, CurrentMaxIter).ToString(Ci);
        btnIterHalve.Click += (_, _) => txtIter.Text = MaxIterations.Halve(txtIter.Text, CurrentMaxIter).ToString(Ci);
        Full(side, Buttons(
            Button("Go", (_, _) => GoFromBoxes()),
            Button("Back", (_, _) => Back()),
            Button("Reset view", (_, _) => _ = RenderNavAsync(Engine.Location.FullView, true))));
        Full(side, Buttons(
            Button("Open scene…", (_, _) => OpenScene()),
            Button("Fraqcel's deep sample", (_, _) => _ = RenderNavAsync(Engine.Location.FraqcelSuggested, true)),
            Button("Copy", (_, _) => CopyLocation())));

        Header(side, "Saved locations");
        Full(side, cmbSaved);
        Full(side, Buttons(
            Button("Save", (_, _) => SaveLocation(cmbSaved.Text)),
            Button("Load", (_, _) => LoadLocation(cmbSaved.Text)),
            Button("Delete", (_, _) => DeleteLocation(cmbSaved.Text))));
        Full(side, Hint("Save keeps the view under the name typed above, or under its zoom and iterations if the box is empty."));

        Header(side, "Zoom");
        Row(side, "Zoom factor", txtZoomFactor);
        Full(side, Hint("Drag a box to zoom into it (the dashed square shows where it lands). " +
                        "Click: zoom in by the factor. Right-click: zoom out. Middle-click: center there. " +
                        "Wheel: zoom in or out by the factor, keeping the point under the pointer in place. " +
                        "Ctrl+Shift+Z, X, C: zoom in, zoom out or center on the pixel under the pointer (the middle when it's off the image). " +
                        "Esc: cancel."));

        Header(side, "Fractal");
        Row(side, "Type", cmbType);
        Row(side, "Parameter", txtParam);
        Row(side, "Navigation size", cmbNav);

        Header(side, "Coloring");
        Row(side, "Mode", cmbColoring);
        Row(side, "Stripe density", txtStripe);
        Row(side, "Palette", cmbPalette);
        Row(side, "Offset", numOffset);
        Row(side, "Divide iteration", txtDivide);
        Row(side, "Set color", Buttons(pnlSetColor, Button("Choose…", (_, _) => ChooseSetColor())));
        Full(side, chkReverse);
        Full(side, chkBump);
        Row(side, "Light direction", Inline(trkBumpDirection, lblBumpDirection));
        Row(side, "Light elevation", Inline(trkBumpElevation, lblBumpElevation));
        Row(side, "Bump strength", Inline(trkBumpStrength, lblBumpStrength));

        Header(side, "Precision");
        Full(side, chkPert);
        Row(side, "Series", cmbSeries);
        Full(side, chkRebasing);
        Full(side, chkForceMP);
        Row(side, "Threshold", txtThreshold);
        Row(side, "Max references", numMaxRefs);

        Header(side, "Render image");
        Row(side, "Width", numWidth);
        Row(side, "Height", numHeight);
        Row(side, "Samples per pixel", cmbSamples);
        Full(side, Buttons(btnRenderImage, btnSaveView));
        Full(side, Hint("Render image renders the view again at the size above. Save view saves the image on screen."));

        Header(side, "Last render");
        Full(side, txtInfo);

        Header(side, "Fraqcel DLL");
        Full(side, lblDll);
        Full(side, Buttons(Button("Change…", (_, _) => ChangeDll())));
        UpdateDllLabel();

        var status = new StatusStrip();
        status.Items.AddRange(new ToolStripItem[] { lblStatus, lblPixel, progressBar, btnCancel, btnOpenImage });

        Controls.Add(canvas);
        Controls.Add(side);
        Controls.Add(status);
        ResumeLayout(true);

        // Events
        canvas.BoxSelected += OnBoxSelected;
        canvas.PixelClicked += OnPixelClicked;
        canvas.PixelHovered += p =>
        {
            hoveredPixel = p;
            lblPixel.Text = p is Point q ? PixelText(q) : "";
        };
        canvas.WheelStep += OnWheelStep;
        btnRenderImage.Click += (_, _) => _ = RenderImageAsync();
        btnSaveView.Click += (_, _) => SaveView();
        btnCancel.Click += (_, _) => Cancel();
        btnOpenImage.Click += (_, _) => OpenLastImage();
        cmbPalette.SelectedIndexChanged += (_, _) =>
        {
            SetSetColor(SelectedPreset.SetColor); // a preset brings its own set color, as in Fraqcel
            Recolor();
        };
        numOffset.ValueChanged += (_, _) => Recolor();
        chkReverse.CheckedChanged += (_, _) => Recolor();
        chkBump.CheckedChanged += (_, _) =>
        {
            UpdateBumpControls();
            Recolor();
        };
        foreach (var t in new[] { trkBumpDirection, trkBumpElevation, trkBumpStrength })
        {
            t.ValueChanged += (_, _) =>
            {
                UpdateBumpControls();
                if (chkBump.Checked) Recolor();
            };
        }
        pnlSetColor.Click += (_, _) => ChooseSetColor();
        SetSetColor(SelectedPreset.SetColor);
        UpdateBumpControls();
        txtDivide.Leave += (_, _) => Recolor();
        cmbColoring.SelectedIndexChanged += (_, _) => Rerender();
        cmbNav.SelectedIndexChanged += (_, _) => Rerender();
        foreach (var t in new[] { txtRe, txtIm, txtScale, txtIter })
            t.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; GoFromBoxes(); } };
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) Cancel(); };
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (selfTestDir != null)
        {
            await RunSelfTestAsync(selfTestDir);
            Close();
            return;
        }

        // The last session's settings and view. A scene given on the command line wins over the view.
        var saved = HostSettings.Load(settingsPath);
        var (lastView, lastViewMs) = RestoreSession(saved);
        RefreshSavedList(saved);

        Location start = Engine.Location.FullView;
        if (startKfr != null)
        {
            try { start = SceneFile.ReadKfr(startKfr); }
            catch (Exception ex) when (ex is IOException or FormatException or ArgumentException) { Status(ex.Message); }
        }
        else if (lastView != null)
        {
            // Cancel only stops between glitch-correction passes, so a view that took long to
            // render waits for Go instead of starting by itself.
            if (lastViewMs is double ms && ms > SlowStartMs)
            {
                ShowLocation(lastView);
                canvas.EmptyText = "Your last view took " + (ms / 1000).ToString("0", Ci) + " s to render. Press Go to render it.";
                Status("Your last view is in the Location boxes. Press Go to render it.");
                return;
            }
            start = lastView;
        }
        await RenderNavAsync(start, false);
    }

    /// <summary>A last view that took longer than this to render isn't rendered again at start.</summary>
    private const double SlowStartMs = 10_000;

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        if (selfTestDir != null) return; // a self-test never writes the real settings
        try
        {
            var s = HostSettings.Load(settingsPath);
            if (s.ReadProblem != null) return; // don't overwrite a file that couldn't be read
            SaveSession(s);
            s.Save(settingsPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Closing anyway: the settings stay as they were.
        }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData) =>
        HandleShortcut(keyData, hoveredPixel) || base.ProcessCmdKey(ref msg, keyData);

    /// <summary>
    /// Fraqcel's Ctrl+Shift+Z, X and C: zoom in, zoom out, or center on pixel <paramref name="at"/>
    /// (x = column, y = row), the one under the pointer.
    /// </summary>
    private bool HandleShortcut(Keys keyData, Point? at)
    {
        if ((keyData & Keys.Modifiers) != (Keys.Control | Keys.Shift)) return false;
        ZoomDirection direction;
        switch (keyData & Keys.KeyCode)
        {
            case Keys.Z: direction = ZoomDirection.In; break;
            case Keys.X: direction = ZoomDirection.Out; break;
            case Keys.C: direction = ZoomDirection.Pan; break;
            default: return false;
        }
        if (navResult == null) return true;
        // Off the image, the pixel just above and left of the middle: a single-pixel zoom there keeps the center.
        int n = navResult.Geometry.OutRSize;
        var p = at ?? new Point(n / 2 - 1, n / 2 - 1);
        ZoomTo(CellSelection.Pixel(p.Y, p.X), direction);
        return true;
    }

    private void OnWheelStep(int row, int col, int sign)
    {
        if (current == null || navResult == null) return;
        if (busy) { Status("Busy: the wheel works again when this render finishes (Esc cancels)."); return; }
        try
        {
            double zoomFactor = ParseDouble(txtZoomFactor.Text, "Zoom factor");
            int maxIter = ParseInt(txtIter.Text, "Max iterations");
            var next = Navigator.ZoomAround(current.WithMaxIter(maxIter), navResult.Geometry.OutRSize, row, col,
                sign > 0 ? ZoomDirection.In : ZoomDirection.Out, zoomFactor, BuildSettings().Precision);
            _ = RenderNavAsync(next, true);
        }
        catch (Exception e) when (e is ArgumentException or FormatException)
        {
            Status(e.Message);
        }
    }

    // ---- actions -------------------------------------------------------------------------------

    private void GoFromBoxes()
    {
        try
        {
            var loc = new Location(txtRe.Text, txtIm.Text, ParseDouble(txtScale.Text, "Plot scale"), ParseInt(txtIter.Text, "Max iterations"));
            _ = RenderNavAsync(loc, true);
        }
        catch (Exception e) when (e is ArgumentException or FormatException)
        {
            Status(e.Message);
        }
    }

    private void Back()
    {
        if (busy) { Status("Busy: press Esc to cancel first."); return; }
        if (history.Count == 0) { Status("Nothing to go back to."); return; }
        _ = RenderNavAsync(history.Pop(), false);
    }

    private void Rerender()
    {
        if (current != null) _ = RenderNavAsync(current, false);
    }

    private void OnBoxSelected(int rowA, int colA, int rowB, int colB) =>
        ZoomTo(CellSelection.Box(rowA, colA, rowB, colB), ZoomDirection.In);

    private void OnPixelClicked(int row, int col, MouseButtons button)
    {
        var sel = CellSelection.Pixel(row, col);
        if (button == MouseButtons.Left) ZoomTo(sel, ZoomDirection.In);
        else if (button == MouseButtons.Right) ZoomTo(sel, ZoomDirection.Out);
        else if (button == MouseButtons.Middle) ZoomTo(sel, ZoomDirection.Pan);
    }

    /// <summary>Fraqcel's selectionMove, from the rendered location (not the text boxes) and the image the user clicked.</summary>
    private void ZoomTo(CellSelection sel, ZoomDirection direction)
    {
        if (current == null || navResult == null) return;
        if (busy) { Status("Busy: press Esc to cancel first."); return; }
        try
        {
            double zoomFactor = ParseDouble(txtZoomFactor.Text, "Zoom factor");
            int maxIter = ParseInt(txtIter.Text, "Max iterations");
            var precision = BuildSettings().Precision;
            var next = Navigator.Move(current.WithMaxIter(maxIter), navResult.Geometry.OutRSize, sel, direction, zoomFactor, precision);
            _ = RenderNavAsync(next, true);
        }
        catch (Exception e) when (e is ArgumentException or FormatException)
        {
            Status(e.Message);
        }
    }

    private void OpenScene()
    {
        using var dlg = new OpenFileDialog { Filter = "Bench-kit scene (*.kfr)|*.kfr|All files|*.*", Title = "Open a bench-kit scene" };
        string? scenes = FindKitScenes();
        if (scenes != null) dlg.InitialDirectory = scenes;
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            _ = RenderNavAsync(SceneFile.ReadKfr(dlg.FileName), true);
        }
        catch (Exception e) when (e is IOException or FormatException or ArgumentException)
        {
            Status(e.Message);
        }
    }

    /// <summary>The bench kit's scenes folder, if this build sits inside the kit.</summary>
    private static string? FindKitScenes()
    {
        var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
        for (int i = 0; i < 10 && dir != null; i++, dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "run-all.ps1")) && Directory.Exists(Path.Combine(dir.FullName, "scenes")))
                return Path.Combine(dir.FullName, "scenes");
        return null;
    }

    private void CopyLocation()
    {
        if (current == null) return;
        var sb = new StringBuilder();
        sb.AppendLine("Re: " + current.CenterRe);
        sb.AppendLine("Im: " + current.CenterIm);
        sb.AppendLine("Zoom: " + current.Zoom.ToString("R", Ci));
        sb.AppendLine("Iterations: " + current.MaxIter.ToString(Ci));
        Clipboard.SetText(sb.ToString());
        Status("Location copied in .kfr form.");
    }

    /// <summary>Chooses another DLL. The running viewer keeps its DLL; the new one is used from the next start.</summary>
    private void ChangeDll()
    {
        var settings = HostSettings.Load(settingsPath);
        using var dlg = new FraqcelSetupForm(FraqcelSetupForm.Purpose.Change, settings, settingsPath, settings.ReadProblem,
                                             Screen.FromControl(this));
        if (dlg.ShowDialog(this) == DialogResult.OK) UpdateDllLabel();
    }

    private void UpdateDllLabel()
    {
        string text = "In use: " + FraqcelDll.LoadedPath;
        string? saved = HostSettings.Load(settingsPath).DllPath;
        if (saved != null && !string.Equals(saved, FraqcelDll.LoadedPath, StringComparison.OrdinalIgnoreCase))
            text += Environment.NewLine + "Saved for the next start: " + saved;
        lblDll.Text = text;
    }

    private void Cancel()
    {
        if (cts == null || cts.IsCancellationRequested) return;
        cts.Cancel();
        Status("Canceling after the current pass…");
    }

    private void OpenLastImage()
    {
        if (lastImagePath != null && File.Exists(lastImagePath))
            Process.Start(new ProcessStartInfo(lastImagePath) { UseShellExecute = true });
    }

    /// <summary>The rendered view's max iterations: what Fraqcel's x2 and /2 put back for text that isn't a number.</summary>
    private int CurrentMaxIter => current?.MaxIter ?? Engine.Location.FullView.MaxIter;

    // ---- saving the view -----------------------------------------------------------------------

    /// <summary>Fraqcel's Export Navigation Image: the image on screen, as it is colored now.</summary>
    private void SaveView()
    {
        if (canvas.Image == null || navResult == null) { Status("There is no view to save yet."); return; }
        using var dlg = new SaveFileDialog { Filter = "PNG image (*.png)|*.png", FileName = SuggestedName(navResult.Geometry), Title = "Save view" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        SaveViewTo(dlg.FileName);
    }

    private bool SaveViewTo(string path)
    {
        if (canvas.Image is not Bitmap img) return false;
        try
        {
            ImageWriter.SavePng(img, path);
            lastImagePath = path;
            btnOpenImage.Enabled = true;
            Status("Saved the view to " + path);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.ExternalException or ArgumentException)
        {
            Status("Couldn't save the view: " + e.Message);
            return false;
        }
    }

    // ---- saved locations -----------------------------------------------------------------------

    /// <summary>The settings file that holds saved locations: the real one, or the self-test's own.</summary>
    private string LocationsFile => selfTestDir != null ? Path.Combine(selfTestDir, "session.ini") : settingsPath;

    /// <summary>Saves the rendered view under <paramref name="name"/>, replacing a location of the same name.</summary>
    private bool SaveLocation(string name)
    {
        var loc = current;
        if (loc == null) { Status("There is no view to save yet."); return false; }
        name = SavedLocation.CleanName(name);
        if (name.Length == 0) name = SavedLocation.DefaultName(loc);
        return UpdateLocations(list =>
        {
            int i = list.FindIndex(l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase));
            var saved = new SavedLocation(name, loc);
            if (i >= 0) list[i] = saved;
            else list.Add(saved);
            return "Saved \"" + name + "\".";
        }, select: name);
    }

    private bool DeleteLocation(string name)
    {
        name = SavedLocation.CleanName(name);
        return UpdateLocations(list =>
        {
            int removed = list.RemoveAll(l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase));
            return removed > 0 ? "Deleted \"" + name + "\"." : "No saved location is called \"" + name + "\".";
        }, select: null);
    }

    private void LoadLocation(string name)
    {
        name = SavedLocation.CleanName(name);
        var s = HostSettings.Load(LocationsFile);
        var saved = s.Locations.FirstOrDefault(l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase));
        if (saved == null)
        {
            Status(s.ReadProblem ?? (name.Length == 0 ? "Choose a saved location first." : "No saved location is called \"" + name + "\"."));
            return;
        }
        _ = RenderNavAsync(saved.Location, true);
    }

    /// <summary>Reads the settings file, changes the location list, and writes it back. Other settings are kept.</summary>
    private bool UpdateLocations(Func<List<SavedLocation>, string> change, string? select)
    {
        var s = HostSettings.Load(LocationsFile);
        if (s.ReadProblem != null) { Status(s.ReadProblem); return false; }
        var list = s.Locations.ToList();
        string message = change(list);
        try
        {
            s.Locations = list;
            s.Save(LocationsFile);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Status("Couldn't save the settings file " + LocationsFile + ": " + e.Message);
            return false;
        }
        RefreshSavedList(s);
        cmbSaved.Text = select ?? "";
        Status(message);
        return true;
    }

    private void RefreshSavedList(HostSettings s)
    {
        string text = cmbSaved.Text;
        cmbSaved.Items.Clear();
        foreach (var l in s.Locations) cmbSaved.Items.Add(l.Name);
        cmbSaved.Text = text;
    }

    // ---- coloring controls ---------------------------------------------------------------------

    private PalettePreset SelectedPreset => PalettePresets.All[Math.Max(0, cmbPalette.SelectedIndex)];

    private void ChooseSetColor()
    {
        using var dlg = new ColorDialog { Color = ToColor(setColor), FullOpen = true, AnyColor = true };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        SetSetColor(PaletteBuilder.Rgb(dlg.Color.R, dlg.Color.G, dlg.Color.B));
        Recolor();
    }

    private void SetSetColor(int excelColor)
    {
        setColor = excelColor;
        pnlSetColor.BackColor = ToColor(excelColor);
    }

    private static Color ToColor(int excelColor) =>
        Color.FromArgb(PaletteBuilder.Red(excelColor), PaletteBuilder.Green(excelColor), PaletteBuilder.Blue(excelColor));

    private void UpdateBumpControls()
    {
        lblBumpDirection.Text = trkBumpDirection.Value.ToString(Ci) + "°";
        lblBumpElevation.Text = trkBumpElevation.Value.ToString(Ci) + "°";
        lblBumpStrength.Text = trkBumpStrength.Value.ToString(Ci);
        foreach (var t in new[] { trkBumpDirection, trkBumpElevation, trkBumpStrength }) t.Enabled = chkBump.Checked;
    }

    // ---- rendering -----------------------------------------------------------------------------

    /// <summary>Renders the navigation image. Returns false if it did not finish.</summary>
    private async Task<bool> RenderNavAsync(Location loc, bool push)
    {
        if (busy)
        {
            pendingNav = (loc, push);
            Status("Queued: it renders when the current render finishes.");
            return false;
        }
        RenderSettings settings;
        ColorSettings colors;
        try
        {
            settings = BuildSettings();
            colors = BuildColors();
        }
        catch (Exception e) when (e is ArgumentException or FormatException)
        {
            Status(e.Message);
            return false;
        }

        var geometry = Geometry.Navigation(settings.NavSize);
        canvas.EmptyText = "Rendering…"; // shown until the first image arrives
        BeginBusy("Rendering…");
        bool ok = false;
        try
        {
            var token = cts!.Token;
            var progress = new Progress<string>(OnProgress);
            var result = await Task.Run(() => renderer.Render(loc, settings, geometry, progress, token));
            double colorMs = 0;
            var bmp = await Task.Run(() => Colorizer.ToBitmap(result, colors, out colorMs));
            if (push && current != null) history.Push(current);
            current = loc;
            navResult = result;
            SetImage(bmp);
            ShowLocation(loc);
            ShowStats(result, colorMs, null, null);
            Status(result.IsClean
                ? "Rendered in " + result.EngineMs.ToString("0", Ci) + " ms."
                : "Rendered, but " + NotCleanText(result));
            ok = true;
        }
        catch (OperationCanceledException)
        {
            Status("Canceled.");
        }
        catch (Exception e)
        {
            Status("Render failed: " + Inner(e).Message);
        }
        finally
        {
            EndBusy();
        }
        RunPending();
        return ok;
    }

    private async Task<bool> RenderImageAsync()
    {
        if (current == null) return false;
        if (busy) { Status("Busy: press Esc to cancel first."); return false; }
        Geometry geometry;
        RenderSettings settings;
        ColorSettings colors;
        try
        {
            geometry = Geometry.Publish((int)numWidth.Value, (int)numHeight.Value, int.Parse((string)cmbSamples.SelectedItem, Ci));
            settings = BuildSettings();
            colors = BuildColors();
        }
        catch (Exception e) when (e is ArgumentException or FormatException)
        {
            MessageBox.Show(this, e.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        string path;
        using (var dlg = new SaveFileDialog { Filter = "PNG image (*.png)|*.png", FileName = SuggestedName(geometry), Title = "Render image" })
        {
            if (dlg.ShowDialog(this) != DialogResult.OK) return false;
            path = dlg.FileName;
        }
        return await RenderImageToAsync(path, geometry, settings, colors);
    }

    private async Task<bool> RenderImageToAsync(string path, Geometry geometry, RenderSettings settings, ColorSettings colors)
    {
        var loc = current!;
        BeginBusy("Rendering " + geometry + "…");
        bool ok = false;
        try
        {
            var token = cts!.Token;
            var progress = new Progress<string>(OnProgress);
            var result = await Task.Run(() => renderer.Render(loc, settings, geometry, progress, token));
            double colorMs = 0, encodeMs = 0;
            await Task.Run(() =>
            {
                using var bmp = Colorizer.ToBitmap(result, colors, out colorMs);
                encodeMs = ImageWriter.SavePng(bmp, path);
            });
            lastImagePath = path;
            btnOpenImage.Enabled = true;
            ShowStats(result, colorMs, encodeMs, path);
            Status(result.IsClean ? "Saved " + path : "Saved " + path + ", but " + NotCleanText(result));
            ok = true;
        }
        catch (OperationCanceledException)
        {
            Status("Canceled.");
        }
        catch (Exception e)
        {
            Status("Render failed: " + Inner(e).Message);
        }
        finally
        {
            EndBusy();
        }
        RunPending();
        return ok;
    }

    private void Recolor() => _ = RecolorAsync();

    private async Task RecolorAsync()
    {
        if (navResult == null || busy) return;
        ColorSettings colors;
        try { colors = BuildColors(); }
        catch (Exception e) when (e is ArgumentException or FormatException) { Status(e.Message); return; }
        var result = navResult;
        // A slider sends many changes; only the latest recolor may replace the image.
        int seq = ++recolorSeq;
        try
        {
            double ms = 0;
            var bmp = await Task.Run(() => Colorizer.ToBitmap(result, colors, out ms));
            if (seq == recolorSeq && ReferenceEquals(result, navResult)) SetImage(bmp);
            else bmp.Dispose();
            Status("Recolored in " + ms.ToString("0", Ci) + " ms.");
        }
        catch (Exception e)
        {
            Status("Recolor failed: " + Inner(e).Message);
        }
    }

    private void RunPending()
    {
        if (pendingNav is { } p)
        {
            pendingNav = null;
            _ = RenderNavAsync(p.Loc, p.Push);
        }
    }

    private void BeginBusy(string text)
    {
        busy = true;
        cts = new CancellationTokenSource();
        canvas.Interactive = false;
        btnRenderImage.Enabled = false;
        btnCancel.Enabled = true;
        progressBar.Value = 0;
        Status(text);
    }

    private void EndBusy()
    {
        busy = false;
        cts?.Dispose();
        cts = null;
        canvas.Interactive = true;
        btnRenderImage.Enabled = true;
        btnCancel.Enabled = false;
        progressBar.Value = 0;
    }

    private static readonly Regex Percent = new(@"(\d+)%", RegexOptions.CultureInvariant);

    private void OnProgress(string text)
    {
        lblStatus.Text = text;
        var m = Percent.Match(text);
        if (m.Success && int.TryParse(m.Groups[1].Value, NumberStyles.Integer, Ci, out int pct))
            progressBar.Value = Math.Max(0, Math.Min(100, pct));
    }

    // ---- settings from the controls ------------------------------------------------------------

    private RenderSettings BuildSettings()
    {
        var s = new RenderSettings
        {
            Type = (FractalType)cmbType.SelectedIndex,
            Parameter = ParseDouble(txtParam.Text, "Parameter"),
            Coloring = (ColoringMode)cmbColoring.SelectedIndex,
            StripeDensity = ParseDouble(txtStripe.Text, "Stripe density"),
            NavSize = int.Parse((string)cmbNav.SelectedItem, Ci),
        };
        s.Precision.Perturbation = chkPert.Checked;
        s.Precision.Series = (SeriesType)cmbSeries.SelectedIndex;
        s.Precision.Rebasing = chkRebasing.Checked;
        s.Precision.ForceMultiPrecision = chkForceMP.Checked;
        s.Precision.Threshold = ParseDouble(txtThreshold.Text, "Threshold");
        s.Precision.MaxReferences = (int)numMaxRefs.Value;
        return s;
    }

    private ColorSettings BuildColors() => new()
    {
        Palette = SelectedPreset,
        SetColor = setColor,
        Offset = (int)numOffset.Value,
        Reverse = chkReverse.Checked,
        DivideIteration = ParseDouble(txtDivide.Text, "Divide iteration"),
        Bump = chkBump.Checked,
        BumpDirection = trkBumpDirection.Value,
        BumpElevation = trkBumpElevation.Value,
        BumpStrength = trkBumpStrength.Value,
    };

    // ---- display -------------------------------------------------------------------------------

    private void SetImage(Bitmap bmp)
    {
        var old = canvas.Image;
        canvas.Image = bmp;
        old?.Dispose();
    }

    private void ShowLocation(Location loc)
    {
        txtRe.Text = loc.CenterRe;
        txtIm.Text = loc.CenterIm;
        txtScale.Text = loc.PlotScale.ToString("R", Ci);
        txtIter.Text = loc.MaxIter.ToString(Ci);
    }

    private void ShowStats(RenderResult r, double colorMs, double? encodeMs, string? savedTo)
    {
        var sb = new StringBuilder();
        sb.AppendLine("size        " + r.Geometry);
        sb.AppendLine("zoom        " + r.Location.Zoom.ToString("0.###E+0", Ci) + "   (plot scale " + r.Location.PlotScale.ToString("0.###E+0", Ci) + ")");
        sb.AppendLine("mode        " + Precision.Name(r.Mode) + (r.DgMx > 0 ? ", " + r.DgMx + " digits" : ""));
        sb.AppendLine("references  " + r.References + (r.Skip > 0 ? "   series skip " + r.Skip : ""));
        if (r.Geometry.IsNavigation) sb.AppendLine("iterations  " + StatisticsText(r));
        if (r.Passes.Any(p => p.Glitched > 0))
            sb.AppendLine("glitched    " + string.Join(" > ", r.Passes.Select(p => p.Glitched.ToString(Ci))));
        sb.AppendLine("blended     " + r.Blended + "   unresolved " + r.Unresolved + "   NaN " + r.NonFinite);
        sb.Append("time        render " + r.EngineMs.ToString("0", Ci) + " ms, color " + colorMs.ToString("0", Ci) + " ms");
        if (encodeMs is double e) sb.Append(", PNG " + e.ToString("0", Ci) + " ms");
        sb.AppendLine();
        foreach (var w in r.Warnings) sb.AppendLine("warning     " + w);
        if (savedTo != null) sb.AppendLine("saved       " + savedTo);
        txtInfo.Text = sb.ToString();
    }

    private static string NotCleanText(RenderResult r) =>
        r.Unresolved + " pixels are unresolved glitches and " + r.NonFinite +
        " are NaN: those parts of the image are not trustworthy.";

    private string SuggestedName(Geometry g)
    {
        string zoom = current == null ? "" : "-" + current.Zoom.ToString("0.#E+0", Ci).Replace("+", "");
        return "fraqcel" + zoom + "-" + g.OutCSize + "x" + g.OutRSize + ".png";
    }

    private void Status(string text) => lblStatus.Text = text;

    /// <summary>
    /// The footer's text for the pixel under the pointer: its row and column, and its iteration value
    /// from the navigation render, as Fraqcel showed it before 2.8 (its author's suggestion,
    /// 2026-11-13). An in-set pixel holds the maximum, and says so.
    /// </summary>
    private string PixelText(Point q)
    {
        string where = "row " + q.Y + ", col " + q.X;
        var result = navResult;
        if (result == null) return where;
        var d = result.Data;
        // `Data` is 1-based ([1..rows, 1..cols]); the pixel is 0-based.
        int r = d.GetLowerBound(0) + q.Y, c = d.GetLowerBound(1) + q.X;
        if (r > d.GetUpperBound(0) || c > d.GetUpperBound(1)) return where;
        double v = d[r, c];
        return where + ", iteration " + Iteration(v) + (v >= result.MaxIter ? " (in set)" : "");
    }

    private static string Iteration(double v) => v == Math.Floor(v) ? v.ToString("0", Ci) : v.ToString("0.###", Ci);

    /// <summary>
    /// Fraqcel's Calculation Statistics for the navigation image: total, max and min of the
    /// iteration data (in-set pixels count as max iterations), shown as "--" for both stripe
    /// colorings, whose data aren't iteration counts. The total is in Excel's Scientific format.
    /// </summary>
    private static string StatisticsText(RenderResult r)
    {
        if (r.Settings.Coloring is ColoringMode.StripeFixedRange or ColoringMode.StripeIterationRange)
            return "total --, max --, min -- (stripe coloring)";
        var (sum, max, min) = r.Statistics();
        return "total " + sum.ToString("0.00E+00", Ci) + ", max " + Iteration(max) + ", min " + Iteration(min);
    }

    private static Exception Inner(Exception e) => e is AggregateException a && a.InnerExceptions.Count == 1 ? a.InnerException! : e;

    private static double ParseDouble(string text, string what)
    {
        if (!double.TryParse(text.Trim(), NumberStyles.Float, Ci, out double v) || double.IsNaN(v) || double.IsInfinity(v))
            throw new FormatException(what + ": not a number.");
        return v;
    }

    private static int ParseInt(string text, string what)
    {
        if (!int.TryParse(text.Trim(), NumberStyles.Integer, Ci, out int v)) throw new FormatException(what + ": not a whole number.");
        return v;
    }

    // ---- layout helpers ------------------------------------------------------------------------

    private static TextBox Box(string text = "") => new() { Text = text, Anchor = AnchorStyles.Left | AnchorStyles.Right };

    private static ComboBox Combo(params string[] items)
    {
        var c = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Anchor = AnchorStyles.Left | AnchorStyles.Right };
        c.Items.AddRange(items);
        return c;
    }

    private static CheckBox Check(string text, bool on = false) => new() { Text = text, Checked = on, AutoSize = true };

    private static Button Button(string text, EventHandler onClick)
    {
        var b = new Button { Text = text, AutoSize = true };
        b.Click += onClick;
        return b;
    }

    private static FlowLayoutPanel Buttons(params Control[] controls)
    {
        var f = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Margin = new Padding(0, 2, 0, 2) };
        f.Controls.AddRange(controls);
        return f;
    }

    /// <summary>A button just wide enough for its short text.</summary>
    private static Button SmallButton(string text) => new()
    {
        Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(34, 0),
        Margin = new Padding(3, 1, 0, 1),
    };

    private static TrackBar Slider(int min, int max, int value) => new()
    {
        Minimum = min, Maximum = max, Value = value, TickStyle = TickStyle.None, AutoSize = false, Height = 26,
        SmallChange = 1, LargeChange = 10, Dock = DockStyle.Fill,
    };

    private static Label ValueLabel() => new() { AutoSize = false, Width = 44, TextAlign = ContentAlignment.MiddleRight, Anchor = AnchorStyles.Right };

    /// <summary>A control that takes the row's width, followed by small controls at their own size.</summary>
    private static TableLayoutPanel Inline(Control fill, params Control[] rest)
    {
        var t = new TableLayoutPanel
        {
            ColumnCount = 1 + rest.Length, RowCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill, Margin = Padding.Empty,
        };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (var _ in rest) t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        if (fill is not TrackBar) fill.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        t.Controls.Add(fill, 0, 0);
        for (int i = 0; i < rest.Length; i++) t.Controls.Add(rest[i], i + 1, 0);
        return t;
    }

    private static Label Hint(string text) => new()
    {
        Text = text, AutoSize = true, MaximumSize = new Size(380, 0), ForeColor = SystemColors.GrayText,
        Margin = new Padding(3, 2, 3, 6),
    };

    private static void Header(TableLayoutPanel t, string text)
    {
        var l = new Label
        {
            Text = text, AutoSize = true, Margin = new Padding(0, t.RowCount == 0 ? 2 : 12, 0, 4),
        };
        l.Font = new Font(l.Font, FontStyle.Bold);
        Full(t, l);
    }

    private static void Row(TableLayoutPanel t, string label, Control c)
    {
        int row = t.RowCount++;
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        t.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 6, 8, 3) }, 0, row);
        t.Controls.Add(c, 1, row);
    }

    private static void Full(TableLayoutPanel t, Control c)
    {
        int row = t.RowCount++;
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        t.Controls.Add(c, 0, row);
        t.SetColumnSpan(c, 2);
    }

    // ---- self-test -----------------------------------------------------------------------------

    /// <summary>
    /// Renders the starting view, checks the panel's controls, zooms into a box and with a click
    /// through the same handlers the mouse uses, renders an image, checks saved locations and the
    /// session, and writes the results to <paramref name="dir"/>. It never writes the real settings
    /// file: everything it saves goes to files in <paramref name="dir"/>.
    /// </summary>
    private async Task RunSelfTestAsync(string dir)
    {
        Directory.CreateDirectory(dir);
        var log = new StringBuilder();
        bool pass = true;
        void Expect(bool ok, string what)
        {
            log.AppendLine((ok ? "PASS " : "FAIL ") + what);
            pass &= ok;
        }
        var screen = Screen.FromControl(this);
        log.AppendLine("dpi " + DeviceDpi + ", awareness " + WindowPlacement.DpiAwareness() + ", screen " + screen.DeviceName + " " + screen.Bounds +
                       ", window " + Bounds + ", canvas " + canvas.ClientSize.Width + "x" + canvas.ClientSize.Height);

        // The title carries the version as a plain number (no "+commit"), read from the assembly.
        Expect(Version.TryParse(DisplayVersion, out _) && Text.StartsWith("Fraqcel viewer " + DisplayVersion + " ", StringComparison.Ordinal),
               "title shows the version: \"" + Text + "\"");

        Expect(await RenderNavAsync(Engine.Location.FullView, false), "render the starting view");
        canvas.Image?.Save(Path.Combine(dir, "1-start.png"));
        SaveWindow(this, Path.Combine(dir, "1-window.png"));

        // The footer's iteration value (2026-11-13): over every pixel, it says "in set" where the
        // DLL's own image shows the interior color (the center's), so the 1-based data and the
        // 0-based pixel are indexed alike. CONTROL: one pixel to the right, it must disagree far
        // more (a shifted index differs only along the set's edge, so this is the test's teeth).
        if (canvas.Image is Bitmap img && navResult != null)
        {
            int argbIn = img.GetPixel(img.Width / 2, img.Height / 2).ToArgb();
            int inSet = 0, disagree = 0, shifted = 0;
            for (int y = 0; y < img.Height; y++)
            {
                for (int x = 0; x < img.Width; x++)
                {
                    bool looksIn = img.GetPixel(x, y).ToArgb() == argbIn;
                    bool saysIn = PixelText(new Point(x, y)).EndsWith("(in set)");
                    if (saysIn) inSet++;
                    if (saysIn != looksIn) disagree++;
                    if (x + 1 < img.Width && PixelText(new Point(x + 1, y)).EndsWith("(in set)") != looksIn) shifted++;
                }
            }
            Expect(inSet > 1000 && disagree * 5 < shifted,
                   "the footer says in set where the image shows the set: " + inSet + " in set, " + disagree + " disagree, " + shifted +
                   " one pixel off; the center reads \"" + PixelText(new Point(img.Width / 2, img.Height / 2)) + "\", a corner \"" +
                   PixelText(new Point(0, 0)) + "\"");
        }

        // The controls added on 2026-11-21 (MainForm.FeatureChecks.cs); they end back at the starting view.
        await CheckStartViewFeaturesAsync(dir, Expect);

        // Box around the seahorse valley, image rows 360..400, columns 330..370.
        var before = current!;
        OnBoxSelected(360, 330, 400, 370);
        while (busy) await Task.Delay(50);
        var expected = Navigator.Move(before, 800, CellSelection.Box(360, 330, 400, 370), ZoomDirection.In, 4, new PrecisionSettings());
        Expect(current != null && current.CenterRe == expected.CenterRe && current.CenterIm == expected.CenterIm &&
              current.PlotScale == expected.PlotScale, "box zoom lands on " + expected);
        canvas.Image?.Save(Path.Combine(dir, "2-box-zoom.png"));

        // A click zooms 4x around the clicked pixel.
        before = current!;
        OnPixelClicked(399, 399, MouseButtons.Left);
        while (busy) await Task.Delay(50);
        Expect(current != null && Math.Abs(current.PlotScale - before.PlotScale / 4) < 1e-15 * before.PlotScale,
              "click zooms by the factor: " + current);
        canvas.Image?.Save(Path.Combine(dir, "3-click-zoom.png"));

        // Back returns to the box zoom.
        Back();
        while (busy) await Task.Delay(50);
        Expect(current != null && current.PlotScale == before.PlotScale, "back returns to the previous view");

        // Fraqcel's deep sample: perturbation, series and glitch correction.
        Expect(await RenderNavAsync(Engine.Location.FraqcelSuggested, true), "render Fraqcel's deep sample");
        Expect(navResult != null && navResult.IsClean && navResult.References > 1,
              "deep sample is clean after glitch correction (" + navResult?.Summary() + ")");
        canvas.Image?.Save(Path.Combine(dir, "4-deep.png"));
        SaveWindow(this, Path.Combine(dir, "4-window.png"));

        // Render an image through the same path as the button (minus the file dialog).
        string png = Path.Combine(dir, "5-image-640x360.png");
        Expect(await RenderImageToAsync(png, Geometry.Publish(640, 360), BuildSettings(), BuildColors()) &&
              ImageWriter.EndsWithIend(png), "render image to " + png);

        // Saved locations and the session, at the deep sample, in DIR's own settings files.
        await CheckSavedAndSessionAsync(dir, Expect);

        // The DLL dialog, as Change… opens it, saving to a settings file in DIR (never the real one).
        string testSettings = Path.Combine(dir, "settings.ini");
        File.Delete(testSettings);
        string inUse = FraqcelDll.LoadedPath!;
        string inUseFolder = Path.GetDirectoryName(inUse)!;
        using (var dlg = new FraqcelSetupForm(FraqcelSetupForm.Purpose.Change, new HostSettings(), testSettings, null, Screen.FromControl(this)))
        {
            dlg.Show(this);
            await Task.Delay(300);
            // The link for someone without Fraqcel: one link, on the site's name, to the download page.
            // Not clicked here: that would open the browser.
            var links = dlg.DownloadLinkLabel.Links;
            Expect(links.Count == 1 && (string)links[0].LinkData == FraqcelDll.DownloadPage &&
                   dlg.DownloadLinkLabel.Text.Substring(links[0].Start, links[0].Length) == "fraqcel.weebly.com",
                   "the dialog links to Fraqcel's download page: \"" + dlg.DownloadLinkLabel.Text + "\" -> " +
                   (links.Count > 0 ? links[0].LinkData : "no link"));
            dlg.DllBox.Text = Application.ExecutablePath;
            Expect(!dlg.CheckPassed && dlg.CheckText.StartsWith("Unknown Fraqcel DLL"), "the dialog refuses another file: " + dlg.CheckText);
            dlg.OkButton.PerformClick();
            Expect(dlg.Visible && !File.Exists(testSettings), "OK on a refused file keeps the dialog open and saves nothing");

            dlg.FolderBox.Text = ""; // so the next line is a change even if it is the folder shown
            dlg.FolderBox.Text = inUseFolder;
            Expect(string.Equals(dlg.DllBox.Text, inUse, StringComparison.OrdinalIgnoreCase),
                   "choosing the folder fills in its DLL: " + dlg.DllBox.Text);
            Expect(dlg.CheckPassed, "the dialog accepts it: " + dlg.CheckText);
            await Task.Delay(100);
            SaveWindow(dlg, Path.Combine(dir, "6-dll-dialog.png"));
            dlg.OkButton.PerformClick();
            var saved = HostSettings.Load(testSettings);
            Expect(!dlg.Visible && saved.DllPath == inUse && saved.FraqcelFolder == inUseFolder,
                   "OK saves folder " + saved.FraqcelFolder + " and DLL " + saved.DllPath);
        }

        log.AppendLine(pass ? "ALL PASS" : "SOME FAILED");
        File.WriteAllText(Path.Combine(dir, "selftest.log"), log.ToString());
        ExitCode = pass ? 0 : 1;
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        WindowPlacement.FixDpi(this);
    }

    private static void SaveWindow(Control c, string path)
    {
        using var bmp = new Bitmap(c.Width, c.Height);
        c.DrawToBitmap(bmp, new Rectangle(0, 0, c.Width, c.Height));
        bmp.Save(path);
    }
}
