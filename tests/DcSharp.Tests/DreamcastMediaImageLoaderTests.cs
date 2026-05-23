using DcSharp.Core.Media;

namespace DcSharp.Tests;

public class DreamcastMediaImageLoaderTests
{
    [Fact]
    public void LoadFromFileAccepts2048ByteSectorData()
    {
        var mediaData = new byte[4096];
        for (var i = 0; i < mediaData.Length; i++)
        {
            mediaData[i] = (byte)i;
        }

        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, mediaData);

            var media = DreamcastMediaImageLoader.LoadFromFile(path);

            Assert.Equal(2048, media.SectorSize);
            Assert.Equal(2ul, media.SectorCount);

            Span<byte> sector = stackalloc byte[2048];
            Assert.True(media.TryReadSector(1, sector, out var bytesRead));
            Assert.Equal(2048, bytesRead);
            Assert.Equal(0, sector[0]);
            Assert.Equal(255, sector[255]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LoadFromFileAccepts2352ByteCdData()
    {
        var sector0 = CreateCdSector([0xA0, 0xA1, 0xA2, 0xA3]);
        var sector1 = CreateCdSector([0xB0, 0xB1, 0xB2, 0xB3]);
        var mediaData = sector0.Concat(sector1).ToArray();

        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, mediaData);

            var media = DreamcastMediaImageLoader.LoadFromFile(path);

            Assert.Equal(2048, media.SectorSize);
            Assert.Equal(2ul, media.SectorCount);

            Span<byte> sector = stackalloc byte[2048];
            Assert.True(media.TryReadSector(0, sector, out var bytesRead));
            Assert.Equal(2048, bytesRead);
            Assert.Equal(0xA0, sector[0]);
            Assert.Equal(0xA1, sector[1]);
            Assert.Equal(0xA2, sector[2]);
            Assert.Equal(0xA3, sector[3]);

            Assert.True(media.TryReadSector(1, sector, out bytesRead));
            Assert.Equal(2048, bytesRead);
            Assert.Equal(0xB0, sector[0]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LoadFromCueSelectsMode1Track()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempRoot);
        var dataTrack1 = CreateCdSector([0x11, 0x12, 0x13, 0x14]);
        var dataTrack2 = CreateCdSector([0x21, 0x22, 0x23, 0x24]);
        var track1Path = Path.Combine(tempRoot, "track1.bin");
        var track2Path = Path.Combine(tempRoot, "track2.bin");
        var cuePath = Path.Combine(tempRoot, "game.cue");

        try
        {
            File.WriteAllBytes(track1Path, dataTrack1);
            File.WriteAllBytes(track2Path, dataTrack2);
            File.WriteAllText(
                cuePath,
                $$"""
                REM cue
                FILE "{{Path.GetFileName(track1Path)}}" BINARY
                  TRACK 01 AUDIO
                    INDEX 01 00:00:00
                FILE "{{Path.GetFileName(track2Path)}}" BINARY
                  TRACK 02 MODE1/2352
                    INDEX 01 00:00:00
                """);

            var media = DreamcastMediaImageLoader.LoadFromFile(cuePath);

            Assert.Equal(2048, media.SectorSize);
            Assert.Equal(1ul, media.SectorCount);
            Span<byte> sector = stackalloc byte[2048];
            Assert.True(media.TryReadSector(0, sector, out var bytesRead));
            Assert.Equal(2048, bytesRead);
            Assert.Equal(0x21, sector[0]);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void LoadFromCueSupportsQuotedTrackPathWithSpaces()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempRoot);
        var track1Path = Path.Combine(tempRoot, "track one.bin");
        var cuePath = Path.Combine(tempRoot, "game with spaces.cue");

        try
        {
            File.WriteAllBytes(track1Path, CreateCdSector([0x55, 0x56, 0x57, 0x58]));
            File.WriteAllText(
                cuePath,
                $$"""
                FILE "track one.bin" BINARY
                  TRACK 01 MODE1/2352
                    INDEX 01 00:00:00
                """);

            var media = DreamcastMediaImageLoader.LoadFromFile(cuePath);

            Assert.Equal(2048, media.SectorSize);
            Assert.Equal(1ul, media.SectorCount);
            Span<byte> sector = stackalloc byte[2048];
            Assert.True(media.TryReadSector(0, sector, out var bytesRead));
            Assert.Equal(2048, bytesRead);
            Assert.Equal(0x55, sector[0]);
            Assert.Equal(0x56, sector[1]);
            Assert.Equal(0x57, sector[2]);
            Assert.Equal(0x58, sector[3]);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void LoadFromCueWithoutDataTrackFails()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempRoot);
        var dataTrack = CreateCdSector([0x11, 0x12, 0x13, 0x14]);
        var track1Path = Path.Combine(tempRoot, "track1.bin");
        var cuePath = Path.Combine(tempRoot, "game.cue");

        try
        {
            File.WriteAllBytes(track1Path, dataTrack);
            File.WriteAllText(
                cuePath,
                $$"""
                FILE "{{Path.GetFileName(track1Path)}}" BINARY
                  TRACK 01 AUDIO
                    INDEX 01 00:00:00
                """);

            var exception = Assert.Throws<InvalidDataException>(
                () => DreamcastMediaImageLoader.LoadFromFile(cuePath));

            Assert.Equal($"No mode1/mode2 data track found in cue sheet '{cuePath}'.", exception.Message);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    private static byte[] CreateCdSector(byte[] payloadHeader)
    {
        var sector = new byte[2352];
        Array.Copy(payloadHeader, 0, sector, 16, payloadHeader.Length);
        return sector;
    }
}
