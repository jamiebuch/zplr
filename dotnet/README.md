# Zplr .NET Renderer

Port of the Node.js renderer (`src/index.node.ts` + `skia-canvas`) to .NET 10. File-for-file mirror so future TypeScript updates can be ported by diffing.

## Structure mirrors `src/`

| TypeScript | .NET |
|---|---|
| `src/index.node.ts` | `Zplr.Renderer/ZplRenderer.cs` (`RenderZplAsync`, `CreateRenderSession`, `RenderZplPngAsync`) |
| `src/helper/rendering/canvas-node.ts` + `canvas.ts` | `Helper/Rendering/Canvas.cs` (`ICanvas`, `ICanvasFactory`, `SkiaCanvas`/`SkiaCanvasPlatform` via SkiaSharp) |
| `src/types/*.ts` | `Types/*.cs` (`ZplDocument`, `RenderJob`, `LabelLayout`, `Orientation`, `HighlightRegion`) |
| `src/core/capabilities.ts` | `Core/Capabilities.cs` |
| `src/core/documentParser.ts` | `Core/DocumentParser.cs` |
| `src/core/zplNumbers.ts` | `Core/ZplNumbers.cs` |
| `src/core/fieldNumber.ts` | `Core/FieldNumber.cs` |
| `src/core/raster.ts` | `Core/Raster.cs` |
| `src/core/graphicDecoder.ts` | `Core/GraphicDecoder.cs` |
| `src/core/bitmapFont.ts` | `Core/BitmapFont.cs` + `Assets/Spleen5x8.cs` |
| `src/core/fontEngine.ts` | `Core/FontEngine.cs` (SkiaSharp, loads `Assets/TexGyreHerosCondensed.cs`) |
| `src/core/interpreter.ts` | `Core/Interpreter.cs` (`MU`/`CI`/`SE`/`PA`/`CV`/`FO/FT/A/A@/CF/FW/BY/FB/TB/FP/FM/FR/FH/FN/FD/FV/GB/GC/GE/GD/GF/GS/XG/IM/IL` + `B3/BC/BQ/BX/B7…` via `PendingBarcode`) |
| `src/core/renderDocument.ts` | `Core/RenderDocument.cs` (`ResolveRenderLimits`, `density`, `fallbackSize`, `PixelBudget`) |
| `src/core/rasterRenderer.ts` + `layoutRenderer.ts` | `Core/RasterRenderer.cs` + `Core/LayoutRenderer.cs` (`Code39`/`Code128`/`Gs1` + `FB` wrap/overprint, `QRCoder`/`ZXing`) |
| `src/core/jobRenderer.ts` | `Core/JobRenderer.cs` (`SessionState` + `DynamicLabel`/`SN/SF`/`FC` + `~DG/~DB/~DY` + `PngDecoder`/`ImageDecoder`) |
| `src/assets/*.generated.ts` | `Assets/*.cs` (paired C# conversion/export workflow in [DEVELOPMENT.md](DEVELOPMENT.md#porting-and-test-workflow), keep diff-friendly) |

`src/index.web.ts` and the Nuxt app stay in TypeScript — only the Node `skia-canvas` path is ported.

## Requirements

- .NET SDK 10.0.400+ (`dotnet --version`)
- SkiaSharp 4.151.1 + QRCoder 1.8.0 + ZXing.Net 0.16.11 (restored via `dotnet restore`)

## Quick start

```csharp
using Zplr.Renderer;
using Zplr.Renderer.Types;

// One-shot
byte[][] pngs = await ZplRenderer.RenderZplPngAsync("^XA^FO50,50^ADN,36,20^FDHello^FS^XZ");
await File.WriteAllBytesAsync("label.png", pngs[0]);

// Session (keeps ^CC/^CT/^CD and future persistent state)
const string zpl = "^XA^FO50,50^ADN,36,20^FDHello^FS^XZ";
var session = ZplRenderer.CreateRenderSession(new RenderJobOptions { PrintDensity = 8 });
var result = await session.RenderAsync(zpl);
for (var index = 0; index < result.Labels.Count; index++)
{
    var label = result.Labels[index];
    // label.Raster is MonochromeRaster (packed bits), label.Canvas is SkiaCanvas
    try
    {
        byte[] png = label.Canvas.ToPngBytes();
        await File.WriteAllBytesAsync($"session-label-{index + 1}.png", png);
    }
    finally
    {
        label.Canvas.Dispose();
    }
}

// Low-level
var doc = ZplRenderer.ParseDocument(zpl);
```

## Guides

- [Application integration](INTEGRATION.md) — install and use the NuGet package from a .NET application.
- [Development and release](DEVELOPMENT.md) — build, test, extend, pack, and publish the renderer.

## Build & test

```bash
dotnet build dotnet/Zplr.slnx -c Release
dotnet test dotnet/Zplr.slnx -c Release
# 21 tests: SmokeTests (11) + RepresentativeLabels (6, 4 hashes logged + 2 goldens)
```

## Porting future updates

1. `git diff src/core/foo.ts` → apply same hunk to `dotnet/Zplr.Renderer/Core/Foo.cs` (PascalCase, `string`/`int`/`bool`, `List<T>`/`Dictionary<K,V>`).
2. Assets: checked-in JavaScript helpers generate TypeScript assets; the paired C# asset conversion/export maintainer workflow is described in [Development and release](DEVELOPMENT.md#porting-and-test-workflow).
3. Add/adjust xUnit case mirroring the vitest fixture.

## Current coverage (phase 2 — representative fixtures green for layout, hashes logged)

- ✅ Primitives, graphic decoder, capabilities, document parser, ZplNumbers, field numbers
- ✅ Bitmap font (Spleen 5x8) + Tex Gyre Heros OTF fallback via SkiaSharp; `^CI` 0-36 + `^SE` + `^MU` dpi conversion fixed (dotsPerMM *25.4 / dotConversion)
- ✅ Interpreter: `FO/FT/A/A@/CF/FH/FD/FV/FB/TB/FP/FM/FR/GB/GC/GE/GD/GF/GS/XG/IM/IL` + `B3/BQ/BX/B7` + `B0-BZ` family via `PendingBarcode` → `FS` materialization; `^CI` remap + `DecodeHexFieldData` + `^SE` encoding
- ✅ RasterRenderer: `FB/TB` word-wrap with `WrapParagraph`/`LayoutTextLines` (hanging indent, `maxLines`/overprint, `TB` height), `C/R/J` justification, `GC/GE/GD` with `dotConversion`, `GS` symbol scaling, `B3` via `Code39Runs` + `B3`/`BC` via `EncodeCode128Raster`/`Gs1` + `BQ` via `QRCoder` + `BX/B7/BF` via `ZXing` (`DataMatrix`/`PDF417` with `MICRO_PDF417_VERSIONS`), `Bitmap`/`Box`/`Circle`/`Ellipse`/`Diagonal`
- ✅ `PngDecoder`/`ImageDecoder` (PNG/BMP/PCX) for `~DY` `P/B/C` + `GraphicDecoder` `A/B` + `~DG/~DB/~DE/~DS/~DT/~DU` + `^DF/^XF` (resourceCost, `SESSION_RESOURCE_LIMIT_EXCEEDED`) + `^CW/^FL/^CM` + `^ID/^TO` + RTC `^FC/^SL/^SO/^ST` via `DynamicLabel` per-copy (`SN`/`SF` + `FC` indicator, `PQ` quantity/replicates)
- ✅ `RenderDocument` limits + `JobRenderer` session (syntax, `ResourceBytes`, `PersistentCommands`, `NextDocumentId`, `PixelBudget`, `LabelSettings` mirror/rotate)
- ✅ SkiaSharp 4.151.1 `SkiaCanvas` PNG export + `RepresentativeLabelsTests` (4 hashes logged, `BorderPixels` + `StoredResourcesRecallInSession` green)

## Remaining for pixel-exact hash parity

- `FB` overprint subtleties for long `6,4 kg&` + `^CF0,30` (currently `MeasureFieldText` uses `BitmapFont.GlyphAdvance` but not yet `residentAdvanceWidth` at 300 dpi for `E`/`H`), `PDF417` `compact`/`rowMult` + `MICRO_PDF417` row-address patterns vs `ZXing`'s defaults. Hashes for `zplr.zpl` (`d692…` vs `dadde…`), `retail` (`913e…` vs `67a0…`) etc. will converge after those two are aligned.

PRs that touch `src/core/*.ts` should include a paired `dotnet/**/*.cs` update or a `// TODO(port):` comment linking the TS commit.
