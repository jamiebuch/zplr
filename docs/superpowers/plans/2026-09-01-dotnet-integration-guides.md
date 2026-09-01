# .NET Integration Guides Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add separate, accurate usage and maintainer guides for the .NET renderer and link them from the .NET project README.

**Architecture:** Keep the consumer workflow in `dotnet/INTEGRATION.md` and the repository workflow in `dotnet/DEVELOPMENT.md`. Update only the .NET README’s navigation and stale dependency-version line; do not duplicate the detailed guides or alter the renderer implementation. Validate all API examples against the current public C# types and verify the repository build/test commands.

**Tech Stack:** Markdown, .NET 10.0.400+, C# 14/current project language version, NuGet, SkiaSharp 4.151.1, QRCoder 1.8.0, ZXing.Net 0.16.11, xUnit.

## Global Constraints

- The consumer guide targets application developers consuming the `Zplr.Renderer` NuGet package from the configured private feed.
- The maintainer guide targets contributors who build, test, port, pack, and publish the repository’s .NET renderer.
- The current project target is `net10.0`; the repository README requires .NET SDK `10.0.400+`.
- The current package version is `0.3.0`; examples may use it, but must tell maintainers to update it when the project version changes.
- Use `http://nuget.wnConsign.com/nuget` as the configured source URL without including an API key, password, or username in committed files.
- The public entry points are `ZplRenderer.RenderZplPngAsync`, `ZplRenderer.RenderZplAsync`, `ZplRenderer.CreateRenderSession`, and `ZplRenderer.ParseDocument`.
- `SkiaCanvas` is in `Zplr.Renderer.Helper.Rendering`, implements `IDisposable`, and must be disposed when returned by `RenderZplAsync` or a session; `RenderZplPngAsync` disposes its canvases internally.
- Sessions expose `RenderAsync`, `RenderDocumentAsync`, and `ResetAsync`, but do not implement `IDisposable`; the guide must not show `using` or `await using` for a session.
- Diagnostics are returned as `ZplDiagnostic` values; examples must show filtering `ZplDiagnosticSeverity.Error` rather than claiming every unsupported command throws.
- Do not modify the existing uncommitted dependency changes in `dotnet/Zplr.Renderer/Zplr.Renderer.csproj` while adding documentation.
- Do not commit, push, or publish repository changes unless the user explicitly requests that operation.

---

### Task 1: Write the consumer integration guide

**Files:**
- Create: `dotnet/INTEGRATION.md`
- Read for API verification: `dotnet/Zplr.Renderer/ZplRenderer.cs`, `dotnet/Zplr.Renderer/Types/RenderJob.cs`, `dotnet/Zplr.Renderer/Types/ZplDocument.cs`, `dotnet/Zplr.Renderer/Helper/Rendering/Canvas.cs`

**Interfaces:**
- Consumes: `ZplRenderer`, `RenderJobOptions`, `RenderJobResult<SkiaCanvas>`, `RenderedLabel<SkiaCanvas>`, `IZplRenderSession<SkiaCanvas>`, `ZplDocument`, `ZplDiagnostic`, and `SkiaCanvas`.
- Produces: A standalone guide that a .NET 10 application developer can follow without reading renderer internals.

- [ ] **Step 1: Add package setup and requirements**

Start the file with the package purpose, .NET SDK/runtime requirement, and the current package/source setup. Show source selection without credentials:

```powershell
$env:NUGET_SOURCE = "http://nuget.wnConsign.com/nuget"
dotnet add package Zplr.Renderer --version 0.3.0 --source $env:NUGET_SOURCE
```

State that private-feed authentication belongs in the developer’s NuGet credential provider or user-level NuGet configuration, never in source control or committed project files. Mention the transitive rendering dependencies by their current project versions: SkiaSharp 4.151.1, QRCoder 1.8.0, and ZXing.Net 0.16.11.

- [ ] **Step 2: Add the one-shot PNG example**

Use this exact shape for the first runnable example:

```csharp
using Zplr.Renderer;

const string zpl = "^XA^FO50,50^ADN,36,20^FDHello from .NET^FS^XZ";
var pngs = await ZplRenderer.RenderZplPngAsync(zpl);

for (var index = 0; index < pngs.Length; index++)
{
    await File.WriteAllBytesAsync($"label-{index + 1}.png", pngs[index]);
}
```

Explain that one input can produce multiple labels and that this convenience method returns PNG byte arrays and owns disposal of the intermediate canvases.

- [ ] **Step 3: Add detailed rendering and diagnostics**

Show `RenderZplAsync` with the actual namespaces and a `try/finally` around each canvas:

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

if (result.Diagnostics.Any(d => d.Severity == ZplDiagnosticSeverity.Error))
{
    foreach (var diagnostic in result.Diagnostics)
    {
        Console.Error.WriteLine($"{diagnostic.Severity}: {diagnostic.Code}: {diagnostic.Message}");
    }
}

for (var index = 0; index < result.Labels.Count; index++)
{
    var label = result.Labels[index];
    try
    {
        var labelErrors = label.Diagnostics
            .Where(d => d.Severity == ZplDiagnosticSeverity.Error)
            .ToArray();

        Console.WriteLine($"label {index + 1}: {label.Width}x{label.Height} dots at {label.PrintDensity} dpmm");
        if (labelErrors.Length == 0)
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

Document the distinction between result-level and label-level diagnostics, plus `Raster`, `Width`, `Height`, `PrintDensity`, `HighlightRegions`, and `Canvas`.

- [ ] **Step 4: Add session and parser examples**

Show that a session retains persistent ZPL state and must be isolated per independent job:

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

Also show `ZplRenderer.ParseDocument(source)` returning a `ZplDocument`, reading `Labels` and `Diagnostics`, and explain that `ResetAsync` clears persistent session state but does not replace application-level synchronization.

- [ ] **Step 5: Document options and service integration**

Include an options table for `Width`, `Height`, `PrintDensity`, `FallbackSize`, `Strict`, `Limits`, `FieldValues`, `Clock`, and `FontProvider`. Use this actual configuration example:

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

Add one short ASP.NET Core endpoint example using `RenderZplPngAsync` and `Results.File`, with an explicit single-label check before returning `image/png`; keep the example independent of a specific hosting/bootstrap style.

- [ ] **Step 6: Add safety, API summary, and troubleshooting**

Explain request-size limits, `RenderLimits`, strict mode, untrusted ZPL, session isolation, canvas disposal, and the fact that Skia-backed rendering should be exercised on each deployment platform. Add a table mapping the four entry points to their use cases. Link to `../docs/COMMAND_SUPPORT.md`, `../docs/DIAGNOSTICS.md`, and `README.md`. Include the current known parity gaps from `dotnet/README.md` without presenting them as fixed.

### Task 2: Write the maintainer/developer guide

**Files:**
- Create: `dotnet/DEVELOPMENT.md`
- Read for repository workflow: `dotnet/README.md`, `dotnet/Zplr.slnx`, `dotnet/Zplr.Renderer/Zplr.Renderer.csproj`, `dotnet/Zplr.Renderer.Tests/SmokeTests.cs`, `dotnet/Zplr.Renderer.Tests/RepresentativeLabelsTests.cs`

**Interfaces:**
- Consumes: The repository’s existing build/test commands, project layout, test names, package metadata, and TypeScript-to-C# porting notes.
- Produces: A maintainer workflow guide that can be used to build, test, extend, pack, and publish the .NET renderer without exposing credentials.

- [ ] **Step 1: Document prerequisites and repository layout**

State `.NET SDK 10.0.400+`, the `net10.0` target, and the current direct package versions. Add a concise table covering `dotnet/Zplr.Renderer`, `dotnet/Zplr.Renderer.Tests`, `src/`, `fixtures/`, `dotnet/Zplr.slnx`, and `dotnet/README.md`. Explain that the C# project mirrors the Node renderer where the port is intended to remain file-for-file comparable.

- [ ] **Step 2: Document restore, build, and test commands**

Include these commands exactly:

```powershell
dotnet restore dotnet/Zplr.slnx
dotnet build dotnet/Zplr.slnx -c Release
dotnet test dotnet/Zplr.slnx -c Release
dotnet test dotnet/Zplr.slnx -c Release --filter "FullyQualifiedName~SmokeTests.RenderSimpleTextProducesPng"
```

Explain the purpose of `SmokeTests`, `RepresentativeLabelsTests`, diagnostic assertions, and the intentionally logged-but-not-yet-enforced representative hashes.

- [ ] **Step 3: Document the porting and test workflow**

Use the repository’s current workflow: compare `src/core/foo.ts` with `dotnet/Zplr.Renderer/Core/Foo.cs`, preserve PascalCase/C# type conventions, add or update a mirrored xUnit test, and update generated assets using the checked-in generation helpers. Include a concrete checklist for a renderer change: source diff, C# port, fixture/test, `dotnet test`, and README parity notes if behavior is not pixel-exact.

- [ ] **Step 4: Document packing and local package inspection**

Show a Release package command and its expected output location:

```powershell
dotnet pack dotnet/Zplr.Renderer/Zplr.Renderer.csproj -c Release --no-restore --output dotnet/artifacts/packages
Get-ChildItem -LiteralPath dotnet/artifacts/packages -Filter "Zplr.Renderer.0.3.0.nupkg"
```

Explain that `<Version>0.3.0</Version>` currently lives in `dotnet/Zplr.Renderer/Zplr.Renderer.csproj`, that a release version must be changed before packing, and that the generated `.nupkg` should be inspected before publication.

- [ ] **Step 5: Document secret-safe publishing**

Use environment variables and never show the real API key:

```powershell
if (-not $env:NUGET_SOURCE) { throw "Set NUGET_SOURCE before publishing." }
if (-not $env:NUGET_API_KEY) { throw "Set NUGET_API_KEY before publishing." }

dotnet nuget push "dotnet/artifacts/packages/Zplr.Renderer.0.3.0.nupkg" `
    --source $env:NUGET_SOURCE `
    --api-key $env:NUGET_API_KEY
```

State that `NUGET_SOURCE` should point to `http://nuget.wnConsign.com/nuget`, credentials must be supplied through the developer’s secure environment/credential tooling, and the command should not be pasted with a literal key into shell history or documentation. Mention that an already-published version may be rejected by the feed and should not be overwritten unless the feed policy permits it.

- [ ] **Step 6: Add release checklist and known limitations**

Add a checklist covering version update, clean restore/build/test, package inspection, dependency review, diagnostics review, feed publication, and consumer smoke test. Copy the current `FB` overprint and PDF417/micro-PDF417 parity limitations from `dotnet/README.md`, and link back to `README.md`, `../docs/COMMAND_SUPPORT.md`, and `../docs/CONFORMANCE.md`.

### Task 3: Link and synchronize the .NET README

**Files:**
- Modify: `dotnet/README.md:28-33` for current direct dependency versions
- Modify: `dotnet/README.md:33-55` to add the Guides section near Quick start

**Interfaces:**
- Consumes: `dotnet/INTEGRATION.md` and `dotnet/DEVELOPMENT.md`.
- Produces: A discoverable README entry point whose requirements do not contradict the project file.

- [ ] **Step 1: Correct the dependency version line**

Change the dependency line to match `dotnet/Zplr.Renderer/Zplr.Renderer.csproj`: SkiaSharp 4.151.1, QRCoder 1.8.0, and ZXing.Net 0.16.11.

- [ ] **Step 2: Add guide links without duplicating content**

Insert this section after Quick start:

```markdown
## Guides

- [Application integration](INTEGRATION.md) — install and use the NuGet package from a .NET application.
- [Development and release](DEVELOPMENT.md) — build, test, extend, pack, and publish the renderer.
```

Do not copy the guide examples into the README.

### Task 4: Validate documentation examples and repository state

**Files:**
- Read: `dotnet/INTEGRATION.md`, `dotnet/DEVELOPMENT.md`, `dotnet/README.md`, `docs/superpowers/specs/2026-09-01-dotnet-integration-guides-design.md`
- Temporary verification project: `D:\Cache\Temp\opencode\zplr-dotnet-docs-check`

**Interfaces:**
- Consumes: The completed Markdown examples and current project reference.
- Produces: Fresh evidence that the examples compile, the solution builds/tests, links resolve, and no credential was added.

- [ ] **Step 1: Compile the consumer snippets against the project**

Create a temporary .NET 10 console project outside the repository, reference the local project, replace its generated `Program.cs` with this complete API smoke program, and compile it:

```csharp
using Zplr.Renderer;
using Zplr.Renderer.Helper.Rendering;
using Zplr.Renderer.Types;

const string zpl = "^XA^FO50,50^ADN,36,20^FDDocs check^FS^XZ";

var pngs = await ZplRenderer.RenderZplPngAsync(zpl);
if (pngs.Length != 1 || pngs[0].Length == 0)
{
    throw new InvalidOperationException("One-shot rendering did not return one PNG.");
}

var result = await ZplRenderer.RenderZplAsync(zpl, new RenderJobOptions { PrintDensity = 8 });
foreach (var label in result.Labels)
{
    try
    {
        _ = label.Raster;
        _ = label.Canvas.ToPngBytes();
    }
    finally
    {
        label.Canvas.Dispose();
    }
}

var session = ZplRenderer.CreateRenderSession();
var sessionResult = await session.RenderAsync(zpl);
foreach (var label in sessionResult.Labels)
{
    label.Canvas.Dispose();
}
await session.ResetAsync();

var document = ZplRenderer.ParseDocument(zpl);
_ = document.Labels;
_ = document.Diagnostics;

var options = new RenderJobOptions
{
    FallbackSize = new FallbackSize(4, 6, "in"),
    Strict = true,
    FieldValues = new Dictionary<string, string> { ["orderNumber"] = "A-1042" },
    Clock = new DateTime(2023, 5, 17, 12, 0, 0, DateTimeKind.Utc),
};
_ = await ZplRenderer.RenderZplAsync(zpl, options);
_ = ZplDiagnosticSeverity.Error;
```

Run:

```powershell
Test-Path -LiteralPath "D:\Cache\Temp\opencode"
dotnet new console --framework net10.0 --output "D:\Cache\Temp\opencode\zplr-dotnet-docs-check"
dotnet add "D:\Cache\Temp\opencode\zplr-dotnet-docs-check" reference "D:\github\zplr\dotnet\Zplr.Renderer\Zplr.Renderer.csproj"
dotnet build "D:\Cache\Temp\opencode\zplr-dotnet-docs-check" --configuration Release
```

Expected result: the temporary project builds with exit code 0 and the program compiles calls to `RenderZplPngAsync`, `RenderZplAsync`, `CreateRenderSession`, `ParseDocument`, `RenderJobOptions`, `FallbackSize`, `ZplDiagnosticSeverity`, and `SkiaCanvas.Dispose()`.

- [ ] **Step 2: Run the complete repository verification**

Run:

```powershell
dotnet build dotnet/Zplr.slnx -c Release
dotnet test dotnet/Zplr.slnx -c Release
```

Expected result: both commands exit with code 0; the test command reports all discovered tests passing.

- [ ] **Step 3: Check links, secrets, and consistency**

Verify the relative links from `dotnet/README.md`, `dotnet/INTEGRATION.md`, and `dotnet/DEVELOPMENT.md` resolve to existing files. Search the changed documents for `wnNug`, `API_KEY`, `NUGET_API_KEY`, `password`, and `secret`; only the variable name and secret-handling guidance may remain, never a literal credential. Compare every documented public API name and dependency version with the current C# source/project file.

- [ ] **Step 4: Inspect the final implementation diff**

Run `git status --short` and `git diff -- dotnet/README.md dotnet/INTEGRATION.md dotnet/DEVELOPMENT.md`. Confirm that only the two guides and intended README navigation/version corrections are part of this documentation change; preserve the pre-existing `dotnet/Zplr.Renderer/Zplr.Renderer.csproj` modification.
