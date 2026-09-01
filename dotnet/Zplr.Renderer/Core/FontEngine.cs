// Port of src/core/fontEngine.ts — minimal SkiaSharp-based implementation
// For phase 1, we use SkiaSharp to rasterize scalable fonts; fallback to BitmapFont for resident fonts.
using SkiaSharp;
using Zplr.Renderer.Assets;
using Zplr.Renderer.Types;

namespace Zplr.Renderer.Core;

public sealed class FontEngine
{
    private readonly IFontProvider? _provider;
    private readonly int _maxCachedPixels;
    private readonly Dictionary<string, SKTypeface> _typefaceCache = new();
    private readonly SKTypeface _builtIn;
    private const double VerticalScale = 1.071429;
    private const double TopOffsetRatio = -0.023809523809523808;

    public FontEngine(IFontProvider? provider = null, int maxCachedPixels = int.MaxValue)
    {
        _provider = provider;
        _maxCachedPixels = maxCachedPixels;
        // Load built-in TeX Gyre Heros
        try
        {
            var data = TexGyreHerosCondensed.GetOtfBytes();
            _builtIn = SKTypeface.FromData(SKData.CreateCopy(data)) ?? SKTypeface.FromFamilyName("Arial", SKFontStyle.Normal);
        }
        catch { _builtIn = SKTypeface.FromFamilyName("Arial", SKFontStyle.Normal); }
    }

    public async Task<MonochromeRaster?> RasterizeBuiltInAsync(string character, int width, int height)
    {
        return await Task.FromResult(RasterizeWithSkia(_builtIn, character, width, height, VerticalScale, TopOffsetRatio));
    }

    public async Task<MonochromeRaster?> RasterizeAsync(string name, string character, int width, int height)
    {
        // Try provider first
        if (_provider != null)
        {
            var data = await _provider.ResolveFontAsync(name);
            if (data != null)
            {
                try
                {
                    var tf = SKTypeface.FromData(SKData.CreateCopy(data));
                    if (tf != null) return RasterizeWithSkia(tf, character, width, height);
                } catch {}
            }
        }
        // Try cached or load by name via provider alias
        if (_typefaceCache.TryGetValue(name.ToUpperInvariant(), out var cached)) return RasterizeWithSkia(cached, character, width, height);
        // Fallback to built-in
        return await RasterizeBuiltInAsync(character, width, height);
    }

    private MonochromeRaster? RasterizeWithSkia(SKTypeface typeface, string character, int width, int height, double verticalScale = 1.0, double topOffsetRatio = 0)
    {
        if (character.Length == 0) return Raster.CreateMonochromeRaster(Math.Max(1,width), Math.Max(1,height));
        width = Math.Max(1, width); height = Math.Max(1, height);
        var raster = Raster.CreateMonochromeRaster(width, height);
        // Prepare SKPaint to render glyph centered? Use simple approach: draw text at baseline
        // For fidelity, we render to temporary bitmap then threshold
        try
        {
            var info = new SKImageInfo(width, height, SKColorType.Gray8, SKAlphaType.Opaque);
            // Use alpha 8 to render glyph then threshold
            using var surface = SKSurface.Create(info);
            var canvas = surface.Canvas;
            canvas.Clear(SKColors.White);
            using var font = new SKFont(typeface, height * 0.8f); // approximate cap height scaling
            using var paint = new SKPaint { Color = SKColors.Black, IsAntialias = false };
            // Measure
            var textWidth = font.MeasureText(character, paint);
            float x = (width - textWidth) / 2f;
            if (x < 0) x = 0;
            float y = height * 0.75f + (float)(height * topOffsetRatio); // approximate baseline
            canvas.DrawText(character, x, y, SKTextAlign.Left, font, paint);
            canvas.Flush();
            using var image = surface.Snapshot();
            using var bitmap = SKBitmap.FromImage(image);
            for (int y2=0; y2<height; y2++)
                for (int x2=0; x2<width; x2++)
                {
                    var c = bitmap.GetPixel(x2, y2);
                    if (c.Red < 128) Raster.SetDot(raster, x2, y2);
                }
        } catch { return raster; }
        return raster;
    }
}
