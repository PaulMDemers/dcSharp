using DcSharp.Core.Dreamcast.Video;

namespace DcSharp.Tests;

public class DreamcastFramebufferPngWriterTests
{
    [Theory]
    [InlineData(0xF800, 0xFF, 0x00, 0x00)]
    [InlineData(0x07E0, 0x00, 0xFF, 0x00)]
    [InlineData(0x001F, 0x00, 0x00, 0xFF)]
    [InlineData(0xFFFF, 0xFF, 0xFF, 0xFF)]
    public void ConvertsRgb565ToRgba32(ushort pixel, byte red, byte green, byte blue)
    {
        var rgba = DreamcastFramebufferPngWriter.Rgb565ToRgba32(pixel);

        Assert.Equal([red, green, blue, 0xFF], rgba);
    }

    [Fact]
    public void WritesPngSignatureAndHeader()
    {
        byte[] vram =
        [
            0x00, 0xF8,
            0xE0, 0x07,
            0x1F, 0x00,
            0xFF, 0xFF
        ];
        using var stream = new MemoryStream();

        DreamcastFramebufferPngWriter.WriteRgb565Png(stream, vram, 2, 2);

        var png = stream.ToArray();
        Assert.Equal([137, 80, 78, 71, 13, 10, 26, 10], png[..8]);
        Assert.Equal((byte)'I', png[12]);
        Assert.Equal((byte)'H', png[13]);
        Assert.Equal((byte)'D', png[14]);
        Assert.Equal((byte)'R', png[15]);
        Assert.Contains((byte)'I', png);
    }
}
