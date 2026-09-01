// Port of src/core/pngDecoder.ts
using System.IO.Compression;

namespace Zplr.Renderer.Core;

public sealed class PngDecodeError : Exception
{
    public PngDecodeError(string message) : base(message) { }
}

public static class PngDecoder
{
    private static uint UInt32BE(byte[] data, int offset) =>
        (uint)(data[offset] * 0x1000000 + (data[offset + 1] << 16) + (data[offset + 2] << 8) + data[offset + 3]);

    private static readonly uint[] Crc32Table = Enumerable.Range(0, 256).Select(v =>
    {
        uint crc = (uint)v;
        for (int b = 0; b < 8; b++) crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
        return crc;
    }).ToArray();

    private static uint Crc32(byte[] data, int start, int end)
    {
        uint crc = 0xFFFFFFFFu;
        for (int off = start; off < end; off++) crc = Crc32Table[(crc ^ data[off]) & 0xFF] ^ (crc >> 8);
        return (crc ^ 0xFFFFFFFFu);
    }

    private static uint Adler32(byte[] data)
    {
        uint first = 1, second = 0;
        for (int off = 0; off < data.Length; off += 2655)
        {
            int end = Math.Min(off + 2655, data.Length);
            for (int i = off; i < end; i++) { first += data[i]; second += first; }
            first %= 65521; second %= 65521;
        }
        return (second << 16) | first;
    }

    private static byte[] InflatePngData(byte[] compressed, int expectedBytes)
    {
        var output = new byte[expectedBytes];
        // Use ZLibStream (raw deflate with zlib header). PNG uses zlib wrapper.
        try
        {
            using var ms = new MemoryStream(compressed);
            using var zlib = new ZLibStream(ms, CompressionMode.Decompress);
            int total = 0;
            while (total < expectedBytes)
            {
                int read = zlib.Read(output, total, expectedBytes - total);
                if (read == 0) break;
                total += read;
            }
            if (total != expectedBytes) throw new PngDecodeError("Downloaded PNG has an unexpected decompressed size.");
            // Validate adler
            if (compressed.Length < 6 || Adler32(output) != UInt32BE(compressed, compressed.Length - 4))
                throw new PngDecodeError("Downloaded PNG image data has an invalid checksum.");
            // Ensure no trailing data? ZLibStream will have consumed all; check not exceeded
            return output;
        }
        catch (PngDecodeError) { throw; }
        catch { throw new PngDecodeError("Downloaded PNG image data could not be decompressed."); }
    }

    private static int Paeth(int left, int above, int upperLeft)
    {
        int pred = left + above - upperLeft;
        int ld = Math.Abs(pred - left), ad = Math.Abs(pred - above), uld = Math.Abs(pred - upperLeft);
        return ld <= ad && ld <= uld ? left : ad <= uld ? above : upperLeft;
    }

    private static int Sample(byte[] row, int bitOffset, int bitDepth)
    {
        if (bitDepth == 8) return row[bitOffset >> 3];
        int shift = 8 - bitDepth - (bitOffset & 7);
        return (row[bitOffset >> 3] >> shift) & ((1 << bitDepth) - 1);
    }

    public static Interpreter.StoredGraphic DecodePng(byte[] data, int maxBytes)
    {
        byte[] sig = [137, 80, 78, 71, 13, 10, 26, 10];
        for (int i = 0; i < 8; i++) if (data[i] != sig[i]) throw new PngDecodeError("Downloaded PNG has an invalid signature.");

        int width = 0, height = 0, bitDepth = 0, colorType = 0, interlace = 0;
        bool sawHeader = false, sawEnd = false, sawPalette = false, sawTransparency = false, sawImageData = false;
        bool imageDataEnded = false;
        int compressedBytes = 0, imageDataChunks = 0, endOffset = 0;
        byte[] palette = Array.Empty<byte>(), transparency = Array.Empty<byte>();

        for (int offset = 8; offset + 12 <= data.Length;)
        {
            int length = (int)UInt32BE(data, offset);
            int chunkEnd = offset + 12 + length;
            if (chunkEnd > data.Length) throw new PngDecodeError("Downloaded PNG contains a truncated chunk.");
            string type = System.Text.Encoding.ASCII.GetString(data, offset + 4, 4);
            if (!System.Text.RegularExpressions.Regex.IsMatch(type, "^[A-Za-z]{4}$"))
                throw new PngDecodeError("Downloaded PNG contains an invalid chunk type.");
            if ((type[2] & 0x20) != 0) throw new PngDecodeError("Downloaded PNG contains a chunk with an invalid reserved bit.");
            byte[] body = data.AsSpan(offset + 8, length).ToArray();
            uint expectedCrc = UInt32BE(data, offset + 8 + length);
            if (Crc32(data, offset + 4, offset + 8 + length) != expectedCrc)
                throw new PngDecodeError($"Downloaded PNG {type} chunk has an invalid CRC.");
            if (type == "IHDR")
            {
                if (sawHeader || offset != 8 || length != 13) throw new PngDecodeError("Downloaded PNG has an invalid IHDR chunk.");
                sawHeader = true;
                width = (int)UInt32BE(body, 0);
                height = (int)UInt32BE(body, 4);
                bitDepth = body[8]; colorType = body[9];
                if (body[10] != 0 || body[11] != 0) throw new PngDecodeError("Downloaded PNG uses an unsupported compression or filter method.");
                interlace = body[12];
            }
            else if (type == "PLTE")
            {
                if (!sawHeader || sawPalette || sawTransparency || sawImageData || body.Length == 0 || body.Length > 768 || body.Length % 3 != 0)
                    throw new PngDecodeError("Downloaded PNG has an invalid palette.");
                sawPalette = true; palette = (byte[])body.Clone();
            }
            else if (type == "tRNS")
            {
                if (!sawHeader || sawTransparency || sawImageData || body.Length == 0 || !new[] { 0, 2, 3 }.Contains(colorType) || (colorType == 3 && !sawPalette))
                    throw new PngDecodeError("Downloaded PNG has an invalid transparency chunk.");
                sawTransparency = true; transparency = (byte[])body.Clone();
            }
            else if (type == "IDAT")
            {
                if (!sawHeader || imageDataEnded) throw new PngDecodeError("Downloaded PNG has non-consecutive image data chunks.");
                sawImageData = true; imageDataChunks++; compressedBytes += body.Length;
                if (compressedBytes > maxBytes) throw new GraphicDecodeError("GRAPHIC_LIMIT_EXCEEDED", "Compressed PNG data exceeds the configured graphic budget.");
            }
            else if (type == "IEND")
            {
                if (!sawHeader || !sawImageData || body.Length != 0) throw new PngDecodeError("Downloaded PNG has an invalid IEND chunk.");
                sawEnd = true; endOffset = chunkEnd; break;
            }
            else
            {
                if (!sawHeader) throw new PngDecodeError("Downloaded PNG must begin with an IHDR chunk.");
                if ((type[0] & 0x20) == 0) throw new PngDecodeError($"Downloaded PNG uses unsupported critical chunk {type}.");
            }
            if (sawImageData && type != "IDAT") imageDataEnded = true;
            offset = chunkEnd;
        }
        if (!sawHeader || !sawEnd || endOffset != data.Length || imageDataChunks == 0 || width <= 0 || height <= 0 || interlace != 0)
            throw new PngDecodeError("Only positive-size, non-interlaced PNG images are supported.");

        int channels = colorType == 0 ? 1 : colorType == 2 ? 3 : colorType == 3 ? 1 : colorType == 4 ? 2 : colorType == 6 ? 4 : 0;
        if (channels == 0 || !new[] { 1, 2, 4, 8 }.Contains(bitDepth)) throw new PngDecodeError("Downloaded PNG uses an unsupported color type or bit depth.");
        if (colorType != 0 && colorType != 3 && bitDepth != 8) throw new PngDecodeError("True-color PNG images must use 8-bit channels.");
        if (colorType == 3 && (palette.Length == 0 || palette.Length / 3 > (1 << bitDepth))) throw new PngDecodeError("Indexed PNG images require a palette that fits their bit depth.");
        if ((colorType == 0 || colorType == 4) && sawPalette) throw new PngDecodeError("Grayscale PNG images cannot contain a palette.");
        if ((colorType == 0 && transparency.Length != 0 && transparency.Length != 2) ||
            (colorType == 2 && transparency.Length != 0 && transparency.Length != 6) ||
            (colorType == 3 && transparency.Length > palette.Length / 3) ||
            ((colorType == 4 || colorType == 6) && transparency.Length != 0))
            throw new PngDecodeError("Downloaded PNG has an invalid transparency chunk.");
        int maxSample = (1 << bitDepth) - 1;
        if ((colorType == 0 && transparency.Length == 2 && (transparency[0] * 256 + transparency[1] > maxSample)) ||
            (colorType == 2 && transparency.Length == 6 && (transparency[0] != 0 || transparency[2] != 0 || transparency[4] != 0)))
            throw new PngDecodeError("Downloaded PNG transparency samples exceed its bit depth.");

        int rowBytes = (int)Math.Ceiling(width * channels * bitDepth / 8.0);
        int expectedInflated = (rowBytes + 1) * height;
        int bytesPerRow = (int)Math.Ceiling(width / 8.0);
        int outputBytes = bytesPerRow * height;
        if (expectedInflated > maxBytes || outputBytes > maxBytes) throw new GraphicDecodeError("GRAPHIC_LIMIT_EXCEEDED", "Decoded PNG working data exceeds the configured graphic budget.");

        byte[] compressed = new byte[compressedBytes];
        int target = 0;
        for (int off = 8; off < endOffset;)
        {
            int len = (int)UInt32BE(data, off);
            if (data[off + 4] == 0x49 && data[off + 5] == 0x44 && data[off + 6] == 0x41 && data[off + 7] == 0x54)
            {
                Array.Copy(data, off + 8, compressed, target, len); target += len;
            }
            off += len + 12;
        }
        byte[] inflated = InflatePngData(compressed, expectedInflated);
        int bpp = Math.Max(1, (int)Math.Ceiling(channels * bitDepth / 8.0));
        byte[] output = new byte[outputBytes];
        byte[]? previous = null;
        int inOff = 0;
        for (int y = 0; y < height; y++)
        {
            int filter = inflated[inOff++];
            byte[] src = inflated.AsSpan(inOff, rowBytes).ToArray(); inOff += rowBytes;
            byte[] row = new byte[rowBytes];
            for (int x = 0; x < rowBytes; x++)
            {
                int raw = src[x];
                int left = x >= bpp ? row[x - bpp] : 0;
                int above = previous != null ? previous[x] : 0;
                int upperLeft = x >= bpp && previous != null ? previous[x - bpp] : 0;
                int pred = filter == 0 ? 0 : filter == 1 ? left : filter == 2 ? above : filter == 3 ? (left + above) / 2 : filter == 4 ? Paeth(left, above, upperLeft) : int.MinValue;
                if (pred == int.MinValue) throw new PngDecodeError("PNG uses an invalid row filter.");
                row[x] = (byte)((raw + pred) & 0xFF);
            }
            for (int x = 0; x < width; x++)
            {
                int red = 255, green = 255, blue = 255, alpha = 255;
                if (colorType == 0)
                {
                    int rawGray = Sample(row, x * bitDepth, bitDepth);
                    int gray = (int)Math.Round(rawGray * 255.0 / maxSample);
                    red = green = blue = gray;
                    if (transparency.Length == 2 && rawGray == (transparency[0] << 8) + transparency[1]) alpha = 0;
                }
                else if (colorType == 3)
                {
                    int idx = Sample(row, x * bitDepth, bitDepth);
                    if (idx * 3 + 2 >= palette.Length) throw new PngDecodeError("Indexed PNG pixel refers past its palette.");
                    red = palette[idx * 3]; green = palette[idx * 3 + 1]; blue = palette[idx * 3 + 2]; alpha = transparency.Length > idx ? transparency[idx] : (byte)255;
                }
                else
                {
                    int off = x * channels;
                    red = row[off];
                    green = colorType == 4 ? red : row[off + 1];
                    blue = colorType == 4 ? red : row[off + 2];
                    if (colorType == 4) alpha = row[off + 1];
                    if (colorType == 6) alpha = row[off + 3];
                    if (colorType == 2 && transparency.Length == 6 && red == (transparency[0] << 8) + transparency[1] && green == (transparency[2] << 8) + transparency[3] && blue == (transparency[4] << 8) + transparency[5]) alpha = 0;
                }
                double lum = red * 0.2126 + green * 0.7152 + blue * 0.0722;
                if (alpha >= 128 && lum < 128) output[y * bytesPerRow + (x >> 3)] |= (byte)(0x80 >> (x & 7));
            }
            previous = row;
        }
        return new Interpreter.StoredGraphic(output, bytesPerRow, width, height);
    }
}
