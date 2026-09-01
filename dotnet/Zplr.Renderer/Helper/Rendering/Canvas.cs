// Port of src/helper/rendering/canvas.ts + canvas-node.ts + canvas.web.ts
// Provides platform-agnostic canvas abstraction mirroring TS CanvasLike / CanvasFactory / CanvasPlatform.
using SkiaSharp;
using Zplr.Renderer.Types;

namespace Zplr.Renderer.Helper.Rendering;

/// <summary>
/// Minimal canvas surface, mirrors TS <c>CanvasLike</c>.
/// In .NET this wraps an <see cref="SKBitmap"/> / <see cref="SKSurface"/>.
/// </summary>
public interface ICanvas : IDisposable
{
    int Width { get; }
    int Height { get; }

    /// <summary>Encode to PNG bytes — mirrors <c>canvas.toBuffer("png")</c> in Node and <c>toBlob</c> in Web.</summary>
    byte[] ToPngBytes();

    /// <summary>Underlying Skia bitmap for advanced interop.</summary>
    SKBitmap ToSKBitmap();
}

public interface ICanvasFactory<TCanvas> where TCanvas : ICanvas
{
    TCanvas Create(int width = 300, int height = 150);
}

public interface ICanvasPlatform<TCanvas> where TCanvas : ICanvas
{
    ICanvasFactory<TCanvas> CanvasFactory { get; }
}

/// <summary>SkiaSharp-backed canvas — equivalent to <c>canvas-node.ts</c> using skia-canvas.</summary>
public sealed class SkiaCanvas : ICanvas
{
    private readonly SKBitmap _bitmap;
    private readonly SKCanvas _canvas;

    public SkiaCanvas(int width = 300, int height = 150)
    {
        Width = Math.Max(0, width);
        Height = Math.Max(0, height);
        _bitmap = new SKBitmap(Width == 0 ? 1 : Width, Height == 0 ? 1 : Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        // For zero-sized labels we keep 1x1 backing but report 0x0 like TS raster path does.
        // RenderDocument handles 0x0 as blank.
        _canvas = new SKCanvas(_bitmap);
        _canvas.Clear(SKColors.White);
        if (Width == 0 || Height == 0)
        {
            // Ensure callers see transparent/white 0x0 PNG via ToPngBytes override below.
        }
    }

    public int Width { get; }
    public int Height { get; }

    public SKCanvas SKCanvas => _canvas;
    public SKBitmap SKBitmap => _bitmap;

    public SKBitmap ToSKBitmap() => _bitmap;

    /// <summary>Write a <see cref="MonochromeRaster"/> into this canvas — mirrors <c>canvasFromRaster</c> in renderDocument.ts.</summary>
    /// <remarks>
    /// Perf divergence from TS: TS does rasterToRgba + putImageData as a single memcpy.
    /// Previous .NET port did SetPixel per dot (1M+ calls per label). This version does a
    /// single bulk copy via SKPixmap.InstallPixels + direct row blit. Keep in sync with
    /// src/helper/rendering/canvas-node.ts if the TS path changes.
    /// </remarks>
    public void DrawRaster(MonochromeRaster raster)
    {
        if (raster.Width == 0 || raster.Height == 0) return;
        var copyWidth = Math.Min(raster.Width, Width);
        var copyHeight = Math.Min(raster.Height, Height);
        if (copyWidth <= 0 || copyHeight <= 0) return;

        var pixelCount = copyWidth * copyHeight;
        var rgbaLen = pixelCount * 4;
        var rented = System.Buffers.ArrayPool<byte>.Shared.Rent(rgbaLen);
        try
        {
            var rgba = rented.AsSpan(0, rgbaLen);
            var srcData = raster.Data;
            var srcStride = raster.Stride;
            var dstIdx = 0;
            for (var y = 0; y < copyHeight; y++)
            {
                var rowOffset = y * srcStride;
                for (var x = 0; x < copyWidth; x++)
                {
                    var black = (srcData[rowOffset + (x >> 3)] & (0x80 >> (x & 7))) != 0;
                    var v = black ? (byte)0 : (byte)255;
                    rgba[dstIdx++] = v;
                    rgba[dstIdx++] = v;
                    rgba[dstIdx++] = v;
                    rgba[dstIdx++] = 255;
                }
            }

            var info = new SKImageInfo(copyWidth, copyHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
            unsafe
            {
                fixed (byte* p = rented)
                {
                    var srcPixmap = new SKPixmap(info, (nint)p, info.RowBytes);
                    // FromPixels copies by default when we immediately create image then draw
                    using var srcImage = SKImage.FromPixels(srcPixmap);
                    _canvas.DrawImage(srcImage, 0, 0);
                }
            }
        }
        finally
        {
            System.Buffers.ArrayPool<byte>.Shared.Return(rented);
        }
    }

    /// <summary>
    /// Fast path: encode PNG directly from raster without an intermediate canvas.
    /// Used by RenderZplPngAsync to avoid SkiaCanvas allocation + DrawRaster when only bytes are needed.
    /// Mirrors TS optimization where canvas is only needed for highlight regions.
    /// </summary>
    public static byte[] EncodeMonochromeRasterToPng(MonochromeRaster raster)
    {
        if (raster.Width == 0 || raster.Height == 0)
        {
            using var emptyBmp = new SKBitmap(1, 1, SKColorType.Rgba8888, SKAlphaType.Premul);
            emptyBmp.Erase(SKColors.White);
            using var img0 = SKImage.FromBitmap(emptyBmp);
            using var data0 = img0.Encode(SKEncodedImageFormat.Png, 100);
            return data0.ToArray();
        }
        var w = raster.Width; var h = raster.Height;
        var info = new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul);
        var rgba = new byte[w * h * 4];
        var srcData = raster.Data; var srcStride = raster.Stride;
        var off = 0;
        for (var y = 0; y < h; y++)
        {
            var row = y * srcStride;
            for (var x = 0; x < w; x++)
            {
                var black = (srcData[row + (x >> 3)] & (0x80 >> (x & 7))) != 0;
                var v = black ? (byte)0 : (byte)255;
                rgba[off++] = v; rgba[off++] = v; rgba[off++] = v; rgba[off++] = 255;
            }
        }
        unsafe
        {
            fixed (byte* p = rgba)
            {
                var pixmap = new SKPixmap(info, (nint)p, info.RowBytes);
                using var image = SKImage.FromPixels(pixmap);
                using var data = image.Encode(SKEncodedImageFormat.Png, 100);
                return data.ToArray();
            }
        }
    }

    public byte[] ToPngBytes()
    {
        if (Width == 0 || Height == 0)
        {
            // Return 1x1 white PNG for 0x0 labels to match TS behavior of 0x0 canvas.toBuffer still producing bytes
            // Alternative: return empty array. We match Node's skia-canvas 0x0 behavior (returns minimal PNG).
            using var img0 = SKImage.FromBitmap(_bitmap);
            using var data0 = img0.Encode(SKEncodedImageFormat.Png, 100);
            // Crop to requested size: if 0x0 requested, return 1x1 cropped logically empty handled by caller
            return data0.ToArray();
        }
        using var image = SKImage.FromBitmap(_bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    public void Dispose()
    {
        _canvas.Dispose();
        _bitmap.Dispose();
    }
}

public sealed class SkiaCanvasFactory : ICanvasFactory<SkiaCanvas>
{
    public SkiaCanvas Create(int width = 300, int height = 150) => new(width, height);
}

public sealed class SkiaCanvasPlatform : ICanvasPlatform<SkiaCanvas>
{
    public ICanvasFactory<SkiaCanvas> CanvasFactory { get; } = new SkiaCanvasFactory();
}
