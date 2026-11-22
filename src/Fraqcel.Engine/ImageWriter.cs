// Part of the unofficial Fraqcel host prototype.

using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace Fraqcel.Engine;

public static class ImageWriter
{
    /// <summary>
    /// Writes a PNG with GDI+ to a temporary file, checks that it ends with an IEND chunk, then
    /// moves it into place, so a half-written file never sits under the final name.
    /// </summary>
    public static double SavePng(Bitmap bitmap, string path)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        string full = Path.GetFullPath(path);
        string? dir = Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        string tmp = full + ".tmp";
        bitmap.Save(tmp, ImageFormat.Png);
        if (!EndsWithIend(tmp))
        {
            File.Delete(tmp);
            throw new IOException("The PNG written to " + tmp + " is incomplete.");
        }
        if (File.Exists(full)) File.Delete(full);
        File.Move(tmp, full);
        return sw.Elapsed.TotalMilliseconds;
    }

    /// <summary>True when the file ends with a PNG IEND chunk.</summary>
    public static bool EndsWithIend(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (fs.Length < 12) return false;
        fs.Seek(-12, SeekOrigin.End);
        var b = new byte[12];
        int n = fs.Read(b, 0, 12);
        return n == 12 && b[4] == (byte)'I' && b[5] == (byte)'E' && b[6] == (byte)'N' && b[7] == (byte)'D';
    }
}
