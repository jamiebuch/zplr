# Zplr .NET renderer development guide

This guide is for maintainers extending, testing, packaging, and publishing the
.NET renderer. The .NET implementation is intended to remain a file-for-file
comparable port of the Node renderer where the port is applicable, so changes
can be reviewed by comparing the corresponding TypeScript and C# files.

## Prerequisites and repository layout

Install the .NET SDK **10.0.400 or later**. The renderer targets **`net10.0`**.
The current direct package references are:

| Package | Version |
| --- | ---: |
| SkiaSharp | 4.151.1 |
| QRCoder | 1.8.0 |
| ZXing.Net | 0.16.11 |

The versions above are read from `dotnet/Zplr.Renderer/Zplr.Renderer.csproj`,
which is authoritative when other documentation has not yet been synchronized.

| Path | Purpose |
| --- | --- |
| `dotnet/Zplr.Renderer` | The `Zplr.Renderer` library: parser, interpreter, raster/layout renderers, Skia-backed canvas, and generated assets. |
| `dotnet/Zplr.Renderer.Tests` | xUnit smoke, representative-fixture, diagnostic, and session tests for the .NET port. |
| `src/` | The Node/TypeScript renderer and its tests; use the relevant TypeScript implementation as the porting reference. |
| `fixtures/` | Shared ZPL inputs used by representative renderer tests. |
| `dotnet/Zplr.slnx` | The solution containing the renderer and test projects. |
| `dotnet/README.md` | User-facing .NET overview, API examples, port map, coverage, and known parity notes. |

The port does not include the browser renderer or Nuxt application. In
particular, `src/index.web.ts` remains TypeScript; the .NET project mirrors the
Node `skia-canvas` rendering path.

## Restore, build, and test

Run these commands from the repository root in PowerShell:

```powershell
dotnet restore dotnet/Zplr.slnx
dotnet build dotnet/Zplr.slnx -c Release
dotnet test dotnet/Zplr.slnx -c Release
dotnet test dotnet/Zplr.slnx -c Release --filter "FullyQualifiedName~SmokeTests.RenderSimpleTextProducesPng"
```

`SmokeTests` are fast end-to-end checks for basic parsing, PNG generation,
sessions, fields, graphics, text wrapping, barcodes, and quantities. The
`RenderSimpleTextProducesPng` filter is a focused sanity check: it confirms that
a simple label produces one non-empty PNG with the PNG signature.

`RepresentativeLabelsTests` render the shared files in `fixtures/`, check for
error diagnostics, exercise stored-resource recall, and inspect border pixels.
They also calculate and log canonical raster hashes. The representative hashes
are intentionally logged but not yet enforced: the tests currently document
the comparison values while known `^FB` and PDF417/micro-PDF417 differences are
still being aligned. A passing test run therefore does not claim pixel-exact
hash parity.

Diagnostic assertions are part of the port contract. A rendered image can exist
even when the input produced parser, unsupported-command, resource, or render
limit diagnostics, so tests should assert the relevant diagnostic severity and
codes rather than treating successful PNG creation as full support.

## Porting and test workflow

For a renderer change, compare the TypeScript implementation with its C#
counterpart. For example:

```powershell
git diff -- src/core/foo.ts
# Apply the corresponding change in:
# dotnet/Zplr.Renderer/Core/Foo.cs
```

Use PascalCase for C# types and members and the existing C# conventions for
`string`, `int`, `bool`, `List<T>`, and `Dictionary<TKey, TValue>`. Keep the
implementation structure and behavior comparable to the Node renderer, while
using the appropriate .NET and SkiaSharp APIs.

When generated font assets are affected, run the JavaScript helper that owns the
TypeScript asset, then perform the C# conversion/export as a separate, reviewed
step. These helpers do **not** update the .NET asset files:

1. `node scripts/generate-font-assets.mjs` reads
   `scripts/font-sources/TeXGyreHerosCn-Bold.otf` and writes only
   `src/assets/texGyreHerosCondensed.generated.ts`.
2. `scripts/generate-spleen-font.mjs` reads a Spleen 5x8 BDF from standard input
   and writes only `src/assets/spleen5x8.generated.ts`. In PowerShell, provide an
   explicit local BDF path, for example:

   ```powershell
   Get-Content -Raw -LiteralPath "C:\fonts\spleen-5x8.bdf" |
       node scripts/generate-spleen-font.mjs
   ```

This checkout has no checked-in converter that directly produces
`dotnet/Zplr.Renderer/Assets/*.cs`. The prior `dotnet/README.md` reference to
`D:\Cache\Temp\opencode\gen*.py` describes an external/local workflow, not a
command available from a fresh checkout. After either TypeScript helper runs,
compare its generated output with the paired C# asset, update the C# asset using
the available local conversion/export workflow, and review the generated diff.
Preserve the source SHA-256, source version, and calibration constants from the
generated asset (including the TeX Gyre advance/vertical/top-offset values and
the Spleen version/hash) when exporting to C#. The JavaScript helpers are not a
substitute for that C# step and must not be described as updating `Assets/*.cs`.

### Renderer-change checklist

- [ ] Review the relevant `src/core/*.ts` diff and identify the behavior being ported.
- [ ] Apply the equivalent change to the matching `dotnet/Zplr.Renderer/Core/*.cs` file, preserving C# naming and types.
- [ ] Add or update a mirrored xUnit test in `dotnet/Zplr.Renderer.Tests`.
- [ ] Add or update a shared fixture when the behavior is best represented by a complete ZPL job.
- [ ] Regenerate affected assets with the checked-in helper and inspect the result.
- [ ] Run `dotnet test dotnet/Zplr.slnx -c Release` and review diagnostics, not only exit status.
- [ ] If output is not pixel-exact, add the parity limitation to the appropriate README notes and explain the expected difference in the test or change description.

## Pack and inspect a local package

The project currently declares version `0.3.0` in
`dotnet/Zplr.Renderer/Zplr.Renderer.csproj`. To create a local Release package
using that current version:

```powershell
dotnet pack dotnet/Zplr.Renderer/Zplr.Renderer.csproj -c Release --no-restore --output dotnet/artifacts/packages
Get-ChildItem -LiteralPath dotnet/artifacts/packages -Filter "Zplr.Renderer.0.3.0.nupkg"
```

Before a release, change the `<Version>` value in
`dotnet/Zplr.Renderer/Zplr.Renderer.csproj` to the new, unused release version
and update the package filename used in the inspection and publishing steps.
Do not publish a package merely because `dotnet pack` succeeded. Open or list
the generated `.nupkg` and inspect its contents, dependency versions, license
and metadata files, and included library assets before publication.

## Secret-safe publishing

Supply the feed and API key through the developer's secure environment or NuGet
credential tooling. Never place a real key in this guide, source control, a
committed project file, or a command copied into shell history.

```powershell
if (-not $env:NUGET_SOURCE) { throw "Set NUGET_SOURCE before publishing." }
if (-not $env:NUGET_API_KEY) { throw "Set NUGET_API_KEY before publishing." }

dotnet nuget push "dotnet/artifacts/packages/Zplr.Renderer.0.3.0.nupkg" `
    --source $env:NUGET_SOURCE `
    --api-key $env:NUGET_API_KEY
```

Set `NUGET_SOURCE` to `http://nuget.wnConsign.com/nuget` through the secure
environment used for the release. This URL matches the current private-feed
setup and should only be used from an approved, trusted private network.
Prefer HTTPS, and enable it here if the feed supports HTTPS. The example
deliberately references
`$env:NUGET_API_KEY` rather than showing a literal credential. An already
published version may be rejected by the feed; do not overwrite it unless the
feed policy explicitly permits that operation. Confirm the package version and
contents before retrying a rejected publication.

## Release checklist

- [ ] Update `<Version>` in `dotnet/Zplr.Renderer/Zplr.Renderer.csproj` to the intended unused version.
- [ ] Perform a clean restore, Release build, and full Release test run.
- [ ] Review test diagnostics and representative hash logs; investigate unexpected changes.
- [ ] Pack the renderer and inspect the `.nupkg` contents and dependency metadata.
- [ ] Review dependency versions and transitive package changes before release.
- [ ] Confirm `NUGET_SOURCE` and credentials are supplied by secure tooling, not committed files or literal command arguments.
- [ ] Publish the package to the configured feed and record the feed result.
- [ ] Run a consumer smoke test against the published package, including PNG generation and disposal of returned Skia resources where applicable.

## Known limitations and references

The current .NET renderer has two notable pixel-parity limitations:

- `^FB` overprint subtleties remain for the long `6,4 kg&` case with `^CF0,30`. In particular, `MeasureFieldText` currently uses `BitmapFont.GlyphAdvance` without the reference renderer's `residentAdvanceWidth` behavior at 300 dpi for `E` and `H`.
- PDF417 `compact`/`rowMult` behavior and `MICRO_PDF417_VERSIONS` row-address patterns can differ from ZXing defaults. Representative hashes such as `zplr.zpl` (`d692…` versus `dadde…`) and `retail` (`913e…` versus `67a0…`) therefore remain logged rather than enforced until those paths converge.

These limitations should be considered when reviewing output or changing the
barcode and text layout paths. See the related project references:

- [.NET renderer README](README.md)
- [ZPL command support](../docs/COMMAND_SUPPORT.md)
- [Conformance notes](../docs/CONFORMANCE.md)
