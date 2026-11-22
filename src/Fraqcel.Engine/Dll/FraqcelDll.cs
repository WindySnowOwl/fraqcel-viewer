// Part of the unofficial Fraqcel host prototype.

using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;

namespace Fraqcel.Engine.Dll;

/// <summary>
/// Finds, verifies and loads the stock Fraqcel DLL (design 4.2 and 4.3).
/// <list type="bullet">
/// <item>Only an explicit path, or <c>FraqcelLib2150_x64.dll</c> next to the executable, is used.</item>
/// <item>The file is held open (no writers, no delete) while it is hashed and loaded, so it
/// cannot change between the check and the load.</item>
/// <item>A hash that is not the known 2.15.0 hash is refused unless the caller allows it.</item>
/// </list>
/// Call <see cref="Load"/> before any code that touches FraqcelLib types runs.
/// </summary>
public static class FraqcelDll
{
    public const string FileNameX64 = "FraqcelLib2150_x64.dll";
    public const string FileNameX86 = "FraqcelLib2150_x86.dll";

    /// <summary>Fraqcel's website.</summary>
    public const string Website = "https://fraqcel.weebly.com/";

    /// <summary>Fraqcel's download page. The DLLs are in the download's "optional DLL files\fraqcel" folder.</summary>
    public const string DownloadPage = "https://fraqcel.weebly.com/download.html";

    /// <summary>Known SHA-256 hashes of the shipped Fraqcel 2.15.0 DLLs.</summary>
    public static readonly (string File, string Sha256)[] Known =
    {
        (FileNameX64, "51c575eabbb5757eae7046440600b041debabc332f64482fb28903152ab8cf46"),
        (FileNameX86, "6778ced499e0cefc87103ef9a7196625dcae5e6e2f5fb1bb0bed1d61dda4cfad"),
    };

    private static readonly object Gate = new();
    private static Assembly? loaded;

    public static bool IsLoaded => loaded != null;
    public static string? LoadedPath { get; private set; }
    public static string? LoadedSha256 { get; private set; }
    public static bool LoadedIsKnown { get; private set; }

    /// <summary>The file name for this process's bitness.</summary>
    public static string FileNameForThisProcess => Environment.Is64BitProcess ? FileNameX64 : FileNameX86;

    /// <summary>The default location: next to the executable.</summary>
    public static string DefaultPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, FileNameForThisProcess);

    /// <summary>Loads the DLL once. Later calls with the same path do nothing.</summary>
    /// <exception cref="DllLoadException">The DLL is missing, of the wrong bitness, or unknown.</exception>
    public static void Load(string? path = null, bool allowUnknown = false)
    {
        lock (Gate)
        {
            string full = FullPath(path ?? DefaultPath);
            if (loaded != null)
            {
                if (!string.Equals(full, LoadedPath, StringComparison.OrdinalIgnoreCase))
                    throw new DllLoadException("A different Fraqcel DLL is already loaded: " + LoadedPath);
                return;
            }

            RefusePlantedCopies();

            // Hold the file open with read sharing only until loading returns.
            using var stream = Open(full);
            string sha = Verify(stream, full, allowUnknown, out bool known);

            Assembly asm = Assembly.LoadFile(full);
            if (asm.GetName().Name != "FraqcelLib")
                throw new DllLoadException("Not the Fraqcel DLL (assembly name " + asm.GetName().Name + "): " + full);

            loaded = asm;
            LoadedPath = full;
            LoadedSha256 = sha;
            LoadedIsKnown = known;
            AppDomain.CurrentDomain.AssemblyResolve += Resolve;
        }
    }

    /// <summary>
    /// Runs <see cref="Load"/>'s checks on a file without loading it: present, this process's
    /// bitness, and (unless allowed) the known hash. Returns the file's SHA-256.
    /// </summary>
    /// <exception cref="DllLoadException">The file fails a check.</exception>
    public static string Check(string path, bool allowUnknown = false)
    {
        string full = FullPath(path);
        using var stream = Open(full);
        return Verify(stream, full, allowUnknown, out _);
    }

    /// <summary>
    /// The DLL for this process's bitness in <paramref name="folder"/>, or else in one of its
    /// immediate subfolders (in name order). Null if there is none, or the folder can't be read.
    /// </summary>
    public static string? FindIn(string folder)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return null;
            string direct = Path.Combine(folder, FileNameForThisProcess);
            if (File.Exists(direct)) return Path.GetFullPath(direct);
            var subs = Directory.GetDirectories(folder);
            Array.Sort(subs, StringComparer.OrdinalIgnoreCase);
            foreach (string sub in subs)
            {
                string p = Path.Combine(sub, FileNameForThisProcess);
                if (File.Exists(p)) return Path.GetFullPath(p);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            // An unreadable or malformed folder has no DLL we can use.
        }
        return null;
    }

    private static string FullPath(string path)
    {
        try
        {
            return Path.GetFullPath(path.Trim());
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new DllLoadException("Not a usable path: " + path);
        }
    }

    private static FileStream Open(string full)
    {
        if (!File.Exists(full))
            throw new DllLoadException("Fraqcel DLL not found: " + full);
        try
        {
            return new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new DllLoadException("Can't read " + full + ": " + e.Message);
        }
    }

    /// <summary>Bitness and hash checks on an open file. Returns the SHA-256.</summary>
    private static string Verify(FileStream stream, string full, bool allowUnknown, out bool known)
    {
        try
        {
            CheckBitness(stream, full);
        }
        catch (EndOfStreamException)
        {
            throw new DllLoadException("Not a PE file: " + full);
        }
        stream.Position = 0;
        string sha;
        using (var hasher = SHA256.Create())
            sha = ToHex(hasher.ComputeHash(stream));
        known = Array.Exists(Known, k => k.Sha256 == sha);
        if (!known && !allowUnknown)
            throw new DllLoadException("Unknown Fraqcel DLL (sha256 " + sha + "): " + full +
                                       ". Only the shipped 2.15.0 DLL is accepted.");
        return sha;
    }

    /// <summary>Throws if <see cref="Load"/> has not run.</summary>
    internal static void EnsureLoaded()
    {
        if (loaded == null) throw new InvalidOperationException("FraqcelDll.Load must be called first.");
    }

    private static Assembly? Resolve(object? sender, ResolveEventArgs args)
    {
        // Compile-time references to FraqcelLib bind to the verified assembly.
        return new AssemblyName(args.Name).Name == "FraqcelLib" ? loaded : null;
    }

    /// <summary>
    /// A FraqcelLib.dll in the probing path would be bound by the runtime before the resolve
    /// handler runs, skipping the hash check.
    /// </summary>
    private static void RefusePlantedCopies()
    {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        foreach (string name in new[] { "FraqcelLib.dll", "FraqcelLib.exe" })
        {
            string p = Path.Combine(baseDir, name);
            if (File.Exists(p))
                throw new DllLoadException("Refusing to start: " + p + " would be loaded without a hash check. Remove it.");
        }
    }

    private static void CheckBitness(Stream s, string path)
    {
        // PE header: offset of the "PE\0\0" signature at 0x3C, then the 2-byte machine field.
        var br = new BinaryReader(s);
        if (s.Length < 0x40) throw new DllLoadException("Not a PE file: " + path);
        s.Position = 0x3C;
        int peOffset = br.ReadInt32();
        if (peOffset < 0 || peOffset > s.Length - 6) throw new DllLoadException("Not a PE file: " + path);
        s.Position = peOffset;
        if (br.ReadUInt32() != 0x00004550) throw new DllLoadException("Not a PE file: " + path);
        ushort machine = br.ReadUInt16();
        bool dll64 = machine == 0x8664;
        bool dll32 = machine == 0x014C;
        if (!dll64 && !dll32) throw new DllLoadException("Unexpected machine type 0x" + machine.ToString("X4") + ": " + path);
        if (dll64 != Environment.Is64BitProcess)
            throw new DllLoadException((dll64 ? "64-bit" : "32-bit") + " DLL in a " +
                                       (Environment.Is64BitProcess ? "64-bit" : "32-bit") + " process: " + path);
    }

    private static string ToHex(byte[] bytes)
    {
        var sb = new System.Text.StringBuilder(bytes.Length * 2);
        foreach (byte b in bytes) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }
}

/// <summary>The Fraqcel DLL could not be used.</summary>
public sealed class DllLoadException : Exception
{
    public DllLoadException(string message) : base(message) { }
}
