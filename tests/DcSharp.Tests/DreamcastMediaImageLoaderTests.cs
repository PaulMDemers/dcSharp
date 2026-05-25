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

    [Fact]
    public void InspectFindsDreamcastBootSectorInRawCdData()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, CreateCdSector(CreateBootSector("1ST_READ.BIN", "TEST TITLE")));

            var report = DreamcastMediaInspector.Inspect(path);

            var boot = Assert.IsType<DreamcastBootSectorInfo>(report.BootSector);
            Assert.Equal(0u, boot.Sector);
            Assert.Equal("SEGA SEGAKATANA", boot.HardwareId);
            Assert.Equal("1ST_READ.BIN", boot.BootFile);
            Assert.Equal("TEST TITLE", boot.Title);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void InspectFindsDreamcastBootSectorAtGdiDataTrackStart()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempRoot);
        var trackPath = Path.Combine(tempRoot, "track03.bin");
        var gdiPath = Path.Combine(tempRoot, "game.gdi");

        try
        {
            File.WriteAllBytes(trackPath, CreateCdSector(CreateBootSector("BOOT.BIN", "GDI TITLE")));
            File.WriteAllText(
                gdiPath,
                $$"""
                1
                3 45000 4 2352 "{{Path.GetFileName(trackPath)}}" 0
                """);

            var report = DreamcastMediaInspector.Inspect(gdiPath);

            var boot = Assert.IsType<DreamcastBootSectorInfo>(report.BootSector);
            Assert.Equal(45000u, boot.Sector);
            Assert.Equal("BOOT.BIN", boot.BootFile);
            Assert.Equal("GDI TITLE", boot.Title);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void InspectReportsCueTrackLayout()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempRoot);
        var audioPath = Path.Combine(tempRoot, "audio.bin");
        var dataPath = Path.Combine(tempRoot, "data track.bin");
        var cuePath = Path.Combine(tempRoot, "game.cue");

        try
        {
            File.WriteAllBytes(audioPath, new byte[2352]);
            File.WriteAllBytes(dataPath, CreateCdSector(CreateBootSector("1ST_READ.BIN", "CUE TITLE")));
            File.WriteAllText(
                cuePath,
                $$"""
                FILE "audio.bin" BINARY
                  TRACK 01 AUDIO
                    INDEX 01 00:00:00
                FILE "data track.bin" BINARY
                  TRACK 03 MODE1/2352
                    INDEX 01 00:00:00
                """);

            var report = DreamcastMediaInspector.Inspect(cuePath);

            Assert.Collection(
                report.CueTracks,
                track =>
                {
                    Assert.Equal(1, track.TrackNumber);
                    Assert.Equal("AUDIO", track.Type);
                    Assert.False(track.IsData);
                },
                track =>
                {
                    Assert.Equal(3, track.TrackNumber);
                    Assert.Equal("MODE1/2352", track.Type);
                    Assert.True(track.IsData);
                    Assert.Equal(dataPath, track.FilePath);
                });
            Assert.Equal("CUE TITLE", report.BootSector?.Title);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void LoadFromGdiMapsAbsoluteLbaFrom2352ByteDataTrack()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempRoot);
        var trackPath = Path.Combine(tempRoot, "track data.bin");
        var gdiPath = Path.Combine(tempRoot, "game.gdi");

        try
        {
            File.WriteAllBytes(trackPath, CreateCdSector([0xA0, 0xA1]).Concat(CreateCdSector([0xB0, 0xB1])).ToArray());
            File.WriteAllText(
                gdiPath,
                $$"""
                1
                3 45000 4 2352 "{{Path.GetFileName(trackPath)}}" 0
                """);

            var media = DreamcastMediaImageLoader.LoadFromFile(gdiPath);

            Assert.Equal(2048, media.SectorSize);
            Assert.Equal(45002ul, media.SectorCount);
            Assert.Equal(45002u, media.LeadoutFad);
            var track = Assert.Single(media.Tracks);
            Assert.Equal(3, track.TrackNumber);
            Assert.Equal(45000u, track.StartFad);
            Assert.Equal(2ul, track.SectorCount);

            Span<byte> sector = stackalloc byte[2048];
            Assert.False(media.TryReadSector(44999, sector, out var bytesRead));
            Assert.Equal(0, bytesRead);

            Assert.True(media.TryReadSector(45000, sector, out bytesRead));
            Assert.Equal(2048, bytesRead);
            Assert.Equal(0xA0, sector[0]);
            Assert.Equal(0xA1, sector[1]);

            Assert.True(media.TryReadSector(45001, sector, out bytesRead));
            Assert.Equal(2048, bytesRead);
            Assert.Equal(0xB0, sector[0]);
            Assert.Equal(0xB1, sector[1]);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void LoadFromGdiMapsMultipleDataTracksByAbsoluteFad()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempRoot);
        var track3Path = Path.Combine(tempRoot, "track03.bin");
        var track4Path = Path.Combine(tempRoot, "track04.bin");
        var gdiPath = Path.Combine(tempRoot, "game.gdi");

        try
        {
            File.WriteAllBytes(track3Path, Create2048Sector([0x33]).Concat(Create2048Sector([0x34])).ToArray());
            File.WriteAllBytes(track4Path, Create2048Sector([0x44]).Concat(Create2048Sector([0x45])).ToArray());
            File.WriteAllText(
                gdiPath,
                $$"""
                2
                3 45000 4 2048 {{Path.GetFileName(track3Path)}} 0
                4 45150 4 2048 {{Path.GetFileName(track4Path)}} 0
                """);

            var media = DreamcastMediaImageLoader.LoadFromFile(gdiPath);

            Assert.Equal(45152ul, media.SectorCount);
            Assert.Equal(45152u, media.LeadoutFad);
            Assert.Collection(
                media.Tracks,
                track =>
                {
                    Assert.Equal(3, track.TrackNumber);
                    Assert.Equal(45000u, track.StartFad);
                    Assert.Equal(2ul, track.SectorCount);
                },
                track =>
                {
                    Assert.Equal(4, track.TrackNumber);
                    Assert.Equal(45150u, track.StartFad);
                    Assert.Equal(2ul, track.SectorCount);
                });

            Span<byte> sector = stackalloc byte[2048];
            Assert.True(media.TryReadSector(45000, sector, out var bytesRead));
            Assert.Equal(2048, bytesRead);
            Assert.Equal(0x33, sector[0]);

            Assert.False(media.TryReadSector(45002, sector, out bytesRead));
            Assert.Equal(0, bytesRead);

            Assert.True(media.TryReadSector(45150, sector, out bytesRead));
            Assert.Equal(2048, bytesRead);
            Assert.Equal(0x44, sector[0]);

            Assert.True(media.TryReadSector(45151, sector, out bytesRead));
            Assert.Equal(2048, bytesRead);
            Assert.Equal(0x45, sector[0]);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void LoadFromGdiMapsOffset2352DataTrackWithNonDefaultTrackNumber()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempRoot);
        var trackPath = Path.Combine(tempRoot, "track05.bin");
        var gdiPath = Path.Combine(tempRoot, "game.gdi");

        try
        {
            File.WriteAllBytes(trackPath, Enumerable.Repeat((byte)0xCC, 32).Concat(CreateCdSector([0x5A, 0x5B])).Concat(CreateCdSector([0x6A, 0x6B])).ToArray());
            File.WriteAllText(
                gdiPath,
                $$"""
                1
                5 45200 4 2352 {{Path.GetFileName(trackPath)}} 32
                """);

            var media = DreamcastMediaImageLoader.LoadFromFile(gdiPath);

            Assert.Equal(45202ul, media.SectorCount);
            Assert.Equal(45202u, media.LeadoutFad);
            var track = Assert.Single(media.Tracks);
            Assert.Equal(5, track.TrackNumber);
            Assert.Equal(45200u, track.StartFad);
            Assert.Equal(2ul, track.SectorCount);

            Span<byte> sector = stackalloc byte[2048];
            Assert.False(media.TryReadSector(45199, sector, out var bytesRead));
            Assert.Equal(0, bytesRead);

            Assert.True(media.TryReadSector(45200, sector, out bytesRead));
            Assert.Equal(2048, bytesRead);
            Assert.Equal(0x5A, sector[0]);
            Assert.Equal(0x5B, sector[1]);

            Assert.True(media.TryReadSector(45201, sector, out bytesRead));
            Assert.Equal(2048, bytesRead);
            Assert.Equal(0x6A, sector[0]);
            Assert.Equal(0x6B, sector[1]);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void LoadFromGdiHonorsDataTrackFileOffset()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempRoot);
        var trackPath = Path.Combine(tempRoot, "track03.bin");
        var gdiPath = Path.Combine(tempRoot, "game.gdi");

        try
        {
            File.WriteAllBytes(trackPath, Enumerable.Repeat((byte)0xCC, 16).Concat(Create2048Sector([0x44, 0x45])).ToArray());
            File.WriteAllText(
                gdiPath,
                $$"""
                1
                3 45000 4 2048 {{Path.GetFileName(trackPath)}} 16
                """);

            var media = DreamcastMediaImageLoader.LoadFromFile(gdiPath);

            Span<byte> sector = stackalloc byte[2048];
            Assert.True(media.TryReadSector(45000, sector, out var bytesRead));
            Assert.Equal(2048, bytesRead);
            Assert.Equal(0x44, sector[0]);
            Assert.Equal(0x45, sector[1]);
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

    private static byte[] CreateBootSector(string bootFile, string title)
    {
        var sector = new byte[2048];
        WriteAscii(sector, 0x00, 0x10, "SEGA SEGAKATANA");
        WriteAscii(sector, 0x10, 0x10, "SEGA ENTERPRISES");
        WriteAscii(sector, 0x20, 0x10, "DCSH GD-ROM1/1");
        WriteAscii(sector, 0x30, 0x08, "U");
        WriteAscii(sector, 0x38, 0x08, "0799A10");
        WriteAscii(sector, 0x40, 0x0A, "T0000N");
        WriteAscii(sector, 0x4A, 0x06, "V1.000");
        WriteAscii(sector, 0x50, 0x10, "20260525");
        WriteAscii(sector, 0x60, 0x10, bootFile);
        WriteAscii(sector, 0x70, 0x10, "DCSHARP");
        WriteAscii(sector, 0x80, 0x80, title);
        return sector;
    }

    private static void WriteAscii(byte[] data, int offset, int length, string text)
    {
        var bytes = System.Text.Encoding.ASCII.GetBytes(text);
        Array.Copy(bytes, 0, data, offset, Math.Min(bytes.Length, length));
        for (var index = bytes.Length; index < length; index++)
        {
            data[offset + index] = 0x20;
        }
    }

    private static byte[] Create2048Sector(byte[] payloadHeader)
    {
        var sector = new byte[2048];
        Array.Copy(payloadHeader, sector, payloadHeader.Length);
        return sector;
    }
}
