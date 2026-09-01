// Port of src/types/ZplDocument.ts
namespace Zplr.Renderer.Types;

public enum ZplProfile
{
    ZplIi2025,
}

public sealed record SourceSpan(int Start, int End);

public enum ZplDiagnosticSeverity
{
    Info,
    Warning,
    Error,
}

public enum ZplDiagnosticPhase
{
    Parse,
    Semantic,
    Render,
}

public sealed class ZplDiagnostic
{
    public string Code { get; set; }
    public ZplDiagnosticSeverity Severity { get; set; }
    public ZplDiagnosticPhase Phase { get; set; }
    public string Message { get; set; }
    public SourceSpan? Span { get; set; }
    public IReadOnlyList<SourceSpan>? RelatedSpans { get; set; }
    public string? Command { get; set; }
    public int? LabelIndex { get; set; }

    public ZplDiagnostic(string code, ZplDiagnosticSeverity severity, ZplDiagnosticPhase phase, string message, SourceSpan? span = null, IReadOnlyList<SourceSpan>? relatedSpans = null, string? command = null, int? labelIndex = null)
    {
        Code = code; Severity = severity; Phase = phase; Message = message; Span = span; RelatedSpans = relatedSpans; Command = command; LabelIndex = labelIndex;
    }
}

public enum ZplPrefixKind
{
    Format,
    Control,
    ControlCharacter,
}

public sealed record ZplSyntaxState(string FormatPrefix, string ControlPrefix, string Delimiter);

public enum CommandCapabilityStatus
{
    Supported,
    Partial,
    NonRendering,
    Unsupported,
    Unknown,
}

public enum CommandCategory
{
    Format,
    Text,
    Barcode,
    Graphic,
    Storage,
    Printer,
    Network,
    Rfid,
}

public enum CommandEffect
{
    Raster,
    Job,
    Device,
}

public enum CommandPersistenceScope
{
    Field,
    Format,
    Job,
    Session,
}

public sealed record CommandCapability(
    string Canonical,
    string Prefix, // "^" or "~"
    string Code,
    string Name,
    CommandCategory Category,
    CommandEffect Effect,
    CommandPersistenceScope Scope,
    CommandCapabilityStatus Status,
    IReadOnlyList<string>? Limitations = null,
    string? Reference = null
);

public sealed class ZplCommandNode
{
    public string Kind => "command";
    public string Code { get; set; } = "";
    public string Canonical { get; set; } = "";
    public string Prefix { get; set; } = "";
    public ZplPrefixKind PrefixKind { get; set; }
    public string RawParameters { get; set; } = "";
    public List<string> Parameters { get; set; } = new();
    public string Delimiter { get; set; } = ",";
    public SourceSpan Span { get; set; } = new(0, 0);
    public int Index { get; set; }
    public CommandCapabilityStatus Capability { get; set; }

    public ZplCommandNode Clone()
    {
        return new ZplCommandNode
        {
            Code = Code,
            Canonical = Canonical,
            Prefix = Prefix,
            PrefixKind = PrefixKind,
            RawParameters = RawParameters,
            Parameters = new List<string>(Parameters),
            Delimiter = Delimiter,
            Span = new SourceSpan(Span.Start, Span.End),
            Index = Index,
            Capability = Capability,
        };
    }
}

public sealed class ZplLabelNode
{
    public string Kind => "label";
    public bool Explicit { get; set; }
    public List<ZplCommandNode> Commands { get; set; } = new();
    public SourceSpan Span { get; set; } = new(0, 0);
}

public sealed class ZplDocument
{
    public string Kind => "document";
    public string Source { get; set; } = "";
    public ZplProfile Profile { get; set; } = ZplProfile.ZplIi2025;
    public List<object> Items { get; set; } = new(); // ZplLabelNode | ZplCommandNode
    public List<ZplLabelNode> Labels { get; set; } = new();
    public ZplSyntaxState Syntax { get; set; } = new("^", "~", ",");
    public List<ZplDiagnostic> Diagnostics { get; set; } = new();
}

public class ParseDocumentOptions
{
    public ZplProfile? Profile { get; set; }
    public ZplSyntaxState? InitialSyntax { get; set; }
}

public class RenderDocumentOptions
{
    public double? Width { get; set; }
    public double? Height { get; set; }
    public int? PrintDensity { get; set; } // 6 | 8 | 12 | 24
    public FallbackSize? FallbackSize { get; set; }
    public bool Strict { get; set; }
    public RenderLimits? Limits { get; set; }
}

public sealed record FallbackSize(double Width, double Height, string Unit); // dots|mm|in

public sealed record RenderLimits(
    int MaxDimension = 32768,
    int MaxPixels = 40000000,
    int MaxGraphicBytes = 16 * 1024 * 1024,
    int MaxSessionBytes = 32 * 1024 * 1024,
    int MaxTemplateDepth = 16,
    int MaxExpandedCommands = 100000,
    int MaxLabels = 10000
);
