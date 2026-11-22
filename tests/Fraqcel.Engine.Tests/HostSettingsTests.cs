using System;
using System.IO;
using Fraqcel.Engine.Dll;
using Xunit;

namespace Fraqcel.Engine.Tests;

/// <summary>A fresh folder under %TEMP% for one test, deleted afterward.</summary>
internal sealed class TempFolder : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "fraqcel-tests-" + Guid.NewGuid().ToString("N"));
    public TempFolder() => Directory.CreateDirectory(Path);
    public string File(string name) => System.IO.Path.Combine(Path, name);
    public void Dispose()
    {
        try { Directory.Delete(Path, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}

public class HostSettingsTests
{
    [Fact]
    public void Save_and_load_round_trip_and_keep_unknown_keys()
    {
        using var tmp = new TempFolder();
        string path = tmp.File(@"sub\settings.ini"); // the folder is created on save
        var s = new HostSettings { FraqcelFolder = @"C:\fraqcel", DllPath = @"C:\fraqcel\FraqcelLib2150_x64.dll" };
        s.Set("later_tool_key", "kept");
        s.Save(path);

        var back = HostSettings.Load(path);
        Assert.Null(back.ReadProblem);
        Assert.Equal(@"C:\fraqcel", back.FraqcelFolder);
        Assert.Equal(@"C:\fraqcel\FraqcelLib2150_x64.dll", back.DllPath);
        Assert.Equal("kept", back.Get("LATER_TOOL_KEY")); // keys are case-insensitive

        back.DllPath = @"D:\other\FraqcelLib2150_x64.dll";
        back.Save(path); // replaces the existing file
        var again = HostSettings.Load(path);
        Assert.Equal(@"D:\other\FraqcelLib2150_x64.dll", again.DllPath);
        Assert.Equal("kept", again.Get("later_tool_key"));
        Assert.False(System.IO.File.Exists(path + ".tmp"));
    }

    [Fact]
    public void A_missing_file_is_empty_with_no_problem()
    {
        using var tmp = new TempFolder();
        var s = HostSettings.Load(tmp.File("none.ini"));
        Assert.Null(s.ReadProblem);
        Assert.Null(s.DllPath);
        Assert.Null(s.FraqcelFolder);
    }

    [Fact]
    public void An_unreadable_file_is_empty_and_says_why()
    {
        using var tmp = new TempFolder();
        string path = tmp.File("locked.ini");
        System.IO.File.WriteAllText(path, "fraqcel_dll=C:\\x.dll\r\n");
        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var s = HostSettings.Load(path);
            Assert.NotNull(s.ReadProblem);
            Assert.Null(s.DllPath);
        }
    }

    [Fact]
    public void Comments_blank_and_malformed_lines_are_skipped_and_later_duplicates_win()
    {
        using var tmp = new TempFolder();
        string path = tmp.File("s.ini");
        System.IO.File.WriteAllText(path,
            "\uFEFF# comment\r\n\r\n; another\r\nno equals sign\r\n=no key\r\n" +
            "fraqcel_dll = C:\\a\\first.dll \r\nFRAQCEL_DLL=C:\\a\\second.dll\r\nfraqcel_folder=\r\n");
        var s = HostSettings.Load(path);
        Assert.Null(s.ReadProblem);
        Assert.Equal(@"C:\a\second.dll", s.DllPath);
        Assert.Null(s.FraqcelFolder); // an empty value is no value
    }

    [Fact]
    public void Values_with_line_breaks_are_refused()
    {
        var s = new HostSettings();
        Assert.Throws<ArgumentException>(() => s.DllPath = "C:\\a\r\nfraqcel_folder=C:\\b");
        Assert.Throws<ArgumentException>(() => s.Set("a=b", "x"));
    }
}

public class DllLocationTests
{
    [Fact]
    public void FindIn_finds_the_DLL_in_the_folder_then_in_a_subfolder()
    {
        using var tmp = new TempFolder();
        Assert.Null(FraqcelDll.FindIn(tmp.Path));

        Directory.CreateDirectory(tmp.File("b"));
        Directory.CreateDirectory(tmp.File("a"));
        System.IO.File.WriteAllText(tmp.File(@"b\" + FraqcelDll.FileNameX64), "x");
        System.IO.File.WriteAllText(tmp.File(@"a\" + FraqcelDll.FileNameX64), "x");
        Assert.Equal(tmp.File(@"a\" + FraqcelDll.FileNameX64), FraqcelDll.FindIn(tmp.Path)); // name order

        System.IO.File.WriteAllText(tmp.File(FraqcelDll.FileNameX64), "x");
        Assert.Equal(tmp.File(FraqcelDll.FileNameX64), FraqcelDll.FindIn(tmp.Path)); // the folder itself first
    }

    [Fact]
    public void FindIn_is_null_for_a_missing_blank_or_malformed_folder()
    {
        Assert.Null(FraqcelDll.FindIn(@"C:\no\such\folder\here"));
        Assert.Null(FraqcelDll.FindIn(""));
        Assert.Null(FraqcelDll.FindIn("C:\\a|b"));
    }

    [Fact]
    public void Check_accepts_the_stock_DLL_without_loading_another()
    {
        string stock = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, FraqcelDll.FileNameX64);
        Assert.Equal("51c575eabbb5757eae7046440600b041debabc332f64482fb28903152ab8cf46", FraqcelDll.Check(stock));
    }

    [Fact]
    public void Check_refuses_missing_short_unknown_and_malformed_paths()
    {
        using var tmp = new TempFolder();
        string shortFile = tmp.File("short.dll");
        System.IO.File.WriteAllText(shortFile, "not a dll");
        string notPe = tmp.File("zeros.dll");
        System.IO.File.WriteAllBytes(notPe, new byte[4096]);

        Assert.Contains("not found", Assert.Throws<DllLoadException>(() => FraqcelDll.Check(tmp.File("none.dll"))).Message);
        Assert.Contains("Not a PE file", Assert.Throws<DllLoadException>(() => FraqcelDll.Check(shortFile)).Message);
        Assert.Contains("Not a PE file", Assert.Throws<DllLoadException>(() => FraqcelDll.Check(notPe)).Message);
        // This test assembly is a real 64-bit PE file, but not Fraqcel's.
        string self = typeof(DllLocationTests).Assembly.Location;
        Assert.Contains("Unknown Fraqcel DLL", Assert.Throws<DllLoadException>(() => FraqcelDll.Check(self)).Message);
        Assert.Contains("Not a usable path", Assert.Throws<DllLoadException>(() => FraqcelDll.Check("C:\\a|b")).Message);
    }
}
