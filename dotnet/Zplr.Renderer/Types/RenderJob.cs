// Port of src/types/RenderJob.ts
using Zplr.Renderer.Types;

namespace Zplr.Renderer.Types;

public enum PrintDensity
{
    Dpmm6 = 6,
    Dpmm8 = 8,
    Dpmm12 = 12,
    Dpmm24 = 24,
}

public sealed record MonochromeRaster(
    int Width,
    int Height,
    int Stride,
    string BitOrder, // "msb-first"
    byte[] Data
);

public sealed record DownloadedFontSource(
    string Name,
    string Format, // intellifont | bounded-truetype | unbounded-truetype | truetype-extension
    byte[] Data
);

public interface IFontProvider
{
    Task<byte[]?> ResolveFontAsync(string name, DownloadedFontSource? source = null);
}

public sealed record DownloadedBitmapGlyph(
    int CodePoint,
    int Width,
    int Height,
    int XOffset,
    int YOffset,
    int Advance,
    int BytesPerRow,
    byte[] Data
);

public sealed record DownloadedBitmapFont(
    int CellWidth,
    int CellHeight,
    int Baseline,
    int SpaceWidth,
    IReadOnlyDictionary<int, DownloadedBitmapGlyph> Glyphs
);

public sealed class RenderJobOptions : ParseDocumentOptions
{
    // RenderDocumentOptions fields flattened for convenience (mirrors TS intersection)
    public double? Width { get; set; }
    public double? Height { get; set; }
    public int? PrintDensity { get; set; }
    public FallbackSize? FallbackSize { get; set; }
    public bool Strict { get; set; }
    public RenderLimits? Limits { get; set; }

    public IFontProvider? FontProvider { get; set; }
    public IReadOnlyDictionary<string, string>? FieldValues { get; set; }
    public object? Clock { get; set; } // DateTime | Func<DateTime>
}

public sealed record RenderedLabel<TCanvas>(
    MonochromeRaster Raster,
    int Width,
    int Height,
    PrintDensity PrintDensity,
    IReadOnlyList<ZplDiagnostic> Diagnostics,
    IReadOnlyList<HighlightRegion> HighlightRegions,
    TCanvas Canvas
);

public sealed record RenderJobResult<TCanvas>(
    ZplDocument Document,
    IReadOnlyList<RenderedLabel<TCanvas>> Labels,
    IReadOnlyList<ZplDiagnostic> Diagnostics
);

public interface IZplRenderSession<TCanvas>
{
    Task<RenderJobResult<TCanvas>> RenderAsync(string source, RenderJobOptions? options = null);
    Task<RenderJobResult<TCanvas>> RenderDocumentAsync(ZplDocument document, RenderJobOptions? options = null);
    Task ResetAsync();
}
