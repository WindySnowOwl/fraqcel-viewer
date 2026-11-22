# Fraqcel host prototype

An unofficial prototype that runs Fraqcel 2.15.0's stock DLL without Excel. It has a viewer where you pick a location, zoom with the mouse and render an image, and a command-line renderer.

- Design: `fraqcel-host-design.md` (revision 2), my design notes. They are not part of the shared source; comments that cite "design N.N" refer to their sections.
- Status: prototype, 2026-11-11. Several parts of the design are deliberately left out; see [Prototype shortcuts](#prototype-shortcuts).

## Layout

```text
source/
  README.md                          for users: getting started, what it does, limits
  docs/                              the screenshots README.md shows
  Fraqcel.Host.sln
  Directory.Build.props / .targets   shared settings; copies the stock DLL next to the CLI and the tests
  lib/                               the stock DLLs (fetched by tools/get-fraqcel-dll.ps1; not checked in)
  src/
    Fraqcel.Engine/                  the port of Fraqcel's VBA driver around the DLL
      Dll/FraqcelDll.cs              finds, hash-checks and loads the DLL
      HostSettings.cs                the saved settings file (where Fraqcel and its DLL are, saved locations, the session)
      SavedLocation.cs               a named location as one line of the settings file
      Dll/DllCalls.cs                typed calls into MainClass, progress capture
      Renderer.cs                    calcAndPlot and the per-mode drivers, glitch-correction loop, result checks
      GlitchRegions.cs               pertUtilities.bas, line by line
      Navigator.cs                   selectionMove: box, click and pan zooms; the wheel's zoom around a point
      MaxIterations.cs               iterPosBtn_Click and iterNegBtn_Click (x2 and /2)
      Precision.cs, Geometry.cs      isXnumNeeded, setSize/setScale, Fraqcel's size rules
      Xnum.cs                        stand-in for XNUMBERS (see shortcuts)
      Palette.cs, Colorizer.cs       presets, colorBtn_Click, createBmp, getBmpPixels
      Supersampling.cs, ImageWriter.cs, Location.cs, SceneFile.cs, Vba.cs
    Fraqcel.Cli/                     fraqcel-cli.exe
    Fraqcel.Viewer/                  FraqcelViewer.exe (WinForms)
      MainForm.cs                    the window, its controls and the self-test
      MainForm.Session.cs            the controls and last view, saved between runs
      MainForm.FeatureChecks.cs      self-test checks for the panel's controls, saved locations and the session
      FraqcelSetupForm.cs            asks for the DLL
  tests/Fraqcel.Engine.Tests/        xUnit tests (88)
  tools/get-fraqcel-dll.ps1
  out/                               scratch output (ignored)
```

## Build

Requirements:

- Windows with .NET Framework 4.8, which Windows 10 and 11 include.
- A .NET SDK. The prototype builds with 6.0.203 and with 10.0.404, the version the design calls for.
- The .NET Framework 4.8 reference assemblies (Developer Pack).
- Fraqcel 2.15.0's DLLs, both x64 and x86, in `C:\fraqcel` as Fraqcel's own instructions place them, or in the folder given to `-Source`. They are in the `optional DLL files\fraqcel` folder of Fraqcel's download, <https://fraqcel.weebly.com/download.html>.

Steps:

```powershell
powershell -ExecutionPolicy Bypass -File tools\get-fraqcel-dll.ps1   # copies the DLLs into lib\ after a hash check
dotnet build Fraqcel.Host.sln -c Release
dotnet test tests\Fraqcel.Engine.Tests -c Release
```

The version is `<Version>` in `Directory.Build.props` (0.1 to start). The viewer shows it in its title bar.

## Viewer

```powershell
src\Fraqcel.Viewer\bin\Release\net48\FraqcelViewer.exe [--kfr scene.kfr] [--monitor right|left|primary] [--dll PATH] [--settings FILE]
```

Where Fraqcel is:

- On the first start, the viewer asks for the folder Fraqcel is installed in and its 64-bit DLL, `FraqcelLib2150_x64.dll`.
  - For someone without Fraqcel, it links to Fraqcel's download page and says which folder of the download has the DLLs.
  - It fills in `C:\fraqcel` if that folder exists, since Fraqcel's own instructions put the DLLs there. Choosing a folder fills in the DLL found in it, or in one of its subfolders.
  - The dialog checks the file as you choose it: it must be the released 2.15.0 DLL, by its SHA-256.
  - OK loads the DLL and saves both paths to `%LOCALAPPDATA%\FraqcelHost\settings.ini`. Exit closes the viewer.
- Later starts use the saved DLL without asking. If it can't be used (moved, replaced, unreadable), the dialog comes back and says why.
- **Change…**, at the bottom of the side panel, picks another DLL. The running viewer keeps the one it started with; the new one is used from the next start.
- `--dll PATH` uses that DLL for this run only, and doesn't change the saved setting.
- `--settings FILE` uses another settings file. Tests use it, so they never touch the real one.
- The viewer's build output has no copy of the DLL.

Controls:

- **Drag a box** to zoom into it. A dashed square shows where the zoom will land.
- **Click** to zoom in by the zoom factor, **right-click** to zoom out, **middle-click** to center.
- **The wheel** zooms in or out by the zoom factor and keeps the point under the pointer where it is. This is the viewer's own; Fraqcel has no wheel zoom.
- **Ctrl+Shift+Z, X and C** zoom in, zoom out or center on the pixel under the pointer, as Fraqcel's shortcuts do on the selected cell. With the pointer off the image they use the middle.
- **Esc** or **Cancel** stops a render after the glitch-correction pass in progress.
- **Back** returns to the previous view. **Go** renders the location typed in the boxes.
- **×2** and **÷2** double and halve max iterations, as Fraqcel's buttons do: the box changes, and the next render uses it. They round as VBA does and keep the value between 1 and 10,000,000; text that isn't a number goes back to the rendered view's value.
- **Open scene…** loads a `.kfr` scene file (`Re:`, `Im:`, `Zoom:` and `Iterations:` lines, as in my renderer benchmark kit). **Copy** puts the location on the clipboard in `.kfr` form.
- **Fraqcel's deep sample** loads the deep location saved in the shipped workbook (slot 20; scene 62 in my benchmark kit).
- **Saved locations:** **Save** keeps the rendered view under the name typed in the box (or under its zoom and iterations), **Load** renders it, and **Delete** removes it. They live in the settings file, with no fixed number of slots.
- **Render image…** renders the current location at the size and samples per pixel set on the panel, and saves a PNG. **Save view…** saves the image on screen as it is colored now (Fraqcel's Export Navigation Image).
- Palette, offset, divide, reverse, set color and bump mapping recolor without rendering again.
  - **Set color** is the color of the set. Choosing a palette preset sets it to the preset's own, as in Fraqcel.
  - **Light direction**, **light elevation** and **bump strength** are Fraqcel's three bump scroll bars, with its shipped values (0°, 0° and 50) to start. They're active while bump mapping is on.

The status bar and the "Last render" box show:

- the mode and digits;
- the references used, the iterations the series skipped, and pixels flagged per pass;
- for the navigation image, Fraqcel's calculation statistics: the total, max and min of the iteration data, or "--" for both stripe colorings, as Fraqcel shows them;
- blended, unresolved and NaN pixels.

A render with unresolved glitches or NaN is reported as untrustworthy instead of being shown as fine (design 5.10).

Between runs, the viewer remembers its controls and the last view in the settings file, under `viewer.` keys, as Fraqcel's workbook keeps its own state.

- The next start renders the last view. If that view took more than 10 seconds to render, it waits in the Location boxes for Go instead, since a render can only be canceled between glitch-correction passes.
- `--kfr` wins over the last view.
- A value that can't be read leaves its control at the default, and is written back correctly at the next close.

`--self-test DIR` runs a scripted check through the same handlers the mouse uses, saves images and `selftest.log` to DIR, and exits. It never writes the real settings file. The check:

- renders the starting view and checks the footer;
- checks the panel's controls:
  - ×2 and ÷2;
  - the statistics line against the data;
  - the set color, which must change exactly the in-set pixels;
  - each bump slider;
  - Save view, against the image on screen;
  - the wheel, which must keep the pixel under the pointer in place;
  - Ctrl+Shift+Z, X and C;
- makes a box zoom and a click zoom, goes back, renders the deep sample and saves an image;
- saves, loads and deletes locations in `DIR\session.ini`;
- saves the session and reads it back;
- drives the DLL dialog: it links to Fraqcel's download page (not clicked), refuses another file, fills in the DLL from its folder, and saves the choice to `DIR\settings.ini`.

The self-test never asks for the DLL, so give it `--dll PATH` or have a saved setting. `--monitor` picks the monitor it opens on. The self-test skips the session at start and close; those were checked by starting the viewer with a scratch `--settings` file (2026-11-21).

## Command line

```powershell
$cli = "src\Fraqcel.Cli\bin\Release\net48\fraqcel-cli.exe"
& $cli info
& $cli render --fraqcel-default --out full.png
& $cli render --kfr scene.kfr --size 1920x1080 --coloring smooth --out scene.png
& $cli render --re -0.75 --im 0.1 --zoom 1e9 --iter 5000 --coloring smooth --palette "Blue Waves" --out x.png
```

`fraqcel-cli --help` lists every option. It prints one `fraqcel status=... key=value ...` line. Exit codes follow the design's section 6.3:

| Code | Meaning                                                             |
| ---- | ------------------------------------------------------------------- |
| 0    | ok                                                                  |
| 2    | bad arguments                                                       |
| 3    | outside capability, for example a zoom beyond the range of a double |
| 4    | failed                                                              |
| 5    | out of memory                                                       |
| 7    | output failed                                                       |
| 8    | DLL missing or unknown                                              |
| 9    | rendered with unresolved glitches or NaN                            |

## What is verified

- **The full view matches an independent host.** Fraqcel's starting view (800x800, 500 iterations, discrete) hashes identically to the buffer from a throwaway host that called `fraqcelDbl` directly: `daa2a013…`.
- **Glitch correction converges as in Fraqcel.** On Fraqcel's deep sample (plot scale 8.8e-32, default settings), flagged pixels fall 39,650 → 4,025 → 386 → 42 → 9 over 5 references. The last 9 single pixels are blended, leaving 0 unresolved. With correction turned off, the render is reported as unresolved (exit 9).
- **A box zoom lands where it was drawn.** The self-test compares the selected box from the old image with the new render.
- **The footer's iteration value is the hovered pixel's.** Hovering a pixel shows its row, column and iteration value, marked "(in set)" at the maximum, as Fraqcel did before 2.8 (its author's suggestion, 2026-11-13). Over every pixel of the starting view, the self-test finds "in set" exactly where the DLL's image shows the interior color: 0 of 60,440 disagree, against 1,644 when read one pixel off.
- **Each DLL entry point renders:** all seven types; double, multi-precision, perturbation, SD and HF series, and rebasing; all four coloring modes; supersampling.
- **The precision paths agree on geometry.** Double, multi-precision and perturbation agree on about 95% of pixels for the same view. Shifting either image by one pixel makes the agreement much worse, so the samples line up. The rest are high-count escaped pixels, where each arithmetic rounds differently.
- **The unit tests cover:**
  - VBA rounding (banker's rounding, 15-digit `CStr`, `Log(x)/Log(10)`);
  - `isXnumNeeded`'s rules;
  - the Default palette against the workbook's 84 colors, and the band expression with an odd band count;
  - offset and reverse;
  - region labeling (8-connectivity, lowest-label ties, corner index past 4, centroid rounding);
  - `averagePixel`'s three roundings;
  - the zoom arithmetic, and the wheel's zoom around a point in double and multi-precision (with a control: a neighboring pixel must move);
  - Fraqcel's x2 and /2 rules;
  - saved locations in the settings file, including a center held as a Double;
  - Fraqcel's size rules;
  - determinism.

## Prototype shortcuts

These are deliberate, and each one is a later step in the design:

- **Coordinates use a stand-in for XNUMBERS** (`Xnum.cs`): the DLL's EDecimal at DgMx digits with half-up rounding, with doubles entering as VBA's 15-digit text.
  - It is close to Fraqcel's arithmetic but not proven identical. The last digit of a coordinate can differ, which can flip a few pixels.
  - The exact port and its identical-strings gate are milestone M0 (design 5.3).
- **x64 only.** 32-bit Excel runs the x86 DLL; an x86 build is later (design 7.7).
- **Rendering runs in the viewer's own process.**
  - Cancel takes effect between glitch-correction passes, not in the middle of one.
  - The design's single-use worker processes, which make cancel immediate, are milestone M3 (design 8.3).
- **Not yet in the prototype:**
  - resolution 50% and 33%;
  - quick pan, refresh, range recalculation, recalc on one pixel;
  - the palette editor (key colors, bands, random palettes, user presets), video, Fraqcel data files;
  - the benchmark kit's framing, raw EXR output, phase timing, capabilities and NA logic (beyond the double range), and the memory preflight.
- **Coloring defaults differ.** The viewer starts with smooth coloring, which is easier to look at; Fraqcel ships with discrete. The CLI defaults to discrete.
- **Supersample rounding** uses `Math.Round(x, k, ToEven)` as an approximation of VBA's `Round`.
- **Plain multi-precision is very slow in the DLL itself.** About 170 s for a 200x120 image at 3000 iterations.

## Notes

- **DLL loading** (`FraqcelDll.cs`):
  - Only an explicit path is used: the viewer's `--dll` or saved setting, or the CLI's `--dll`. Without `--dll`, the CLI uses `FraqcelLib2150_x64.dll` next to it.
  - The file is hashed while held open, and loaded before it is released. Unknown hashes are refused unless `--allow-unknown-dll` is given.
  - A `FraqcelLib.dll` next to the executable is refused, because the runtime would load it without the check.
- **Display scaling:** the viewer is per-monitor DPI aware.
  - WinForms on .NET Framework lays out a new window for the main monitor's DPI even when the window opens on another monitor, so the form corrects itself once on load.
  - Mouse positions always map through image pixels, never screen or cell grids.
- **License:** the prototype's own code is under 0BSD ([LICENSE](LICENSE)). Ported code and presets carry Fraqcel's BSD-2-Clause notice; see [NOTICES.md](NOTICES.md).
