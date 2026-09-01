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
    public void DrawRaster(MonochromeRaster raster)
    {
        if (raster.Width == 0 || raster.Height == 0) return;
        // Match TS rasterToRgba + putImageData: black dot -> 0, white -> 255
        var info = new SKImageInfo(raster.Width, raster.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var img = SKImage.Create(info);
        // Build RGBA via direct pixel copy using raster data
        var rgba = new byte[raster.Width * raster.Height * 4];
        for (var y = 0; y < raster.Height; y++)
        {
            for (var x = 0; x < raster.Width; x++)
            {
                var byteIndex = y * raster.Stride + (x >> 3);
                var mask = (byte)(0x80 >> (x & 7));
                var black = (raster.Data[byteIndex] & mask) != 0;
                var v = black ? (byte)0 : (byte)255;
                var off = (y * raster.Width + x) * 4;
                rgba[off] = v;
                rgba[off + 1] = v;
                rgba[off + 2] = v;
                rgba[off + 3] = 255;
            }
        }
        // Write pixels via Skia
        var pixmap = new SKPixmap(info, (nint)System.Runtime.InteropServices.Marshal.UnsafeAddrOfPinnedArrayElement(rgba, 0), info.RowBytes);
        // Instead use bitmap setPixels
        for (var y = 0; y < raster.Height && y < Height; y++)
        {
            for (var x = 0; x < raster.Width && x < Width; x++)
            {
                var off = (y * raster.Width + x) * 4;
                var v = rgba[off];
                _bitmap.SetPixel(x, y, new SKColor(v, v, v, 255));
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
