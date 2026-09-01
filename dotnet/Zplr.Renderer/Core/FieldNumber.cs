// Port of src/core/fieldNumber.ts
using System.Text.RegularExpressions;

namespace Zplr.Renderer.Core;

public sealed record ParsedFieldNumber(string Number, string? Prompt);

public static class FieldNumber
{
    private static readonly Regex Pattern = new(@"^(\d+)(?:""([^""]{0,255})"")?$", RegexOptions.Compiled);

    public static ParsedFieldNumber? Parse(string? value)
    {
        var source = value?.Trim() ?? "";
        var m = Pattern.Match(source);
        if (!m.Success) return null;
        if (!long.TryParse(m.Groups[1].Value, out var parsed)) return null;
        if (parsed < 0 || parsed > 9999) return null;
        // Safe integer check already satisfied for this range
        return new ParsedFieldNumber(parsed.ToString(), string.IsNullOrEmpty(m.Groups[2].Value) ? null : m.Groups[2].Value);
    }
}
