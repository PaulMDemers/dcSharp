using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace DcSharp.Core.Dreamcast.Video;

public static class DreamcastFramebufferPngWriter
{
    private static readonly byte[] PngSignature = [137, 80, 78, 71, 13, 10, 26, 10];

    public static void WriteRgb565Png(Stream output, ReadOnlySpan<byte> vram, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(output);

        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Framebuffer dimensions must be positive.");
        }

        var requiredBytes = checked(width * height * 2);
        if (vram.Length < requiredBytes)
        {
            throw new ArgumentException("VRAM snapshot is smaller than the requested framebuffer.", nameof(vram));
        }

        output.Write(PngSignature);
        WriteChunk(output, "IHDR", CreateHeader(width, height));
        WriteChunk(output, "IDAT", CreateImageData(vram, width, height));
        WriteChunk(output, "IEND", []);
    }

    public static byte[] Rgb565ToRgba32(ushort pixel)
    {
        var r5 = (pixel >> 11) & 0x1F;
        var g6 = (pixel >> 5) & 0x3F;
        var b5 = pixel & 0x1F;

        return
        [
            (byte)((r5 << 3) | (r5 >> 2)),
            (byte)((g6 << 2) | (g6 >> 4)),
            (byte)((b5 << 3) | (b5 >> 2)),
            0xFF
        ];
    }

    private static byte[] CreateHeader(int width, int height)
    {
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0, 4), width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4, 4), height);
        header[8] = 8;
        header[9] = 6;
        header[10] = 0;
        header[11] = 0;
        header[12] = 0;
        return header;
    }

    private static byte[] CreateImageData(ReadOnlySpan<byte> vram, int width, int height)
    {
        using var raw = new MemoryStream(checked(height * (1 + width * 4)));
        for (var y = 0; y < height; y++)
        {
            raw.WriteByte(0);
            var rowOffset = y * width * 2;
            for (var x = 0; x < width; x++)
            {
                var pixelOffset = rowOffset + x * 2;
                var pixel = (ushort)(vram[pixelOffset] | (vram[pixelOffset + 1] << 8));
                raw.Write(Rgb565ToRgba32(pixel));
            }
        }

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
        {
            raw.TryGetBuffer(out var buffer);
            zlib.Write(buffer.AsSpan(0, (int)raw.Length));
        }

        return compressed.ToArray();
    }

    private static void WriteChunk(Stream output, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        output.Write(length);

        var typeBytes = Encoding.ASCII.GetBytes(type);
        output.Write(typeBytes);
        output.Write(data);

        Span<byte> crcBytes = stackalloc byte[4];
        var crc = Crc32(typeBytes, data);
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc);
        output.Write(crcBytes);
    }

    private static uint Crc32(ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFF_FFFFu;
        crc = UpdateCrc(crc, type);
        crc = UpdateCrc(crc, data);
        return crc ^ 0xFFFF_FFFFu;
    }

    private static uint UpdateCrc(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (var value in data)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB8_8320u : crc >> 1;
            }
        }

        return crc;
    }
}
