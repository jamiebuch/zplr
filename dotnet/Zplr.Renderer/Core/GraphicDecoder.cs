// Port of src/core/graphicDecoder.ts — keep line-for-line where feasible.
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace Zplr.Renderer.Core;

public sealed class GraphicDecodeError : Exception
{
    public string Code { get; }
    public GraphicDecodeError(string code, string message) : base(message) { Code = code; }
}

public sealed record DecodedGraphic(byte[] Data, int BytesPerRow, int Width, int Height);

public static class GraphicDecoder
{
    public static (int width, int height) ValidateGraphicGeometry(int bytesPerRow, int expectedBytes, int maxBytes)
    {
        if (bytesPerRow <= 0 || expectedBytes <= 0)
            throw new GraphicDecodeError("INVALID_GRAPHIC_DIMENSIONS", "Graphic byte count and bytes per row must be positive safe integers.");
        var width = bytesPerRow * 8;
        var height = (int)Math.Ceiling(expectedBytes / (double)bytesPerRow);
        var rasterBytes = bytesPerRow * height;
        if (rasterBytes > maxBytes || expectedBytes > maxBytes)
            throw new GraphicDecodeError("GRAPHIC_LIMIT_EXCEEDED", $"Graphic raster requires {rasterBytes} bytes, exceeding the {maxBytes}-byte limit.");
        if (expectedBytes % bytesPerRow != 0)
            throw new GraphicDecodeError("INVALID_GRAPHIC_DIMENSIONS", "Graphic byte count must contain a whole number of rows.");
        return (width, height);
    }

    private static int? RepeatCount(char c)
    {
        if (c >= 'G' && c <= 'Z') return c - 70; // 'G'==71 -> 1, 'Z'==90 ->20
        if (c >= 'g' && c <= 'z') return (c - 102) * 20;
        return null;
    }

    private static string DecodeCompressedHex(string source, int bytesPerRow, int expectedBytes)
    {
        var rowNibbles = bytesPerRow * 2;
        var expectedNibbles = expectedBytes * 2;
        var rows = new List<string>();
        var row = "";
        var repeats = 0;

        int DecodedNibbles() => rows.Count * rowNibbles + row.Length;
        void EnsureWithinLimit(int additional = 0)
        {
            if (rows.Count * rowNibbles + row.Length + additional > expectedNibbles)
                throw new GraphicDecodeError("GRAPHIC_BYTE_COUNT_MISMATCH", $"Graphic declared {expectedBytes} bytes but its ASCII data expands beyond that size.");
        }
        void FinishRow()
        {
            if (row.Length == 0) return;
            EnsureWithinLimit(Math.Max(0, rowNibbles - row.Length));
            rows.Add(row.PadRight(rowNibbles, '0')[..rowNibbles]);
            row = "";
        }

        foreach (var ch in source)
        {
            if (char.IsWhiteSpace(ch)) continue;
            if (DecodedNibbles() >= expectedNibbles) break;
            var rc = RepeatCount(ch);
            if (rc != null) { repeats += rc.Value; continue; }
            if (ch == ',' || ch == '!')
            {
                if (repeats > 0)
                    throw new GraphicDecodeError("INVALID_GRAPHIC_COMPRESSION", "A graphic repeat count must be followed by a hexadecimal value.");
                row = row.PadRight(rowNibbles, ch == ',' ? '0' : 'F');
                FinishRow();
                continue;
            }
            if (ch == ':')
            {
                if (repeats > 0)
                    throw new GraphicDecodeError("INVALID_GRAPHIC_COMPRESSION", "A graphic repeat count must be followed by a hexadecimal value.");
                FinishRow();
                if (DecodedNibbles() >= expectedNibbles) break;
                if (rows.Count == 0)
                    throw new GraphicDecodeError("INVALID_GRAPHIC_COMPRESSION", "A repeated graphic row was requested before any row was decoded.");
                EnsureWithinLimit(rowNibbles);
                rows.Add(rows[^1]);
                continue;
            }
            if (!Uri.IsHexDigit(ch))
                throw new GraphicDecodeError("INVALID_GRAPHIC_HEX", $"Graphic data contains invalid character {System.Text.Json.JsonSerializer.Serialize(ch.ToString())}.");
            var countToAppend = Math.Min(repeats == 0 ? 1 : repeats, expectedNibbles - DecodedNibbles());
            row += new string(ch, countToAppend);
            repeats = 0;
            while (row.Length >= rowNibbles)
            {
                rows.Add(row[..rowNibbles]);
                row = row[rowNibbles..];
            }
        }
        if (repeats > 0 && DecodedNibbles() < expectedNibbles)
            throw new GraphicDecodeError("INVALID_GRAPHIC_COMPRESSION", "A graphic repeat count must be followed by a hexadecimal value.");
        FinishRow();
        return string.Concat(rows);
    }

    private static byte[] DecodeBase64(string value)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
        var compact = Regex.Replace(value, @"\s+", "");
        if (!Regex.IsMatch(compact, @"^[A-Za-z0-9+/]*={0,2}$"))
            throw new GraphicDecodeError("INVALID_GRAPHIC_BASE64", "Graphic Base64 data is invalid.");
        var padding = compact.Length - compact.TrimEnd('=').Length;
        var normalized = compact[..(compact.Length - padding)];
        var remainder = normalized.Length % 4;
        var expectedPadding = remainder == 2 ? 2 : remainder == 3 ? 1 : 0;
        if (remainder == 1 || (padding > 0 && (compact.Length % 4 != 0 || padding != expectedPadding)))
            throw new GraphicDecodeError("INVALID_GRAPHIC_BASE64", "Graphic Base64 padding or length is invalid.");
        var bytes = new List<byte>();
        int buffer = 0, bits = 0;
        foreach (var ch in normalized)
        {
            var idx = alphabet.IndexOf(ch);
            buffer = (buffer << 6) | idx;
            bits += 6;
            if (bits >= 8) { bits -= 8; bytes.Add((byte)((buffer >> bits) & 0xff)); buffer &= bits == 0 ? 0 : (1 << bits) - 1; }
        }
        if (buffer != 0)
            throw new GraphicDecodeError("INVALID_GRAPHIC_BASE64", "Graphic Base64 data contains non-zero trailing bits.");
        return bytes.ToArray();
    }

    public static string Crc16Ccitt(string source)
    {
        int crc = 0;
        foreach (var ch in source)
        {
            var b = (int)ch;
            if (b > 0x7f) throw new GraphicDecodeError("INVALID_GRAPHIC_BASE64", "Graphic Base64 data contains a non-ASCII character.");
            crc ^= b << 8;
            for (var bit = 0; bit < 8; bit++)
                crc = (crc & 0x8000) != 0 ? ((crc << 1) ^ 0x1021) & 0xffff : (crc << 1) & 0xffff;
        }
        return crc.ToString("X4");
    }

    private static uint Adler32(byte[] data)
    {
        uint first = 1, second = 0;
        for (int offset = 0; offset < data.Length; offset += 2655)
        {
            int end = Math.Min(offset + 2655, data.Length);
            for (int i = offset; i < end; i++) { first += data[i]; second += first; }
            first %= 65521; second %= 65521;
        }
        return (second << 16) | first;
    }

    private static uint UInt32BE(byte[] data, int offset) =>
        (uint)(data[offset] * 0x1000000 + (data[offset + 1] << 16) + (data[offset + 2] << 8) + data[offset + 3]);

    private static byte[]? DecodeWrappedData(string source, int expectedBytes, int maxBytes)
    {
        var trimmed = source.Trim();
        var m = Regex.Match(trimmed, @"^:(Z64|B64):([^:]+):([0-9A-Fa-f]{4})$");
        if (!m.Success)
        {
            if (Regex.IsMatch(trimmed, @"^:(?:Z64|B64):"))
                throw new GraphicDecodeError("INVALID_GRAPHIC_WRAPPER", "B64/Z64 graphic data must end with a four-digit hexadecimal CRC.");
            return null;
        }
        var kind = m.Groups[1].Value;
        var encodedText = Regex.Replace(m.Groups[2].Value, @"\s+", "");
        var maximumDecodedInput = kind == "B64" ? expectedBytes : maxBytes + (int)Math.Ceiling(maxBytes / 16384.0) * 5 + 64;
        var maximumEncodedLength = 4 * (int)Math.Ceiling(maximumDecodedInput / 3.0);
        if (encodedText.Length > maximumEncodedLength)
            throw new GraphicDecodeError("GRAPHIC_LIMIT_EXCEEDED", $"Encoded graphic data exceeds the bounded {maxBytes}-byte graphic budget.");
        var expected = m.Groups[3].Value.ToUpperInvariant();
        var actual = Crc16Ccitt(encodedText);
        if (actual != expected)
            throw new GraphicDecodeError("GRAPHIC_CRC_MISMATCH", $"Graphic CRC {expected} does not match encoded-data CRC {actual}.");
        var encoded = DecodeBase64(encodedText);
        byte[] decoded;
        try
        {
            if (kind == "Z64")
            {
                using var input = new MemoryStream(encoded);
                using var zlib = new ZLibStream(input, CompressionMode.Decompress);
                using var output = new MemoryStream();
                zlib.CopyTo(output);
                decoded = output.ToArray();
                if (decoded.Length > expectedBytes + 1) decoded = decoded[..(expectedBytes + 1)];
            }
            else decoded = encoded;
        }
        catch
        {
            throw new GraphicDecodeError("INVALID_GRAPHIC_ZLIB", "Z64 graphic data could not be decompressed.");
        }
        if (kind == "Z64" && decoded.Length == expectedBytes && (encoded.Length < 6 || Adler32(decoded) != UInt32BE(encoded, encoded.Length - 4)))
            throw new GraphicDecodeError("INVALID_GRAPHIC_ZLIB", "Z64 graphic data has an invalid checksum.");
        return decoded;
    }

    public static DecodedGraphic DecodeGraphic(string source, int bytesPerRow, int expectedBytes, int maxBytes)
    {
        var (width, height) = ValidateGraphicGeometry(bytesPerRow, expectedBytes, maxBytes);
        var wrapped = DecodeWrappedData(source, expectedBytes, maxBytes);
        string? hex = wrapped == null ? DecodeCompressedHex(source, bytesPerRow, expectedBytes) : null;
        byte[] data;
        if (wrapped != null) data = wrapped;
        else
        {
            var bytes = new List<byte>();
            for (int i = 0; i + 1 < hex!.Length; i += 2)
                bytes.Add(Convert.ToByte(hex.Substring(i, 2), 16));
            data = bytes.ToArray();
        }
        if (data.Length != expectedBytes)
            throw new GraphicDecodeError("GRAPHIC_BYTE_COUNT_MISMATCH", $"Graphic declared {expectedBytes} bytes but decoded {data.Length}.");
        return new DecodedGraphic(data, bytesPerRow, width, height);
    }

    public static DecodedGraphic DecodeBinaryGraphic(string source, int bytesPerRow, int transmittedBytes, int expectedBytes, bool compressed, int maxBytes)
    {
        var (width, height) = ValidateGraphicGeometry(bytesPerRow, expectedBytes, maxBytes);
        if (transmittedBytes < 0)
            throw new GraphicDecodeError("INVALID_GRAPHIC_DIMENSIONS", "The transmitted graphic byte count must be a non-negative safe integer.");
        if (transmittedBytes > maxBytes)
            throw new GraphicDecodeError("GRAPHIC_LIMIT_EXCEEDED", $"Graphic exceeds the {maxBytes}-byte graphic budget.");
        if (compressed)
            throw new GraphicDecodeError("UNSUPPORTED_GRAPHIC_FORMAT", "Zebra compressed-binary ^GFC payloads are not supported.");
        var data = BinaryBytes(source, transmittedBytes);
        if (data.Length != expectedBytes)
            throw new GraphicDecodeError("GRAPHIC_BYTE_COUNT_MISMATCH", $"Graphic declared {expectedBytes} expanded bytes but decoded {data.Length}.");
        return new DecodedGraphic(data, bytesPerRow, width, height);
    }

    private static byte[] BinaryBytes(string source, int count)
    {
        if (source.Length < count)
            throw new GraphicDecodeError("GRAPHIC_BYTE_COUNT_MISMATCH", $"Graphic declared {count} transmitted bytes but supplied {source.Length}.");
        var data = new byte[count];
        for (int i = 0; i < count; i++)
        {
            var v = (int)source[i];
            if (v > 0xff) throw new GraphicDecodeError("INVALID_GRAPHIC_BINARY", "Binary graphic data must contain byte-valued characters only.");
            data[i] = (byte)v;
        }
        return data;
    }

    public static byte[] DecodeDownloadData(string source, int expectedBytes, int maxBytes)
    {
        if (expectedBytes < 0)
            throw new GraphicDecodeError("OBJECT_BYTE_COUNT_MISMATCH", $"Downloaded object declares an invalid byte count: {expectedBytes}.");
        if (expectedBytes > maxBytes)
            throw new GraphicDecodeError("GRAPHIC_LIMIT_EXCEEDED", $"Downloaded object requires {expectedBytes} bytes, exceeding the {maxBytes}-byte limit.");
        var wrapped = DecodeWrappedData(source, expectedBytes, maxBytes);
        if (wrapped != null)
        {
            if (wrapped.Length != expectedBytes)
                throw new GraphicDecodeError("OBJECT_BYTE_COUNT_MISMATCH", $"Object declared {expectedBytes} bytes but decoded {wrapped.Length}.");
            return wrapped;
        }
        var data = new byte[expectedBytes];
        int? highNibble = null;
        int offset = 0;
        foreach (var ch in source)
        {
            if (char.IsWhiteSpace(ch)) continue;
            if (!Uri.IsHexDigit(ch) || !int.TryParse(ch.ToString(), System.Globalization.NumberStyles.HexNumber, null, out var nibble))
                throw new GraphicDecodeError("INVALID_OBJECT_DATA", "Downloaded object data must be hexadecimal, B64, or Z64 encoded.");
            if (highNibble == null) { highNibble = nibble; continue; }
            if (offset >= expectedBytes)
                throw new GraphicDecodeError("OBJECT_BYTE_COUNT_MISMATCH", $"Object declared {expectedBytes} bytes but supplied more data.");
            data[offset++] = (byte)((highNibble.Value << 4) | nibble);
            highNibble = null;
        }
        if (highNibble != null)
            throw new GraphicDecodeError("INVALID_OBJECT_DATA", "Downloaded object hexadecimal data must contain complete byte pairs.");
        if (offset != expectedBytes)
            throw new GraphicDecodeError("OBJECT_BYTE_COUNT_MISMATCH", $"Object declared {expectedBytes} bytes but decoded {offset}.");
        return data;
    }
}
