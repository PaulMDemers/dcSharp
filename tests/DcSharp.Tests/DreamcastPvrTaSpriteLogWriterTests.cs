using DcSharp.Core.Dreamcast.Video;
using DcSharp.Core.Execution;

namespace DcSharp.Tests;

public class DreamcastPvrTaSpriteLogWriterTests
{
    [Fact]
    public void WritesSpritePayloadWordsAndSourcePcs()
    {
        var sprite = CreateSpriteSummary();
        using var writer = new StringWriter();

        DreamcastPvrTaSpriteLogWriter.WriteText(writer, [sprite], limit: null, previewStatus: null);

        var text = writer.ToString();
        Assert.Contains("# sprites=1 matched=1 skipped=0 limit=all status=all", text);
        Assert.Contains("#0 status=renderable region=TA_INPUT list=OpaquePolygon headerPc=0x8C1007FA controlPc=0x8C10084C payloadPcRange=0x8C10084C-0x8C100850", text);
        Assert.Contains("points=A:1,1:0,0/B:3,1:0,0/C:1,3:0,0/D:3,3:0,0", text);
        Assert.Contains("rawPoints=A:0x3F800000,0x3F800000,z=0x3F800000", text);
        Assert.Contains("payloadWords=Ax=0x3F800000/Ay=0x3F800000/Az=0x3F800000/Bx=0x40400000", text);
    }

    [Fact]
    public void FiltersByPreviewStatusAndPreservesOriginalIndexes()
    {
        var renderable = CreateSpriteSummary();
        var degenerate = CreateSpriteSummary(hasRenderablePreviewArea: false);
        using var writer = new StringWriter();

        DreamcastPvrTaSpriteLogWriter.WriteText(writer, [renderable, degenerate], limit: 1, previewStatus: "degenerate");

        var text = writer.ToString();
        Assert.Contains("# sprites=2 matched=1 skipped=0 limit=1 status=degenerate", text);
        Assert.DoesNotContain("#0 status=renderable", text);
        Assert.Contains("#1 status=degenerate", text);
    }

    [Fact]
    public void RejectsNegativeLimit()
    {
        using var writer = new StringWriter();

        Assert.Throws<ArgumentOutOfRangeException>(() => DreamcastPvrTaSpriteLogWriter.WriteText(writer, [], limit: -1));
    }

    private static DreamcastPvrTaSpriteSummary CreateSpriteSummary(bool hasRenderablePreviewArea = true)
    {
        var header = new DreamcastPvrTaCommandWrite(
            0x1000_0000,
            "0x10000000",
            "TA_INPUT",
            "SpriteHeader",
            0,
            "OpaquePolygon",
            false,
            4,
            0xA084_0000,
            "0xA0840000");
        var payload = DreamcastPvrTaSpriteHeaderPayload.FromPayload(header, [0, 0, 0, 0xFFFF_0000, 0, 0, 0]);
        var sprite = new DreamcastPvrTaSprite(
            "TA_INPUT",
            0,
            "OpaquePolygon",
            0xA084_0000,
            "0xA0840000",
            0x8C10_07FA,
            "0x8C1007FA",
            payload,
            0xF000_0000,
            "0xF0000000",
            0x8C10_084C,
            "0x8C10084C",
            0x8C10_084C,
            "0x8C10084C",
            0x8C10_0850,
            "0x8C100850",
            true,
            0xF800,
            "0xF800",
            [
                new("Ax", 0x3F80_0000, "0x3F800000"),
                new("Ay", 0x3F80_0000, "0x3F800000"),
                new("Az", 0x3F80_0000, "0x3F800000"),
                new("Bx", 0x4040_0000, "0x40400000")
            ],
            [
                CreateVertex("A", 1, 1),
                CreateVertex("B", 3, 1),
                CreateVertex("C", 1, 3),
                CreateVertex("D", 3, 3)
            ]);

        var summary = DreamcastPvrTaSpriteSummary.FromSprite(sprite);
        return hasRenderablePreviewArea
            ? summary
            : summary with { HasRenderablePreviewArea = false };
    }

    private static DreamcastPvrTaSpriteVertex CreateVertex(string name, int x, int y) =>
        new(
            name,
            x,
            y,
            1.0f,
            0x3F80_0000,
            "0x3F800000",
            BitConverter.SingleToUInt32Bits(x),
            $"0x{BitConverter.SingleToUInt32Bits(x):X8}",
            BitConverter.SingleToUInt32Bits(y),
            $"0x{BitConverter.SingleToUInt32Bits(y):X8}",
            0.0f,
            0.0f,
            0,
            "0x00000000");
}
