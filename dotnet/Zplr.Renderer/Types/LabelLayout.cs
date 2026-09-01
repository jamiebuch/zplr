// Port of src/types/LabelLayout.ts
namespace Zplr.Renderer.Types;

public sealed record LayoutFontResources(
    IReadOnlyDictionary<string, DownloadedBitmapFont> BitmapFonts,
    IReadOnlyDictionary<string, IReadOnlyList<string>> FontLinks,
    IReadOnlyDictionary<string, string> MemoryAliases,
    IFontProvider? FontProvider = null
);

public sealed record LayoutFont(
    string Key,
    string? Name,
    int Height,
    int Width,
    Orientation Orientation,
    LayoutFontResources? Resources = null
);

public sealed record LayoutFieldBlock(
    int Width,
    int MaxLines,
    int LineSpacing,
    string Justification, // L|C|R|J
    int HangingIndent,
    int? Height = null,
    string? Mode = null // FB|TB
);

public abstract record LayoutField(
    int X,
    int Y,
    Orientation Orientation,
    bool Reverse,
    int CommandIndex,
    SourceSpan SourceSpan
);

public sealed record TextLayoutField(
    int X,
    int Y,
    Orientation Orientation,
    bool Reverse,
    int CommandIndex,
    SourceSpan SourceSpan,
    string Data,
    LayoutFont Font,
    LayoutFieldBlock? Block = null,
    bool? Typeset = null,
    string Direction = "H", // H|V|R
    int CharacterGap = 0,
    string OriginJustification = "L", // L|R|A
    AdvancedTextOptions? AdvancedText = null
) : LayoutField(X, Y, Orientation, Reverse, CommandIndex, SourceSpan);

public sealed record AdvancedTextOptions(
    bool DefaultGlyph,
    bool Bidirectional,
    bool Shaping,
    bool OpenType
);

public sealed record BoxLayoutField(
    int X,
    int Y,
    Orientation Orientation,
    bool Reverse,
    int CommandIndex,
    SourceSpan SourceSpan,
    int Width,
    int Height,
    int Thickness,
    string Color, // B|W
    int Rounding
) : LayoutField(X, Y, Orientation, Reverse, CommandIndex, SourceSpan);

public sealed record CircleLayoutField(
    int X,
    int Y,
    Orientation Orientation,
    bool Reverse,
    int CommandIndex,
    SourceSpan SourceSpan,
    int Diameter,
    int Thickness,
    string Color
) : LayoutField(X, Y, Orientation, Reverse, CommandIndex, SourceSpan);

public sealed record EllipseLayoutField(
    int X,
    int Y,
    Orientation Orientation,
    bool Reverse,
    int CommandIndex,
    SourceSpan SourceSpan,
    int Width,
    int Height,
    int Thickness,
    string Color
) : LayoutField(X, Y, Orientation, Reverse, CommandIndex, SourceSpan);

public sealed record DiagonalLayoutField(
    int X,
    int Y,
    Orientation Orientation,
    bool Reverse,
    int CommandIndex,
    SourceSpan SourceSpan,
    int Width,
    int Height,
    int Thickness,
    string Color,
    string Direction // L|R
) : LayoutField(X, Y, Orientation, Reverse, CommandIndex, SourceSpan);

public sealed record BitmapLayoutField(
    int X,
    int Y,
    Orientation Orientation,
    bool Reverse,
    int CommandIndex,
    SourceSpan SourceSpan,
    int Width,
    int Height,
    int BytesPerRow,
    byte[] Data,
    int ScaleX,
    int ScaleY
) : LayoutField(X, Y, Orientation, Reverse, CommandIndex, SourceSpan);

public sealed record GraphicSymbolLayoutField(
    int X,
    int Y,
    Orientation Orientation,
    bool Reverse,
    int CommandIndex,
    SourceSpan SourceSpan,
    string Code, // A|B|C|D|E
    int Width,
    int Height
) : LayoutField(X, Y, Orientation, Reverse, CommandIndex, SourceSpan);

public abstract record BarcodeLayoutField(
    int X,
    int Y,
    Orientation Orientation,
    bool Reverse,
    int CommandIndex,
    SourceSpan SourceSpan,
    string Data,
    int ModuleWidth,
    int Height,
    bool PrintInterpretationBelow,
    bool PrintInterpretationAbove,
    LayoutFont InterpretationFont,
    bool? Validation = null
) : LayoutField(X, Y, Orientation, Reverse, CommandIndex, SourceSpan);

public sealed record Code39LayoutField(
    int X,
    int Y,
    Orientation Orientation,
    bool Reverse,
    int CommandIndex,
    SourceSpan SourceSpan,
    string Data,
    int ModuleWidth,
    int Height,
    bool PrintInterpretationBelow,
    bool PrintInterpretationAbove,
    LayoutFont InterpretationFont,
    bool? Validation,
    double Ratio,
    bool Mod43CheckDigit
) : BarcodeLayoutField(X, Y, Orientation, Reverse, CommandIndex, SourceSpan, Data, ModuleWidth, Height, PrintInterpretationBelow, PrintInterpretationAbove, InterpretationFont, Validation);

public sealed record Code128LayoutField(
    int X,
    int Y,
    Orientation Orientation,
    bool Reverse,
    int CommandIndex,
    SourceSpan SourceSpan,
    string Data,
    int ModuleWidth,
    int Height,
    bool PrintInterpretationBelow,
    bool PrintInterpretationAbove,
    LayoutFont InterpretationFont,
    bool? Validation,
    bool UccCheckDigit,
    string Mode // N|U|A|D
) : BarcodeLayoutField(X, Y, Orientation, Reverse, CommandIndex, SourceSpan, Data, ModuleWidth, Height, PrintInterpretationBelow, PrintInterpretationAbove, InterpretationFont, Validation);

public sealed record QrInputSegment(string Mode, string Data); // N|A|B|K
public sealed record QrStructuredAppend(int Index, int Total, int Parity);

public sealed record QrLayoutField(
    int X,
    int Y,
    Orientation Orientation,
    bool Reverse,
    int CommandIndex,
    SourceSpan SourceSpan,
    string Data,
    int ModuleWidth,
    int Height,
    bool PrintInterpretationBelow,
    bool PrintInterpretationAbove,
    LayoutFont InterpretationFont,
    bool? Validation,
    string Model, // 1|2
    int Magnification,
    string Reliability, // H|Q|M|L
    int Mask,
    string InputMode, // A|M
    string? CharacterMode, // N|A|B|K
    IReadOnlyList<QrInputSegment>? Segments,
    QrStructuredAppend? StructuredAppend
) : BarcodeLayoutField(X, Y, Orientation, Reverse, CommandIndex, SourceSpan, Data, ModuleWidth, Height, PrintInterpretationBelow, PrintInterpretationAbove, InterpretationFont, Validation);

public sealed record ExtendedBarcodeLayoutField(
    int X,
    int Y,
    Orientation Orientation,
    bool Reverse,
    int CommandIndex,
    SourceSpan SourceSpan,
    string Data,
    int ModuleWidth,
    int Height,
    bool PrintInterpretationBelow,
    bool PrintInterpretationAbove,
    LayoutFont InterpretationFont,
    bool? Validation,
    string Symbology, // B0, B1 etc.
    string Encoder,
    bool Matrix,
    double? Ratio,
    int? OverallHeight,
    IReadOnlyDictionary<string, object> EncoderOptions
) : BarcodeLayoutField(X, Y, Orientation, Reverse, CommandIndex, SourceSpan, Data, ModuleWidth, Height, PrintInterpretationBelow, PrintInterpretationAbove, InterpretationFont, Validation);

public sealed record LayoutOrigin(
    int X,
    int Y,
    int CommandIndex,
    SourceSpan SourceSpan
);

public sealed class LabelLayout
{
    public IReadOnlyList<LayoutField> Fields { get; set; } = Array.Empty<LayoutField>();
    public IReadOnlyList<LayoutOrigin> Origins { get; set; } = Array.Empty<LayoutOrigin>();
    public IReadOnlyList<ZplDiagnostic> Diagnostics { get; set; } = Array.Empty<ZplDiagnostic>();
    public LabelSettings? Settings { get; set; }
}

public sealed record LabelSettings(
    int? Width,
    int? Height,
    int ShiftX,
    int Top,
    bool Rotate180,
    bool Mirror,
    bool Reverse
);
