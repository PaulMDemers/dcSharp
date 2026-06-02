using DcSharp.Core.Dreamcast.Video;

namespace DcSharp.Tests;

public class DreamcastPvrTaLogWriterTests
{
    [Fact]
    public void WritesDecodedTailWithOriginalStreamIndexes()
    {
        var writes = new[]
        {
            CreateWrite(0x1000_0000, "TA_INPUT", 0x8084_0000),
            CreateWrite(0x1000_0004, "TA_INPUT", 0x0600_0000),
            CreateWrite(0x1000_0008, "TA_INPUT", 0x8010_0000),
            CreateWrite(0x1000_000C, "TA_INPUT", 0x4800_1234)
        };
        using var writer = new StringWriter();

        DreamcastPvrTaLogWriter.WriteText(writer, writes, limit: 2);

        var text = writer.ToString();
        Assert.Contains("# writes=4 decoded=4 skipped=2 limit=2", text);
        Assert.DoesNotContain("#0 pc=0x8C100700 addr=0x10000000", text);
        Assert.Contains("#2 pc=0x8C100708 addr=0x10000008 region=TA_INPUT role=Payload controlKind=PolygonHeader rawKind=ModifierVolume list=OpaquePolygon endOfStrip=False payloadIndex=1 payloadRemaining=5 payloadName=Mode2 controlValue=0x80840000 value=0x80100000", text);
        Assert.Contains("#3 pc=0x8C10070C addr=0x1000000C region=TA_INPUT role=Payload controlKind=PolygonHeader rawKind=Unknown list=OpaquePolygon endOfStrip=False payloadIndex=2 payloadRemaining=4 payloadName=Mode3 controlValue=0x80840000 value=0x48001234", text);
    }

    [Fact]
    public void RejectsNegativeLimit()
    {
        using var writer = new StringWriter();

        Assert.Throws<ArgumentOutOfRangeException>(() => DreamcastPvrTaLogWriter.WriteText(writer, [], limit: -1));
    }

    private static DreamcastPvrTaCommandWrite CreateWrite(uint address, string region, uint value)
    {
        var command = DreamcastPvrTaCommandDecoder.Decode(region, value);
        return new DreamcastPvrTaCommandWrite(
            address,
            $"0x{address:X8}",
            region,
            command.Kind,
            command.ListType,
            command.ListTypeName,
            command.EndOfStrip,
            4,
            value,
            $"0x{value:X8}",
            0x8C10_0700 + (address - 0x1000_0000),
            $"0x{0x8C10_0700 + (address - 0x1000_0000):X8}");
    }
}
