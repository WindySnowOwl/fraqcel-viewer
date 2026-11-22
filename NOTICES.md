# Notices

This prototype is unofficial. It is not made or endorsed by Fraqcel's author.

## This prototype

The prototype's own code is under the Zero-Clause BSD license (0BSD): anyone may use, copy, modify and distribute it for any purpose. See [LICENSE](LICENSE). The parts ported from Fraqcel, listed below, keep Fraqcel's license.

## Fraqcel

Parts of this prototype are ported from Fraqcel 2.15.0's VBA, or taken from its workbook:

- the render flow and glitch correction: `Renderer.cs` and `GlitchRegions.cs`;
- the zoom arithmetic: `Navigator.cs`;
- the max iterations x2 and /2 buttons: `MaxIterations.cs`;
- the precision rules: `Precision.cs`;
- the image size rules: `Geometry.cs`;
- palette building and the presets: `Palette.cs`;
- coloring parameters: `Colorizer.cs`;
- supersampling: `Supersampling.cs`;
- defaults: `RenderSettings.cs`.

The prototype calls Fraqcel's stock DLL (`FraqcelLib2150_x64.dll`), which it does not modify. The DLL is fetched from a local Fraqcel installation by `tools/get-fraqcel-dll.ps1`; it is not included here.

Fraqcel's license:

> Copyright (c) 2026 Kevin Orloske. All rights reserved.
>
> Redistribution and use in source and binary forms, with or without modification, are permitted provided that the following conditions are met:
>
> 1. Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer.
>
> 2. Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following disclaimer in the documentation and/or other materials provided with the distribution.
>
> THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

## PeterO.Numbers

The DLL includes PeterO.Numbers (public domain, Unlicense). This prototype uses its `EDecimal` type, from the DLL, for coordinate arithmetic.

## XNUMBERS

This prototype does not yet contain XNUMBERS code: `Xnum.cs` is a stand-in. The planned exact port (design section 5.3) must keep XNUMBERS' notice:

- credit to Leonardo Volpi and the Foxes Team;
- John Beyers' changes for version 6.0.5.6;
- Kevin Orloske's truncation for Fraqcel;
- a statement of the port's own modifications.
