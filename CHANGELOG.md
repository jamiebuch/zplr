# Changelog

All notable changes follow Keep a Changelog. ZPLr uses semantic versioning from 0.3.0 onward.

## [0.3.1] - 2026-09-01

### Changed — .NET renderer performance

- `SkiaCanvas.DrawRaster` now does a single bulk 1bpp→RGBA expansion with `ArrayPool` + `SKImage.FromPixels` → `DrawImage` instead of `SetPixel` per dot, targeting all print densities `6/8/12/24` dpmm.
- `FontEngine` now shares a process-wide `ConcurrentDictionary` glyph cache for the built-in `TexGyreHeros` OTF (keyed by `char:width:height` so densities do not collide, 64M-pixel budget) with a static `Lazy<SKTypeface>`; provider fonts remain per-engine. See `dotnet/PERFORMANCE.md`.
- `Raster.FillRect`/`BlitRaster` operate on packed bytes with masks instead of per-dot `SetDot`/`GetDot`; `BitmapFont.GlyphAdvance` no longer uses `Regex`.
- All literal `Regex` patterns in `Interpreter`, `GraphicDecoder`, `JobRenderer`, `LayoutRenderer`, `PngDecoder`, `DocumentParser` are now `static readonly Compiled`.
- `Interpreter.DecodeFieldBytes`/`DecodeHexFieldData` avoid LINQ allocations.

## [0.3.0] - 2026-07-27

### Added

- Stable job APIs: `renderZpl`, `renderZplPNG`, `createRenderSession`, and `parseDocument` on Node-default, Node, and web entry points.
- UTF-16 source navigation with `findCommandAtOffset` and source-linked raster navigation with `findHighlightRegionAtPoint`.
- Required Node/browser canvases, packed one-bit rasters, stable diagnostic documentation, resource limits, font providers, and FIFO virtual-printer sessions.
- Pinned October 10, 2025 command catalog with capability smoke coverage, category-specific semantic/raster evidence, and raster-neutral device-command verification.
- Seeded malformed-input and resource-limit fuzzing, package/API snapshots, tarball consumers, bundle budgets, and cross-browser playground gates.

### Changed

- `zplr` is now the Node-default alias; ESM on Node 22/24 and evergreen browsers is the supported runtime baseline.
- Command capability lookup requires a full identity such as `^FO` or `~DG`.
- Rendering now has one canonical parser → interpreter → packed-raster → platform-canvas pipeline.
- Raster output now follows physical-printer captures for graphic primitives, QR/Aztec/CODABLOCK/GS1 and postal barcode geometry, interpretation lines, unit conversion, and resident-font metrics. Font 0 uses the bundled open-source TeX Gyre Heros Condensed Bold substitute.
- Syntax and semantic failures plus safety limits resolve through diagnostics; documented parameter defaults remain silent, while operational host-adapter and user-callback/provider failures reject.

### Removed

- The 0.2 command-class renderer, legacy `parse`/`render`, top-level `renderDocument`, `parseAndRender*`, advanced render helpers, index-based browser helpers, and legacy command/context types.
- The `dpi` option, `zpl-ii-2006` alias, prefixless capability lookup, and dependencies used only by the superseded Canvas pipeline.

### Release status

The package remains unreleased until `0.3.0-rc.1` passes external npm installation and Cloudflare preview testing. See `docs/RELEASE.md`.

## [0.2.0] - 2025-10-11

- Last release carrying the compatibility command-object API.
