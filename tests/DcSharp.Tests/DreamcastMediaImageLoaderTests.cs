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
    public void LoadFromCueUsesMode2PayloadOffset()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempRoot);
        var trackPath = Path.Combine(tempRoot, "mode2.bin");
        var cuePath = Path.Combine(tempRoot, "game.cue");

        try
        {
            var track = new byte[2352];
            track[16] = 0x16;
            track[24] = 0x24;
            track[25] = 0x25;
            File.WriteAllBytes(trackPath, track);
            File.WriteAllText(
                cuePath,
                $$"""
                FILE "{{Path.GetFileName(trackPath)}}" BINARY
                  TRACK 01 MODE2/2352
                    INDEX 01 00:00:00
                """);

            var media = DreamcastMediaImageLoader.LoadFromFile(cuePath);

            Span<byte> sector = stackalloc byte[2048];
            Assert.True(media.TryReadSector(0, sector, out var bytesRead));
            Assert.Equal(2048, bytesRead);
            Assert.Equal(0x24, sector[0]);
            Assert.Equal(0x25, sector[1]);
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
    public void InspectReportsCueDirectoryBootCandidates()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempRoot);
        var cueDataPath = Path.Combine(tempRoot, "game.bin");
        var track3Path = Path.Combine(tempRoot, "game (Track 3).bin");
        var cuePath = Path.Combine(tempRoot, "game.cue");

        try
        {
            File.WriteAllBytes(cueDataPath, CreateCdSector([0x00]));
            File.WriteAllBytes(track3Path, CreateCdSector(CreateBootSector("1ST_READ.BIN", "ADJACENT TITLE")));
            File.WriteAllText(
                cuePath,
                $$"""
                FILE "{{Path.GetFileName(cueDataPath)}}" BINARY
                  TRACK 01 MODE2/2352
                    INDEX 01 00:00:00
                """);

            var report = DreamcastMediaInspector.Inspect(cuePath);

            Assert.Null(report.BootSector);
            var candidate = Assert.Single(report.BootSectorCandidates);
            Assert.Equal(track3Path, candidate.FilePath);
            Assert.Equal(2352, candidate.SourceSectorSize);
            Assert.Equal(16, candidate.PayloadOffset);
            Assert.Equal(16, candidate.ByteOffset);
            Assert.Equal("1ST_READ.BIN", candidate.BootSector.BootFile);
            Assert.Equal("ADJACENT TITLE", candidate.BootSector.Title);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void ExtractBootFileReadsIso9660BootBinary()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, CreateBootableIsoImage("1ST_READ.BIN", "BOOT DATA"u8.ToArray()));

            var result = DreamcastBootExtractor.ExtractBootFile(path);

            Assert.Equal("1ST_READ.BIN", result.BootSector.BootFile);
            Assert.Equal("TEST ISO", result.VolumeIdentifier);
            Assert.Equal("1ST_READ.BIN;1", result.File.Name);
            Assert.Equal(21u, result.File.ExtentSector);
            Assert.Equal("BOOT DATA"u8.ToArray(), result.Data);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ExtractBootFileMapsDreamcastAbsoluteIsoExtents()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, CreateBootableIsoImage("1ST_READ.BIN", "FAD BOOT"u8.ToArray(), extentBias: 45_000));

            var result = DreamcastBootExtractor.ExtractBootFile(path);

            Assert.Equal("1ST_READ.BIN", result.BootSector.BootFile);
            Assert.Equal(21u, result.File.ExtentSector);
            Assert.Equal("FAD BOOT"u8.ToArray(), result.Data);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ExtractBootFilePrefersHighDensityGdiVolumeWhenLowVolumeLacksBootFile()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempRoot);
        var lowTrackPath = Path.Combine(tempRoot, "track01.bin");
        var highTrackPath = Path.Combine(tempRoot, "track03.bin");
        var gdiPath = Path.Combine(tempRoot, "game.gdi");

        try
        {
            var lowIso = CreateBootableIsoImage("OTHER.BIN", "LOW"u8.ToArray(), advertisedBootFile: "1ST_READ.BIN");
            var highIso = CreateBootableIsoImage("1ST_READ.BIN", "HIGH BOOT"u8.ToArray(), extentBias: 45_000);

            File.WriteAllBytes(lowTrackPath, ToCdSectors(lowIso));
            File.WriteAllBytes(highTrackPath, ToCdSectors(highIso));
            File.WriteAllText(
                gdiPath,
                $$"""
                2
                1     0 4 2352 "{{Path.GetFileName(lowTrackPath)}}" 0
                3 45000 4 2352 "{{Path.GetFileName(highTrackPath)}}" 0
                """);

            var result = DreamcastBootExtractor.ExtractBootFile(gdiPath);

            Assert.Equal(gdiPath, result.SourcePath);
            Assert.Equal("1ST_READ.BIN", result.BootSector.BootFile);
            Assert.Equal(45_021u, result.File.ExtentSector);
            Assert.Equal("HIGH BOOT"u8.ToArray(), result.Data);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void ExtractBootFileContinuesAfterUnreadableAdjacentCandidate()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempRoot);
        var cueDataPath = Path.Combine(tempRoot, "game.bin");
        var shortTrackPath = Path.Combine(tempRoot, "game (Track 1).bin");
        var usableTrackPath = Path.Combine(tempRoot, "game (Track 3).bin");
        var cuePath = Path.Combine(tempRoot, "game.cue");

        try
        {
            File.WriteAllBytes(cueDataPath, CreateCdSector([0x00]));
            File.WriteAllBytes(shortTrackPath, ToCdSectors(CreateBootableIsoImage("1ST_READ.BIN", "BAD"u8.ToArray(), fileExtent: 200)));
            File.WriteAllBytes(usableTrackPath, ToCdSectors(CreateBootableIsoImage("1ST_READ.BIN", "GOOD"u8.ToArray())));
            File.WriteAllText(
                cuePath,
                $$"""
                FILE "{{Path.GetFileName(cueDataPath)}}" BINARY
                  TRACK 01 MODE2/2352
                    INDEX 01 00:00:00
                """);

            var result = DreamcastBootExtractor.ExtractBootFile(cuePath);

            Assert.Equal(usableTrackPath, result.SourcePath);
            Assert.Equal("GOOD"u8.ToArray(), result.Data);
            Assert.Contains(result.PriorAttempts, attempt => attempt.Contains("Failed to read ISO9660 sector", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void ExtractBootFileCanUseCandidateMetadataWithPrimaryCueFilesystem()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempRoot);
        var cueDataPath = Path.Combine(tempRoot, "game.bin");
        var candidatePath = Path.Combine(tempRoot, "game (Track 3).bin");
        var cuePath = Path.Combine(tempRoot, "game.cue");

        try
        {
            var primaryIso = CreateBootableIsoImage("1ST_READ.BIN", "PRIMARY"u8.ToArray());
            Array.Clear(primaryIso, 0, 2048);
            File.WriteAllBytes(cueDataPath, ToCdSectors(primaryIso, payloadOffset: 24));
            File.WriteAllBytes(candidatePath, CreateCdSector(CreateBootSector("1ST_READ.BIN", "CANDIDATE")));
            File.WriteAllText(
                cuePath,
                $$"""
                FILE "{{Path.GetFileName(cueDataPath)}}" BINARY
                  TRACK 01 MODE2/2352
                    INDEX 01 00:00:00
                """);

            var result = DreamcastBootExtractor.ExtractBootFile(cuePath);

            Assert.Equal(cuePath, result.SourcePath);
            Assert.Equal("PRIMARY"u8.ToArray(), result.Data);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void ExtractBootFileCanResolveBootExtentFromLaterAdjacentTrack()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempRoot);
        var cueDataPath = Path.Combine(tempRoot, "game.bin");
        var track3Path = Path.Combine(tempRoot, "game (Track 3).bin");
        var track4Path = Path.Combine(tempRoot, "game (Track 4).bin");
        var track5Path = Path.Combine(tempRoot, "game (Track 5).bin");
        var cuePath = Path.Combine(tempRoot, "game.cue");

        try
        {
            var bootData = "ADJACENT"u8.ToArray();
            var track5Start = 45_000u + 24u + 1u;
            var track3Iso = CreateBootableIsoImage("1ST_READ.BIN", bootData, extentBias: 45_000, fileExtent: track5Start + 1 - 45_000);
            var track5Iso = new byte[2048 * 2];
            bootData.CopyTo(track5Iso.AsSpan(2048));

            File.WriteAllBytes(cueDataPath, CreateCdSector([0x00]));
            File.WriteAllBytes(track3Path, ToCdSectors(track3Iso));
            File.WriteAllBytes(track4Path, CreateCdSector([0x00]));
            File.WriteAllBytes(track5Path, ToCdSectors(track5Iso));
            File.WriteAllText(
                cuePath,
                $$"""
                FILE "{{Path.GetFileName(cueDataPath)}}" BINARY
                  TRACK 01 MODE2/2352
                    INDEX 01 00:00:00
                """);

            var result = DreamcastBootExtractor.ExtractBootFile(cuePath);

            Assert.Equal(track3Path, result.SourcePath);
            Assert.Equal(bootData, result.Data);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void DreamcastBootScramblerRoundTripsData()
    {
        var original = Enumerable.Range(0, 4096).Select(value => (byte)value).ToArray();

        var scrambled = DreamcastBootScrambler.Scramble(original);
        var descrambled = DreamcastBootScrambler.Descramble(scrambled);

        Assert.NotEqual(original, scrambled);
        Assert.Equal(original, descrambled);
    }

    [Fact]
    public void DreamcastBootScramblerRoundTripsLargeChunks()
    {
        var original = Enumerable.Range(0, (2048 * 1024) + 64).Select(value => (byte)value).ToArray();

        var scrambled = DreamcastBootScrambler.Scramble(original);
        var descrambled = DreamcastBootScrambler.Descramble(scrambled);

        Assert.Equal(original, descrambled);
    }

    [Fact]
    public void BootBinaryAnalyzerReportsOriginalStartupStub()
    {
        var data = CreateDreamcastStartupStubBinary();

        var analysis = DreamcastBootBinaryAnalyzer.Analyze(data, "1ST_READ.BIN", "test");

        Assert.Equal("original", analysis.RecommendedLayout);
        Assert.True(analysis.Original.HasDreamcastStartupStub);
        Assert.Equal("0x8C010000", analysis.LoadAddressHex);
    }

    [Fact]
    public void BootBinaryAnalyzerDetectsWindowsCeEntryHeader()
    {
        var data = CreateWindowsCeBootHeaderBinary();

        var analysis = DreamcastBootBinaryAnalyzer.Analyze(data, "0WINCEOS.BIN", "test");

        Assert.Equal("original", analysis.RecommendedLayout);
        Assert.True(analysis.Original.HasWindowsCeHeader);
        Assert.Equal(0x800u, analysis.Original.WindowsCeEntryOffset);
        Assert.Equal(0x800u, analysis.Original.WindowsCePayloadOffset);
        Assert.Equal("0x8C010000", analysis.Original.SuggestedEntryPointHex);
        Assert.Equal(0x8C010820u, analysis.Original.WindowsCeEntryJumpTarget);
        Assert.Equal(0x1020u, analysis.Original.WindowsCeEntryJumpTargetFileOffset);
        Assert.StartsWith(
            "0x0009 0xE001 0x000B 0x0009",
            analysis.Original.WindowsCeEntryJumpTargetFirstWordsHex,
            StringComparison.Ordinal);
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

    private static byte[] CreateBootableIsoImage(string bootFile, byte[] bootData, uint extentBias = 0, uint fileExtent = 21, string? advertisedBootFile = null)
    {
        var image = new byte[2048 * 24];
        Array.Copy(CreateBootSector(advertisedBootFile ?? bootFile, "ISO BOOT TEST"), image, 2048);

        var pvd = image.AsSpan(16 * 2048, 2048);
        pvd[0] = 1;
        System.Text.Encoding.ASCII.GetBytes("CD001").CopyTo(pvd[1..]);
        pvd[6] = 1;
        WriteAscii(image, (16 * 2048) + 40, 32, "TEST ISO");
        WriteDirectoryRecord(pvd, 156, extentBias + 20, 2048, 0x02, [0]);

        var directory = image.AsSpan(20 * 2048, 2048);
        var offset = 0;
        offset += WriteDirectoryRecord(directory, offset, extentBias + 20, 2048, 0x02, [0]);
        offset += WriteDirectoryRecord(directory, offset, extentBias + 20, 2048, 0x02, [1]);
        WriteDirectoryRecord(directory, offset, extentBias + fileExtent, (uint)bootData.Length, 0x00, System.Text.Encoding.ASCII.GetBytes($"{bootFile};1"));
        var fileOffset = (long)fileExtent * 2048;
        if (fileOffset + bootData.Length <= image.Length)
        {
            Array.Copy(bootData, 0, image, fileOffset, bootData.Length);
        }

        return image;
    }

    private static byte[] ToCdSectors(byte[] isoImage, int payloadOffset = 16)
    {
        var sectors = isoImage.Length / 2048;
        var raw = new byte[sectors * 2352];
        for (var sector = 0; sector < sectors; sector++)
        {
            Array.Copy(isoImage, sector * 2048, raw, (sector * 2352) + payloadOffset, 2048);
        }

        return raw;
    }

    private static byte[] CreateDreamcastStartupStubBinary()
    {
        var data = new byte[256];
        var words = new ushort[]
        {
            0x0009,
            0x0009,
            0x0009,
            0x0009,
            0x0009,
            0x0009,
            0xD005,
            0x6102,
            0xD205,
            0x2129,
            0x9204,
            0x212B
        };

        for (var index = 0; index < words.Length; index++)
        {
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(index * 2, 2), words[index]);
        }

        return data;
    }

    private static byte[] CreateWindowsCeBootHeaderBinary()
    {
        var data = new byte[0x1800];
        WriteUInt32BothEndian(data, 0x14, 0x0C01_0000);
        WriteUInt32BothEndian(data, 0x18, 0x800);
        data[0x800] = 0x09;
        data[0x801] = 0x00;
        data[0x802] = 0x09;
        data[0x803] = 0x00;
        data[0x804] = 0x01;
        data[0x805] = 0xD0;
        data[0x806] = 0x09;
        data[0x807] = 0x00;
        data[0x808] = 0x2B;
        data[0x809] = 0x40;
        data[0x80A] = 0x09;
        data[0x80B] = 0x00;
        WriteUInt32LittleEndian(data, 0x80C, 0x8C01_0820);
        WriteUInt16LittleEndian(data, 0x1020, 0x0009);
        WriteUInt16LittleEndian(data, 0x1022, 0xE001);
        WriteUInt16LittleEndian(data, 0x1024, 0x000B);
        WriteUInt16LittleEndian(data, 0x1026, 0x0009);
        return data;
    }

    private static int WriteDirectoryRecord(Span<byte> destination, int offset, uint extent, uint dataLength, byte flags, byte[] name)
    {
        var length = 33 + name.Length + (name.Length % 2 == 0 ? 1 : 0);
        var record = destination.Slice(offset, length);
        record.Clear();
        record[0] = (byte)length;
        WriteUInt32BothEndian(record, 2, extent);
        WriteUInt32BothEndian(record, 10, dataLength);
        record[25] = flags;
        record[28] = 1;
        record[29] = 0;
        record[30] = 1;
        record[31] = 0;
        record[32] = (byte)name.Length;
        name.CopyTo(record[33..]);
        return length;
    }

    private static void WriteUInt32BothEndian(Span<byte> destination, int offset, uint value)
    {
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(offset, 4), value);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(destination.Slice(offset + 4, 4), value);
    }

    private static void WriteUInt32LittleEndian(Span<byte> destination, int offset, uint value) =>
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(offset, 4), value);

    private static void WriteUInt16LittleEndian(Span<byte> destination, int offset, ushort value) =>
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(offset, 2), value);
}
