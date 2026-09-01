# ZPLr .NET integration guide

`Zplr.Renderer` parses ZPL/ZPL II jobs and renders their printable labels to
Skia-backed canvases, packed monochrome rasters, or PNG byte arrays. It is a
renderer and parser, not a printer driver: it does not send jobs to a printer,
perform network operations, or look up files on behalf of a ZPL job.

This guide targets a .NET 10 application. Use the .NET SDK 10.0.400 or later
and a `net10.0` runtime. The current package version is `0.3.0`. The current
source setup uses the project's private NuGet feed; select it for the package
installation without putting credentials in the command or in the project:

```powershell
$env:NUGET_SOURCE = "http://nuget.wnConsign.com/nuget"
dotnet add package Zplr.Renderer --version 0.3.0 --source $env:NUGET_SOURCE
```

This URL matches the current private-feed setup and should only be used from
an approved, trusted private network. Prefer HTTPS, and enable it here if the
feed supports HTTPS.

Keep the hardcoded `0.3.0` in this example synchronized with the `<Version>`
value in the current `dotnet/Zplr.Renderer/Zplr.Renderer.csproj`. Maintainers
and consumers must update every `0.3.0` in this setup example whenever the
project/package version changes.

Authenticate to a private feed through the developer's NuGet credential
provider or a user-level NuGet configuration. Never put a feed password,
access token, or credential-bearing URL in source control or in a committed
project file. The renderer currently brings these transitive rendering
dependencies into the package/project graph:

| Dependency | Current version |
| --- | ---: |
| SkiaSharp | 4.151.1 |
| QRCoder | 1.8.0 |
| ZXing.Net | 0.16.11 |

These dependency versions are synchronized between `dotnet/README.md` and the
current `dotnet/Zplr.Renderer/Zplr.Renderer.csproj`. The `.csproj` remains
authoritative for future version updates; synchronize the README and this guide
when that project file changes.

The examples below assume a normal .NET 10 project with implicit usings
enabled. If implicit usings are disabled, add the corresponding framework
usings such as `System.IO` and `System.Linq`.

## One-shot PNG rendering

When PNG bytes are the only result an application needs, use the convenience
method `RenderZplPngAsync`:

```csharp
using Zplr.Renderer;

const string zpl = "^XA^FO50,50^ADN,36,20^FDHello from .NET^FS^XZ";
var pngs = await ZplRenderer.RenderZplPngAsync(zpl);

for (var index = 0; index < pngs.Length; index++)
{
    await File.WriteAllBytesAsync($"label-{index + 1}.png", pngs[index]);
}
```

One input can produce multiple labels. For example, a job can contain several
formats or use `^PQ` to request a quantity, so `pngs` is a `byte[][]` rather
than one byte array. Each element is a PNG image. This convenience method
disposes the intermediate `SkiaCanvas` instances on the normal PNG-encoding
path; if `ToPngBytes()` throws, the current implementation does not expose that
canvas for caller cleanup. Use the detailed `RenderZplAsync` API below when
explicit failure-path ownership is required, following its `try/finally`
disposal pattern. The caller owns the returned byte arrays and does not need to
dispose anything after writing them.

Use this entry point when label dimensions, diagnostics, source-linked
regions, or the packed raster are not needed. For those cases, use the detailed
API below.

## Detailed rendering, metadata, and diagnostics

`RenderZplAsync` returns a `RenderJobResult<SkiaCanvas>`. It preserves the
parsed `ZplDocument`, per-label metadata, the packed `MonochromeRaster`, and
the Skia canvas used to produce the label image. Each returned canvas is owned
by the caller and must be disposed, even when the application ultimately
rejects the label because of diagnostics.

```csharp
using Zplr.Renderer;
using Zplr.Renderer.Helper.Rendering;
using Zplr.Renderer.Types;

const string zpl = "^XA^FO50,50^ADN,36,20^FDHello from .NET^FS^XZ";
var result = await ZplRenderer.RenderZplAsync(zpl, new RenderJobOptions
{
    PrintDensity = 8,
    Strict = true,
});

static bool IsRejected(ZplDiagnostic diagnostic) =>
    diagnostic.Severity == ZplDiagnosticSeverity.Error ||
    diagnostic.Code is "UNKNOWN_COMMAND" or "INVALID_COMMAND_PREFIX" or "UNSUPPORTED_COMMAND";

// Strict does not throw or promote diagnostics by itself; enforce policy from
// the returned diagnostic severity and codes before writing any PNG output.
var aggregateRejected = result.Diagnostics.Any(IsRejected);
if (aggregateRejected)
{
    foreach (var diagnostic in result.Diagnostics)
    {
        if (IsRejected(diagnostic))
        {
            Console.Error.WriteLine($"{diagnostic.Severity}: {diagnostic.Code}: {diagnostic.Message}");
        }
    }
}

for (var index = 0; index < result.Labels.Count; index++)
{
    var label = result.Labels[index];
    try
    {
        var labelRejected = aggregateRejected || label.Diagnostics.Any(IsRejected);

        Console.WriteLine($"label {index + 1}: {label.Width}x{label.Height} dots at {label.PrintDensity} dpmm");
        if (!labelRejected)
        {
            await File.WriteAllBytesAsync($"label-{index + 1}.png", label.Canvas.ToPngBytes());
        }
    }
    finally
    {
        label.Canvas.Dispose();
    }
}
```

There are two useful diagnostic views:

- `result.Diagnostics` is the aggregate job view. It includes document/parser
  diagnostics, job-level semantic or limit diagnostics, and the diagnostics
  associated with rendered labels.
- `label.Diagnostics` is the view for one `RenderedLabel<SkiaCanvas>`. Use it
  when deciding whether that particular canvas can be emitted. A label
  diagnostic can therefore also appear in the aggregate result list; the two
  collections are not guaranteed to be disjoint.

Each `ZplDiagnostic` includes a stable `Code`, `Severity`, `Phase`, and
`Message`, and may include a source `Span`, `RelatedSpans`, the relevant
`Command`, and a `LabelIndex`. Branch on `Code` and `Severity` rather than on
English message text.

`RenderedLabel<SkiaCanvas>` exposes:

| Member | Meaning |
| --- | --- |
| `Raster` | A `MonochromeRaster` containing the packed one-bit label data. Its `Data` is MSB-first and `Stride` is the number of bytes per row. |
| `Width` / `Height` | The actual rendered raster dimensions in dots. |
| `PrintDensity` | The selected `PrintDensity` value (`Dpmm6`, `Dpmm8`, `Dpmm12`, or `Dpmm24`). The render option is an `int?`, and valid values are 6, 8, 12, and 24. |
| `HighlightRegions` | Source-linked geometry for rendered fields and shapes. Each region carries a `SourceSpan` and coordinates, with optional size/radius and text caret stops. |
| `Canvas` | The `SkiaCanvas` for PNG export or Skia interop. Call `ToPngBytes()` or `ToSKBitmap()` before `Dispose()`. |

`RenderJobResult<SkiaCanvas>.Document` is the parsed `ZplDocument` used for the
job. A canvas is a disposable native-resource wrapper even if the application
only uses `Raster`, so keep the `try/finally` pattern when using the detailed
API.

### Strict mode

`RenderJobOptions.Strict` is a public property in the strict-mode option
surface. In the current implementation, setting it to `true` does not
automatically throw exceptions or promote warning diagnostics. Callers must
enforce their policy from the returned diagnostic `Severity` and `Code`; the
structured filtering in the example above is therefore still required.

For validation-oriented workflows, set `Strict = true` if that option is part
of the application policy, then reject output when an error diagnostic is
present. When strict command support is required, also treat
`UNKNOWN_COMMAND`, `INVALID_COMMAND_PREFIX`, and `UNSUPPORTED_COMMAND` as
failures. This keeps the decision in the application even when a release
reports a support diagnostic with warning severity.

## Sessions and parsing

### Persistent render sessions

`ZplRenderer.CreateRenderSession` returns an
`IZplRenderSession<SkiaCanvas>`. A session retains virtual-printer state across
renders, including syntax characters, persistent print settings, downloaded
graphics and fonts, stored formats, encodings, and other session resources.
That is useful for a template/resource setup followed by one or more labels,
but it also means a session must be private to one independent job, tenant, or
workflow. Do not share one session between unrelated requests unless the
application provides the ownership and synchronization policy.

The session queues its render operations, but `ResetAsync` is a state reset,
not an application-level ownership or synchronization primitive. Serialize
operations according to the application’s request model and do not use reset
as a substitute for that synchronization.

```csharp
using Zplr.Renderer;
using Zplr.Renderer.Types;

const string firstZpl = "^XA^FO10,10^ADN,18,10^FDOne^FS^XZ";
const string secondZpl = "^XA^FO10,10^ADN,18,10^FDTwo^FS^XZ";
var session = ZplRenderer.CreateRenderSession(new RenderJobOptions { PrintDensity = 8 });
var first = await session.RenderAsync(firstZpl);
var second = await session.RenderAsync(secondZpl);

var labels = first.Labels.Concat(second.Labels).ToArray();
for (var index = 0; index < labels.Length; index++)
{
    var label = labels[index];
    try
    {
        await File.WriteAllBytesAsync($"session-label-{index + 1}.png", label.Canvas.ToPngBytes());
    }
    finally
    {
        label.Canvas.Dispose();
    }
}

await session.ResetAsync();
```

`ResetAsync` clears the session’s persistent syntax, resource, and printer
state for future renders. It does not retroactively dispose canvases already
returned by `RenderAsync`; dispose those canvases at the call site as shown.
For a pre-parsed document, the same interface also provides
`RenderDocumentAsync(ZplDocument document, RenderJobOptions? options = null)`.

### Parse without rendering

`ZplRenderer.ParseDocument(source)` is synchronous and returns a `ZplDocument`.
It is useful for validation, command inspection, and editor tooling before a
render is requested. The document exposes `Labels` and `Diagnostics` directly:

```csharp
using Zplr.Renderer;
using Zplr.Renderer.Types;

const string source = "^XA^FO50,50^ADN,36,20^FDParse only^FS^XZ";
ZplDocument document = ZplRenderer.ParseDocument(source);

Console.WriteLine($"parsed labels: {document.Labels.Count}");
foreach (var diagnostic in document.Diagnostics)
{
    Console.WriteLine($"{diagnostic.Severity}: {diagnostic.Code}: {diagnostic.Message}");
}
```

Parsing does not create a canvas or PNG. `ZplDocument.Source` retains the
source text, `Labels` contains parsed label nodes, and `Diagnostics` contains
parse/semantic findings that can be shown before rendering. The parser also
recognizes job-level commands outside explicit `^XA`/`^XZ` formats where the
selected profile permits them.

## Render options

`RenderJobOptions` is the main configuration object. It inherits the parsing
options `Profile` and `InitialSyntax` from `ParseDocumentOptions`; the table
below covers the rendering and application-integration options.

| Option | Type | Effect |
| --- | --- | --- |
| `Width` | `double?` | Explicit label width in dots. It takes precedence over `^PW` and the fallback width. |
| `Height` | `double?` | Explicit label height in dots. It takes precedence over `^LL` and the fallback height. |
| `PrintDensity` | `int?` | Print density in dots per millimetre. Valid values are `6`, `8`, `12`, and `24`; the default is 8. |
| `FallbackSize` | `FallbackSize?` | Width/height used when neither the option nor `^PW`/`^LL` supplies a dimension. `Unit` is `dots`, `mm`, or `in`; the default is 4 × 6 inches. |
| `Strict` | `bool` | Exposes the strict-mode option. The current implementation does not automatically throw or promote diagnostics; callers must enforce policy from returned severity and codes. |
| `Limits` | `RenderLimits?` | Bounds dimensions, pixels, downloaded graphics, session resources, stored-format depth, expanded commands, and output label count. |
| `FieldValues` | `IReadOnlyDictionary<string, string>?` | Supplies values for numbered `^FN` fields for this render. Keys are strings because they arrive through the public options object. See the field-key note below. |
| `Clock` | `object?` | Supplies deterministic clock data as either a `DateTime` or a `Func<DateTime>`. A UTC `DateTime` is useful for repeatable RTC fields. |
| `FontProvider` | `IFontProvider?` | Resolves a font name asynchronously when the job requests a font that must come from the application. Keep the provider controlled and deterministic for untrusted input. |

This is the current configuration shape:

```csharp
using Zplr.Renderer.Types;

var options = new RenderJobOptions
{
    Width = 812,
    Height = 1218,
    PrintDensity = 8,
    FallbackSize = new FallbackSize(4, 6, "in"),
    Strict = true,
    FieldValues = new Dictionary<string, string>
    {
        ["orderNumber"] = "A-1042",
    },
    Clock = new DateTime(2023, 5, 17, 12, 0, 0, DateTimeKind.Utc),
};
```

**Current field-key behavior:** the example above is valid C# and shows the
requested options shape, but the current C# renderer resolves `FieldValues`
against numeric `^FN` field numbers (`"0"` through `"9999"`). A named key such
as `"orderNumber"` currently produces an `INVALID_FIELD_VALUE_KEY` diagnostic
and is ignored. For a template containing `^FN1`, use
`["1"] = "A-1042"` until named field keys are implemented. This distinction is
important when moving a template from a system that uses application-level
field names.

The default `RenderLimits` values are:

| Limit | Default |
| --- | ---: |
| `MaxDimension` | 32,768 dots |
| `MaxPixels` | 40,000,000 pixels |
| `MaxGraphicBytes` | 16 MiB per decompressed graphic/object |
| `MaxSessionBytes` | 32 MiB of session resources |
| `MaxTemplateDepth` | 16 stored-format expansion levels |
| `MaxExpandedCommands` | 100,000 commands |
| `MaxLabels` | 10,000 output labels |

Pass a `RenderLimits` value when the application needs tighter per-request or
per-tenant bounds. The library’s raster/resource limits do not replace an HTTP
request-body limit or a maximum source-text length; enforce those at the
transport/application boundary before calling the renderer.

### ASP.NET Core endpoint

The following is an endpoint handler body that can be registered for a route
using the hosting or endpoint style already used by the application. It keeps
the PNG response unambiguous by checking that exactly one label was produced:

```csharp
using Microsoft.AspNetCore.Http;
using Zplr.Renderer;
using Zplr.Renderer.Types;

static async Task<IResult> RenderSingleLabelPng(string zpl)
{
    static bool IsRejected(ZplDiagnostic diagnostic) =>
        diagnostic.Severity == ZplDiagnosticSeverity.Error ||
        diagnostic.Code is "UNKNOWN_COMMAND" or "INVALID_COMMAND_PREFIX" or "UNSUPPORTED_COMMAND";

    var result = await ZplRenderer.RenderZplAsync(zpl, new RenderJobOptions { Strict = true });
    var aggregateRejected = result.Diagnostics.Any(IsRejected);
    if (result.Labels.Count != 1)
    {
        foreach (var label in result.Labels)
        {
            label.Canvas.Dispose();
        }

        return Results.BadRequest($"Expected exactly one label, but the renderer produced {result.Labels.Count}.");
    }

    var singleLabel = result.Labels[0];
    try
    {
        var labelRejected = singleLabel.Diagnostics.Any(IsRejected);
        if (aggregateRejected || labelRejected)
        {
            return Results.BadRequest("The ZPL job was rejected by the strict diagnostic policy.");
        }

        return Results.File(singleLabel.Canvas.ToPngBytes(), "image/png");
    }
    finally
    {
        singleLabel.Canvas.Dispose();
    }
}

// Register RenderSingleLabelPng as the handler for POST /labels/png.
```

The handler should be paired with the application’s request-size, request
timeout, authentication, and authorization policy. Do not accept an unlimited
body merely because a single PNG is expected: a ZPL job can request multiple
labels or contain large resources.

## Safety and deployment guidance

### Treat ZPL as untrusted input

ZPL is data, but rendering it can consume CPU and memory. For data received
from a browser, customer, queue, or external integration:

1. Enforce a maximum request body and source-text length before parsing.
2. Set `RenderJobOptions.Limits` to the smallest dimensions, pixel budget,
   graphic budget, session budget, expansion depth, command count, and label
   count that the use case needs.
3. Set `Strict = true` if it is part of the validation workflow, then inspect
   returned diagnostics and reject errors in the caller. If the workflow cannot
   tolerate unknown, prefix-invalid, unsupported, or partial commands, reject
   their diagnostic codes explicitly as well; setting `Strict` alone does not
   throw or promote those diagnostics.
4. Do not share a render session across independent users or jobs. A session
   can retain downloaded objects, formats, fonts, syntax, and printer state.
5. Keep custom `IFontProvider` implementations under application control.
   Provider failures are operational failures and can reject the render; do
   not let arbitrary ZPL choose an untrusted callback or filesystem source.
6. Dispose every `SkiaCanvas` returned by `RenderZplAsync` or a session. Use
   `RenderZplPngAsync` for its normal-path intermediate-canvas disposal when
   explicit failure-path ownership is not required; use the detailed API and
   its `try/finally` pattern when it is.

The renderer reports parse, semantic, unsupported-command, resource, and
render-limit findings as `ZplDiagnostic` values. A rendered canvas is not a
guarantee that the input was fully supported: inspect the aggregate and
per-label diagnostics before returning or storing the image.

### Skia deployment check

`SkiaCanvas` uses SkiaSharp-backed native resources. Exercise a small PNG
smoke test on every operating system, CPU architecture, container base image,
and publishing mode used in production. A build that succeeds on a developer
machine does not by itself prove that the native Skia assets are available in
the deployment environment. Verify the PNG signature, dimensions, and
disposal path as part of the deployment test.

### Known .NET parity gaps

The current `.NET` README records these remaining pixel-exact parity gaps; do
not treat them as fixed by this integration guide:

- `^FB` overprint subtleties for the long `6,4 kg&` case with `^CF0,30` remain
  to be aligned with the reference renderer. The current note specifically
  calls out `MeasureFieldText`, `BitmapFont.GlyphAdvance`, and the
  `residentAdvanceWidth` behavior at 300 dpi for `E`/`H`.
- PDF417 `compact`/`rowMult` behavior and `MICRO_PDF417_VERSIONS` row-address
  patterns can differ from ZXing defaults. The README records hash differences
  for fixtures such as `zplr.zpl` (`d692…` versus `dadde…`) and `retail`
  (`913e…` versus `67a0…`) until those paths are aligned.

If pixel-for-pixel parity with a printer, the TypeScript renderer, or a stored
golden image matters, test the exact ZPL and barcode/font combinations used by
the application rather than relying only on a successful PNG response.

## API selection summary

| Entry point | Return value | Use it when |
| --- | --- | --- |
| `ZplRenderer.RenderZplPngAsync(source, options?)` | `Task<byte[][]>` | PNG bytes are the only required output; the helper disposes intermediate canvases on the normal encoding path. Use the detailed API for explicit failure-path disposal. |
| `ZplRenderer.RenderZplAsync(source, options?)` | `Task<RenderJobResult<SkiaCanvas>>` | The application needs diagnostics, `ZplDocument`, rasters, highlight regions, dimensions, or direct canvas access. |
| `ZplRenderer.CreateRenderSession(options?)` | `IZplRenderSession<SkiaCanvas>` | A private workflow needs persistent ZPL/printer state across renders or needs to call `RenderDocumentAsync` for a pre-parsed document. |
| `ZplRenderer.ParseDocument(source, options?)` | `ZplDocument` | The application needs synchronous parsing, label inspection, or diagnostics without rasterizing. |

## Troubleshooting

| Symptom | What to check |
| --- | --- |
| More than one PNG or label | Inspect `result.Labels.Count` or `pngs.Length`. Check for multiple formats and `^PQ`; do not return `image/png` from a single-label endpoint until the count is exactly one. |
| `FALLBACK_LABEL_WIDTH` or `FALLBACK_LABEL_HEIGHT` | Supply `Width`/`Height`, include `^PW`/`^LL`, or set an appropriate `FallbackSize`. The fallback is converted to dots using the selected print density. |
| A canvas exists but the output is rejected | Read both aggregate and per-label diagnostics. Check `Code`, `Severity`, `Phase`, `Command`, and `LabelIndex`; consult the diagnostic catalog rather than matching message text. |
| Unknown or unsupported ZPL | Enforce a strict application policy by checking returned diagnostic severity/codes; setting `Strict` alone does not throw or promote diagnostics. Review the command-support table for the selected profile and its limitations. |
| Field values are not substituted | Use numeric string keys that match `^FN` values, such as `"1"` for `^FN1`; named keys such as `"orderNumber"` are currently diagnosed and ignored by the C# implementation. |
| Output dimensions or text differ | Confirm `PrintDensity`, explicit dimensions, `FallbackSize`, font availability, and the known parity gaps. Run the same ZPL on the target deployment platform. |
| Resource/label limit diagnostic | Tighten or adjust the request policy deliberately through `RenderLimits`; do not simply remove limits for untrusted input. |
| PNG export fails only in production | Run the Skia deployment check for the exact OS, architecture, container, and publish mode. Ensure native Skia assets are present. |

For the complete support and diagnostic references, see:

- [ZPL command support](../docs/COMMAND_SUPPORT.md)
- [Diagnostic codes](../docs/DIAGNOSTICS.md)
- [.NET renderer README](README.md)
- [Project README](../README.md)
