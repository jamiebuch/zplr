// Port of src/core/raster.ts — 1:1 line-for-line where possible.
using Zplr.Renderer.Types;

namespace Zplr.Renderer.Core;

public static class Raster
{
    public static MonochromeRaster CreateMonochromeRaster(int width, int height)
    {
        var normalizedWidth = Math.Max(0, width);
        var normalizedHeight = Math.Max(0, height);
        var stride = (int)Math.Ceiling(normalizedWidth / 8.0);
        return new MonochromeRaster(normalizedWidth, normalizedHeight, stride, "msb-first", new byte[stride * normalizedHeight]);
    }

    public static MonochromeRaster CropRasterHeight(MonochromeRaster raster, int height)
    {
        var normalized = Math.Max(0, Math.Min(raster.Height, height));
        var data = new byte[raster.Stride * normalized];
        Array.Copy(raster.Data, 0, data, 0, data.Length);
        return raster with { Height = normalized, Data = data };
    }

    public static int LastDarkRow(MonochromeRaster raster)
    {
        for (var y = raster.Height - 1; y >= 0; y--)
        {
            var start = y * raster.Stride;
            for (var x = 0; x < raster.Stride; x++)
                if (raster.Data[start + x] != 0) return y;
        }
        return -1;
    }

    public static bool GetDot(MonochromeRaster raster, int x, int y)
    {
        if (x < 0 || y < 0 || x >= raster.Width || y >= raster.Height) return false;
        var mask = (byte)(0x80 >> (x & 7));
        return (raster.Data[y * raster.Stride + (x >> 3)] & mask) != 0;
    }

    public static void SetDot(MonochromeRaster raster, int x, int y, string operation = "set")
    {
        if (x < 0 || y < 0 || x >= raster.Width || y >= raster.Height) return;
        var index = y * raster.Stride + (x >> 3);
        var mask = (byte)(0x80 >> (x & 7));
        if (operation == "set") raster.Data[index] |= mask;
        else if (operation == "clear") raster.Data[index] &= (byte)~mask;
        else raster.Data[index] ^= mask;
    }

    public static void FillRect(MonochromeRaster raster, int x, int y, int width, int height, string operation = "set")
    {
        var startX = Math.Max(0, x);
        var startY = Math.Max(0, y);
        var endX = Math.Min(raster.Width, (int)Math.Ceiling(x + (double)width));
        var endY = Math.Min(raster.Height, (int)Math.Ceiling(y + (double)height));
        if (startX >= endX || startY >= endY) return;
        // Fast path: operate on packed bytes instead of per-dot SetDot
        // This is hot for boxes, barcodes, graphics (often hundreds of rects per label)
        var isSet = operation == "set";
        var isClear = operation == "clear";
        // xor otherwise
        var stride = raster.Stride;
        var data = raster.Data;
        var startByte = startX >> 3;
        var endByteExcl = (endX + 7) >> 3; // ceil
        var sameByteRow = (startX >> 3) == ((endX - 1) >> 3);
        for (var py = startY; py < endY; py++)
        {
            var rowBase = py * stride;
            if (sameByteRow)
            {
                var bIdx = rowBase + startByte;
                int startOff = startX & 7;
                int endOff = endX & 7;
                int mask;
                if (endOff == 0) mask = 0xFF >> startOff;
                else mask = (0xFF >> startOff) & (0xFF << (8 - endOff) & 0xFF);
                if (isSet) data[bIdx] |= (byte)mask;
                else if (isClear) data[bIdx] &= (byte)~mask;
                else data[bIdx] ^= (byte)mask;
            }
            else
            {
                // first partial byte
                {
                    var bIdx = rowBase + startByte;
                    int startOff = startX & 7;
                    int mask = 0xFF >> startOff;
                    if (isSet) data[bIdx] |= (byte)mask;
                    else if (isClear) data[bIdx] &= (byte)~mask;
                    else data[bIdx] ^= (byte)mask;
                }
                // middle full bytes
                for (var b = startByte + 1; b < endByteExcl - 1; b++)
                {
                    var bIdx = rowBase + b;
                    if (isSet) data[bIdx] = 0xFF;
                    else if (isClear) data[bIdx] = 0x00;
                    else data[bIdx] ^= 0xFF;
                }
                // last partial byte
                {
                    var lastIdx = endByteExcl - 1;
                    var bIdx = rowBase + lastIdx;
                    int endOff = endX & 7;
                    int mask = endOff == 0 ? 0xFF : (0xFF << (8 - endOff) & 0xFF);
                    // mask covers bits 0..endOff-1 ; e.g. endOff=3 => 11100000
                    if (isSet) data[bIdx] |= (byte)mask;
                    else if (isClear) data[bIdx] &= (byte)~mask;
                    else data[bIdx] ^= (byte)mask;
                }
            }
        }
    }

    public static void StrokeRect(MonochromeRaster raster, int x, int y, int width, int height, int thickness, string operation = "set")
    {
        var t = Math.Max(1, thickness);
        FillRect(raster, x, y, width, Math.Min(t, height), operation);
        FillRect(raster, x, y + Math.Max(0, height - t), width, Math.Min(t, height), operation);
        FillRect(raster, x, y + t, Math.Min(t, width), Math.Max(0, height - t * 2), operation);
        FillRect(raster, x + Math.Max(0, width - t), y + t, Math.Min(t, width), Math.Max(0, height - t * 2), operation);
    }

    private static bool InsideRoundedRect(int px, int py, int width, int height, double radius)
    {
        if (px < 0 || py < 0 || px >= width || py >= height) return false;
        if (radius <= 0) return true;
        var r = Math.Min(radius, Math.Min(width / 2.0, height / 2.0));
        var sampleX = px + 0.5;
        var sampleY = py + 0.5;
        if (sampleX >= r && sampleX <= width - r) return true;
        if (sampleY >= r && sampleY <= height - r) return true;
        var centerX = sampleX < r ? r : width - r;
        var centerY = sampleY < r ? r : height - r;
        var dx = sampleX - centerX;
        var dy = sampleY - centerY;
        return dx * dx + dy * dy <= r * r;
    }

    public static void StrokeRoundedRect(MonochromeRaster raster, int x, int y, int width, int height, int thickness, int rounding, string operation = "set")
    {
        var w = Math.Max(1, width);
        var h = Math.Max(1, height);
        var t = Math.Min(Math.Max(1, thickness), (int)Math.Ceiling(Math.Min(w, h) / 2.0));
        var radius = (Math.Min(8, Math.Max(0, rounding)) / 8.0) * (Math.Min(w, h) / 2.0);
        if (w == 1 && h == 1 && radius > 0)
        {
            FillRect(raster, x, y, 2, 2, operation);
            return;
        }
        if (radius <= 0)
        {
            StrokeRect(raster, x, y, w, h, t, operation);
            return;
        }
        var innerWidth = Math.Max(0, w - t * 2);
        var innerHeight = Math.Max(0, h - t * 2);
        var innerRadius = Math.Max(0, radius - t);
        for (var py = 0; py < h; py++)
            for (var px = 0; px < w; px++)
            {
                if (!InsideRoundedRect(px, py, w, h, radius)) continue;
                var inner = innerWidth > 0 && innerHeight > 0 && InsideRoundedRect(px - t, py - t, innerWidth, innerHeight, innerRadius);
                if (!inner) SetDot(raster, x + px, y + py, operation);
            }
    }

    public static void DrawLine(MonochromeRaster raster, int x0, int y0, int x1, int y1, int thickness = 1, string operation = "set")
    {
        // Bresenham with thickness — mirrors TS drawLine
        var dx = Math.Abs(x1 - x0);
        var sx = x0 < x1 ? 1 : -1;
        var dy = -Math.Abs(y1 - y0);
        var sy = y0 < y1 ? 1 : -1;
        var error = dx + dy;
        var t = Math.Max(1, thickness);
        var offset = t / 2;
        while (true)
        {
            FillRect(raster, x0 - offset, y0 - offset, t, t, operation);
            if (x0 == x1 && y0 == y1) break;
            var doubled = 2 * error;
            if (doubled >= dy) { error += dy; x0 += sx; }
            if (doubled <= dx) { error += dx; y0 += sy; }
        }
    }

    private static void StrokeOvalEquation(MonochromeRaster raster, int x, int y, int width, int height, double radiusX, double radiusY, double centerX, double centerY, double effectiveThickness, string operation = "set")
    {
        var innerRadiusX = Math.Max(0, radiusX - effectiveThickness);
        var innerRadiusY = Math.Max(0, radiusY - effectiveThickness);
        for (var py = 0; py <= height; py++)
            for (var px = 0; px <= width; px++)
            {
                var dx = (px - centerX) / radiusX;
                var dy = (py - centerY) / radiusY;
                var outer = dx * dx + dy * dy <= 1;
                var inner = innerRadiusX > 0 && innerRadiusY > 0 && Math.Pow((px - centerX) / innerRadiusX, 2) + Math.Pow((py - centerY) / innerRadiusY, 2) < 1;
                if (outer && !inner) SetDot(raster, x + px, y + py, operation);
            }
    }

    public static void StrokeCircle(MonochromeRaster raster, int x, int y, int diameter, int thickness, string operation = "set")
    {
        var d = Math.Max(3, diameter);
        if (d == 3) { FillRect(raster, x, y + 1, 2, 1, operation); return; }
        if (d == 4 || d == 5) { FillRect(raster, x, y + 1, 4, 3, operation); return; }
        StrokeOvalEquation(raster, x, y, d, d, (d + 1.5) / 2.0, (d - 1) / 2.0, d / 2.0, d / 2.0, Math.Max(3, thickness + 1), operation);
    }

    public static void StrokeEllipse(MonochromeRaster raster, int x, int y, int width, int height, int thickness, string operation = "set")
    {
        var w = Math.Max(3, width);
        var h = Math.Max(3, height);
        if (w >= 4095 && h <= 4) return;
        if (h == 3 && w <= 5) { FillRect(raster, x + (w - 2) / 2, y + 1, 2, 1, operation); return; }
        StrokeOvalEquation(raster, x, y, w, h, Math.Max(0.5, (w - 1) / 2.0), Math.Max(0.5, (h - 1.5) / 2.0), w / 2.0, h / 2.0 - 0.5, Math.Max(1, thickness + 2), operation);
    }

    public static void DrawDiagonal(MonochromeRaster raster, int x, int y, int width, int height, int thickness, string direction, string operation = "set")
    {
        var w = Math.Max(3, width);
        var h = Math.Max(3, height);
        var t = Math.Max(2, thickness);
        var tinyRightAdjustment = direction == "R" && w <= 4 && h <= 4 ? 1 : 0;
        for (var py = 0; py < h; py++)
        {
            var progress = (int)Math.Round((py + 0.5) * w / h);
            var start = direction == "R" ? x + w - progress - 1 + tinyRightAdjustment : x + progress + Math.Max(0, t - 2);
            FillRect(raster, start, y + py, t, 1, operation);
        }
    }

    public static (int width, int height) BlitRaster(MonochromeRaster target, MonochromeRaster source, int x, int y, Orientation orientation = Orientation.N, int scaleX = 1, int scaleY = 1, string operation = "set")
    {
        scaleX = Math.Max(1, scaleX);
        scaleY = Math.Max(1, scaleY);
        var logicalWidth = source.Width * scaleX;
        var logicalHeight = source.Height * scaleY;
        var orientedWidth = orientation == Orientation.R || orientation == Orientation.B ? logicalHeight : logicalWidth;
        var orientedHeight = orientation == Orientation.R || orientation == Orientation.B ? logicalWidth : logicalHeight;
        var startX = Math.Max(0, -x);
        var startY = Math.Max(0, -y);
        var endX = Math.Min(orientedWidth, target.Width - x);
        var endY = Math.Min(orientedHeight, target.Height - y);
        if (startX >= endX || startY >= endY) return (orientedWidth, orientedHeight);
        // Fast path for the common case: N orientation, no scaling
        // This covers 95% of text glyph blits and bitmap field blits
        if (orientation == Orientation.N && scaleX == 1 && scaleY == 1)
        {
            var srcStride = source.Stride;
            var dstStride = target.Stride;
            var srcData = source.Data;
            var dstData = target.Data;
            var isSet = operation == "set";
            var isClear = operation == "clear";
            // Precompute dest byte offset for x
            for (var dy = startY; dy < endY; dy++)
            {
                var srcY = dy;
                var dstY = y + dy;
                if ((uint)srcY >= (uint)source.Height || (uint)dstY >= (uint)target.Height) continue;
                var srcRow = srcY * srcStride;
                var dstRow = dstY * dstStride;
                // For each X, do bit test without function call overhead
                for (var dx = startX; dx < endX; dx++)
                {
                    var srcX = dx;
                    if ((srcData[srcRow + (srcX >> 3)] & (0x80 >> (srcX & 7))) == 0) continue;
                    var dstX = x + dx;
                    var idx = dstRow + (dstX >> 3);
                    var mask = (byte)(0x80 >> (dstX & 7));
                    if (isSet) dstData[idx] |= mask;
                    else if (isClear) dstData[idx] &= (byte)~mask;
                    else dstData[idx] ^= mask;
                }
            }
            return (orientedWidth, orientedHeight);
        }
        // General path with orientation/scale - keep string compare hoisted
        var isSet2 = operation == "set";
        var isClear2 = operation == "clear";
        var srcStride2 = source.Stride;
        var dstStride2 = target.Stride;
        var sData2 = source.Data;
        var dData2 = target.Data;
        for (var destinationY = startY; destinationY < endY; destinationY++)
            for (var destinationX = startX; destinationX < endX; destinationX++)
            {
                int logicalX, logicalY;
                if (orientation == Orientation.R) { logicalX = destinationY; logicalY = logicalHeight - 1 - destinationX; }
                else if (orientation == Orientation.I) { logicalX = logicalWidth - 1 - destinationX; logicalY = logicalHeight - 1 - destinationY; }
                else if (orientation == Orientation.B) { logicalX = logicalWidth - 1 - destinationY; logicalY = destinationX; }
                else { logicalX = destinationX; logicalY = destinationY; }
                var sx = logicalX / scaleX;
                var sy = logicalY / scaleY;
                if ((uint)sx >= (uint)source.Width || (uint)sy >= (uint)source.Height) continue;
                if ((sData2[sy * srcStride2 + (sx >> 3)] & (0x80 >> (sx & 7))) == 0) continue;
                var dx2 = x + destinationX;
                var dy2 = y + destinationY;
                var dIdx = dy2 * dstStride2 + (dx2 >> 3);
                var m = (byte)(0x80 >> (dx2 & 7));
                if (isSet2) dData2[dIdx] |= m;
                else if (isClear2) dData2[dIdx] &= (byte)~m;
                else dData2[dIdx] ^= m;
            }
        return (orientedWidth, orientedHeight);
    }

    public static MonochromeRaster TransformRaster(MonochromeRaster source, bool invert = false, bool mirrorX = false, bool rotate180 = false)
    {
        var target = CreateMonochromeRaster(source.Width, source.Height);
        for (var y = 0; y < source.Height; y++)
            for (var x = 0; x < source.Width; x++)
            {
                var sourceX = mirrorX ? source.Width - 1 - x : x;
                var sourceY = y;
                var rotatedX = rotate180 ? source.Width - 1 - sourceX : sourceX;
                var rotatedY = rotate180 ? source.Height - 1 - sourceY : sourceY;
                var black = GetDot(source, rotatedX, rotatedY);
                if (invert ? !black : black) SetDot(target, x, y);
            }
        return target;
    }

    public static byte[] RasterToRgba(MonochromeRaster raster)
    {
        var rgba = new byte[raster.Width * raster.Height * 4];
        for (var y = 0; y < raster.Height; y++)
            for (var x = 0; x < raster.Width; x++)
            {
                var value = GetDot(raster, x, y) ? (byte)0 : (byte)255;
                var off = (y * raster.Width + x) * 4;
                rgba[off] = value;
                rgba[off + 1] = value;
                rgba[off + 2] = value;
                rgba[off + 3] = 255;
            }
        return rgba;
    }
}
