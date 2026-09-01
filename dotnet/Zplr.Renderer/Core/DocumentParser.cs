// Port of src/core/documentParser.ts
using Zplr.Renderer.Types;

namespace Zplr.Renderer.Core;

public static class DocumentParser
{
    private const string STX = "\u0002";
    private const string ETX = "\u0003";
    private const string SI = "\u000f";

    private static ZplDiagnostic Diagnostic(string code, string message, SourceSpan span, ZplDiagnosticSeverity severity = ZplDiagnosticSeverity.Warning, string? command = null)
        => new(code, severity, ZplDiagnosticPhase.Parse, message, span, null, command, null);

    private static bool IsControlCharacter(string value) => value == STX || value == ETX || value == SI;

    private static bool IsBoundary(string value, ZplSyntaxState state) =>
        value == state.FormatPrefix || value == state.ControlPrefix || IsControlCharacter(value);

    private static int FindBoundary(string source, int from, ZplSyntaxState state)
    {
        for (int index = from; index < source.Length; index++)
            if (IsBoundary(source[index].ToString(), state)) return index;
        return source.Length;
    }

    private static string ControlCharacterCommand(string value)
    {
        if (value == STX) return "XA";
        if (value == ETX) return "XZ";
        return "FS";
    }

    private static int? BinaryCommandEnd(string source, int parameterStart, string delimiter, string code, ZplPrefixKind prefixKind, ZplSyntaxState state)
    {
        int headerFields = (code == "DY" && prefixKind == ZplPrefixKind.Control) ? 5 : (code == "GF" && prefixKind == ZplPrefixKind.Format) ? 4 : 0;
        if (headerFields == 0) return null;
        var delimiters = new List<int>();
        for (int index = parameterStart; index < source.Length; index++)
        {
            if (source[index].ToString() == delimiter) { delimiters.Add(index); if (delimiters.Count == headerFields) break; }
            else if (IsBoundary(source[index].ToString(), state)) return null;
        }
        if (delimiters.Count < headerFields) return null;
        var lastDelimiter = delimiters[headerFields - 1];
        var header = source.Substring(parameterStart, lastDelimiter - parameterStart).Split(delimiter);
        var format = (header.Length > (code == "DY" ? 1 : 0) ? header[code == "DY" ? 1 : 0]?.Trim().ToUpperInvariant() : null) ?? "";
        if (format != "B" && format != "C") return null;
        var byteCountStr = header.Length > (code == "DY" ? 3 : 1) ? header[code == "DY" ? 3 : 1]?.Trim() ?? "" : "";
        if (!System.Text.RegularExpressions.Regex.IsMatch(byteCountStr, @"^\d+$")) return null;
        if (!int.TryParse(byteCountStr, out var bytes)) return null;
        return Math.Min(source.Length, lastDelimiter + 1 + bytes);
    }

    private static (string code, int length)? CommandCodeAt(string source, int prefixIndex)
    {
        if (prefixIndex + 1 >= source.Length) return null;
        var first = source[prefixIndex + 1].ToString();
        if (first == "A")
        {
            if (prefixIndex + 2 < source.Length && source[prefixIndex + 2].ToString() == "@") return ("A@", 2);
            return ("A", 1);
        }
        if (prefixIndex + 2 >= source.Length) return null;
        var code = source.Substring(prefixIndex + 1, 2);
        if (!System.Text.RegularExpressions.Regex.IsMatch(code, @"^[A-Z0-9]{2}$")) return null;
        return (code, 2);
    }

    private sealed class TokenizeResult
    {
        public List<ZplCommandNode> Commands { get; } = new();
        public List<ZplDiagnostic> Diagnostics { get; } = new();
        public ZplSyntaxState Syntax { get; set; } = new("^", "~", ",");
    }

    private static TokenizeResult Tokenize(string source, ZplSyntaxState? initialSyntax)
    {
        var result = new TokenizeResult();
        var state = new ZplSyntaxState(
            (initialSyntax?.FormatPrefix?.Length > 0 ? initialSyntax.FormatPrefix[0].ToString() : "^"),
            (initialSyntax?.ControlPrefix?.Length > 0 ? initialSyntax.ControlPrefix[0].ToString() : "~"),
            (initialSyntax?.Delimiter?.Length > 0 ? initialSyntax.Delimiter[0].ToString() : ",")
        );
        int index = 0;
        while (index < source.Length)
        {
            var boundary = FindBoundary(source, index, state);
            if (boundary > index)
            {
                var skipped = source.Substring(index, boundary - index);
                if (skipped.Trim().Length > 0)
                    result.Diagnostics.Add(Diagnostic("TEXT_OUTSIDE_COMMAND", "Text outside a ZPL command was ignored.", new SourceSpan(index, boundary)));
            }
            if (boundary >= source.Length) break;
            var prefix = source[boundary].ToString();
            if (IsControlCharacter(prefix))
            {
                var code = ControlCharacterCommand(prefix);
                result.Commands.Add(new ZplCommandNode
                {
                    Code = code,
                    Canonical = $"^{code}",
                    Prefix = prefix,
                    PrefixKind = ZplPrefixKind.ControlCharacter,
                    RawParameters = "",
                    Parameters = new List<string>(),
                    Delimiter = state.Delimiter,
                    Span = new SourceSpan(boundary, boundary + 1),
                    Index = 0,
                    Capability = Capabilities.GetCommandCapabilityStatus($"^{code}")
                });
                index = boundary + 1;
                continue;
            }
            var codeInfo = CommandCodeAt(source, boundary);
            if (codeInfo == null)
            {
                var end = Math.Min(boundary + 3, source.Length);
                result.Diagnostics.Add(Diagnostic("INVALID_COMMAND", "A command prefix was not followed by a valid ZPL command code.", new SourceSpan(boundary, end), ZplDiagnosticSeverity.Error));
                index = boundary + 1;
                continue;
            }
            var prefixKind = prefix == state.FormatPrefix ? ZplPrefixKind.Format : ZplPrefixKind.Control;
            var canonicalPrefix = prefixKind == ZplPrefixKind.Format ? "^" : "~";
            var canonical = $"{canonicalPrefix}{codeInfo.Value.code}";
            var parameterStart = boundary + 1 + codeInfo.Value.length;
            var changesLexicalState = new HashSet<string> { "^CC", "~CC", "^CD", "~CD", "^CT", "~CT" }.Contains(canonical);
            var binaryEnd = BinaryCommandEnd(source, parameterStart, state.Delimiter, codeInfo.Value.code, prefixKind, state);
            int endPos;
            if (changesLexicalState) endPos = Math.Min(parameterStart + 1, source.Length);
            else if (binaryEnd != null) endPos = binaryEnd.Value;
            else endPos = FindBoundary(source, parameterStart, state);
            var rawParameters = source.Substring(parameterStart, endPos - parameterStart);
            var activeDelimiter = state.Delimiter;
            var node = new ZplCommandNode
            {
                Code = codeInfo.Value.code,
                Canonical = canonical,
                Prefix = prefix,
                PrefixKind = prefixKind,
                RawParameters = rawParameters,
                Parameters = rawParameters.Length == 0 ? new List<string>() : rawParameters.Split(activeDelimiter).ToList(),
                Delimiter = activeDelimiter,
                Span = new SourceSpan(boundary, endPos),
                Index = 0,
                Capability = Capabilities.GetCommandCapabilityStatus(canonical)
            };
            result.Commands.Add(node);
            if (node.Capability == CommandCapabilityStatus.Unknown)
            {
                var codeIsKnown = Capabilities.CommandCapabilities.Any(c => c.Code == node.Code);
                result.Diagnostics.Add(Diagnostic(codeIsKnown ? "INVALID_COMMAND_PREFIX" : "UNKNOWN_COMMAND",
                    codeIsKnown ? $"{node.Canonical} uses a prefix that is not documented for {node.Code}." : $"{node.Canonical} is not recognized and was retained without interpretation.",
                    node.Span, ZplDiagnosticSeverity.Warning, node.Canonical));
            }
            else if (node.Capability == CommandCapabilityStatus.Unsupported)
                result.Diagnostics.Add(Diagnostic("UNSUPPORTED_COMMAND", $"{node.Canonical} is recognized but is not supported by this profile.", node.Span, ZplDiagnosticSeverity.Warning, node.Canonical));
            else if (node.Capability == CommandCapabilityStatus.Partial)
            {
                var limitations = Capabilities.GetCommandCapability(node.Canonical)?.Limitations ?? Array.Empty<string>();
                var msg = $"{node.Canonical} is supported with limitations" + (limitations.Count > 0 ? $": {string.Join(" ", limitations)}" : ".");
                result.Diagnostics.Add(Diagnostic("PARTIALLY_SUPPORTED_COMMAND", msg, node.Span, ZplDiagnosticSeverity.Info, node.Canonical));
            }
            else if (node.Capability == CommandCapabilityStatus.NonRendering)
                result.Diagnostics.Add(Diagnostic("NON_RENDERING_COMMAND", $"{node.Canonical} is recognized and has no label-raster effect.", node.Span, ZplDiagnosticSeverity.Info, node.Canonical));

            if (changesLexicalState && rawParameters.Length == 0)
                result.Diagnostics.Add(Diagnostic("MISSING_PREFIX_PARAMETER", $"{codeInfo.Value.code} requires the next character as its parameter.", node.Span, ZplDiagnosticSeverity.Error, node.Canonical));
            else if (node.Canonical == "^CC" || node.Canonical == "~CC")
                state = new ZplSyntaxState(rawParameters.Length > 0 ? rawParameters[0].ToString() : state.FormatPrefix, state.ControlPrefix, state.Delimiter);
            else if (node.Canonical == "^CT" || node.Canonical == "~CT")
                state = new ZplSyntaxState(state.FormatPrefix, rawParameters.Length > 0 ? rawParameters[0].ToString() : state.ControlPrefix, state.Delimiter);
            else if (node.Canonical == "^CD" || node.Canonical == "~CD")
                state = new ZplSyntaxState(state.FormatPrefix, state.ControlPrefix, rawParameters.Length > 0 ? rawParameters[0].ToString() : state.Delimiter);

            if (state.FormatPrefix == state.ControlPrefix)
                result.Diagnostics.Add(Diagnostic("PREFIX_COLLISION", "Format and control prefixes are the same; commands are treated as format commands.", node.Span, ZplDiagnosticSeverity.Warning, node.Canonical));

            index = Math.Max(endPos, boundary + 1);
        }
        result.Syntax = state;
        return result;
    }

    private static ZplLabelNode MakeLabel(List<ZplCommandNode> commands, bool explicitFlag)
    {
        for (int i = 0; i < commands.Count; i++) commands[i].Index = i;
        return new ZplLabelNode
        {
            Explicit = explicitFlag,
            Commands = new List<ZplCommandNode>(commands),
            Span = new SourceSpan(commands.Count > 0 ? commands[0].Span.Start : 0, commands.Count > 0 ? commands[^1].Span.End : 0)
        };
    }

    private static (List<object> items, List<ZplLabelNode> labels) GroupItems(List<ZplCommandNode> commands, List<ZplDiagnostic> diagnostics)
    {
        var items = new List<object>();
        var labels = new List<ZplLabelNode>();
        var current = new List<ZplCommandNode>();
        bool explicitFlag = false;

        bool CurrentIsSessionSetup() =>
            !explicitFlag && current.Count > 0 && current.All(c => Capabilities.GetCommandCapability(c.Canonical)?.Scope == CommandPersistenceScope.Session);

        void FinishSessionSetup() { foreach (var c in current) items.Add(c); current.Clear(); explicitFlag = false; }
        void FinishCurrent()
        {
            if (current.Count > 0)
            {
                var label = MakeLabel(current, explicitFlag);
                labels.Add(label);
                items.Add(label);
            }
            current.Clear(); explicitFlag = false;
        }

        foreach (var command in commands)
        {
            if (command.Canonical == "^XA")
            {
                if (current.Count > 0)
                {
                    if (CurrentIsSessionSetup()) FinishSessionSetup();
                    else if (explicitFlag)
                        diagnostics.Add(Diagnostic("NESTED_FORMAT", "A new XA command started before the previous format ended.", command.Span, ZplDiagnosticSeverity.Error, "^XA"));
                    else
                        diagnostics.Add(Diagnostic("IMPLICIT_LABEL", "Commands before XA were retained as an implicit label.", new SourceSpan(current[0].Span.Start, current[^1].Span.End)));
                    FinishCurrent();
                }
                explicitFlag = true;
                current.Add(command);
                continue;
            }
            if (command.Canonical == "^XZ")
            {
                if (current.Count == 0)
                {
                    diagnostics.Add(Diagnostic("UNMATCHED_FORMAT_END", "XZ was received without a matching XA.", command.Span, ZplDiagnosticSeverity.Error, "^XZ"));
                    items.Add(command);
                    continue;
                }
                else if (!explicitFlag)
                    diagnostics.Add(Diagnostic("IMPLICIT_LABEL", "A fragment ending in XZ was retained as an implicit label.", new SourceSpan(current[0].Span.Start, command.Span.End)));
                current.Add(command);
                FinishCurrent();
                continue;
            }
            if (!explicitFlag && current.Count == 0)
            {
                var cap = Capabilities.GetCommandCapability(command.Canonical);
                if (cap != null && (cap.Effect == CommandEffect.Job || cap.Effect == CommandEffect.Device))
                {
                    items.Add(command);
                    continue;
                }
            }
            current.Add(command);
        }
        if (current.Count > 0)
        {
            if (CurrentIsSessionSetup()) FinishSessionSetup();
            else
            {
                diagnostics.Add(Diagnostic(explicitFlag ? "UNTERMINATED_FORMAT" : "IMPLICIT_LABEL",
                    explicitFlag ? "The label started with XA but did not end with XZ." : "Commands outside XA/XZ were retained as an implicit label.",
                    new SourceSpan(current[0].Span.Start, current[^1].Span.End),
                    explicitFlag ? ZplDiagnosticSeverity.Error : ZplDiagnosticSeverity.Warning));
                FinishCurrent();
            }
        }
        return (items, labels);
    }

    public static ZplDocument ParseDocument(string? source, ParseDocumentOptions? options = null)
    {
        const ZplProfile profile = ZplProfile.ZplIi2025;
        if (source is not string s)
        {
            return new ZplDocument
            {
                Source = "",
                Profile = profile,
                Items = new List<object>(),
                Labels = new List<ZplLabelNode>(),
                Syntax = new ZplSyntaxState(options?.InitialSyntax?.FormatPrefix ?? "^", options?.InitialSyntax?.ControlPrefix ?? "~", options?.InitialSyntax?.Delimiter ?? ","),
                Diagnostics = new List<ZplDiagnostic> { Diagnostic("INVALID_INPUT", "ZPL source must be a string.", new SourceSpan(0, 0), ZplDiagnosticSeverity.Error) }
            };
        }
        var tokenized = Tokenize(s, options?.InitialSyntax != null ? new ZplSyntaxState(options.InitialSyntax.FormatPrefix ?? "^", options.InitialSyntax.ControlPrefix ?? "~", options.InitialSyntax.Delimiter ?? ",") : null);
        var diagnostics = new List<ZplDiagnostic>(tokenized.Diagnostics);
        if (options?.Profile != null && options.Profile != profile)
            diagnostics.Add(Diagnostic("UNSUPPORTED_PROFILE", $"Profile {options.Profile} is not supported; {profile} was used.", new SourceSpan(0, 0), ZplDiagnosticSeverity.Error));
        var (items, labels) = GroupItems(tokenized.Commands, diagnostics);
        foreach (var d in diagnostics)
        {
            var idx = labels.FindIndex(l => d.Span != null && d.Span.Start >= l.Span.Start && d.Span.Start < l.Span.End);
            if (idx >= 0) d.LabelIndex = idx;
        }
        return new ZplDocument
        {
            Source = s,
            Profile = profile,
            Items = items,
            Labels = labels,
            Syntax = tokenized.Syntax,
            Diagnostics = diagnostics
        };
    }
}
