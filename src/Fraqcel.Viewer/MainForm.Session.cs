// Part of the unofficial Fraqcel host prototype.

using System;
using System.Linq;
using System.Windows.Forms;
using Fraqcel.Engine;

namespace Fraqcel.Viewer;

/// <summary>
/// The viewer's state between runs, as Fraqcel's workbook keeps its own: the controls and the
/// last view, in the settings file under <c>viewer.</c> keys.
/// </summary>
internal sealed partial class MainForm
{
    private const string SessionPrefix = "viewer.";

    /// <summary>Writes the controls and the rendered view into <paramref name="s"/>. The caller saves the file.</summary>
    private void SaveSession(HostSettings s)
    {
        void Put(string key, string value)
        {
            // A pasted line break would make the file unreadable; the key keeps the rest of the text.
            string clean = new string(value.Select(ch => char.IsControl(ch) ? ' ' : ch).ToArray()).Trim();
            s.Set(SessionPrefix + key, clean);
        }
        string Bool(bool b) => b ? "true" : "false";
        string Int(decimal d) => ((int)d).ToString(Ci);

        Put("type", ((FractalType)cmbType.SelectedIndex).ToString());
        Put("parameter", txtParam.Text);
        Put("nav_size", cmbNav.Text);
        Put("coloring", ((ColoringMode)cmbColoring.SelectedIndex).ToString());
        Put("stripe_density", txtStripe.Text);
        Put("palette", SelectedPreset.Name);
        var sc = ToColor(setColor);
        Put("set_color", "#" + sc.R.ToString("X2", Ci) + sc.G.ToString("X2", Ci) + sc.B.ToString("X2", Ci));
        Put("offset", Int(numOffset.Value));
        Put("divide", txtDivide.Text);
        Put("reverse", Bool(chkReverse.Checked));
        Put("bump", Bool(chkBump.Checked));
        Put("bump_direction", trkBumpDirection.Value.ToString(Ci));
        Put("bump_elevation", trkBumpElevation.Value.ToString(Ci));
        Put("bump_strength", trkBumpStrength.Value.ToString(Ci));
        Put("perturbation", Bool(chkPert.Checked));
        Put("series", ((SeriesType)cmbSeries.SelectedIndex).ToString());
        Put("rebasing", Bool(chkRebasing.Checked));
        Put("force_mp", Bool(chkForceMP.Checked));
        Put("threshold", txtThreshold.Text);
        Put("max_refs", Int(numMaxRefs.Value));
        Put("zoom_factor", txtZoomFactor.Text);
        Put("image_width", Int(numWidth.Value));
        Put("image_height", Int(numHeight.Value));
        Put("samples", cmbSamples.Text);
        if (current != null && navResult != null)
        {
            Put("view", new SavedLocation("last view", current).ToSettingsValue());
            Put("view_ms", navResult.EngineMs.ToString("0", Ci));
        }
    }

    /// <summary>
    /// Sets the controls from <paramref name="s"/> and returns the last view and how long it took
    /// to render. A missing or unreadable value leaves its control as it is.
    /// </summary>
    private (Location? View, double? ViewMs) RestoreSession(HostSettings s)
    {
        void Use(string key, Action<string> apply)
        {
            string? v = s.Get(SessionPrefix + key);
            if (v == null) return;
            try { apply(v); }
            catch (Exception e) when (e is FormatException or ArgumentException or OverflowException) { }
        }
        static T Named<T>(string v) where T : struct, Enum =>
            Enum.TryParse(v, true, out T e) && Enum.IsDefined(typeof(T), e) ? e : throw new FormatException(v);
        static bool Bool(string v) => bool.Parse(v);
        static void Pick(ComboBox c, int index) { if (index >= 0 && index < c.Items.Count) c.SelectedIndex = index; }
        static void PickText(ComboBox c, string text) => Pick(c, c.Items.IndexOf(text));
        static void Clamp(NumericUpDown n, int v) => n.Value = Math.Max(n.Minimum, Math.Min(n.Maximum, v));
        static void Slide(TrackBar t, int v) => t.Value = Math.Max(t.Minimum, Math.Min(t.Maximum, v));
        // A text box only takes a value that reads as a number.
        static void Number(TextBox t, string v)
        {
            ParseDouble(v, t.Name);
            t.Text = v;
        }

        Use("type", v => Pick(cmbType, (int)Named<FractalType>(v)));
        Use("parameter", v => Number(txtParam, v));
        Use("nav_size", v => PickText(cmbNav, v));
        Use("coloring", v => Pick(cmbColoring, (int)Named<ColoringMode>(v)));
        Use("stripe_density", v => Number(txtStripe, v));
        Use("palette", v => Pick(cmbPalette, PalettePresets.All.ToList().IndexOf(PalettePresets.Find(v))));
        // After the palette, which sets the preset's own set color.
        Use("set_color", v =>
        {
            if (v.Length != 7 || v[0] != '#') throw new FormatException(v);
            int rgb = int.Parse(v.Substring(1), System.Globalization.NumberStyles.HexNumber, Ci);
            SetSetColor(PaletteBuilder.Rgb(rgb >> 16 & 255, rgb >> 8 & 255, rgb & 255));
        });
        Use("offset", v => Clamp(numOffset, ParseInt(v, "offset")));
        Use("divide", v => Number(txtDivide, v));
        Use("reverse", v => chkReverse.Checked = Bool(v));
        Use("bump", v => chkBump.Checked = Bool(v));
        Use("bump_direction", v => Slide(trkBumpDirection, ParseInt(v, "bump_direction")));
        Use("bump_elevation", v => Slide(trkBumpElevation, ParseInt(v, "bump_elevation")));
        Use("bump_strength", v => Slide(trkBumpStrength, ParseInt(v, "bump_strength")));
        Use("perturbation", v => chkPert.Checked = Bool(v));
        Use("series", v => Pick(cmbSeries, (int)Named<SeriesType>(v)));
        Use("rebasing", v => chkRebasing.Checked = Bool(v));
        Use("force_mp", v => chkForceMP.Checked = Bool(v));
        Use("threshold", v => Number(txtThreshold, v));
        Use("max_refs", v => Clamp(numMaxRefs, ParseInt(v, "max_refs")));
        Use("zoom_factor", v => Number(txtZoomFactor, v));
        Use("image_width", v => Clamp(numWidth, ParseInt(v, "image_width")));
        Use("image_height", v => Clamp(numHeight, ParseInt(v, "image_height")));
        Use("samples", v => PickText(cmbSamples, v));
        UpdateBumpControls();

        Location? view = null;
        double? viewMs = null;
        Use("view", v => { if (SavedLocation.TryParse(v, out var saved)) view = saved!.Location; });
        Use("view_ms", v => viewMs = ParseDouble(v, "view_ms"));
        return (view, viewMs);
    }
}
