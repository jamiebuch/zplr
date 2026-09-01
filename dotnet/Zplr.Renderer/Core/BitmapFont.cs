// Port of src/core/bitmapFont.ts
using Zplr.Renderer.Assets;
using Zplr.Renderer.Types;

namespace Zplr.Renderer.Core;

public static class BitmapFont
{
    public static readonly HashSet<string> ResidentFontKeys = new() { "A","B","C","D","E","F","G","H","P","Q","R","S","T","U","V" };

    private static readonly Dictionary<string, (int height, int width, int intercharacterGap, int baseline, bool uppercaseOnly, bool outlineFace)> FontMetrics8Dpmm = new()
    {
        ["A"] = (9, 5, 1, 7, false, false),
        ["B"] = (11, 7, 2, 11, true, false),
        ["C"] = (18, 10, 2, 14, false, false),
        ["D"] = (18, 10, 2, 14, false, false),
        ["E"] = (28, 15, 5, 23, false, true),
        ["F"] = (26, 13, 3, 21, false, false),
        ["G"] = (60, 40, 8, 48, false, false),
        ["H"] = (21, 13, 6, 21, true, true),
        ["P"] = (20, 18, 3, 16, false, true),
        ["Q"] = (28, 24, 4, 22, false, true),
        ["R"] = (35, 31, 5, 28, false, true),
        ["S"] = (40, 35, 6, 32, false, true),
        ["T"] = (48, 42, 7, 38, false, true),
        ["U"] = (59, 53, 9, 47, false, true),
        ["V"] = (80, 71, 12, 64, false, true),
    };

    public static bool IsResidentFontKey(string value) => ResidentFontKeys.Contains(value);

    public static (int height, int width, int intercharacterGap, int baseline, bool uppercaseOnly, bool outlineFace)? ResidentFontMetrics(string key, int dpi = 200)
    {
        if (!IsResidentFontKey(key)) return null;
        var baseM = FontMetrics8Dpmm[key];
        if (key != "E" && key != "H") return baseM;
        if (key == "E")
        {
            if (dpi == 150) return (21, 10, 3, 17, false, true);
            if (dpi == 300 || dpi == 600) return (42, 20, 7, 35, false, true);
        }
        else
        {
            if (dpi == 150) return (17, 11, 5, 17, true, true);
            if (dpi == 300 || dpi == 600) return (34, 22, 10, 34, true, true);
        }
        return baseM;
    }

    public static int ResidentInkWidth(string key, int requestedWidth) => Math.Max(1, requestedWidth);

    public static int ResidentAdvanceWidth(string key, int requestedWidth)
    {
        var width = ResidentInkWidth(key, requestedWidth);
        var metrics = ResidentFontMetrics(key);
        if (metrics == null) return width;
        var widthMultiplier = Math.Min(10, Math.Max(1, (int)Math.Round(width / (double)metrics.Value.width)));
        return width + metrics.Value.intercharacterGap * widthMultiplier;
    }

    public static bool ResidentUsesOutlineFace(string key) => ResidentFontMetrics(key)?.outlineFace ?? false;

    public static string? ResidentCharacter(string key, string character)
    {
        var metrics = ResidentFontMetrics(key);
        if (metrics?.uppercaseOnly != true) return character;
        var upper = character.ToUpperInvariant();
        return upper.Length == 1 || upper.Length == 2 && char.IsSurrogatePair(upper, 0) ? upper : null;
        // Simplified: check if single unicode scalar
    }

    public static bool HasPinnedBitmapGlyph(string character)
    {
        if (character == "□") return true;
        var cp = char.ConvertToUtf32(character, 0);
        return Spleen5x8.Rows.ContainsKey(cp);
    }

    // Perf: avoid Regex per glyph - this is called thousands of times during FB wrapping
    private static bool IsNarrowAdvance(char ch) => ch == '.' || ch == ',' || ch == ':' || ch == ';' || ch == '!' || ch == '|' || ch == '\'' || ch == 'I' || ch == 'l' || ch == '1';
    private static bool IsWideAdvance(char ch) => ch == 'M' || ch == 'W' || ch == '@' || ch == '#' || ch == '%';

    public static int GlyphAdvance(string character, int requestedWidth, bool proportional)
    {
        var width = Math.Max(1, requestedWidth);
        if (!proportional) return width;
        var cp = char.ConvertToUtf32(character, 0);
        // TexGyre ratios are per codepoint, fallback heuristic
        if ((uint)cp < (uint)TexGyreHerosCondensed.AdvanceRatios.Length)
        {
            var ratio = TexGyreHerosCondensed.AdvanceRatios[cp];
            if (ratio != 0) return Math.Max(1, (int)Math.Round(width * ratio));
        }
        if (character.Length == 1)
        {
            var ch = character[0];
            if (ch == ' ') return Math.Max(1, (int)Math.Round(width * 0.5));
            if (IsNarrowAdvance(ch)) return Math.Max(1, (int)Math.Round(width * 0.45));
            if (IsWideAdvance(ch)) return width;
        }
        else
        {
            // Surrogate pair or multi-char (should be single scalar): fallback to string checks
            if (character == " ") return Math.Max(1, (int)Math.Round(width * 0.5));
            // For multi-codepoint ligatures, treat as wide-ish
            if (character.Length == 2 && char.IsSurrogatePair(character, 0)) return Math.Max(1, (int)Math.Round(width * 0.75));
        }
        return Math.Max(1, (int)Math.Round(width * 0.75));
    }

    public static MonochromeRaster RasterizeGlyph(string character, int width, int height, bool proportional, string fontKey = "A")
    {
        var advance = proportional ? GlyphAdvance(character, width, true) : ResidentAdvanceWidth(fontKey, width);
        var raster = Raster.CreateMonochromeRaster(advance, Math.Max(1, height));
        var resident = ResidentCharacter(fontKey, character);
        if (resident == null) return raster;
        var cp = char.ConvertToUtf32(resident, 0);
        byte[] rows;
        if (resident == "□") rows = new byte[] { 0, 0xf8, 0x88, 0x88, 0x88, 0xf8, 0, 0 };
        else if (!Spleen5x8.Rows.TryGetValue(cp, out var r)) rows = Spleen5x8.Rows[0x3f];
        else rows = r;
        var inkWidth = proportional ? raster.Width : Math.Min(raster.Width, ResidentInkWidth(fontKey, width));
        for (int y = 0; y < raster.Height; y++)
        {
            int sourceY = fontKey == "B" ? Math.Min(7, 1 + (int)Math.Floor(y * 7.0 / raster.Height)) : Math.Min(7, (int)Math.Floor(y * 8.0 / raster.Height));
            for (int x = 0; x < inkWidth; x++)
            {
                int sourceX = Math.Min(4, (int)Math.Floor(x * 5.0 / inkWidth));
                if ((rows[sourceY] & (0x80 >> sourceX)) != 0) Raster.SetDot(raster, x, y);
            }
        }
        return raster;
    }
}
