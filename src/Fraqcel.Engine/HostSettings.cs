// Part of the unofficial Fraqcel host prototype.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Fraqcel.Engine;

/// <summary>
/// The host's saved settings: a small text file of <c>key=value</c> lines.
/// <list type="bullet">
/// <item>Keys are case-insensitive. A later duplicate replaces an earlier one.</item>
/// <item>Keys this version doesn't know are kept when the file is saved again.</item>
/// <item>Blank lines and lines starting with # or ; are ignored, and not kept.</item>
/// </list>
/// </summary>
public sealed class HostSettings
{
    public const string FolderKey = "fraqcel_folder";
    public const string DllKey = "fraqcel_dll";

    private readonly List<KeyValuePair<string, string>> entries = new();

    /// <summary>%LOCALAPPDATA%\FraqcelHost\settings.ini: per machine, since it holds paths.</summary>
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FraqcelHost", "settings.ini");

    /// <summary>Set when the file exists but could not be read; the settings are then empty.</summary>
    public string? ReadProblem { get; private set; }

    /// <summary>The folder Fraqcel is installed in.</summary>
    public string? FraqcelFolder
    {
        get => Get(FolderKey);
        set => Set(FolderKey, value);
    }

    /// <summary>The Fraqcel DLL to load.</summary>
    public string? DllPath
    {
        get => Get(DllKey);
        set => Set(DllKey, value);
    }

    /// <summary>The start of each saved location's key: <c>location.1</c>, <c>location.2</c>, and so on.</summary>
    public const string LocationPrefix = "location.";

    /// <summary>
    /// The saved locations, in the order of their key numbers. Entries that can't be read are
    /// skipped here; setting the list replaces every <c>location.N</c> key, so it drops them.
    /// </summary>
    public IReadOnlyList<SavedLocation> Locations
    {
        get
        {
            var found = new List<(int N, SavedLocation Saved)>();
            foreach (var e in entries)
            {
                if (!e.Key.StartsWith(LocationPrefix, StringComparison.OrdinalIgnoreCase)) continue;
                if (!int.TryParse(e.Key.Substring(LocationPrefix.Length), System.Globalization.NumberStyles.None,
                        System.Globalization.CultureInfo.InvariantCulture, out int n)) continue;
                if (SavedLocation.TryParse(e.Value, out var saved)) found.Add((n, saved!));
            }
            found.Sort((a, b) => a.N.CompareTo(b.N));
            return found.ConvertAll(f => f.Saved);
        }
        set
        {
            entries.RemoveAll(e => e.Key.StartsWith(LocationPrefix, StringComparison.OrdinalIgnoreCase));
            int n = 1;
            foreach (var saved in value) Set(LocationPrefix + n++.ToString(System.Globalization.CultureInfo.InvariantCulture), saved.ToSettingsValue());
        }
    }

    public string? Get(string key)
    {
        int i = IndexOf(key);
        return i >= 0 && entries[i].Value.Length > 0 ? entries[i].Value : null;
    }

    /// <summary>Sets a value; null or empty removes the key.</summary>
    public void Set(string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(key) || key.IndexOfAny(new[] { '=', '\r', '\n' }) >= 0 || key.Trim() != key)
            throw new ArgumentException("Not a usable settings key: " + key, nameof(key));
        if (value != null && value.IndexOfAny(new[] { '\r', '\n' }) >= 0)
            throw new ArgumentException("A settings value can't contain a line break.", nameof(value));

        int i = IndexOf(key);
        string v = value?.Trim() ?? "";
        if (v.Length == 0)
        {
            if (i >= 0) entries.RemoveAt(i);
        }
        else if (i >= 0)
        {
            entries[i] = new KeyValuePair<string, string>(entries[i].Key, v);
        }
        else
        {
            entries.Add(new KeyValuePair<string, string>(key, v));
        }
    }

    /// <summary>
    /// Reads the file. A missing file gives empty settings. A file that exists but can't be read
    /// also gives empty settings, with <see cref="ReadProblem"/> saying why.
    /// </summary>
    public static HostSettings Load(string path)
    {
        var s = new HostSettings();
        string[] lines;
        try
        {
            if (!File.Exists(path)) return s;
            lines = File.ReadAllLines(path, Encoding.UTF8);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            s.ReadProblem = "Can't read the settings file " + path + ": " + e.Message;
            return s;
        }

        foreach (string raw in lines)
        {
            string line = raw.Trim();
            if (line.Length == 0 || line[0] == '#' || line[0] == ';') continue;
            int eq = line.IndexOf('=');
            if (eq <= 0) continue;
            string key = line.Substring(0, eq).Trim();
            if (key.Length == 0) continue;
            s.Set(key, line.Substring(eq + 1));
        }
        return s;
    }

    /// <summary>Writes the file through a temporary file, so a failed write leaves the old one whole.</summary>
    /// <exception cref="IOException">The file could not be written.</exception>
    /// <exception cref="UnauthorizedAccessException">The folder or file is not writable.</exception>
    public void Save(string path)
    {
        string full = Path.GetFullPath(path);
        string? dir = Path.GetDirectoryName(full);
        if (dir != null) Directory.CreateDirectory(dir);

        var sb = new StringBuilder();
        sb.Append("# Fraqcel host settings (unofficial prototype). One key=value per line.\r\n");
        foreach (var e in entries) sb.Append(e.Key).Append('=').Append(e.Value).Append("\r\n");

        string tmp = full + ".tmp";
        File.WriteAllText(tmp, sb.ToString(), new UTF8Encoding(false));
        if (File.Exists(full)) File.Replace(tmp, full, null);
        else File.Move(tmp, full);
    }

    private int IndexOf(string key) =>
        entries.FindIndex(e => string.Equals(e.Key, key, StringComparison.OrdinalIgnoreCase));
}
