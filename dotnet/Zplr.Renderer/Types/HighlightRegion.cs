// Port of src/types/HighlightRegion.ts
namespace Zplr.Renderer.Types;

public enum HighlightRegionType
{
    Box,
    Circle,
    Ellipse,
    Barcode,
    Origin,
    Text,
}

public sealed record TextCaretStop(
    int Offset,
    double X,
    double Y,
    double EndX,
    double EndY
);

public sealed record HighlightRegion(
    HighlightRegionType Type,
    SourceSpan SourceSpan,
    double X,
    double Y,
    double? Width = null,
    double? Height = null,
    double? Radius = null,
    IReadOnlyList<TextCaretStop>? TextCaretStops = null
);
