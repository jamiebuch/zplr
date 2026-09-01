# .NET Integration Guides Design

## Goal

Add two task-oriented guides for the .NET renderer:

- `dotnet/INTEGRATION.md` for application developers consuming the renderer as a NuGet package.
- `dotnet/DEVELOPMENT.md` for contributors and maintainers working on the renderer repository.

The existing `dotnet/README.md` remains the project overview and gets links to both guides. The guides must describe the current implementation rather than an aspirational API.

## Audience and scope

### Consumer guide

The consumer guide should answer how to install, call, configure, and safely operate the package from a .NET 10 application. It should cover:

1. Prerequisites and package installation from the private NuGet feed without recording credentials in source control.
2. A minimal one-shot ZPL-to-PNG example.
3. Rendering through `RenderZplAsync` when callers need raster data, dimensions, print density, canvases, or diagnostics.
4. Rendering multiple labels and writing each canvas to a file or HTTP response.
5. Reusing `IZplRenderSession<SkiaCanvas>` for persistent ZPL state and resetting it between independent jobs.
6. Parsing with `ParseDocument` when callers need document structure or parser diagnostics without rendering.
7. Relevant `RenderJobOptions`, including print density, fallback size, strict mode, field values, clock, and resource limits.
8. Diagnostic handling, canvas disposal, concurrency/isolation guidance, and resource limits for untrusted input.
9. A compact public API map and troubleshooting notes, including the current supported-target/runtime requirement and known rendering-parity limitations.

Examples should use `using Zplr.Renderer;`, `using Zplr.Renderer.Types;`, `using Zplr.Renderer.Helper.Rendering;` where `SkiaCanvas` is referenced, and the actual public return types. They must not assume ASP.NET Core-specific infrastructure, but one short ASP.NET Core endpoint example may show how to return PNG bytes.

### Maintainer guide

The maintainer guide should answer how to build, test, extend, package, and publish the project. It should cover:

1. Required SDK and dependency restoration.
2. Repository layout and the relationship between `src/` and `dotnet/`.
3. Restore, build, test, and focused-test commands.
4. Test conventions for smoke tests, representative fixtures, diagnostics, and raster/hash checks.
5. The workflow for porting a TypeScript renderer change to C# and updating generated assets.
6. Package creation, version management, local package inspection, and publishing to the private feed.
7. Secret-safe publishing using environment variables or a credential provider; the API key must not appear in the document or command history examples.
8. A release checklist and the current known parity gaps.

## File structure

### `dotnet/INTEGRATION.md`

Sections, in order:

1. Title and package purpose.
2. Requirements and installation.
3. Quick start: one-shot PNG rendering.
4. Rendering results and diagnostics.
5. Sessions and persistent state.
6. Parsing without rendering.
7. Options and input substitution.
8. ASP.NET Core response example.
9. Safety, limits, disposal, and concurrency.
10. API summary and troubleshooting.
11. Links to command support and the repository README.

### `dotnet/DEVELOPMENT.md`

Sections, in order:

1. Title and maintainer scope.
2. Prerequisites and checkout setup.
3. Project structure.
4. Build, test, and focused verification.
5. Adding or porting functionality.
6. Fixtures, assets, and generated code.
7. Pack and inspect a NuGet package.
8. Publish a package safely.
9. Release checklist.
10. Known parity gaps and links to related repository documentation.

### `dotnet/README.md`

Add a short “Guides” section near the quick start with links to `INTEGRATION.md` and `DEVELOPMENT.md`. Do not duplicate their detailed content.

## Technical decisions

- Use the package’s current version and target framework as examples, but make the version easy to update when the project version changes.
- Show the private feed URL as a source configuration value, but use placeholders/environment variables for credentials.
- Prefer `ZplRenderer.RenderZplPngAsync` for the shortest path and `ZplRenderer.RenderZplAsync` for callers needing `RenderedLabel<SkiaCanvas>` details.
- Explicitly dispose `SkiaCanvas` instances returned by `RenderZplAsync` or sessions. The `RenderZplPngAsync` convenience method already disposes its canvases.
- Explain that sessions hold persistent state and should not be shared across unrelated or concurrent jobs unless the caller provides synchronization and isolation.
- Treat diagnostics as data returned by parsing/rendering; examples should show filtering `ZplDiagnosticSeverity.Error` rather than claiming every unsupported command throws.
- Include limits and strict mode as defense-in-depth for untrusted ZPL, without promising that they replace application-level authorization or request-size limits.
- Keep examples compilable against the current public types (`RenderJobOptions`, `RenderJobResult<TCanvas>`, `RenderJobResult<SkiaCanvas>`, `IZplRenderSession<SkiaCanvas>`, `ZplDocument`, and `SkiaCanvas`).

## Validation

Before considering the guides complete:

1. Check every API name and property against the current C# source.
2. Run the code snippets that can be isolated into a small temporary .NET 10 consumer, or compile equivalent examples against the project.
3. Run `dotnet build dotnet/Zplr.slnx -c Release` and `dotnet test dotnet/Zplr.slnx -c Release`.
4. Verify all links and confirm no credentials or real API keys appear in either guide.
5. Review the docs for consistency with the existing README’s requirements, current package version, and stated parity limitations.
