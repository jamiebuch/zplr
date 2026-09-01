// Port of src/core/renderDocument.ts
using Zplr.Renderer.Helper.Rendering;
using Zplr.Renderer.Types;

namespace Zplr.Renderer.Core;

public sealed class DocumentRenderResult<TCanvas> where TCanvas : ICanvas
{
    public TCanvas Canvas { get; set; } = default!;
    public MonochromeRaster Raster { get; set; } = null!;
    public int Width { get; set; }
    public int Height { get; set; }
    public PrintDensity PrintDensity { get; set; }
    public List<ZplDiagnostic> Diagnostics { get; set; } = new();
    public List<HighlightRegion> HighlightRegions { get; set; } = new();
}

public static class RenderDocument
{
    private static readonly RenderLimits DefaultLimits = new();

    public static RenderLimits ResolveRenderLimits(RenderLimits? overrides)
    {
        if (overrides == null) return DefaultLimits;
        return new RenderLimits(
            overrides.MaxDimension != 0 ? overrides.MaxDimension : DefaultLimits.MaxDimension,
            overrides.MaxPixels != 0 ? overrides.MaxPixels : DefaultLimits.MaxPixels,
            overrides.MaxGraphicBytes != 0 ? overrides.MaxGraphicBytes : DefaultLimits.MaxGraphicBytes,
            overrides.MaxSessionBytes != 0 ? overrides.MaxSessionBytes : DefaultLimits.MaxSessionBytes,
            overrides.MaxTemplateDepth != 0 ? overrides.MaxTemplateDepth : DefaultLimits.MaxTemplateDepth,
            overrides.MaxExpandedCommands != 0 ? overrides.MaxExpandedCommands : DefaultLimits.MaxExpandedCommands,
            overrides.MaxLabels != 0 ? overrides.MaxLabels : DefaultLimits.MaxLabels
        );
    }

    private static PrintDensity Density(RenderDocumentOptions opts)
    {
        var pd = opts.PrintDensity;
        if (pd == 6 || pd == 8 || pd == 12 || pd == 24) return (PrintDensity)pd;
        return PrintDensity.Dpmm8;
    }

    private static int LegacyDpi(PrintDensity pd) => pd == PrintDensity.Dpmm6 ? 150 : pd == PrintDensity.Dpmm8 ? 200 : pd == PrintDensity.Dpmm12 ? 300 : 600;

    private static int? CommandDimension(ZplLabelNode label, string code, PrintDensity pd)
    {
        int? value = null;
        string unit = "D";
        double dotConversion = 1;
        bool fieldSeparated = false;
        foreach (var cmd in label.Commands)
        {
            if (cmd.Canonical == "^FS") { fieldSeparated = true; continue; }
            if (cmd.Canonical == "^MU")
            {
                var req = cmd.Parameters.ElementAtOrDefault(0)?.Trim().ToUpperInvariant();
                if (string.IsNullOrEmpty(req)) unit = "D";
                else if (req == "D" || req == "I" || req == "M") unit = req;
                dotConversion = ZplNumbers.ZplDotConversion(cmd.Parameters.ElementAtOrDefault(1), cmd.Parameters.ElementAtOrDefault(2), dotConversion);
                continue;
            }
            if (cmd.Canonical != $"^{code}") continue;
            if (code == "LL" && fieldSeparated) continue;
            var precise = ZplNumbers.ZplNumber(cmd.Parameters.ElementAtOrDefault(0));
            if (precise != null && precise > 0)
            {
                double scale = unit == "I" ? (int)pd * 25.4 : unit == "M" ? (int)pd : dotConversion;
                value = (int)Math.Round(precise.Value * scale);
            }
        }
        return value;
    }

    private static (int width, int height) FallbackDots(RenderDocumentOptions opts, PrintDensity pd)
    {
        var fallback = opts.FallbackSize ?? new FallbackSize(4, 6, "in");
        if (fallback.Unit == "dots") return ((int)Math.Round(fallback.Width), (int)Math.Round(fallback.Height));
        if (fallback.Unit == "mm") return ((int)Math.Round(fallback.Width * (int)pd), (int)Math.Round(fallback.Height * (int)pd));
        return ((int)Math.Round(fallback.Width * 25.4 * (int)pd), (int)Math.Round(fallback.Height * 25.4 * (int)pd));
    }

    private static ZplDiagnostic RenderDiagnostic(string code, string message, int labelIndex, ZplDiagnosticSeverity severity)
        => new(code, severity, ZplDiagnosticPhase.Render, message, null, null, null, labelIndex);

    private static TCanvas CanvasFromRaster<TCanvas>(MonochromeRaster raster, ICanvasPlatform<TCanvas> platform) where TCanvas : ICanvas
    {
        var canvas = platform.CanvasFactory.Create(raster.Width, raster.Height);
        // If SkiaCanvas, draw raster
        if (canvas is SkiaCanvas sk)
        {
            sk.DrawRaster(raster);
        }
        else
        {
            // Generic fallback: try to set via reflection? For now return as is
        }
        return canvas;
    }

    public static async Task<List<DocumentRenderResult<TCanvas>>> RenderDocumentWithPlatformAsync<TCanvas>(ZplDocument document, RenderDocumentOptions? opts, ICanvasPlatform<TCanvas> platform, RenderDocumentContext? ctx = null) where TCanvas : ICanvas
    {
        opts ??= new RenderDocumentOptions();
        var results = new List<DocumentRenderResult<TCanvas>>();
        var pd = Density(opts);
        var fallback = FallbackDots(opts, pd);
        var limits = ResolveRenderLimits(opts.Limits);
        var pixelBudget = ctx?.PixelBudget ?? new PixelBudget { Remaining = limits.MaxPixels };

        for (int labelIndex=0; labelIndex<document.Labels.Count; labelIndex++)
        {
            var label = document.Labels[labelIndex];
            var sourceWidth = CommandDimension(label, "PW", pd);
            var sourceHeight = CommandDimension(label, "LL", pd);
            int width = (int)(opts.Width ?? sourceWidth ?? fallback.width);
            int nominalHeight = (int)(opts.Height ?? sourceHeight ?? fallback.height);
            int height = nominalHeight;
            // simplified variable handling ignored

            var localDiagnostics = document.Diagnostics.Where(d => d.LabelIndex == labelIndex || (d.LabelIndex == null && labelIndex==0)).ToList();
            if (opts.Width == null && sourceWidth == null)
                localDiagnostics.Add(RenderDiagnostic("FALLBACK_LABEL_WIDTH", $"No explicit width or ^PW was provided; {width} dots were assumed.", labelIndex, ZplDiagnosticSeverity.Info));
            if (opts.Height == null && sourceHeight == null)
                localDiagnostics.Add(RenderDiagnostic("FALLBACK_LABEL_HEIGHT", $"No explicit height or ^LL was provided; {height} dots were assumed.", labelIndex, ZplDiagnosticSeverity.Info));

            if (width <=0 || nominalHeight <=0 || width > limits.MaxDimension || nominalHeight > limits.MaxDimension || width * nominalHeight > Math.Min(limits.MaxPixels, pixelBudget.Remaining))
            {
                var msg = $"Label {width}x{nominalHeight} exceeds the configured per-label or remaining job raster budget.";
                var raster0 = new MonochromeRaster(0,0,0,"msb-first", Array.Empty<byte>());
                var canvas0 = platform.CanvasFactory.Create(0,0);
                results.Add(new DocumentRenderResult<TCanvas>{ Canvas=canvas0, Raster=raster0, Width=0, Height=0, PrintDensity=pd, Diagnostics= new List<ZplDiagnostic>(localDiagnostics){ RenderDiagnostic("LABEL_LIMIT_EXCEEDED", msg, labelIndex, ZplDiagnosticSeverity.Error) }, HighlightRegions=new()});
                continue;
            }

            var interpretOpts = new Interpreter.InterpretOptions
            {
                Dpi = LegacyDpi(pd),
                LabelIndex = labelIndex,
                Graphics = ctx?.Graphics,
                MaxGraphicBytes = limits.MaxGraphicBytes,
                FontAliases = ctx?.FontAliases,
                MemoryAliases = ctx?.MemoryAliases,
                Encodings = ctx?.Encodings,
                FontResources = ctx?.FontResources != null ? new LayoutFontResources(ctx.BitmapFonts ?? new Dictionary<string,DownloadedBitmapFont>(), ctx.FontLinks ?? new Dictionary<string,IReadOnlyList<string>>(), ctx.MemoryAliases ?? new Dictionary<string,string>(), ctx.FontProvider) : null,
                ResourcesAt = ctx?.ResourcesAt != null ? (node => {
                    var r = ctx.ResourcesAt(node);
                    if (r == null) return null;
                    return new Interpreter.InterpretResourceContext(r.Graphics, r.FontAliases, r.MemoryAliases, r.Encodings, r.FontResources != null ? new LayoutFontResources(r.FontResources.BitmapFonts, r.FontResources.FontLinks, r.FontResources.MemoryAliases, r.FontResources.FontProvider) : new LayoutFontResources(new Dictionary<string,DownloadedBitmapFont>(), new Dictionary<string,IReadOnlyList<string>>(), new Dictionary<string,string>(), null));
                }) : null
            };
            var layout = Interpreter.InterpretLabel(label, interpretOpts);
            var rendered = await RasterRenderer.RenderLayoutToRasterAsync(layout, width, height, labelIndex, new RasterRenderContext{ FontProvider=ctx?.FontProvider, InitialRaster=ctx?.InitialRaster, BitmapFonts=ctx?.BitmapFonts, FontLinks=ctx?.FontLinks, MaxFieldPixels= Math.Min(limits.MaxPixels, pixelBudget.Remaining) });
            pixelBudget.Remaining -= rendered.Raster.Width * rendered.Raster.Height;
            results.Add(new DocumentRenderResult<TCanvas>{
                Canvas = CanvasFromRaster(rendered.Raster, platform),
                Raster = rendered.Raster,
                Width = rendered.Raster.Width,
                Height = rendered.Raster.Height,
                PrintDensity = pd,
                Diagnostics = new List<ZplDiagnostic>(localDiagnostics.Concat(rendered.Diagnostics).DistinctBy(d => $"{d.Code}:{d.Span?.Start}:{d.Span?.End}")),
                HighlightRegions = rendered.HighlightRegions
            });
        }
        return results;
    }

    public sealed class PixelBudget { public int Remaining; }
    public sealed class RenderDocumentContext
    {
        public IReadOnlyDictionary<string, Interpreter.StoredGraphic>? Graphics { get; set; }
        public IReadOnlyDictionary<string, string>? FontAliases { get; set; }
        public IReadOnlyDictionary<string, string>? MemoryAliases { get; set; }
        public IReadOnlyDictionary<string, IReadOnlyDictionary<int,int>>? Encodings { get; set; }
        public LayoutFontResources? FontResources { get; set; }
        public IReadOnlyDictionary<string, DownloadedBitmapFont>? BitmapFonts { get; set; }
        public IReadOnlyDictionary<string, IReadOnlyList<string>>? FontLinks { get; set; }
        public IFontProvider? FontProvider { get; set; }
        public MonochromeRaster? InitialRaster { get; set; }
        public Func<ZplCommandNode, Interpreter.InterpretResourceContext?>? ResourcesAt { get; set; }
        public PixelBudget? PixelBudget { get; set; }
    }
}
