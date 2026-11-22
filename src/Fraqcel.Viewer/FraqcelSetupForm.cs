// Part of the unofficial Fraqcel host prototype.

using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Fraqcel.Engine;
using Fraqcel.Engine.Dll;

namespace Fraqcel.Viewer;

/// <summary>
/// Asks where Fraqcel is: its folder, and the DLL in it. The choice is saved to the settings file.
/// <list type="bullet">
/// <item>At start-up, OK loads the DLL, and the viewer then opens.</item>
/// <item>From the viewer (Change…), OK only checks the DLL: a running viewer keeps the DLL it
/// started with, so a new choice takes effect at the next start.</item>
/// </list>
/// </summary>
internal sealed class FraqcelSetupForm : Form
{
    internal enum Purpose { Startup, Change }

    /// <summary>Where Fraqcel's own instructions put the DLLs.</summary>
    private const string FraqcelInstructionsFolder = @"C:\fraqcel";

    private static readonly Color Good = Color.FromArgb(0, 110, 40);
    private static readonly Color Bad = Color.FromArgb(180, 20, 20);

    private readonly Purpose purpose;
    private readonly HostSettings settings;
    private readonly string settingsPath;
    private readonly Screen screen;

    private readonly TextBox txtFolder = new() { Width = 460, Anchor = AnchorStyles.Left | AnchorStyles.Right };
    private readonly TextBox txtDll = new() { Width = 460, Anchor = AnchorStyles.Left | AnchorStyles.Right };
    private readonly Label lblCheck = new() { AutoSize = true, MaximumSize = new Size(460, 0), Margin = new Padding(3, 2, 3, 8) };
    private readonly Button btnOk = new() { Text = "OK", AutoSize = true, MinimumSize = new Size(88, 0) };
    private readonly Button btnCancel = new() { AutoSize = true, MinimumSize = new Size(88, 0), DialogResult = DialogResult.Cancel };
    private bool syncing;

    /// <param name="problem">Why the dialog is shown (for example, the saved DLL failed), or null.</param>
    /// <param name="screen">The monitor to open on.</param>
    public FraqcelSetupForm(Purpose purpose, HostSettings settings, string settingsPath, string? problem, Screen screen)
    {
        this.purpose = purpose;
        this.settings = settings;
        this.settingsPath = settingsPath;
        this.screen = screen;

        SuspendLayout();
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = "Fraqcel viewer: choose Fraqcel's DLL";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = purpose == Purpose.Startup; // at start-up there is no other window yet
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        WindowPlacement.CreateOn(this, screen);

        var t = new TableLayoutPanel
        {
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 3,
            Padding = new Padding(12, 12, 12, 10),
        };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        Span(t, 0, 3, Wrapped(
            "The viewer runs Fraqcel's own DLL, unmodified. Choose the folder Fraqcel is installed in, " +
            "and its 64-bit DLL, " + FraqcelDll.FileNameX64 + ". Fraqcel's instructions put it in " +
            FraqcelInstructionsFolder + ". The viewer saves your choice and uses it from then on.", SystemColors.ControlText));
        if (problem != null) Span(t, 0, 3, Wrapped(problem, Bad));

        Field(t, "Fraqcel folder", txtFolder, "Browse…", BrowseFolder);
        Field(t, "Fraqcel DLL", txtDll, "Browse…", BrowseDll);
        Span(t, 1, 2, lblCheck);
        Span(t, 0, 3, Wrapped("Saved in " + settingsPath, SystemColors.GrayText));

        btnCancel.Text = purpose == Purpose.Startup ? "Exit" : "Cancel";
        var buttons = new FlowLayoutPanel
        {
            AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Anchor = AnchorStyles.Right,
            Margin = new Padding(0, 6, 0, 0),
        };
        buttons.Controls.Add(btnCancel); // right-to-left: Cancel ends up on the right
        buttons.Controls.Add(btnOk);
        Span(t, 0, 3, buttons);

        Controls.Add(t);
        AcceptButton = btnOk;
        CancelButton = btnCancel;
        ResumeLayout(true);

        txtFolder.TextChanged += (_, _) => OnFolderChanged();
        txtDll.TextChanged += (_, _) => UpdateCheck();
        btnOk.Click += (_, _) => Accept();

        // Start from the saved choice, or else Fraqcel's usual folder if it is there.
        syncing = true;
        txtFolder.Text = settings.FraqcelFolder ?? (Directory.Exists(FraqcelInstructionsFolder) ? FraqcelInstructionsFolder : "");
        txtDll.Text = settings.DllPath ?? FraqcelDll.FindIn(txtFolder.Text) ?? "";
        syncing = false;
        UpdateCheck();
    }

    // For the viewer's self-test, which drives the dialog the way a person would.
    internal TextBox FolderBox => txtFolder;
    internal TextBox DllBox => txtDll;
    internal Button OkButton => btnOk;
    internal string CheckText => lblCheck.Text;
    internal bool CheckPassed { get; private set; }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        WindowPlacement.FixDpi(this);
        WindowPlacement.CenterOn(this, screen);
    }

    // ---- behavior ------------------------------------------------------------------------------

    /// <summary>A new folder fills in the DLL found in it, if any.</summary>
    private void OnFolderChanged()
    {
        if (syncing) return;
        string? found = FraqcelDll.FindIn(txtFolder.Text.Trim());
        syncing = true;
        if (found != null) txtDll.Text = found;
        else if (!IsInside(txtDll.Text.Trim(), txtFolder.Text.Trim())) txtDll.Text = "";
        syncing = false;
    }

    private void UpdateCheck()
    {
        string dll = txtDll.Text.Trim();
        CheckPassed = false;
        if (dll.Length == 0)
        {
            SetCheck("Choose the DLL, or a folder that has it.", SystemColors.GrayText);
            return;
        }
        try
        {
            FraqcelDll.Check(dll);
            CheckPassed = true;
            SetCheck("Fraqcel 2.15.0, 64-bit: the file matches the released DLL.", Good);
        }
        catch (DllLoadException e)
        {
            SetCheck(e.Message, Bad);
        }
    }

    private void Accept()
    {
        string dll = txtDll.Text.Trim();
        string folder = txtFolder.Text.Trim();
        if (dll.Length == 0)
        {
            SetCheck("Choose the DLL, or a folder that has it.", Bad);
            return;
        }
        if (folder.Length > 0 && !Directory.Exists(folder))
        {
            SetCheck("Folder not found: " + folder, Bad);
            return;
        }
        try
        {
            // At start-up this is the real load; its checks run again on the open file.
            if (purpose == Purpose.Startup) FraqcelDll.Load(dll);
            else FraqcelDll.Check(dll);
        }
        catch (DllLoadException e)
        {
            CheckPassed = false;
            SetCheck(e.Message, Bad);
            return;
        }

        string fullDll = Path.GetFullPath(dll);
        settings.DllPath = fullDll;
        settings.FraqcelFolder = folder.Length > 0 ? Path.GetFullPath(folder) : Path.GetDirectoryName(fullDll);
        try
        {
            settings.Save(settingsPath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, "The DLL is fine, but the choice couldn't be saved, so the viewer will ask again next time." +
                                  Environment.NewLine + Environment.NewLine + e.Message,
                            Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        if (purpose == Purpose.Change && FraqcelDll.LoadedPath != null &&
            !string.Equals(fullDll, FraqcelDll.LoadedPath, StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(this, "Saved. The viewer uses this DLL the next time it starts.", Text,
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        DialogResult = DialogResult.OK;
        Close();
    }

    private void BrowseFolder()
    {
        using var dlg = new FolderBrowserDialog
        {
            Description = "Choose the folder Fraqcel is installed in.",
            ShowNewFolderButton = false,
        };
        if (Directory.Exists(txtFolder.Text.Trim())) dlg.SelectedPath = txtFolder.Text.Trim();
        if (dlg.ShowDialog(this) == DialogResult.OK) txtFolder.Text = dlg.SelectedPath; // fills in the DLL
    }

    private void BrowseDll()
    {
        using var dlg = new OpenFileDialog
        {
            Title = "Choose Fraqcel's 64-bit DLL",
            Filter = "Fraqcel DLL (FraqcelLib*.dll)|FraqcelLib*.dll|DLL files (*.dll)|*.dll|All files|*.*",
            FileName = FraqcelDll.FileNameForThisProcess,
        };
        string folder = txtFolder.Text.Trim();
        if (Directory.Exists(folder)) dlg.InitialDirectory = folder;
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        syncing = true;
        txtDll.Text = dlg.FileName;
        if (folder.Length == 0 || !IsInside(dlg.FileName, folder))
            txtFolder.Text = Path.GetDirectoryName(dlg.FileName) ?? "";
        syncing = false;
    }

    private void SetCheck(string text, Color color)
    {
        lblCheck.Text = text;
        lblCheck.ForeColor = color;
    }

    /// <summary>True if <paramref name="file"/> is in <paramref name="folder"/> or below it.</summary>
    private static bool IsInside(string file, string folder)
    {
        if (file.Length == 0 || folder.Length == 0) return false;
        try
        {
            string f = Path.GetFullPath(folder).TrimEnd('\\') + "\\";
            return Path.GetFullPath(file).StartsWith(f, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    // ---- layout helpers ------------------------------------------------------------------------

    private static Label Wrapped(string text, Color color) => new()
    {
        Text = text, AutoSize = true, MaximumSize = new Size(640, 0), ForeColor = color, Margin = new Padding(3, 2, 3, 8),
    };

    private static void Field(TableLayoutPanel t, string label, TextBox box, string buttonText, Action onClick)
    {
        int row = t.RowCount++;
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        t.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 6, 8, 3) }, 0, row);
        t.Controls.Add(box, 1, row);
        var b = new Button { Text = buttonText, AutoSize = true, Anchor = AnchorStyles.Left };
        b.Click += (_, _) => onClick();
        t.Controls.Add(b, 2, row);
    }

    private static void Span(TableLayoutPanel t, int column, int span, Control c)
    {
        int row = t.RowCount++;
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        t.Controls.Add(c, column, row);
        t.SetColumnSpan(c, span);
    }
}
