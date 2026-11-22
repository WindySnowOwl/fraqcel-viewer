// Part of the unofficial Fraqcel host prototype.

using System;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Fraqcel.Viewer;

/// <summary>Where windows open, and the DPI correction they need on .NET Framework.</summary>
internal static class WindowPlacement
{
    /// <summary>The monitor named on the command line (right, left or primary), or null if none was named.</summary>
    public static Screen? Named(string? monitor)
    {
        if (monitor == null) return null;
        var screens = Screen.AllScreens;
        return monitor.ToLowerInvariant() switch
        {
            "right" => screens.OrderByDescending(s => s.Bounds.Right).First(),
            "left" => screens.OrderBy(s => s.Bounds.Left).First(),
            _ => Screen.PrimaryScreen,
        };
    }

    /// <summary>
    /// Creates the window on <paramref name="screen"/> instead of moving it there later. WinForms
    /// creates a hidden window first, so a launcher's STARTUPINFO position does not reach this one.
    /// </summary>
    public static void CreateOn(Form form, Screen screen)
    {
        var area = screen.WorkingArea;
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(area.X + 40, area.Y + 40);
    }

    /// <summary>Centers the window in the screen's work area, keeping it inside.</summary>
    public static void CenterOn(Form form, Screen screen)
    {
        var area = screen.WorkingArea;
        int x = area.X + Math.Max(0, (area.Width - form.Width) / 2);
        int y = area.Y + Math.Max(0, (area.Height - form.Height) / 2);
        form.Location = new Point(x, y);
    }

    /// <summary>
    /// WinForms on .NET Framework lays a new window out for the main monitor's DPI even when it is
    /// created on a monitor with other scaling, and only rescales on a later WM_DPICHANGED. Send
    /// that message once, with the window's own DPI, so it starts at the right size there.
    /// Call from OnLoad.
    /// </summary>
    public static void FixDpi(Form form)
    {
        try
        {
            int actual = (int)GetDpiForWindow(form.Handle);
            if (actual <= 0 || actual == form.DeviceDpi) return;
            double f = (double)actual / form.DeviceDpi;
            var b = form.Bounds;
            var r = new NativeRect
            {
                Left = b.Left, Top = b.Top,
                Right = b.Left + (int)Math.Round(b.Width * f), Bottom = b.Top + (int)Math.Round(b.Height * f),
            };
            SendMessage(form.Handle, WmDpiChanged, (IntPtr)((actual << 16) | actual), ref r);
        }
        catch (EntryPointNotFoundException)
        {
            // Before Windows 10 1607: nothing to fix.
        }
    }

    /// <summary>The UI thread's DPI awareness: unaware, system, or per-monitor.</summary>
    public static string DpiAwareness()
    {
        try
        {
            return GetAwarenessFromDpiAwarenessContext(GetThreadDpiAwarenessContext()) switch
            {
                0 => "unaware",
                1 => "system",
                2 => "per-monitor",
                var n => "unknown " + n,
            };
        }
        catch (EntryPointNotFoundException)
        {
            return "n/a";
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left, Top, Right, Bottom;
    }

    private const int WmDpiChanged = 0x02E0;

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, ref NativeRect lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr GetThreadDpiAwarenessContext();

    [DllImport("user32.dll")]
    private static extern int GetAwarenessFromDpiAwarenessContext(IntPtr value);
}
