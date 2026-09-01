// Port of src/core/zplNumbers.ts — keep behavior identical for diffability.
using System.Text.RegularExpressions;

namespace Zplr.Renderer.Core;

public static class ZplNumbers
{
    private static readonly Regex ZplDecimal = new(@"^[+-]?(?:\d+(?:\.\d*)?|\.\d+)$", RegexOptions.Compiled);

    public static double? ZplNumber(string? value)
    {
        var normalized = value?.Trim() ?? "";
        if (!ZplDecimal.IsMatch(normalized)) return null;
        if (double.TryParse(normalized, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed) && double.IsFinite(parsed))
            return parsed;
        return null;
    }

    public static long? ZplInteger(string? value)
    {
        var parsed = ZplNumber(value);
        if (parsed is null) return null;
        // Match JS Number.isSafeInteger: -2^53+1 .. 2^53-1
        const long MaxSafe = 9007199254740991L;
        const long MinSafe = -9007199254740991L;
        var d = parsed.Value;
        if (d % 1 != 0) return null;
        if (d < MinSafe || d > MaxSafe) return null;
        return (long)d;
    }

    private static readonly HashSet<int> FormatDpi = new() { 150, 200, 300, 600 };
    private static readonly HashSet<string> FormatDpiConversions = new()
    {
        "150:150","150:300","150:600","200:200","200:600","300:300","600:600"
    };

    public sealed record ZplDpiConversion(int Base, int Desired);

    public static ZplDpiConversion? ZplDpiConversionParse(string? baseValue, string? desiredValue)
    {
        var b = ZplInteger(baseValue);
        var d = ZplInteger(desiredValue);
        if (b is null || d is null) return null;
        int bi = (int)b.Value, di = (int)d.Value;
        if (!FormatDpi.Contains(bi) || !FormatDpi.Contains(di)) return null;
        if (!FormatDpiConversions.Contains($"{bi}:{di}")) return null;
        return new ZplDpiConversion(bi, di);
    }

    public static double ZplDotConversion(string? baseValue, string? desiredValue, double previous)
    {
        var conv = ZplDpiConversionParse(baseValue, desiredValue);
        return conv != null ? (double)conv.Desired / conv.Base : previous;
    }
}
