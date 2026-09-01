// Port of src/core/fontEngine.ts — now with process-wide glyph cache
// TS uses OpenTypeFontEngine with WeakMap per LayoutFontResources + glyph budget.
// .NET uses SkiaSharp; to match TS perf and target all densities (150/200/300/600 dpi),
// this engine shares a process-wide ConcurrentDictionary cache for built-in glyphs
// (Tex Gyre Heros) and per-engine cache for provider fonts. See dotnet/PERFORMANCE.md
// for the design and the TS upsert boundary.
using System.Collections.Concurrent;
using SkiaSharp;
using Zplr.Renderer.Assets;
using Zplr.Renderer.Types;

namespace Zplr.Renderer.Core;

public sealed class FontEngine
{
    private readonly IFontProvider? _provider;
    private readonly int _maxCachedPixels;
    private readonly Dictionary<string, SKTypeface> _typefaceCache = new();
    private const double VerticalScale = 1.071429;
    private const double TopOffsetRatio = -0.023809523809523808;

    // Process-wide shared state — mirrors TS WeakMap but faster for dotnet's
    // single-process server scenario. Target all densities: key includes width/height
    // so 150/200/300/600 dpi glyphs don't collide. Cache is bounded by pixel budget
    // to avoid unbounded memory on large jobs. See PERFORMANCE.md for eviction policy.
    private static readonly Lazy<SKTypeface> SharedBuiltIn = new(() =>
    {
        try
        {
            var data = TexGyreHerosCondensed.GetOtfBytes();
            return SKTypeface.FromData(SKData.CreateCopy(data)) ?? SKTypeface.FromFamilyName("Arial", SKFontStyle.Normal);
        }
        catch { return SKTypeface.FromFamilyName("Arial", SKFontStyle.Normal); }
    });
    private static SKTypeface BuiltIn => SharedBuiltIn.Value;

    // Global glyph cache: key = "B:char:width:height" for built-in, or per-provider glyphs are instance-local
    private static readonly ConcurrentDictionary<string, MonochromeRaster> GlobalGlyphCache = new();
    private static long _globalCachedPixels;
    private static readonly object _globalCacheLock = new();

    private readonly ConcurrentDictionary<string, MonochromeRaster> _localGlyphCache = new();
    private long _localCachedPixels;

    public FontEngine(IFontProvider? provider = null, int maxCachedPixels = int.MaxValue)
    {
        _provider = provider;
        _maxCachedPixels = maxCachedPixels;
    }

    public async Task<MonochromeRaster?> RasterizeBuiltInAsync(string character, int width, int height)
    {
        // Fast path: hit process-wide cache before allocating SKSurface
        var key = BuiltInKey(character, width, height);
        if (GlobalGlyphCache.TryGetValue(key, out var cached)) return cached;
        var raster = RasterizeWithSkia(BuiltIn, character, width, height, VerticalScale, TopOffsetRatio);
        TryCacheGlobal(key, raster, width, height);
        return await Task.FromResult(raster);
    }

    public async Task<MonochromeRaster?> RasterizeAsync(string name, string character, int width, int height)
    {
        // Try provider first - these are not globally cached because provider bytes may vary per request
        if (_provider != null)
        {
            var data = await _provider.ResolveFontAsync(name);
            if (data != null)
            {
                try
                {
                    // Use instance-local cache keyed by name+char+size to avoid re-parsing
                    var localKey = $"{name.ToUpperInvariant()}:{character}:{width}:{height}";
                    if (_localGlyphCache.TryGetValue(localKey, out var localHit)) return localHit;
                    var tf = SKTypeface.FromData(SKData.CreateCopy(data));
                    if (tf != null)
                    {
                        var r = RasterizeWithSkia(tf, character, width, height);
                        TryCacheLocal(localKey, r, width, height);
                        return r;
                    }
                } catch {}
            }
        }
        if (_typefaceCache.TryGetValue(name.ToUpperInvariant(), out var cachedTf))
        {
            var k2 = $"{name.ToUpperInvariant()}:{character}:{width}:{height}";
            if (_localGlyphCache.TryGetValue(k2, out var hit2)) return hit2;
            var r2 = RasterizeWithSkia(cachedTf, character, width, height);
            TryCacheLocal(k2, r2, width, height);
            return r2;
        }
        return await RasterizeBuiltInAsync(character, width, height);
    }

    private static string BuiltInKey(string character, int width, int height) => $"0:{character}:{width}:{height}";

    private void TryCacheLocal(string key, MonochromeRaster? raster, int width, int height)
    {
        if (raster == null) return;
        var pixels = (long)Math.Max(1, width) * Math.Max(1, height);
        if (pixels > _maxCachedPixels - _localCachedPixels) return; // mirror TS budget check per engine
        if (_localGlyphCache.TryAdd(key, raster)) Interlocked.Add(ref _localCachedPixels, pixels);
    }

    private static void TryCacheGlobal(string key, MonochromeRaster? raster, int width, int height)
    {
        if (raster == null) return;
        var pixels = (long)Math.Max(1, width) * Math.Max(1, height);
        // Bounded global cache: evict arbitrarily when over 64M pixels (~8MB packed, ~64MB RGBA equivalent)
        const long globalLimit = 64_000_000;
        lock (_globalCacheLock)
        {
            if (GlobalGlyphCache.ContainsKey(key)) return;
            if (Interlocked.Read(ref _globalCachedPixels) + pixels > globalLimit)
            {
                // Simple eviction: remove a batch of oldest entries (first enumerated)
                var toRemove = GlobalGlyphCache.Keys.Take(128).ToList();
                foreach (var k in toRemove)
                {
                    if (GlobalGlyphCache.TryRemove(k, out var old))
                    {
                        var p = (long)old.Width * old.Height;
                        Interlocked.Add(ref _globalCachedPixels, -p);
                        if (Interlocked.Read(ref _globalCachedPixels) + pixels <= globalLimit) break;
                    }
                }
            }
            if (Interlocked.Read(ref _globalCachedPixels) + pixels <= globalLimit)
            {
                if (GlobalGlyphCache.TryAdd(key, raster)) Interlocked.Add(ref _globalCachedPixels, pixels);
            }
        }
    }

    private MonochromeRaster? RasterizeWithSkia(SKTypeface typeface, string character, int width, int height, double verticalScale = 1.0, double topOffsetRatio = 0)
    {
        if (character.Length == 0) return Raster.CreateMonochromeRaster(Math.Max(1,width), Math.Max(1,height));
        width = Math.Max(1, width); height = Math.Max(1, height);
        var raster = Raster.CreateMonochromeRaster(width, height);
        try
        {
            var info = new SKImageInfo(width, height, SKColorType.Gray8, SKAlphaType.Opaque);
            using var surface = SKSurface.Create(info);
            if (surface == null) return raster;
            var canvas = surface.Canvas;
            canvas.Clear(SKColors.White);
            using var font = new SKFont(typeface, height * 0.8f);
            using var paint = new SKPaint { Color = SKColors.Black, IsAntialias = false };
            var textWidth = font.MeasureText(character, paint);
            float x = (width - textWidth) / 2f;
            if (x < 0) x = 0;
            float y = height * 0.75f + (float)(height * topOffsetRatio);
            canvas.DrawText(character, x, y, SKTextAlign.Left, font, paint);
            canvas.Flush();
            using var image = surface.Snapshot();
            using var bitmap = SKBitmap.FromImage(image);
            // Bulk threshold: read pixels via GetPixels instead of GetPixel per dot
            var pixmap = bitmap.PeekPixels();
            if (pixmap != null)
            {
                var addr = pixmap.GetPixels();
                unsafe
                {
                    byte* ptr = (byte*)addr.ToPointer();
                    var rowBytes = pixmap.RowBytes;
                    for (int y2 = 0; y2 < height; y2++)
                    {
                        byte* row = ptr + y2 * rowBytes;
                        for (int x2 = 0; x2 < width; x2++)
                        {
                            if (row[x2] < 128) Raster.SetDot(raster, x2, y2);
                        }
                    }
                }
            }
            else
            {
                for (int y2=0; y2<height; y2++)
                    for (int x2=0; x2<width; x2++)
                    {
                        var c = bitmap.GetPixel(x2, y2);
                        if (c.Red < 128) Raster.SetDot(raster, x2, y2);
                    }
            }
        } catch { return raster; }
        return raster;
    }

    // For testing/benchmarking: clear caches
    internal static void ClearGlobalCache()
    {
        lock (_globalCacheLock) { GlobalGlyphCache.Clear(); Interlocked.Exchange(ref _globalCachedPixels, 0); }
    }
}
