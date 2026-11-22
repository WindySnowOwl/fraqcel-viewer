// Part of the unofficial Fraqcel host prototype.

using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using Fraqcel.Engine;
using Fraqcel.Engine.Dll;

namespace Fraqcel.Viewer;

internal static class Program
{
    /// <summary>
    /// FraqcelViewer [--dll PATH] [--settings FILE] [--kfr FILE] [--monitor right|left|primary] [--self-test DIR]
    /// <para>The DLL comes from --dll if given (not saved), or else from the saved setting. If
    /// neither works, a dialog asks for Fraqcel's folder and DLL and saves the choice.</para>
    /// <para>--settings uses another settings file instead of %LOCALAPPDATA%\FraqcelHost\settings.ini.</para>
    /// <para>--monitor creates the window on that monitor (it is never moved there afterward).</para>
    /// <para>--self-test renders the starting view, zooms into a box through the same code the
    /// mouse uses, renders again, saves the images and a log to DIR, and exits. It never asks for
    /// the DLL: it needs --dll or a saved setting.</para>
    /// </summary>
    [STAThread]
    private static int Main(string[] args)
    {
        Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        string? dll = null, kfr = null, selfTest = null, monitor = null, settingsFile = null;
        try
        {
            for (int i = 0; i < args.Length; i++)
            {
                string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException(args[i] + " needs a value");
                switch (args[i])
                {
                    case "--dll": dll = Path.GetFullPath(Next()); break;
                    case "--settings": settingsFile = Path.GetFullPath(Next()); break;
                    case "--kfr": kfr = Path.GetFullPath(Next()); break;
                    case "--self-test": selfTest = Path.GetFullPath(Next()); break;
                    case "--monitor": monitor = Next(); break;
                    default: throw new ArgumentException("Unknown option: " + args[i]);
                }
            }
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            MessageBox.Show(e.Message, "Fraqcel viewer", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 2;
        }

        string settingsPath = settingsFile ?? HostSettings.DefaultPath;
        if (!LoadDll(dll, settingsPath, selfTest, monitor)) return 8;

        using var form = new MainForm(kfr, selfTest, monitor, settingsPath);
        Application.Run(form);
        return form.ExitCode;
    }

    /// <summary>Loads the DLL from --dll, the saved setting, or the setup dialog. False if none worked.</summary>
    private static bool LoadDll(string? dll, string settingsPath, string? selfTest, string? monitor)
    {
        if (dll != null)
        {
            try
            {
                FraqcelDll.Load(dll);
                return true;
            }
            catch (DllLoadException e)
            {
                return Fail(selfTest, e.Message);
            }
        }

        var settings = HostSettings.Load(settingsPath);
        string? problem = settings.ReadProblem;
        if (settings.DllPath != null)
        {
            try
            {
                FraqcelDll.Load(settings.DllPath);
                return true;
            }
            catch (DllLoadException e)
            {
                problem = "The saved DLL can't be used. " + e.Message;
            }
        }

        if (selfTest != null)
            return Fail(selfTest, problem ?? "No Fraqcel DLL: pass --dll, or choose it in the viewer first.");

        var screen = WindowPlacement.Named(monitor) ?? Screen.FromPoint(Cursor.Position);
        using var setup = new FraqcelSetupForm(FraqcelSetupForm.Purpose.Startup, settings, settingsPath, problem, screen);
        return setup.ShowDialog() == DialogResult.OK && FraqcelDll.IsLoaded;
    }

    private static bool Fail(string? selfTest, string message)
    {
        if (selfTest != null)
        {
            Directory.CreateDirectory(selfTest);
            File.WriteAllText(Path.Combine(selfTest, "selftest.log"), "FAIL dll: " + message + Environment.NewLine);
        }
        else
        {
            MessageBox.Show(message, "Fraqcel viewer", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        return false;
    }
}
