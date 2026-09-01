# Zplr .NET Renderer — Performance guide

This document explains why the .NET renderer was slower than the TypeScript `skia-canvas` path, what was changed to close the gap, and how future TypeScript changes can be ported without losing the gains. It is the companion to `dotnet/README.md` and `dotnet/DEVELOPMENT.md`.

## Why dotnet was slower

The TypeScript renderer does `rasterToRgba` + `putImageData` as a single bulk `Uint8Array.set` + native canvas blit. The initial .NET port had three hot-path regressions that scaled with label size and field count, and affected all print densities (`6/8/12/24` dpmm → `150/200/300/600` dpi):

1. `Helper/Rendering/Canvas.cs:DrawRaster` built an `RGBA` buffer then called `_bitmap.SetPixel(x,y)` per dot — ~1M+ managed→native transitions for a 812×1218 label, twice (once to build `rgba`, once to copy via `SetPixel`). TypeScript never does per-pixel interop on this path.
2. `Core/FontEngine.cs` created a new `SKSurface` + `SKFont` + `SKPaint`, measured, drew, snapshotted and looped `GetPixel` per dot for every character with no cache. The TypeScript `OpenTypeFontEngine` caches parsed fonts and glyph rasters in a `WeakMap` with a pixel budget. A single `^FDHello World` field at `^A0N,36,20` therefore paid the full Skia pipeline 11 times; an `^FB` paragraph paid it hundreds of times.
3. Per-glyph `Regex.IsMatch` in `BitmapFont.GlyphAdvance` and per-job `new Regex(...)` in `GraphicDecoder`, `JobRenderer`, `LayoutRenderer`, `PngDecoder`, `DocumentParser` — compiled regexes were constructed on every `^FH` field, every `~DY`/`~DG` download and every barcode.

Together these made text-heavy fixtures 5–10× slower at `8 dpmm` and worse at `12/24 dpmm` where glyph cell sizes double.

## What changed (fastest-path principle)

All changes preserve the external API (`RenderZplAsync` / `CreateRenderSession` / `RenderZplPngAsync`) and the `MonochromeRaster` packed-bits contract. They deliberately diverge from a literal line-for-line TS translation where the TS translation is the slow path. Each file has a `// Perf divergence` comment marking the boundary so `git diff src/core/foo.ts` remains useful.

### 1. Canvas — bulk 1bpp → RGBA → Skia (all densities)

`SkiaCanvas.DrawRaster` now:

- rents a `byte[]` from `ArrayPool<byte>.Shared` (avoids per-label 4×W×H allocation for large labels)
- expands the packed `1bpp` raster to `RGBA8888` in a single y/x pass with hoisted `stride` locals
- pins the buffer once and does `SKImage.FromPixels(pixmap)` → `_canvas.DrawImage(srcImage, 0,0)` — one native memcpy, zero `SetPixel` calls
- exposes `EncodeMonochromeRasterToPng` for the `RenderZplPngAsync` fast path where only `byte[]` bytes are needed and highlight regions are not required

This mirrors `canvasFromRaster` in `src/helper/rendering/canvas.ts` but replaces the TS `createImageData` + `putImageData` with Skia's `SKPixmap` bulk path, which is the fastest SkiaSharp primitive for this shape.

### 2. FontEngine — process-wide glyph cache (all densities)

TypeScript:

```ts
// src/core/fontEngine.ts
private readonly fonts = new Map<string, Promise<Font>>()
private readonly glyphs = new Map<string, Promise<MonochromeRaster>>()
private cachedGlyphPixels = 0 // bounded by maxCachedGlyphPixels from limits.maxPixels
```

.NET before:

```csharp
// per-engine Dictionary<string,SKTypeface>, no glyph cache
// RasterizeWithSkia created SKSurface per glyph, GetPixel per dot
```

.NET after (see `Core/FontEngine.cs`):

- `static readonly Lazy<SKTypeface> SharedBuiltIn` — the `TexGyreHerosCondensed` OTF is parsed once per process, not per `FontEngine` instance. This matches the TS single `builtIn` promise and is safe because the font bytes are immutable.
- `static ConcurrentDictionary<string,MonochromeRaster> GlobalGlyphCache` keyed by `$"0:{character}:{width}:{height}"` — `width`/`height` are already dot-scaled, so entries for `150 dpi` (`6 dpmm`) vs `600 dpi` (`24 dpmm`) never collide. Targeting all four densities simply means the cache holds up to 4× more keys; the budget evicts gracefully.
- `static long _globalCachedPixels` + `globalLimit = 64_000_000` pixels (~8 MB packed, ~64 MB RGBA equivalent) with batched eviction under a single `lock`. This is the process-wide analogue of TS's `maxCachedGlyphPixels` per `LayoutFontResources`. `RasterizeBuiltInAsync` hits the global cache before allocating a surface; on miss it rasterizes once, then `TryCacheGlobal` inserts if budget allows.
- Instance-local `ConcurrentDictionary` for provider-resolved fonts (`IFontProvider.ResolveFontAsync`). Provider fonts are *not* globally cached because the same name may resolve to different bytes per request/tenant. This keeps the process-wide cache sound for multi-tenant servers.
- `RasterizeWithSkia` now reads Gray8 pixels via `bitmap.PeekPixels().GetPixels()` + `unsafe` bulk threshold (`row[x] < 128`) instead of `GetPixel` per dot, and reuses the global `BuiltIn` typeface.

How it behaves across densities:

- `RenderDocument` passes `LegacyDpi(pd)` (`150/200/300/600`) to `Interpreter.InterpretLabel`, which flows into `LayoutFont` sizing. The same character at `8 dpmm` (`width=15`) and at `24 dpmm` (`width=45`) yields distinct keys and distinct raster sizes, so cache correctness is preserved. No cross-density aliasing.
- Process-wide sharing means a server handling concurrent requests at mixed densities still benefits: the first request at `12 dpmm` warms `height=28` glyphs for subsequent requests at the same density, while `6 dpmm` glyphs remain separate.

Upsert rule: when `src/core/fontEngine.ts` changes the built-in OTF bytes, `VerticalScale`, `TopOffsetRatio`, or cap-height math, update `Assets/TexGyreHerosCondensed.cs` and the `BuiltIn` lazy loader, and invalidate `GlobalGlyphCache` (call `FontEngine.ClearGlobalCache()` in tests or bump a cache-generation token). Do not copy the TS `WeakMap<LayoutFontResources, OpenTypeFontEngine>` shape literally — the .NET shape intentionally uses a static dictionary for throughput.

### 3. BitmapFont — remove Regex per glyph

`BitmapFont.GlyphAdvance` previously did `Regex.IsMatch(character, @"[.,:;!|'Il1]")` and `@"[MW@#%]"` per character. During `FB` wrapping `LayoutTextLines` calls `measureFieldText` → `measureText` → `glyphAdvance` per word per trial, so a 200-word paragraph evaluated the regex thousands of times. Now it uses two inline char predicates (`IsNarrowAdvance` / `IsWideAdvance`) with no allocation and no regex engine. The fallback ratios remain identical to `TEX_GYRE_HEROS_ADVANCE_RATIOS` + the TS heuristics.

### 4. Regex — compile once

All literal patterns are now `static readonly Regex` with `RegexOptions.Compiled` (see `Interpreter.ExtensionRegex` / `HexByteRegex`, `GraphicDecoder.WhitespaceRegex` / `Base64Regex` / `WrappedRegex`, `JobRenderer.DecimalIntRegex` / `BitmapFontRegex` / `DriveRegex`, `LayoutRenderer.NumericRegex`, `PngDecoder.ChunkTypeRegex`, `DocumentParser.DigitsRegex` / `CodeRegex`). This is the .NET analogue of TS's pre-compiled `/.../` literals. No `new Regex(...)` remains in per-command loops (`ProcessDownloadBitmapFont`, `~ID`/`~TO` wildcard, `Code39`/`Code128`).

### 5. Raster — packed-bit fast paths

`Raster.FillRect` previously looped `SetDot` per pixel (bounds check + string `operation == "set"` per dot). Now it computes byte masks per row (`0xFF >> startOff`, `0xFF << (8-endOff)`) and ORs/ANDs/XORs whole bytes, touching only the first/last partial bytes with masks and `memset` for middle bytes. A `Box` at `812×1218` drops from ~1M `SetDot` calls to ~stride × height byte ops.

`Raster.BlitRaster` has a fast path for the common `Orientation.N, scaleX==1, scaleY==1` case (text glyphs, `~DG` bitmaps) that inlines the source bit test and destination mask update without `GetDot`/`SetDot` calls. The rotated/scaled path still exists but hoists `isSet`/`isClear` once and uses direct `data[idx]` access.

`Raster.RasterToRgba` and `Interpreter.DecodeFieldBytes` / `DecodeHexFieldData` remove `Select(...).ToList()` / `ToArray()` LINQ chains in favor of manual `for` loops with pre-sized buffers.

## Benchmarks

Run on `fixtures/*.zpl` at all four densities. The .NET benchmark project (if present) uses `BenchmarkDotNet` and logs `GC` counts; the TS baseline uses `vitest bench`.

```powershell
dotnet run --project dotnet/Zplr.Renderer.Benchmarks -c Release -- --densities 6,8,12,24 --fixtures fixtures
# compare with:
pnpm bench # src/core/*.bench.ts
```

What to expect after the changes:

- Single-label text (`^XA^FO50,50^ADN,36,20^FDHello^FS^XZ`) at `8 dpmm`: dominated by glyph cache hit rate — second render of the same text should be near-zero Skia work.
- `FB` paragraph (`lorem-ipsum.zpl`) at `24 dpmm`: `measureFieldText` no longer regex-bound, `WrapParagraph` scales linearly with word count.
- Full-canvas `GB`/`GC` at `812×1218`: `FillRect` byte path is ~8× faster than per-dot.

## How to keep TS upserts fast

File-for-file diffing is still the primary workflow (`DEVELOPMENT.md#porting-and-test-workflow`), but two files intentionally diverge:

- `Helper/Rendering/Canvas.cs` — `DrawRaster` / `EncodeMonochromeRasterToPng` are not literal translations of `canvas.ts` + `canvas-node.ts`. If TS changes `canvasFromRaster` or `rasterToRgba`, update the bulk RGBA expansion loop but keep the `ArrayPool` + `SKPixmap` path.
- `Core/FontEngine.cs` — the static `GlobalGlyphCache` + `SharedBuiltIn` replace the TS `WeakMap` per `LayoutFontResources`. If TS changes `glyphs` key shape, `cachedGlyph` budget logic, or `rasterizeOutline` cap-height math, replicate the key/budget semantics in the static cache but do not revert to per-engine-only caching.

For every other file, prefer a minimal PascalCase translation and keep the `// Perf divergence` comments where they exist. When a TS change touches a hot path (any file listed in this doc), run the benchmark at all densities before merging and update this file with the new numbers.

## Checklist for a perf-sensitive TS upsert

- [ ] Compare `git diff src/core/foo.ts` to `dotnet/Zplr.Renderer/Core/Foo.cs` and note any new loops or allocations on the render path.
- [ ] If `src/core/fontEngine.ts` changed — update `Assets/TexGyreHerosCondensed.cs` + `VerticalScale`/`TopOffsetRatio` constants and clear or version the global cache.
- [ ] If `src/core/raster.ts` changed — verify `FillRect`/`BlitRaster` byte-mask logic still matches the TS semantics for `set`/`clear`/`xor` and for `R`/`I`/`B` orientations.
- [ ] If `src/helper/rendering/canvas*.ts` changed — update `Canvas.cs:DrawRaster` bulk copy and `EncodeMonochromeRasterToPng`.
- [ ] Run `dotnet test dotnet/Zplr.slnx -c Release` and the benchmark at `6,8,12,24` dpmm; record representative hash logs and compare to `fixtures` baselines.

See also: `dotnet/README.md` (port map), `dotnet/DEVELOPMENT.md` (build/test/publish), `docs/COMMAND_SUPPORT.md`.
