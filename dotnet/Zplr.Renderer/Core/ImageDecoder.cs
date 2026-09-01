// Port of src/core/imageDecoder.ts
namespace Zplr.Renderer.Core;

public sealed class ImageDecodeError : Exception
{
    public ImageDecodeError(string message) : base(message) { }
}

public static class ImageDecoder
{
    private static int U16(byte[] d, int o) => d[o] | (d[o + 1] << 8);
    private static uint U32(byte[] d, int o) => (uint)(d[o] | (d[o + 1] << 8) | (d[o + 2] << 16) | (d[o + 3] << 24));
    private static int I32(byte[] d, int o) => (int)U32(d, o);
    private static bool Dark(int r, int g, int b, int a = 255) => a >= 128 && r * 0.2126 + g * 0.7152 + b * 0.0722 < 128;
    private static void Set(byte[] outp, int bpr, int x, int y) => outp[y * bpr + (x >> 3)] |= (byte)(0x80 >> (x & 7));
    private static int MaskComponent(int value, int mask)
    {
        if (mask == 0) return 0;
        int shift = 0; while (((mask >> shift) & 1) == 0) shift++;
        int max = mask >> shift;
        return (int)Math.Round(((value & mask) >> shift) * 255.0 / max);
    }
    private static bool ContiguousMask(int mask)
    {
        uint n = (uint)mask;
        if (n == 0) return false;
        while ((n & 1) == 0) n >>= 1;
        return (n & (n + 1)) == 0;
    }
    private static (int r, int g, int b, int a) PaletteColor((int r, int g, int b, int a)[] pal, int idx)
    {
        if (idx < 0 || idx >= pal.Length) throw new ImageDecodeError("BMP pixel refers past its color palette.");
        return pal[idx];
    }

    private static byte[] DecodeRleBmp(byte[] data, int offset, int width, int height, int bits)
    {
        var idx = new byte[width * height];
        int x = 0, y = 0, cursor = offset;
        bool ended = false;
        while (cursor + 1 < data.Length && y < height)
        {
            int count = data[cursor++], value = data[cursor++];
            if (count > 0)
            {
                if (x + count > width) throw new ImageDecodeError("BMP RLE run crosses a scanline boundary.");
                for (int i = 0; i < count; i++) { idx[y * width + x] = (byte)(bits == 8 ? value : (i & 1) != 0 ? value & 0x0F : value >> 4); x++; }
                continue;
            }
            if (value == 0) { x = 0; y++; }
            else if (value == 1) { ended = true; break; }
            else if (value == 2)
            {
                if (cursor + 1 >= data.Length) throw new ImageDecodeError("BMP RLE delta is truncated.");
                x += data[cursor++]; y += data[cursor++];
                if (x > width || y >= height) throw new ImageDecodeError("BMP RLE delta leaves the image bounds.");
            }
            else
            {
                int pixels = value;
                int literalBytes = bits == 8 ? pixels : (pixels + 1) / 2;
                int padded = literalBytes + (literalBytes & 1);
                if (cursor + padded > data.Length) throw new ImageDecodeError("BMP RLE literal run is truncated.");
                if (x + pixels > width) throw new ImageDecodeError("BMP RLE literal run crosses a scanline boundary.");
                for (int i = 0; i < pixels; i++) { int b = data[cursor + (bits == 8 ? i : i >> 1)]; int pi = bits == 8 ? b : (i & 1) != 0 ? b & 0x0F : b >> 4; idx[y * width + x] = (byte)pi; x++; }
                cursor += padded;
            }
        }
        if (!ended && y < height) throw new ImageDecodeError("BMP RLE pixel data is truncated.");
        return idx;
    }

    public static Interpreter.StoredGraphic DecodeBmp(byte[] data, int maxBytes)
    {
        if (data.Length < 26 || data[0] != 0x42 || data[1] != 0x4D) throw new ImageDecodeError("Downloaded BMP has an invalid header.");
        int pixelOffset = (int)U32(data, 10);
        int dibSize = (int)U32(data, 14);
        bool core = dibSize == 12;
        if ((!core && dibSize < 40) || 14 + dibSize > data.Length) throw new ImageDecodeError("Downloaded BMP has an unsupported or truncated DIB header.");
        int width = core ? U16(data, 18) : I32(data, 18);
        int signedHeight = core ? U16(data, 20) : I32(data, 22);
        int height = Math.Abs(signedHeight);
        bool topDown = !core && signedHeight < 0;
        int planes = core ? U16(data, 22) : U16(data, 26);
        int bits = core ? U16(data, 24) : U16(data, 28);
        int compression = core ? 0 : (int)U32(data, 30);
        if (width <= 0 || height <= 0 || planes != 1 || !new[] { 1, 4, 8, 16, 24, 32 }.Contains(bits) || (core && !new[] { 1, 4, 8, 24 }.Contains(bits)))
            throw new ImageDecodeError("Downloaded BMP dimensions or bit depth are unsupported.");
        int bytesPerRow = (width + 7) / 8;
        int outputBytes = bytesPerRow * height;
        if (outputBytes > maxBytes) throw new GraphicDecodeError("GRAPHIC_LIMIT_EXCEEDED", "Decoded BMP exceeds the configured graphic budget.");
        if (!new[] { 0, 1, 2, 3 }.Contains(compression) || (compression == 1 && bits != 8) || (compression == 2 && bits != 4) || (compression == 3 && bits != 16 && bits != 32) || (topDown && compression != 0 && compression != 3))
            throw new ImageDecodeError("Downloaded BMP uses an unsupported compression mode.");
        int paletteEntries = bits <= 8 ? Math.Min(1 << bits, core ? 1 << bits : (U32(data, 46) == 0 ? 1 << bits : (int)U32(data, 46))) : 0;
        int paletteOffset = 14 + dibSize;
        int paletteStride = core ? 3 : 4;
        var palette = new (int r, int g, int b, int a)[paletteEntries];
        for (int i = 0; i < paletteEntries; i++)
        {
            int at = paletteOffset + i * paletteStride;
            if (at + paletteStride > data.Length) throw new ImageDecodeError("BMP palette is truncated.");
            palette[i] = (data[at + 2], data[at + 1], data[at], 255);
        }
        int redMask = bits == 16 ? 0x7C00 : 0x00FF0000;
        int greenMask = bits == 16 ? 0x03E0 : 0x0000FF00;
        int blueMask = bits == 16 ? 0x001F : 0x000000FF;
        int alphaMask = 0;
        if (compression == 3)
        {
            int masks = dibSize >= 52 ? 54 : 14 + dibSize;
            if (masks + 12 > data.Length) throw new ImageDecodeError("BMP color masks are truncated.");
            redMask = (int)U32(data, masks); greenMask = (int)U32(data, masks + 4); blueMask = (int)U32(data, masks + 8);
            if (dibSize >= 56) alphaMask = (int)U32(data, masks + 12);
            int overlap = (redMask & greenMask) | (redMask & blueMask) | (greenMask & blueMask) | (alphaMask & (redMask | greenMask | blueMask));
            bool outside = bits == 16 && (((redMask | greenMask | blueMask | alphaMask) >> 0 & 0xFFFF0000) != 0);
            if (new[] { redMask, greenMask, blueMask }.Any(m => !ContiguousMask(m)) || (alphaMask != 0 && !ContiguousMask(alphaMask)) || overlap != 0 || outside)
                throw new ImageDecodeError("BMP color masks are invalid or overlapping.");
        }
        int minPixelOffset = Math.Max(paletteOffset + paletteEntries * paletteStride, compression == 3 && dibSize < 52 ? 14 + dibSize + 12 : 0);
        if (pixelOffset < minPixelOffset || pixelOffset > data.Length) throw new ImageDecodeError("BMP pixel data offset overlaps its header.");
        if ((compression == 1 || compression == 2) && width * height > maxBytes) throw new GraphicDecodeError("GRAPHIC_LIMIT_EXCEEDED", "Decoded BMP index data exceeds the configured graphic budget.");
        int sourceStride = ((width * bits + 31) / 32) * 4;
        int sourceBytes = sourceStride * height;
        if ((compression == 0 || compression == 3) && pixelOffset + sourceBytes > data.Length) throw new ImageDecodeError("BMP pixel data is truncated.");
        var output = new byte[outputBytes];
        byte[]? indexed = (compression == 1 || compression == 2) ? DecodeRleBmp(data, pixelOffset, width, height, bits) : null;
        for (int y = 0; y < height; y++)
        {
            int storedY = topDown ? y : height - 1 - y;
            for (int x = 0; x < width; x++)
            {
                int r = 255, g = 255, b = 255, a = 255;
                if (indexed != null) { var c = PaletteColor(palette, indexed[storedY * width + x]); r = c.r; g = c.g; b = c.b; a = c.a; }
                else
                {
                    int row = pixelOffset + storedY * sourceStride;
                    if (bits == 1 || bits == 4 || bits == 8)
                    {
                        int idx = bits == 1 ? (data[row + (x >> 3)] >> (7 - (x & 7))) & 1 : bits == 4 ? ((x & 1) != 0 ? data[row + (x >> 1)] & 0x0F : data[row + (x >> 1)] >> 4) : data[row + x];
                        var c = PaletteColor(palette, idx); r = c.r; g = c.g; b = c.b; a = c.a;
                    }
                    else if (bits == 24) { int at = row + x * 3; b = data[at]; g = data[at + 1]; r = data[at + 2]; }
                    else { int at = row + x * (bits >> 3); int val = bits == 16 ? U16(data, at) : (int)U32(data, at); r = MaskComponent(val, redMask); g = MaskComponent(val, greenMask); b = MaskComponent(val, blueMask); a = alphaMask != 0 ? MaskComponent(val, alphaMask) : 255; }
                }
                if (Dark(r, g, b, a)) Set(output, bytesPerRow, x, y);
            }
        }
        return new Interpreter.StoredGraphic(output, bytesPerRow, width, height);
    }

    private static (byte[] pixels, int cursor) DecodePcxRle(byte[] data, int offset, int end, int expected)
    {
        var outp = new byte[expected];
        int cursor = offset, target = 0;
        while (target < expected && cursor < end)
        {
            int lead = data[cursor++];
            int count = (lead & 0xC0) == 0xC0 ? lead & 0x3F : 1;
            if (count == 0) throw new ImageDecodeError("PCX RLE data contains an empty run.");
            bool literal = count == 1 && (lead & 0xC0) != 0xC0;
            if (!literal && cursor >= end) throw new ImageDecodeError("PCX RLE data is invalid or truncated.");
            int val = literal ? lead : data[cursor++];
            if (target + count > expected) throw new ImageDecodeError("PCX RLE data is invalid or truncated.");
            for (int i = 0; i < count; i++) outp[target++] = (byte)val;
        }
        if (target != expected) throw new ImageDecodeError("PCX pixel data is truncated.");
        return (outp, cursor);
    }

    public static Interpreter.StoredGraphic DecodePcx(byte[] data, int maxBytes)
    {
        if (data.Length < 128 || data[0] != 0x0A || data[2] != 1) throw new ImageDecodeError("Downloaded PCX has an invalid header.");
        int bits = data[3];
        int width = U16(data, 8) - U16(data, 4) + 1;
        int height = U16(data, 10) - U16(data, 6) + 1;
        int planes = data[65];
        int sourceBytesPerRow = U16(data, 66);
        int bytesPerRow = (width + 7) / 8;
        if (width <= 0 || height <= 0 || sourceBytesPerRow <= 0 || !((bits == 1 && planes >= 1 && planes <= 4) || (bits == 8 && (planes == 1 || planes == 3))))
            throw new ImageDecodeError("Downloaded PCX dimensions or plane layout are unsupported.");
        int minSource = bits == 1 ? (width + 7) / 8 : width;
        if (sourceBytesPerRow < minSource) throw new ImageDecodeError("Downloaded PCX scanlines are shorter than the declared width.");
        int scanlineBytes = sourceBytesPerRow * planes;
        int outputBytes = bytesPerRow * height;
        int workingBytes = scanlineBytes * height;
        if (outputBytes > maxBytes || workingBytes > maxBytes) throw new GraphicDecodeError("GRAPHIC_LIMIT_EXCEEDED", "Decoded PCX working data exceeds the configured graphic budget.");
        int? candPalOff = bits == 8 && planes == 1 && data.Length >= 769 && data[data.Length - 769] == 0x0C ? data.Length - 768 : (int?)null;
        int? palOff = candPalOff;
        (byte[] pixels, int cursor) decoded;
        (byte[] pixels, int cursor) DecodeThrough(int end)
        {
            var r = DecodePcxRle(data, 128, end, workingBytes);
            if (r.cursor != end) throw new ImageDecodeError("PCX contains trailing data after its pixel stream.");
            return r;
        }
        if (candPalOff == null) decoded = DecodeThrough(data.Length);
        else
        {
            try { decoded = DecodeThrough(candPalOff.Value - 1); }
            catch (ImageDecodeError palEx)
            {
                try { decoded = DecodeThrough(data.Length); palOff = null; }
                catch { throw palEx; }
            }
        }
        var pixels = decoded.pixels;
        var pal16 = new (int r, int g, int b)[16];
        for (int i = 0; i < 16; i++) pal16[i] = (data[16 + i * 3], data[17 + i * 3], data[18 + i * 3]);
        byte[]? pal256 = palOff != null ? data.AsSpan(palOff.Value, 768).ToArray() : null;
        var output = new byte[outputBytes];
        for (int y = 0; y < height; y++)
        {
            int row = y * scanlineBytes;
            for (int x = 0; x < width; x++)
            {
                int r = 255, g = 255, b = 255;
                if (bits == 1)
                {
                    int idx = 0;
                    for (int plane = 0; plane < planes; plane++)
                    {
                        int bte = pixels[row + plane * sourceBytesPerRow + (x >> 3)];
                        idx |= ((bte >> (7 - (x & 7))) & 1) << plane;
                    }
                    (r, g, b) = pal16[idx];
                }
                else if (planes == 1)
                {
                    int idx = pixels[row + x];
                    if (pal256 != null) { r = pal256[idx * 3]; g = pal256[idx * 3 + 1]; b = pal256[idx * 3 + 2]; }
                    else r = g = b = idx;
                }
                else { r = pixels[row + x]; g = pixels[row + sourceBytesPerRow + x]; b = pixels[row + sourceBytesPerRow * 2 + x]; }
                if (Dark(r, g, b)) Set(output, bytesPerRow, x, y);
            }
        }
        return new Interpreter.StoredGraphic(output, bytesPerRow, width, height);
    }
}
