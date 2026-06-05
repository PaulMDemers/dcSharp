using DcSharp.Core.Dreamcast.Video;
using DcSharp.Core.Dreamcast.Memory;
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
        Assert.Contains("texture=False cmdTexture=True effectiveTexture=True uv16=True mode1=0x80000000 mode2=0x941004C0 mode3=0x00001234", text);
        Assert.Contains("texBase=0x00001234 texSize=8x8 texFormat=Argb1555 texLayout=twiddled texFilter=Nearest texShading=ModulateAlpha alpha=poly=True/tex=True", text);
        Assert.Contains("rawSize=2x2 intSize=2x2 fallbackPixels=9", text);
        Assert.Contains("points=A:1,1:0,0/B:3,1:0,0/C:1,3:0,0/D:3,3:0,0", text);
        Assert.Contains("rawPoints=A:0x3F800000,0x3F800000,z=0x3F800000", text);
        Assert.Contains("payloadWords=Ax=0x3F800000/Ay=0x3F800000/Az=0x3F800000/Bx=0x40400000", text);
    }

    [Fact]
    public void WritesGroupedSpriteSourceTrace()
    {
        var sprite = CreateSpriteSummary();
        using var writer = new StringWriter();

        DreamcastPvrTaSpriteSourceTraceWriter.WriteText(writer, [sprite], limit: null, previewStatus: null);

        var text = writer.ToString();
        Assert.Contains("# sprites=1 groups=1 skipped=0 limit=all status=all", text);
        Assert.Contains("#0 status=renderable count=1 region=TA_INPUT list=OpaquePolygon headerPc=0x8C1007FA controlPc=0x8C10084C payloadPcRange=0x8C10084C-0x8C100850", text);
        Assert.Contains("rawW=2/2/2 rawH=2/2/2 fallbackPx=9/9/9", text);
        Assert.Contains("xRanges=A:1/1/1/B:3/3/3/C:1/1/1/D:3/3/3", text);
        Assert.Contains("firstPayload=Ax=0x3F800000/Ay=0x3F800000/Az=0x3F800000/Bx=0x40400000", text);
    }

    [Fact]
    public void WritesSpriteStoreQueueProducerTrace()
    {
        var sprite = CreateSpriteSummary();
        using var writer = new StringWriter();

        DreamcastPvrTaSpriteStoreQueueTraceWriter.WriteText(
            writer,
            [sprite],
            CreateSpriteStoreQueueWrites(),
            limit: null,
            previewStatus: null);

        var text = writer.ToString();
        Assert.Contains("# sprites=1 sqWrites=16 sqPackets=1 matched=1 skipped=0 limit=all status=all", text);
        Assert.Contains("#0 status=renderable region=TA_INPUT list=OpaquePolygon headerPc=0x8C1007FA controlFlushPc=0x8C10084C payloadFlushPcRange=0x8C10084C-0x8C100850", text);
        Assert.Contains("sqBase=0xE0000020 producerPcRange=0x8C100804-0x8C10084A", text);
        Assert.Contains("controlProducer=Control@0xE0000020=0xF0000000,pc=0x8C100804,op=0x2E32 source=r3 trace=\"mov.l r3,@r14\"", text);
        Assert.Contains("payloadProducers=Ax@0xE0000024=0x3F800000,pc=0x8C10080A,op=0x1E21 source=r2 trace=\"mov.l r2,@(4,r14)\"", text);
        Assert.Contains("Az@0xE000002C=0x3F800000,pc=0x8C100810,op=0xFED7 source=fr13 trace=\"fmov.s fr13,@(r0,r14)\"", text);
        Assert.Contains("payloadWords=Ax=0x3F800000/Ay=0x3F800000/Az=0x3F800000/Bx=0x40400000", text);
    }

    [Fact]
    public void WritesSpriteTextureSamplesFromVram()
    {
        var sprite = CreateSpriteSummary();
        var vram = new byte[0x2000];
        vram[0x1234] = 0x01;
        vram[0x1235] = 0x80;
        using var writer = new StringWriter();

        DreamcastPvrTaSpriteTextureSampleTraceWriter.WriteText(writer, [sprite], vram, limit: null, previewStatus: null);

        var text = writer.ToString();
        Assert.Contains("# sprites=1 matched=1 skipped=0 limit=all status=all vramBytes=8192", text);
        Assert.Contains("#0 sample=fallbackAvg status=renderable region=TA_INPUT list=OpaquePolygon", text);
        Assert.Contains("effectiveTexture=True mode1=0x80000000 mode2=0x941004C0 mode3=0x00001234", text);
        Assert.Contains("texBase=0x00001234 texSize=8x8 texFormat=Argb1555 texLayout=twiddled texFilter=Nearest texShading=ModulateAlpha", text);
        Assert.Contains("uv=0,0 adjustedUv=0,0 texel=0,0 texelIndex=0 byteOffset=0x00001234 rawTexel=0x8001 rgb565=0x0001 sampleAlpha=255 inBounds=True sampleable=True", text);
    }

    [Fact]
    public void WritesTextureModeCandidates()
    {
        var sprite = CreateSpriteSummary();
        var vram = new byte[0x20000];
        vram[0x1234] = 0x01;
        vram[0x1235] = 0x80;
        vram[0x4C0] = 0x34;
        vram[0x8060] = 0x12;
        using var writer = new StringWriter();

        DreamcastPvrTaTextureModeTraceWriter.WriteText(writer, [sprite], vram, limit: null, previewStatus: null);

        var text = writer.ToString();
        Assert.Contains("# sprites=1 groups=1 skipped=0 limit=all status=all vramBytes=131072", text);
        Assert.Contains("#0 status=renderable count=1 region=TA_INPUT list=OpaquePolygon", text);
        Assert.Contains("effectiveTexture=True mode1=0x80000000 mode2=0x941004C0 mode3=0x00001234 decodedBase=0x00001234", text);
        Assert.Contains("texSize=8x8 texFormat=Argb1555 texLayout=twiddled vq=False mip=False", text);
        Assert.Contains("decodedMode3@0x00001234:inBounds=True:bytes=128:nonZero=2:first=0x00001234:word0=0x8001", text);
        Assert.Contains("mode2Low16@0x000004C0:inBounds=True:bytes=128:nonZero=1:first=0x000004C0:word0=0x0034", text);
        Assert.Contains("mode2Low16Shift3@0x00002600:inBounds=True", text);
        Assert.Contains("mode2Low21Shift3@0x00802600:inBounds=False", text);
    }

    [Fact]
    public void WritesModeTableProvenanceFromWatchedMemory()
    {
        var sprite = CreateSpriteSummary();
        var reads = new[]
        {
            new MemoryAccess(MemoryAccessKind.Read, 0x8C20_C094, 4, sprite.HeaderValue, 0x8C10_07D0, 0x60C2),
            new MemoryAccess(MemoryAccessKind.Read, 0x8C20_C098, 4, sprite.HeaderPayload.Mode1, 0x8C10_07D8, 0x53C1),
            new MemoryAccess(MemoryAccessKind.Read, 0x8C20_C09C, 4, sprite.HeaderPayload.Mode2, 0x8C10_07DC, 0x52C2),
            new MemoryAccess(MemoryAccessKind.Read, 0x8C20_C0A0, 4, sprite.HeaderPayload.Mode3, 0x8C10_07E4, 0x53C3)
        };
        var writes = new[]
        {
            new MemoryAccess(MemoryAccessKind.Write, 0x8C20_C09C, 4, 0x0000_04C0, 0x8C10_05CC, 0x1C02),
            new MemoryAccess(MemoryAccessKind.Write, 0x8C20_C09C, 4, sprite.HeaderPayload.Mode2, 0x8C10_0658, 0x1C12)
        };
        using var writer = new StringWriter();

        DreamcastPvrTaModeTableTraceWriter.WriteText(writer, [sprite], reads, writes, limit: null, previewStatus: null);

        var text = writer.ToString();
        Assert.Contains("# sprites=1 groups=1 reads=4 writes=2 skipped=0 limit=all status=all", text);
        Assert.Contains("#0 status=renderable count=1 region=TA_INPUT list=OpaquePolygon", text);
        Assert.Contains("header=0xA0840009 control=0xF0000000 effectiveTexture=True mode1=0x80000000 mode2=0x941004C0 mode3=0x00001234", text);
        Assert.Contains("tableCandidates=0x8C20C094:readPcs=0x8C1007D0,0x8C1007D8,0x8C1007DC,0x8C1007E4", text);
        Assert.Contains("tableWrites=0x8C20C094:mode2@+0x08=0x000004C0,pc=0x8C1005CC", text);
        Assert.Contains("mode2@+0x08=0x941004C0,pc=0x8C100658", text);
    }

    [Fact]
    public void InfersModeTableBaseWhenHeaderReadIsNotCaptured()
    {
        var sprite = CreateSpriteSummary();
        var reads = new[]
        {
            new MemoryAccess(MemoryAccessKind.Read, 0x8C20_C098, 4, sprite.HeaderPayload.Mode1, 0x8C10_07D8, 0x53C1),
            new MemoryAccess(MemoryAccessKind.Read, 0x8C20_C09C, 4, sprite.HeaderPayload.Mode2, 0x8C10_07DC, 0x52C2),
            new MemoryAccess(MemoryAccessKind.Read, 0x8C20_C0A0, 4, sprite.HeaderPayload.Mode3, 0x8C10_07E4, 0x53C3)
        };
        using var writer = new StringWriter();

        DreamcastPvrTaModeTableTraceWriter.WriteText(writer, [sprite], reads, [], limit: null, previewStatus: null);

        var text = writer.ToString();
        Assert.Contains("tableCandidates=0x8C20C094:readPcs=0x8C1007D8,0x8C1007DC,0x8C1007E4", text);
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
        Assert.Throws<ArgumentOutOfRangeException>(() => DreamcastPvrTaSpriteTextureSampleTraceWriter.WriteText(writer, [], [], limit: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => DreamcastPvrTaTextureModeTraceWriter.WriteText(writer, [], [], limit: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => DreamcastPvrTaModeTableTraceWriter.WriteText(writer, [], [], [], limit: -1));
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
            0xA084_0009,
            "0xA0840009");
        var payload = DreamcastPvrTaSpriteHeaderPayload.FromPayload(header, [0x8000_0000, 0x9410_04C0, 0x0000_1234, 0xFFFF_0000, 0, 0, 0]);
        var sprite = new DreamcastPvrTaSprite(
            "TA_INPUT",
            0,
            "OpaquePolygon",
            0xA084_0009,
            "0xA0840009",
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

    private static IReadOnlyList<MemoryAccess> CreateSpriteStoreQueueWrites()
    {
        uint[] values =
        [
            0xF000_0000,
            0x3F80_0000,
            0x3F80_0000,
            0x3F80_0000,
            0x4040_0000,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0
        ];
        uint[] pcs =
        [
            0x8C10_0804,
            0x8C10_080A,
            0x8C10_080E,
            0x8C10_0810,
            0x8C10_0816,
            0x8C10_081A,
            0x8C10_081C,
            0x8C10_0822,
            0x8C10_0826,
            0x8C10_0828,
            0x8C10_082C,
            0x8C10_0832,
            0x8C10_0836,
            0x8C10_083C,
            0x8C10_0842,
            0x8C10_084A
        ];
        ushort[] opcodes =
        [
            0x2E32,
            0x1E21,
            0x1E30,
            0xFED7,
            0x1E34,
            0x1E25,
            0xFED7,
            0x1E37,
            0x1E28,
            0xFED7,
            0x1E3A,
            0x1E2B,
            0x1E8C,
            0x1E2D,
            0x1E9E,
            0x1E1F
        ];

        return values
            .Select((value, index) => new MemoryAccess(
                MemoryAccessKind.Write,
                0xE000_0020u + (uint)(index * 4),
                4,
                value,
                pcs[index],
                opcodes[index]))
            .ToArray();
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
