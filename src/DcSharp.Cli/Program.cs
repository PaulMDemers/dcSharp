using DcSharp.Core.Dreamcast.Memory;
using DcSharp.Core.Dreamcast.Timer;
using DcSharp.Core.Dreamcast.Audio;
using DcSharp.Core.Dreamcast.Input;
using DcSharp.Core.Dreamcast.Video;
using DcSharp.Core.Cpu;
using DcSharp.Core.Execution;
using DcSharp.Core.Fixtures;
using DcSharp.Core.Loading;
using DcSharp.Core.Media;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

if (args.Length == 0 || args[0] is "-h" or "--help")
{
    PrintUsage();
    return args.Length == 0 ? 1 : 0;
}

try
{
    switch (args[0])
    {
        case "inspect" when args.Length == 2:
            InspectElf(args[1]);
            return 0;
        case "media" when args.Length >= 3 && args[1] == "inspect":
            InspectMedia(args[2], args[3..]);
            return 0;
        case "media" when args.Length >= 3 && args[1] == "extract-boot":
            ExtractBoot(args[2], args[3..]);
            return 0;
        case "media" when args.Length >= 3 && args[1] == "analyze-boot":
            AnalyzeBoot(args[2], args[3..]);
            return 0;
        case "media" when args.Length >= 3 && args[1] == "boot-smoke":
            BootSmoke(args[2], args[3..]);
            return 0;
        case "run" when args.Length >= 2:
            RunElf(args[1], args[2..]);
            return 0;
        case "fixtures" when args.Length >= 2:
            return RunFixtures(args[1], args[2..]);
        default:
            PrintUsage();
            return 1;
    }
}
catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
{
    Console.Error.WriteLine($"dcsharp: {ex.Message}");
    return 2;
}

static void InspectElf(string path)
{
    using var stream = File.OpenRead(path);
    var elf = ElfFile.Read(stream);

    Console.WriteLine($"Path: {Path.GetFullPath(path)}");
    Console.WriteLine($"Format: ELF32 {elf.Endianness}");
    Console.WriteLine($"Machine: {elf.Machine}");
    Console.WriteLine($"Entry: 0x{elf.EntryPoint:X8}");
    Console.WriteLine($"Program headers: {elf.ProgramHeaderCount}");
    Console.WriteLine($"Section headers: {elf.SectionHeaderCount}");
    Console.WriteLine($"Loadable segments: {elf.ProgramHeaders.Count(header => header.IsLoadable)}");

    if (!elf.IsDreamcastCandidate)
    {
        Console.WriteLine("Dreamcast: no, expected little-endian SH ELF");
        return;
    }

    Console.WriteLine("Dreamcast: yes, plausible SH-4/KallistiOS executable");
}

static void InspectMedia(string path, string[] args)
{
    var emitJson = false;
    var scanSectors = 1024;
    for (var index = 0; index < args.Length; index++)
    {
        switch (args[index])
        {
            case "--json":
                emitJson = true;
                break;
            case "--scan-sectors" when index + 1 < args.Length && int.TryParse(args[index + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedScanSectors):
                scanSectors = parsedScanSectors;
                index++;
                break;
            default:
                throw new InvalidDataException($"Unknown or invalid media inspect option: {args[index]}");
        }
    }

    if (scanSectors < 0)
    {
        throw new InvalidDataException("--scan-sectors must be zero or greater.");
    }

    var report = DreamcastMediaInspector.Inspect(path, scanSectors);
    if (emitJson)
    {
        Console.WriteLine(SerializeJson(report));
        return;
    }

    Console.WriteLine($"Path: {report.Path}");
    Console.WriteLine($"Format: {report.Format}");
    Console.WriteLine($"Sector size: {report.SectorSize}");
    Console.WriteLine($"Sectors: {report.SectorCount}");
    Console.WriteLine($"Leadout: {report.LeadoutFadHex}");
    Console.WriteLine($"Tracks: {report.Tracks.Count}");
    foreach (var track in report.Tracks)
    {
        Console.WriteLine($"  Track {track.TrackNumber}: start={track.StartFadHex}, control={track.Control}, sectors={track.SectorCount}");
    }

    if (report.CueTracks.Count > 0)
    {
        Console.WriteLine("CUE tracks:");
        foreach (var track in report.CueTracks)
        {
            Console.WriteLine($"  Track {track.TrackNumber}: type={track.Type}, data={track.IsData}, file={track.FilePath}");
        }
    }

    if (report.BootSector is not { } boot)
    {
        Console.WriteLine("Dreamcast boot sector: not found");
        PrintBootSectorCandidates(report);
        return;
    }

    Console.WriteLine($"Dreamcast boot sector: found at sector {boot.Sector} ({boot.SectorHex})");
    Console.WriteLine($"  Hardware: {boot.HardwareId}");
    Console.WriteLine($"  Maker: {boot.MakerId}");
    Console.WriteLine($"  Device: {boot.DeviceInfo}");
    Console.WriteLine($"  Area: {boot.AreaSymbols}");
    Console.WriteLine($"  Peripherals: {boot.Peripherals}");
    Console.WriteLine($"  Product: {boot.ProductNumber}");
    Console.WriteLine($"  Version: {boot.Version}");
    Console.WriteLine($"  Release: {boot.ReleaseDate}");
    Console.WriteLine($"  Boot file: {boot.BootFile}");
    Console.WriteLine($"  Software maker: {boot.SoftwareMaker}");
    Console.WriteLine($"  Title: {boot.Title}");
}

static void PrintBootSectorCandidates(DreamcastMediaInspectionReport report)
{
    if (report.BootSectorCandidates.Count == 0)
    {
        return;
    }

    Console.WriteLine("CUE directory boot candidates:");
    foreach (var candidate in report.BootSectorCandidates)
    {
        var boot = candidate.BootSector;
        Console.WriteLine($"  {candidate.FilePath}: sector={boot.SectorHex}, byteOffset={candidate.ByteOffsetHex}, sourceSectorSize={candidate.SourceSectorSize}, payloadOffset={candidate.PayloadOffset}");
        Console.WriteLine($"    Boot file: {boot.BootFile}");
        Console.WriteLine($"    Title: {boot.Title}");
    }
}

static void ExtractBoot(string path, string[] args)
{
    var emitJson = false;
    var scanSectors = 1024;
    string? outputPath = null;
    for (var index = 0; index < args.Length; index++)
    {
        switch (args[index])
        {
            case "--json":
                emitJson = true;
                break;
            case "--out" when index + 1 < args.Length:
                outputPath = args[index + 1];
                index++;
                break;
            case "--scan-sectors" when index + 1 < args.Length && int.TryParse(args[index + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedScanSectors):
                scanSectors = parsedScanSectors;
                index++;
                break;
            default:
                throw new InvalidDataException($"Unknown or invalid media extract-boot option: {args[index]}");
        }
    }

    if (scanSectors < 0)
    {
        throw new InvalidDataException("--scan-sectors must be zero or greater.");
    }

    if (string.IsNullOrWhiteSpace(outputPath))
    {
        throw new InvalidDataException("media extract-boot requires --out <path>.");
    }

    var result = DreamcastBootExtractor.ExtractBootFile(path, scanSectors);
    var fullOutputPath = Path.GetFullPath(outputPath);
    var outputDirectory = Path.GetDirectoryName(fullOutputPath);
    if (!string.IsNullOrEmpty(outputDirectory))
    {
        Directory.CreateDirectory(outputDirectory);
    }

    File.WriteAllBytes(fullOutputPath, result.Data);

    var report = new BootExtractionCliReport(
        result.MediaPath,
        result.SourcePath,
        fullOutputPath,
        result.BootSector.BootFile,
        result.BootSector.Title,
        result.VolumeIdentifier,
        result.File.ExtentSector,
        $"0x{result.File.ExtentSector:X8}",
        result.File.Length,
        result.Data.Length,
        result.PriorAttempts);

    if (emitJson)
    {
        Console.WriteLine(SerializeJson(report));
        return;
    }

    Console.WriteLine($"Media: {report.MediaPath}");
    Console.WriteLine($"Source: {report.SourcePath}");
    Console.WriteLine($"Volume: {report.VolumeIdentifier}");
    Console.WriteLine($"Boot file: {report.BootFile}");
    Console.WriteLine($"Title: {report.Title}");
    Console.WriteLine($"Extent: {report.ExtentSector} ({report.ExtentSectorHex})");
    Console.WriteLine($"Bytes: {report.BytesWritten}");
    Console.WriteLine($"Output: {report.OutputPath}");
    if (report.PriorAttempts.Count > 0)
    {
        Console.WriteLine("Earlier sources skipped:");
        foreach (var attempt in report.PriorAttempts)
        {
            Console.WriteLine($"  {attempt}");
        }
    }
}

static void AnalyzeBoot(string path, string[] args)
{
    var emitJson = false;
    var scanSectors = 1024;
    string? descrambledOutputPath = null;
    for (var index = 0; index < args.Length; index++)
    {
        switch (args[index])
        {
            case "--json":
                emitJson = true;
                break;
            case "--out-descrambled" when index + 1 < args.Length:
                descrambledOutputPath = args[index + 1];
                index++;
                break;
            case "--scan-sectors" when index + 1 < args.Length && int.TryParse(args[index + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedScanSectors):
                scanSectors = parsedScanSectors;
                index++;
                break;
            default:
                throw new InvalidDataException($"Unknown or invalid media analyze-boot option: {args[index]}");
        }
    }

    if (scanSectors < 0)
    {
        throw new InvalidDataException("--scan-sectors must be zero or greater.");
    }

    var (data, sourcePath, sourceKind) = ReadBootAnalysisInput(path, scanSectors);
    var analysis = DreamcastBootBinaryAnalyzer.Analyze(data, sourcePath, sourceKind);
    if (!string.IsNullOrWhiteSpace(descrambledOutputPath))
    {
        var fullOutputPath = Path.GetFullPath(descrambledOutputPath);
        var outputDirectory = Path.GetDirectoryName(fullOutputPath);
        if (!string.IsNullOrEmpty(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        File.WriteAllBytes(fullOutputPath, DreamcastBootScrambler.Descramble(data));
    }

    if (emitJson)
    {
        Console.WriteLine(SerializeJson(analysis));
        return;
    }

    Console.WriteLine($"Source: {analysis.SourcePath}");
    Console.WriteLine($"Source kind: {analysis.SourceKind}");
    Console.WriteLine($"Size: {analysis.Size}");
    Console.WriteLine($"Load address: {analysis.LoadAddressHex}");
    Console.WriteLine($"Recommended layout: {analysis.RecommendedLayout}");
    PrintBootBinaryCandidate(analysis.Original);
    PrintBootBinaryCandidate(analysis.Descrambled);
    if (!string.IsNullOrWhiteSpace(descrambledOutputPath))
    {
        Console.WriteLine($"Descrambled output: {Path.GetFullPath(descrambledOutputPath)}");
    }
}

static (byte[] Data, string SourcePath, string SourceKind) ReadBootAnalysisInput(string path, int scanSectors)
{
    if (IsMediaDescriptorPath(path))
    {
        var extraction = DreamcastBootExtractor.ExtractBootFile(path, scanSectors);
        return (extraction.Data, extraction.SourcePath, "media-extracted");
    }

    var fullPath = Path.GetFullPath(path);
    return (File.ReadAllBytes(fullPath), fullPath, "binary-file");
}

static bool IsMediaDescriptorPath(string path) =>
    Path.GetExtension(path) is { } extension
    && (string.Equals(extension, ".cue", StringComparison.OrdinalIgnoreCase)
        || string.Equals(extension, ".gdi", StringComparison.OrdinalIgnoreCase));

static void PrintBootBinaryCandidate(DreamcastBootBinaryCandidate candidate)
{
    Console.WriteLine($"{candidate.Layout}:");
    Console.WriteLine($"  ELF: {candidate.IsElf}");
    Console.WriteLine($"  Dreamcast startup stub: {candidate.HasDreamcastStartupStub}");
    Console.WriteLine($"  SH-4 opcode sample: {candidate.RecognizedOpcodeCount}/{candidate.TotalOpcodeCount} ({candidate.RecognizedOpcodeRatio:P1})");
    Console.WriteLine($"  NOP/zero/fill opcodes: {candidate.NopCount}/{candidate.ZeroOpcodeCount}/{candidate.FillOpcodeCount}");
    Console.WriteLine($"  First words: {candidate.FirstWordsHex}");
    Console.WriteLine($"  First bytes: {candidate.FirstBytesHex}");
}

static void BootSmoke(string path, string[] args)
{
    const uint ipBinEntryPoint = DreamcastRawBinaryLoader.IpBinLoadAddress + 0x300;
    const uint ipBinInitialStatusRegister = Sh4State.SrMachineBit | 0xF0;
    var (scanSectors, requestedLayout, runArgs) = ParseBootSmokeOptions(args);
    var (data, sourcePath, sourceKind) = ReadBootAnalysisInput(path, scanSectors);
    var analysis = DreamcastBootBinaryAnalyzer.Analyze(data, sourcePath, sourceKind);
    var selectedLayout = ResolveBootLayout(requestedLayout, analysis);
    var bootBytes = selectedLayout == "descrambled"
        ? DreamcastBootScrambler.Descramble(data)
        : data;
    var options = ParseRunOptions(runArgs);
    byte[]? ipBin = null;
    var enterIpBin = false;
    if (IsMediaDescriptorPath(path))
    {
        ipBin = TryReadIpBin(path, scanSectors);
        enterIpBin = HasIpBinExecutableBootstrap(ipBin);
        options = options with
        {
            Emulation = options.Emulation with
            {
                Media = DreamcastMediaImageLoader.LoadFromFile(path),
                SeedInitialVBlank = enterIpBin,
                InitialStatusRegister = enterIpBin && options.Emulation.InitialStatusRegister == 0
                    ? ipBinInitialStatusRegister
                    : options.Emulation.InitialStatusRegister
            }
        };
    }

    var result = new DreamcastRunner().RunRawBinary(
        bootBytes,
        options.Emulation,
        analysis.LoadAddress,
        ipBin,
        enterIpBin ? ipBinEntryPoint : null);

    if (options.FramebufferDumpPath is not null)
    {
        DumpFramebuffer(result, options);
    }

    if (options.AudioWavPath is not null)
    {
        DumpAudioWav(result, options.AudioWavPath);
    }

    if (options.TraceLogPath is not null)
    {
        DumpTraceLog(result, options.TraceLogPath);
    }

    if (options.DeviceLogPath is not null)
    {
        DumpDeviceLog(result, options);
    }

    if (options.MemoryWriteLogPath is not null)
    {
        DumpMemoryWriteLog(result, options.MemoryWriteLogPath);
    }

    var summary = DreamcastRunSummary.FromResult(result, options.Emulation);
    if (options.EmitJson)
    {
        Console.WriteLine(SerializeJson(new BootSmokeCliReport(analysis, selectedLayout, ipBin is not null, result.MemoryRegionWrites, summary)));
        return;
    }

    Console.WriteLine($"Source: {analysis.SourcePath}");
    Console.WriteLine($"Source kind: {analysis.SourceKind}");
    Console.WriteLine($"Selected layout: {selectedLayout}");
    Console.WriteLine($"Analyzer recommendation: {analysis.RecommendedLayout}");
    Console.WriteLine($"Load address: {analysis.LoadAddressHex}");
    Console.WriteLine($"IP.BIN seeded: {ipBin is not null}");
    Console.WriteLine($"Bytes loaded: {bootBytes.Length}");
    Console.WriteLine($"Instructions: {result.Cpu.InstructionsExecuted}");
    Console.WriteLine($"PC: 0x{result.Cpu.Pc:X8}");
    Console.WriteLine($"PR: 0x{result.Cpu.Pr:X8}");
    Console.WriteLine($"SR: 0x{result.Cpu.Sr:X8}");
    PrintGeneralRegisters(result.Cpu);
    Console.WriteLine($"Stopped: {result.StopReason}");
    Console.WriteLine($"Detail: {result.StopDetail}");
    PrintSoftResetCheckpoint(result);
    Console.WriteLine($"Device accesses: {result.DeviceAccesses.Count}");
    Console.WriteLine($"Watched memory writes: {result.WatchedMemoryWrites.Count}");
    Console.WriteLine($"Serial bytes: {result.SerialOutput.Count}");
    var gdrom = result.Gdrom ?? DreamcastGdromSnapshot.Empty;
    Console.WriteLine($"GD-ROM: media={gdrom.HasMedia}, reads={gdrom.ReadCommands.Count}, ok={gdrom.ReadCommands.Count(command => command.Success)}, failed={gdrom.ReadCommands.Count(command => !command.Success)}, tocs={gdrom.TocCommands.Count}");
    PrintMemoryRegionWrites(result.MemoryRegionWrites);

    if (result.DeviceAccesses.Count > 0)
    {
        foreach (var access in result.DeviceAccesses.TakeLast(8))
        {
            Console.WriteLine($"  {access.Kind}: addr=0x{access.Address:X8}, size={access.Size}, value=0x{access.Value:X8}");
        }
    }

    if (result.TraceTail.Count > 0)
    {
        Console.WriteLine("Trace tail:");
        foreach (var step in result.TraceTail)
        {
            Console.WriteLine($"  0x{step.Pc:X8}: 0x{step.Opcode:X4}  {step.Trace}");
        }
    }
}

static void PrintGeneralRegisters(Sh4StateSnapshot cpu)
{
    Console.WriteLine($"R0-R7: {FormatRegisterRange(cpu.R, 0, 8)}");
    Console.WriteLine($"R8-R15: {FormatRegisterRange(cpu.R, 8, 8)}");
}

static string FormatRegisterRange(IReadOnlyList<uint> registers, int start, int count) =>
    string.Join(" ", Enumerable.Range(start, count).Select(index => $"R{index}=0x{registers[index]:X8}"));

static void PrintSoftResetCheckpoint(DreamcastRunResult result)
{
    if (result.StopReason != DreamcastStopReason.FirmwareExit
        || !result.StopDetail.StartsWith("System BIOS soft reset requested", StringComparison.Ordinal)
        || result.Cpu.StackWords is not { Count: > 0 } stackWords)
    {
        return;
    }

    Console.WriteLine("IP.BIN reset checkpoint stack:");
    var baseAddress = stackWords[0].Address;
    foreach (var word in stackWords)
    {
        var offset = word.Address - baseAddress;
        var label = IpBinResetStackLabel(offset);
        var labelText = label is null ? string.Empty : $" {label}";
        Console.WriteLine($"  {word.AddressHex} (+0x{offset:X2}{labelText}): {word.ValueHex}");
    }
}

static string? IpBinResetStackLabel(uint offset) => offset switch
{
    0x00 => "saved-sr-imask",
    0x04 => "outer-wait-count",
    0x08 => "short-delay-count",
    0x0C => "completion-flag",
    0x10 => "elapsed-ticks",
    0x14 => "delta-ticks",
    0x18 => "timer-current",
    0x1C => "timer-start",
    0x20 => "selected-mode",
    0x24 => "status-code",
    _ => null
};

static void PrintMemoryRegionWrites(IReadOnlyList<DreamcastMemoryRegionWriteSummary> writes)
{
    if (writes.Count == 0)
    {
        return;
    }

    Console.WriteLine("Boot region writes:");
    foreach (var region in writes)
    {
        Console.WriteLine($"  {region.Name}: writes={region.WriteCount}, bytes={region.BytesWritten}, range={region.StartHex}-{region.EndHex}, first={region.FirstAddressHex ?? "none"}, last={region.LastAddressHex ?? "none"}");
    }
}

static byte[]? TryReadIpBin(string path, int scanSectors)
{
    const int ipBinSectorCount = 16;
    const int ipBinBytes = ipBinSectorCount * DreamcastMediaImageLoader.DefaultSectorSize;
    var report = DreamcastMediaInspector.Inspect(path, scanSectors);
    if (report.BootSector is not null)
    {
        var image = DreamcastMediaImageLoader.LoadFromFile(path);
        var ipBin = new byte[ipBinBytes];
        for (var sectorIndex = 0; sectorIndex < ipBinSectorCount; sectorIndex++)
        {
            if (!image.TryReadSector(report.BootSector.Sector + (uint)sectorIndex, ipBin.AsSpan(sectorIndex * DreamcastMediaImageLoader.DefaultSectorSize), out var bytesRead)
                || bytesRead < DreamcastMediaImageLoader.DefaultSectorSize)
            {
                return null;
            }
        }

        if (HasIpBinExecutableBootstrap(ipBin) || report.BootSectorCandidates.Count == 0)
        {
            return ipBin;
        }
    }

    byte[]? fallbackIpBin = null;
    foreach (var candidate in report.BootSectorCandidates)
    {
        var ipBin = new byte[ipBinBytes];
        if (!TryReadCandidateIpBin(candidate, ipBin))
        {
            continue;
        }

        if (HasIpBinExecutableBootstrap(ipBin))
        {
            return ipBin;
        }

        fallbackIpBin ??= ipBin;
    }

    return fallbackIpBin;
}

static bool TryReadCandidateIpBin(DreamcastBootSectorCandidate candidate, Span<byte> destination)
{
    const int userDataSectorSize = DreamcastMediaImageLoader.DefaultSectorSize;
    if (candidate.SourceSectorSize < userDataSectorSize
        || candidate.PayloadOffset < 0
        || candidate.PayloadOffset + userDataSectorSize > candidate.SourceSectorSize
        || candidate.ByteOffset < candidate.PayloadOffset
        || destination.Length % userDataSectorSize != 0)
    {
        return false;
    }

    var firstSectorOffset = candidate.ByteOffset - candidate.PayloadOffset;
    using var stream = File.OpenRead(candidate.FilePath);
    var sectorCount = destination.Length / userDataSectorSize;
    for (var sectorIndex = 0; sectorIndex < sectorCount; sectorIndex++)
    {
        var payloadOffset = firstSectorOffset
            + ((long)sectorIndex * candidate.SourceSectorSize)
            + candidate.PayloadOffset;
        if (payloadOffset < 0 || payloadOffset + userDataSectorSize > stream.Length)
        {
            return false;
        }

        stream.Position = payloadOffset;
        var bytesRead = stream.ReadAtLeast(
            destination.Slice(sectorIndex * userDataSectorSize, userDataSectorSize),
            userDataSectorSize,
            throwOnEndOfStream: false);
        if (bytesRead < userDataSectorSize)
        {
            return false;
        }
    }

    return true;
}

static bool HasIpBinExecutableBootstrap(byte[]? ipBin)
{
    const int licenseCodeOffset = 0x300;
    if (ipBin is null || ipBin.Length < licenseCodeOffset + 32)
    {
        return false;
    }

    return ipBin.AsSpan(licenseCodeOffset, 32).IndexOfAnyExcept((byte)0x00, (byte)0xFF) >= 0;
}

static (int ScanSectors, string Layout, string[] RunArgs) ParseBootSmokeOptions(string[] args)
{
    var scanSectors = 1024;
    var layout = "auto";
    var runArgs = new List<string>();
    for (var index = 0; index < args.Length; index++)
    {
        switch (args[index])
        {
            case "--scan-sectors" when index + 1 < args.Length && int.TryParse(args[index + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedScanSectors):
                scanSectors = parsedScanSectors;
                index++;
                break;
            case "--layout" when index + 1 < args.Length:
                layout = args[index + 1].ToLowerInvariant();
                if (layout is not ("auto" or "original" or "descrambled"))
                {
                    throw new InvalidDataException("--layout must be auto, original, or descrambled.");
                }

                index++;
                break;
            default:
                runArgs.Add(args[index]);
                break;
        }
    }

    if (scanSectors < 0)
    {
        throw new InvalidDataException("--scan-sectors must be zero or greater.");
    }

    return (scanSectors, layout, runArgs.ToArray());
}

static string ResolveBootLayout(string requestedLayout, DreamcastBootBinaryAnalysis analysis)
{
    if (requestedLayout is "original" or "descrambled")
    {
        return requestedLayout;
    }

    return analysis.RecommendedLayout == "descrambled" ? "descrambled" : "original";
}

static void RunElf(string path, string[] args)
{
    using var stream = File.OpenRead(path);
    var elf = ElfFile.Read(stream);
    var options = ParseRunOptions(args);
    var result = new DreamcastRunner().Run(elf, options.Emulation);

    if (options.FramebufferDumpPath is not null)
    {
        DumpFramebuffer(result, options);
    }

    if (options.AudioWavPath is not null)
    {
        DumpAudioWav(result, options.AudioWavPath);
    }

    if (options.TraceLogPath is not null)
    {
        DumpTraceLog(result, options.TraceLogPath);
    }

    if (options.DeviceLogPath is not null)
    {
        DumpDeviceLog(result, options);
    }

    if (options.MemoryWriteLogPath is not null)
    {
        DumpMemoryWriteLog(result, options.MemoryWriteLogPath);
    }

    if (options.EmitJson)
    {
        WriteJsonRunSummary(result, options.Emulation);
        return;
    }

    Console.WriteLine($"Path: {Path.GetFullPath(path)}");
    Console.WriteLine("Loaded: yes");
    Console.WriteLine($"Entry: 0x{result.Load.EntryPoint:X8} -> physical 0x{result.Load.TranslatedEntryPoint:X8}");
    Console.WriteLine($"Segments: {result.Load.LoadedSegments.Count}");
    Console.WriteLine($"File bytes: {result.Load.LoadedBytes}");
    Console.WriteLine($"Reserved bytes: {result.Load.ReservedBytes}");

    foreach (var segment in result.Load.LoadedSegments)
    {
        Console.WriteLine(
            $"  PH{segment.Index}: vaddr=0x{segment.VirtualAddress:X8}, physical=0x{segment.PhysicalAddress:X8}, file={segment.FileSize}, mem={segment.MemorySize}, flags=0x{segment.Flags:X}");
    }

    Console.WriteLine($"Instructions: {result.Cpu.InstructionsExecuted}");
    Console.WriteLine($"PC: 0x{result.Cpu.Pc:X8}");
    Console.WriteLine($"PR: 0x{result.Cpu.Pr:X8}");
    Console.WriteLine($"SR: 0x{result.Cpu.Sr:X8}");
    Console.WriteLine($"GBR: 0x{result.Cpu.Gbr:X8}");
    Console.WriteLine($"VBR: 0x{result.Cpu.Vbr:X8}");
    Console.WriteLine($"SPC: 0x{result.Cpu.Spc:X8}");
    Console.WriteLine($"SSR: 0x{result.Cpu.Ssr:X8}");
    Console.WriteLine($"FPSCR: 0x{result.Cpu.Fpscr:X8}");
    Console.WriteLine($"Events: TRA=0x{result.Cpu.Tra:X8}, EXPEVT=0x{result.Cpu.Expevt:X8}, INTEVT=0x{result.Cpu.Intevt:X8}");
    Console.WriteLine($"Stopped: {result.StopReason}");
    Console.WriteLine($"Detail: {result.StopDetail}");
    if (result.StopPc is { } stopPc)
    {
        var symbol = DreamcastSymbolSummary.FromSymbol(result.Load.FindNearestSymbol(stopPc), stopPc);
        if (symbol is not null)
        {
            Console.WriteLine($"Stop symbol: {symbol.Display} ({symbol.AddressHex})");
        }
    }

    var controllerInstruction = EffectiveControllerInstruction(result);
    Console.WriteLine($"Controller A: {FormatController(EffectiveControllerA(options.Emulation, controllerInstruction))}");
    if (EffectiveController(options.Emulation, 0x40, controllerInstruction) is { } controllerB)
    {
        Console.WriteLine($"Controller B: {FormatController(controllerB)}");
    }

    Console.WriteLine($"Video VRAM: nonzero={result.Video.NonZeroBytes}, checksum={result.Video.Fnv1A32Hex}, first={result.Video.FirstNonZeroOffsetHex ?? "none"}");
    Console.WriteLine($"PVR: registers={result.Video.PvrRegisterAccesses.Count}, taWrites={result.Video.PvrTaCommandWrites.Count}");
    var videoSummary = DreamcastVideoSummary.FromSnapshot(result.Video);
    var pvrTaLists = videoSummary.PvrTaLists;
    if (pvrTaLists.Count > 0)
    {
        Console.WriteLine($"PVR TA lists: {FormatPvrTaLists(pvrTaLists)}");
    }

    var pvrTaStrips = videoSummary.PvrTaStrips;
    if (pvrTaStrips.Count > 0)
    {
        Console.WriteLine($"PVR TA strips: {FormatPvrTaStrips(pvrTaStrips)}");
    }

    var pvrTaSprites = videoSummary.PvrTaSprites;
    if (pvrTaSprites.Count > 0)
    {
        Console.WriteLine($"PVR TA sprites: {FormatPvrTaSprites(pvrTaSprites)}");
    }

    if (videoSummary.RecentPvrTaParameterHeaders.Count > 0)
    {
        Console.WriteLine($"PVR TA params: {FormatPvrTaParameterHeaders(videoSummary.RecentPvrTaParameterHeaders)}");
    }

    if (videoSummary.RecentPvrTaStreamWrites.Count > 0)
    {
        Console.WriteLine($"PVR TA stream: {FormatPvrTaStreamWrites(videoSummary.RecentPvrTaStreamWrites)}");
    }

    if (videoSummary.PvrTaPolygonHeaderPayloads.Count > 0)
    {
        Console.WriteLine($"PVR TA polygon payloads: {FormatPvrTaPolygonHeaderPayloads(videoSummary.PvrTaPolygonHeaderPayloads)}");
    }

    if (videoSummary.PvrTaRealVertexPayloads.Count > 0)
    {
        Console.WriteLine($"PVR TA real vertices: {FormatPvrTaRealVertexPayloads(videoSummary.PvrTaRealVertexPayloads)}");
    }

    var currentPvrRegisters = result.Video.PvrRegisters.Where(register => register.Value != 0).Take(8).ToArray();
    if (currentPvrRegisters.Length > 0)
    {
        Console.WriteLine($"PVR current: {string.Join(", ", currentPvrRegisters.Select(register => $"{register.Name}={register.ValueHex}"))}");
    }

    Console.WriteLine($"AICA: registers={result.Audio.RegisterAccesses.Count}, channels={result.Audio.Channels.Count}, active={result.Audio.Channels.Count(channel => channel.Active)}, ramNonZero={result.Audio.NonZeroBytes}");
    var currentAicaRegisters = result.Audio.Registers.Where(register => register.Value != 0).Take(8).ToArray();
    if (currentAicaRegisters.Length > 0)
    {
        Console.WriteLine($"AICA current: {string.Join(", ", currentAicaRegisters.Select(register => $"{register.Name}={register.ValueHex}"))}");
    }

    foreach (var channel in result.Audio.Channels.Take(8))
    {
        Console.WriteLine($"  AICA channel {channel.Channel}: active={channel.Active}, format={channel.SampleFormat}, compressed={channel.Compressed}, streamed={channel.Streamed}, stride={channel.SampleStrideBytes}, sample={channel.SampleAddressHex}, loop={channel.LoopStartHex}-{channel.LoopEndHex}, pan={channel.Pan} send={channel.PanSendLevel} pos={channel.PanPosition} balance={channel.LeftBalance}/{channel.RightBalance}, volume={channel.Volume}, position={channel.PlaybackPosition}, bytePosition={channel.PlaybackBytePosition}, advanced={channel.PlaybackSamplesAdvanced}, bytesAdvanced={channel.PlaybackBytesAdvanced}, stoppedAtLoopEnd={channel.PlaybackStoppedAtLoopEnd}");
    }

    Console.WriteLine($"Maple: transfers={result.Maple.Transfers.Count}, deviceInfo={result.Maple.Transfers.Count(transfer => transfer.CommandName == "DeviceInfo")}, getCondition={result.Maple.Transfers.Count(transfer => transfer.CommandName == "GetCondition")}, dmaBatches={result.Maple.DmaBatches.Count}, descriptorLimitHits={result.Maple.DmaBatches.Count(batch => batch.HitDescriptorLimit)}");
    var gdrom = result.Gdrom ?? DreamcastGdromSnapshot.Empty;
    Console.WriteLine($"GD-ROM: media={gdrom.HasMedia}, sectorSize={gdrom.SectorSize?.ToString(CultureInfo.InvariantCulture) ?? "none"}, sectors={gdrom.SectorCount?.ToString(CultureInfo.InvariantCulture) ?? "none"}, leadout={gdrom.LeadoutFadHex ?? "none"}, tracks={gdrom.MediaTracks.Count}, reads={gdrom.ReadCommands.Count}, ok={gdrom.ReadCommands.Count(command => command.Success)}, failed={gdrom.ReadCommands.Count(command => !command.Success)}, bytes={gdrom.ReadCommands.Sum(command => command.BytesRead)}, tocs={gdrom.TocCommands.Count}");
    Console.WriteLine($"Scheduler: vblanks={result.Scheduler.VBlankEventsRaised}, nextVBlank={result.Scheduler.NextVBlankInstruction}, hardwareTicks={result.Scheduler.HardwareAdvanceTicks}, hardwareBatches={result.Scheduler.HardwareAdvanceBatches}, maxHardwareBatch={result.Scheduler.MaxHardwareAdvanceBatch}, idleTicks={result.Scheduler.IdleAdvanceTicks}, idleBatches={result.Scheduler.IdleAdvanceBatches}, maxIdleBatch={result.Scheduler.MaxIdleAdvanceBatch}, idleWakes=timer:{result.Scheduler.IdleTimerWakeCount}/vblank:{result.Scheduler.IdleVBlankWakeCount}/input:{result.Scheduler.IdleInputWakeCount}, cpuFastForward={result.Scheduler.CpuFastForwardInstructions}, cpuFastForwardBatches={result.Scheduler.CpuFastForwardBatches}, maxCpuFastForward={result.Scheduler.MaxCpuFastForwardBatch}, inputChanges={result.Scheduler.ControllerScriptChanges}");
    var timerSummary = DreamcastTimerSummary.FromSnapshot(result.Timer ?? DreamcastTimerSnapshot.Empty);
    var timerSource = timerSummary.PendingInterrupt is { } pendingTimer
        ? $", channel={pendingTimer.Channel}, priority={pendingTimer.Priority}"
        : string.Empty;
    Console.WriteLine($"TMU: pending={timerSummary.PendingEventCodeHex ?? "none"}{timerSource}");
    var asicSource = result.Asic.PendingInterrupt is { } pendingAsic
        ? $", source={pendingAsic.LevelName}:{pendingAsic.RegisterName}{pendingAsic.Bit}"
        : string.Empty;
    Console.WriteLine($"ASIC: pending={result.Asic.PendingEventCodeHex ?? "none"}, level={result.Asic.PendingLevel?.ToString(CultureInfo.InvariantCulture) ?? "none"}{asicSource}");
    Console.WriteLine($"Device accesses: {result.DeviceAccesses.Count}");
    Console.WriteLine($"Watched memory writes: {result.WatchedMemoryWrites.Count}");
    Console.WriteLine($"Serial bytes: {result.SerialOutput.Count}");

    if (result.SerialOutput.Count > 0)
    {
        Console.WriteLine("Serial output:");
        Console.WriteLine(Encoding.ASCII.GetString(result.SerialOutput.ToArray()));
    }

    foreach (var access in result.DeviceAccesses.TakeLast(8))
    {
        Console.WriteLine($"  {access.Kind}: addr=0x{access.Address:X8}, size={access.Size}, value=0x{access.Value:X8}");
    }

    foreach (var sample in result.Video.Samples.Where(sample => sample.Rgb565 != 0).Take(8))
    {
        Console.WriteLine($"  Video sample {sample.Name}: offset={sample.OffsetHex}, rgb565={sample.Rgb565Hex}");
    }

    foreach (var access in result.Video.PvrRegisterAccesses.TakeLast(8))
    {
        Console.WriteLine($"  PVR {access.Kind} {access.Name}: addr={access.AddressHex}, value={access.ValueHex}");
    }

    foreach (var write in result.Video.PvrTaCommandWrites.TakeLast(8))
    {
        var listText = write.ListTypeName is null ? string.Empty : $", list={write.ListTypeName}";
        Console.WriteLine($"  PVR TA {write.Kind}: region={write.Region}, addr={write.AddressHex}, size={write.Size}, value={write.ValueHex}{listText}");
    }

    foreach (var access in result.Audio.RegisterAccesses.TakeLast(8))
    {
        var channel = access.Channel is { } index ? $", channel={index}" : string.Empty;
        Console.WriteLine($"  AICA {access.Kind} {access.Name}: addr={access.AddressHex}{channel}, value={access.ValueHex}");
    }

    foreach (var transfer in result.Maple.Transfers.TakeLast(8))
    {
        var state = transfer.ControllerState is { } controller ? $", state={FormatController(controller)}" : string.Empty;
        Console.WriteLine($"  Maple {transfer.CommandName}: dest={transfer.DestinationName} ({transfer.DestinationHex}), recv={transfer.ReceiveBufferAddressHex}, response={transfer.ResponseName}, bytes={transfer.ResponseBytes}{state}");
    }

    foreach (var read in gdrom.ReadCommands.TakeLast(8))
    {
        Console.WriteLine($"  GD-ROM read: sector={read.SectorHex ?? "none"}, count={read.SectorCount?.ToString(CultureInfo.InvariantCulture) ?? "none"}, dest={read.DestinationHex ?? "none"}, bytes={read.BytesRead}/{read.BytesRequested}, ok={read.Success}, status={read.Status}");
    }

    foreach (var track in gdrom.MediaTracks)
    {
        Console.WriteLine($"  GD-ROM track: number={track.TrackNumber}, start={track.StartFadHex}, control={track.Control}, sectors={track.SectorCount}");
    }

    foreach (var toc in gdrom.TocCommands.TakeLast(4))
    {
        Console.WriteLine($"  GD-ROM TOC: buffer={toc.BufferAddressHex ?? "none"}, first={toc.FirstTrack?.ToString(CultureInfo.InvariantCulture) ?? "none"}, last={toc.LastTrack?.ToString(CultureInfo.InvariantCulture) ?? "none"}, data={toc.DataTrackStartFadHex ?? "none"}, leadout={toc.LeadoutFadHex ?? "none"}, ok={toc.Success}, status={toc.Status}");
    }

    foreach (var batch in result.Maple.DmaBatches.TakeLast(4))
    {
        Console.WriteLine($"  Maple DMA: start={batch.DescriptorAddressHex}, scanned={batch.DescriptorsScanned}, transfers={batch.TransferCount}, completed={batch.Completed}, descriptorLimit={batch.HitDescriptorLimit}, last={batch.LastDescriptorAddressHex}");
    }

    if (result.TraceTail.Count > 0)
    {
        Console.WriteLine("Trace tail:");
        foreach (var step in result.TraceTail)
        {
            var symbol = DreamcastSymbolSummary.FromSymbol(result.Load.FindNearestSymbol(step.Pc), step.Pc);
            var symbolText = symbol is null ? string.Empty : $" ; {symbol.Display}";
            Console.WriteLine($"  0x{step.Pc:X8}: 0x{step.Opcode:X4}  {step.Trace}{symbolText}");
        }
    }
}

static void WriteJsonRunSummary(DreamcastRunResult result, DreamcastRunOptions options)
{
    var summary = DreamcastRunSummary.FromResult(result, options);
    Console.WriteLine(SerializeJson(summary));
}

static int RunFixtures(string manifestPath, string[] args)
{
    var emitJson = false;
    var validateOnly = false;
    string? artifactDirectoryOverride = null;
    string? reportJsonPath = null;
    string? fixtureFilter = null;
    for (var index = 0; index < args.Length; index++)
    {
        switch (args[index])
        {
            case "--json":
            case "--summary-json":
                emitJson = true;
                break;
            case "--validate-only":
                validateOnly = true;
                break;
            case "--artifacts" when index + 1 < args.Length:
                artifactDirectoryOverride = args[index + 1];
                index++;
                break;
            case "--report-json" when index + 1 < args.Length:
                reportJsonPath = args[index + 1];
                index++;
                break;
            case "--filter" when index + 1 < args.Length:
                fixtureFilter = args[index + 1];
                index++;
                break;
            default:
                throw new InvalidDataException($"Unknown or invalid fixtures option: {args[index]}");
        }
    }

    var repoRoot = FindRepoRoot(Path.GetFullPath(manifestPath)) ?? Directory.GetCurrentDirectory();
    using var stream = File.OpenRead(manifestPath);
    var manifest = DreamcastFixtureManifest.Read(stream);
    var fixtures = CliFixtureSelection.FilterFixtures(manifest.Fixtures, fixtureFilter);
    var artifactDirectory = ResolveRepoPath(repoRoot, artifactDirectoryOverride ?? manifest.ArtifactDirectory);
    if (validateOnly)
    {
        var validationReport = CliFixtureSelection.CreateValidationReport(
            Path.GetFullPath(manifestPath),
            artifactDirectory,
            fixtures);
        var validationJson = SerializeJson(validationReport);
        if (reportJsonPath is not null)
        {
            WriteTextFile(reportJsonPath, validationJson);
        }

        if (emitJson)
        {
            Console.WriteLine(validationJson);
        }
        else
        {
            Console.WriteLine($"Manifest OK: {validationReport.FixtureCount} fixtures");
            Console.WriteLine($"Artifacts: {validationReport.ArtifactDirectory}");
            if (reportJsonPath is not null)
            {
                Console.WriteLine($"Report JSON: {Path.GetFullPath(reportJsonPath)}");
            }
        }

        return 0;
    }

    var results = new List<DreamcastFixtureCheckResult>();

    foreach (var fixture in fixtures)
    {
        var artifactPath = Path.Combine(artifactDirectory, fixture.Artifact);
        if (!File.Exists(artifactPath))
        {
            results.Add(new DreamcastFixtureCheckResult(
                fixture.Name,
                artifactPath,
                Summary: null,
                Failures: [$"missing artifact: {artifactPath}"]));
            continue;
        }

        results.Add(DreamcastFixtureRunner.Run(fixture, artifactPath, repoRoot));
    }

    var reports = results.Select(FixtureReport.FromResult).ToArray();
    var reportJson = SerializeJson(reports);
    if (reportJsonPath is not null)
    {
        WriteTextFile(reportJsonPath, reportJson);
    }

    if (emitJson)
    {
        Console.WriteLine(reportJson);
    }
    else
    {
        foreach (var result in results)
        {
            Console.WriteLine($"{(result.Passed ? "PASS" : "FAIL")} {result.Name}");
            if (result.Summary is not null)
            {
                var scheduler = result.Summary.Scheduler;
                Console.WriteLine($"  stop={result.Summary.StopReason}, instructions={result.Summary.InstructionsExecuted}, serial={result.Summary.SerialBytes}, videoNonZero={result.Summary.Video.NonZeroBytes}, pvrRegs={result.Summary.Video.PvrRegisterAccessCount}, taWrites={result.Summary.Video.PvrTaCommandWriteCount}, taStrips={result.Summary.Video.PvrTaStrips.Count}, taSprites={result.Summary.Video.PvrTaSprites.Count}, aicaRegs={result.Summary.Audio.RegisterAccessCount}, mapleTransfers={result.Summary.Maple.TransferCount}, mapleDmaBatches={result.Summary.Maple.DmaBatchCount}, mapleDescriptorLimitHits={result.Summary.Maple.DescriptorLimitHitCount}, gdromStatuses={result.Summary.Gdrom.StatusCommandCount}, gdromSectorModes={result.Summary.Gdrom.SectorModeCommandCount}, gdromTocs={result.Summary.Gdrom.TocCommandCount}, gdromReads={result.Summary.Gdrom.ReadCommandCount}, gdromBytes={result.Summary.Gdrom.BytesRead}, timerPending={result.Summary.Timer.PendingEventCodeHex ?? "none"}, asicPending={result.Summary.Asic.PendingEventCodeHex ?? "none"}, vblanks={scheduler.VBlankEventsRaised}, schedulerTicks={scheduler.HardwareAdvanceTicks}, schedulerBatches={scheduler.HardwareAdvanceBatches}, maxSchedulerBatch={scheduler.MaxHardwareAdvanceBatch}, idleTicks={scheduler.IdleAdvanceTicks}, idleBatches={scheduler.IdleAdvanceBatches}, maxIdleBatch={scheduler.MaxIdleAdvanceBatch}, idleWakes=timer:{scheduler.IdleTimerWakeCount}/vblank:{scheduler.IdleVBlankWakeCount}/input:{scheduler.IdleInputWakeCount}, cpuFastForward={scheduler.CpuFastForwardInstructions}, cpuFastForwardBatches={scheduler.CpuFastForwardBatches}, maxCpuFastForward={scheduler.MaxCpuFastForwardBatch}, inputChanges={scheduler.ControllerScriptChanges}");
                if (result.Summary.Video.PvrTaLists.Count > 0)
                {
                    Console.WriteLine($"  pvrTaLists={FormatPvrTaLists(result.Summary.Video.PvrTaLists)}");
                }

                if (result.Summary.Video.PvrTaStrips.Count > 0)
                {
                    Console.WriteLine($"  pvrTaStrips={FormatPvrTaStrips(result.Summary.Video.PvrTaStrips)}");
                }

                if (result.Summary.Video.PvrTaSprites.Count > 0)
                {
                    Console.WriteLine($"  pvrTaSprites={FormatPvrTaSprites(result.Summary.Video.PvrTaSprites)}");
                }

                if (result.Summary.Video.RecentPvrTaParameterHeaders.Count > 0)
                {
                    Console.WriteLine($"  recentPvrTaParams={FormatPvrTaParameterHeaders(result.Summary.Video.RecentPvrTaParameterHeaders)}");
                }

                if (result.Summary.Video.RecentPvrTaStreamWrites.Count > 0)
                {
                    Console.WriteLine($"  recentPvrTaStream={FormatPvrTaStreamWrites(result.Summary.Video.RecentPvrTaStreamWrites)}");
                }

                if (result.Summary.Video.PvrTaPolygonHeaderPayloads.Count > 0)
                {
                    Console.WriteLine($"  pvrTaPolygonPayloads={FormatPvrTaPolygonHeaderPayloads(result.Summary.Video.PvrTaPolygonHeaderPayloads)}");
                }

                if (result.Summary.Video.PvrTaRealVertexPayloads.Count > 0)
                {
                    Console.WriteLine($"  pvrTaRealVertices={FormatPvrTaRealVertexPayloads(result.Summary.Video.PvrTaRealVertexPayloads)}");
                }

                if (result.Summary.Gdrom.RecentReadCommands.Count > 0)
                {
                    Console.WriteLine($"  gdromReads={FormatGdromReads(result.Summary.Gdrom.RecentReadCommands)}");
                }

                if (result.Summary.Gdrom.RecentTocCommands.Count > 0)
                {
                    Console.WriteLine($"  gdromTocs={FormatGdromTocs(result.Summary.Gdrom.RecentTocCommands)}");
                }

                if (result.Summary.Gdrom.RecentStatusCommands.Count > 0)
                {
                    Console.WriteLine($"  gdromStatuses={FormatGdromStatuses(result.Summary.Gdrom.RecentStatusCommands)}");
                }

                if (result.Summary.Gdrom.RecentSectorModeCommands.Count > 0)
                {
                    Console.WriteLine($"  gdromSectorModes={FormatGdromSectorModes(result.Summary.Gdrom.RecentSectorModeCommands)}");
                }

                if (result.Summary.Gdrom.MediaTracks.Count > 0)
                {
                    Console.WriteLine($"  gdromTracks={FormatGdromTracks(result.Summary.Gdrom.MediaTracks)}");
                }
            }

            foreach (var failure in result.Failures)
            {
                Console.WriteLine($"  {failure}");
            }
        }

        Console.WriteLine($"Fixtures: {results.Count(result => result.Passed)}/{results.Count} passed");
        if (reportJsonPath is not null)
        {
            Console.WriteLine($"Report JSON: {Path.GetFullPath(reportJsonPath)}");
        }
    }

    return results.All(result => result.Passed) ? 0 : 1;
}

static string SerializeJson<T>(T value)
{
    var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };
    jsonOptions.Converters.Add(new JsonStringEnumConverter());

    return JsonSerializer.Serialize(value, jsonOptions);
}

static void DumpFramebuffer(DreamcastRunResult result, CliRunOptions options)
{
    var path = Path.GetFullPath(options.FramebufferDumpPath!);
    var directory = Path.GetDirectoryName(path);
    if (!string.IsNullOrEmpty(directory))
    {
        Directory.CreateDirectory(directory);
    }

    using var stream = File.Create(path);
    DreamcastFramebufferPngWriter.WriteRgb565Png(stream, result.Video.Vram, options.FramebufferWidth, options.FramebufferHeight);
}

static void DumpAudioWav(DreamcastRunResult result, string path)
{
    var fullPath = Path.GetFullPath(path);
    var directory = Path.GetDirectoryName(fullPath);
    if (!string.IsNullOrEmpty(directory))
    {
        Directory.CreateDirectory(directory);
    }

    using var stream = File.Create(fullPath);
    DreamcastAudioWavWriter.WritePcm16Stereo(stream, result.Audio);
}

static void DumpTraceLog(DreamcastRunResult result, string path)
{
    using var writer = CreateTextLog(path);
    foreach (var step in result.TraceLog)
    {
        var symbol = DreamcastSymbolSummary.FromSymbol(result.Load.FindNearestSymbol(step.Pc), step.Pc);
        var symbolText = symbol is null ? string.Empty : $" ; {symbol.Display}";
        writer.WriteLine($"0x{step.Pc:X8}: 0x{step.Opcode:X4}  {step.Trace}{symbolText}");
    }
}

static void DumpDeviceLog(DreamcastRunResult result, CliRunOptions options)
{
    using var writer = CreateTextLog(options.DeviceLogPath!);
    var accesses = result.DeviceAccesses.AsEnumerable();
    if (options.DeviceKind is { } kind)
    {
        accesses = accesses.Where(access => access.Kind == kind);
    }

    if (options.DeviceAddressRange is { } range)
    {
        accesses = accesses.Where(access => range.Contains(access.Address));
    }

    if (options.DeviceDomain is { } domain)
    {
        accesses = accesses.Where(access => string.Equals(DreamcastDeviceDomainClassifier.Classify(access), domain, StringComparison.OrdinalIgnoreCase));
    }

    foreach (var access in accesses)
    {
        writer.WriteLine($"{access.Kind}: domain={DreamcastDeviceDomainClassifier.Classify(access)}, addr=0x{access.Address:X8}, size={access.Size}, value=0x{access.Value:X8}");
    }
}

static void DumpMemoryWriteLog(DreamcastRunResult result, string path)
{
    using var writer = CreateTextLog(path);
    foreach (var access in result.WatchedMemoryWrites)
    {
        writer.WriteLine($"{access.Kind}: addr=0x{access.Address:X8}, size={access.Size}, value=0x{access.Value:X8}");
    }
}

static StreamWriter CreateTextLog(string path)
{
    var fullPath = Path.GetFullPath(path);
    var directory = Path.GetDirectoryName(fullPath);
    if (!string.IsNullOrEmpty(directory))
    {
        Directory.CreateDirectory(directory);
    }

    return new StreamWriter(File.Create(fullPath), Encoding.UTF8);
}

static void WriteTextFile(string path, string content)
{
    using var writer = CreateTextLog(path);
    writer.Write(content);
}

static CliRunOptions ParseRunOptions(string[] args)
{
    ulong instructionLimit = 1_000;
    var traceTail = 16;
    ulong vblankInterval = 200_000;
    var emitJson = false;
    var controllerA = DreamcastControllerState.Neutral;
    DreamcastControllerState? controllerB = null;
    var controllers = new Dictionary<byte, DreamcastControllerState>();
    DreamcastControllerScript? controllerAScript = null;
    var controllerScripts = new Dictionary<byte, DreamcastControllerScript>();
    string? framebufferDumpPath = null;
    var framebufferWidth = 320;
    var framebufferHeight = 240;
    string? audioWavPath = null;
    string? traceLogPath = null;
    uint? traceStartPc = null;
    uint? traceEndPc = null;
    var traceLogLimit = 4096;
    string? deviceLogPath = null;
    MemoryAccessKind? deviceKind = null;
    AddressRange? deviceAddressRange = null;
    string? deviceDomain = null;
    string? memoryWriteLogPath = null;
    AddressRange? memoryWriteAddressRange = null;
    var memoryWriteLimit = 4096;
    string? mediaPath = null;
    var stopOnUnmapped = false;
    string? stopOnDeviceDomain = null;
    var initialStackPointer = 0x8D00_0000u;
    var initialStatusRegister = 0u;

    for (var index = 0; index < args.Length; index++)
    {
        switch (args[index])
        {
            case "--json":
            case "--summary-json":
                emitJson = true;
                break;
            case "--instructions" when index + 1 < args.Length && ulong.TryParse(args[index + 1], out var parsedLimit):
                instructionLimit = parsedLimit;
                index++;
                break;
            case "--trace-tail" when index + 1 < args.Length && int.TryParse(args[index + 1], out var parsedTraceTail):
                traceTail = parsedTraceTail;
                index++;
                break;
            case "--vblank-interval" when index + 1 < args.Length && ulong.TryParse(args[index + 1], out var parsedVblankInterval):
                vblankInterval = parsedVblankInterval;
                index++;
                break;
            case "--controller-a" when index + 1 < args.Length:
                controllerA = DreamcastControllerStateParser.ParseState(args[index + 1]);
                index++;
                break;
            case "--controller-b" when index + 1 < args.Length:
                controllerB = DreamcastControllerStateParser.ParseState(args[index + 1]);
                index++;
                break;
            case "--controller" when index + 1 < args.Length:
                var (address, state) = DreamcastControllerStateParser.ParseMapEntry(args[index + 1]);
                controllers[address] = state;
                index++;
                break;
            case "--controller-a-script" when index + 1 < args.Length:
                controllerAScript = DreamcastControllerStateParser.ParseScript(args[index + 1]);
                index++;
                break;
            case "--controller-script" when index + 1 < args.Length:
                var (scriptAddress, script) = DreamcastControllerStateParser.ParseScriptMapEntry(args[index + 1]);
                controllerScripts[scriptAddress] = script;
                index++;
                break;
            case "--dump-framebuffer" when index + 1 < args.Length:
                framebufferDumpPath = args[index + 1];
                index++;
                break;
            case "--framebuffer-size" when index + 1 < args.Length:
                (framebufferWidth, framebufferHeight) = ParseFramebufferSize(args[index + 1]);
                index++;
                break;
            case "--pixel-format" when index + 1 < args.Length && string.Equals(args[index + 1], "rgb565", StringComparison.OrdinalIgnoreCase):
                index++;
                break;
            case "--audio-wav" when index + 1 < args.Length:
                audioWavPath = args[index + 1];
                index++;
                break;
            case "--trace-log" when index + 1 < args.Length:
                traceLogPath = args[index + 1];
                index++;
                break;
            case "--trace-pc" when index + 1 < args.Length:
                (traceStartPc, traceEndPc) = ParseAddressRange(args[index + 1]);
                index++;
                break;
            case "--trace-log-limit" when index + 1 < args.Length && int.TryParse(args[index + 1], out var parsedTraceLogLimit):
                traceLogLimit = parsedTraceLogLimit;
                index++;
                break;
            case "--device-log" when index + 1 < args.Length:
                deviceLogPath = args[index + 1];
                index++;
                break;
            case "--device-kind" when index + 1 < args.Length && Enum.TryParse<MemoryAccessKind>(args[index + 1], ignoreCase: true, out var parsedDeviceKind):
                deviceKind = parsedDeviceKind;
                index++;
                break;
            case "--device-address" when index + 1 < args.Length:
                var (start, end) = ParseAddressRange(args[index + 1]);
                deviceAddressRange = new AddressRange(start ?? 0, end ?? start ?? uint.MaxValue);
                index++;
                break;
            case "--device-domain" when index + 1 < args.Length:
                deviceDomain = ParseDeviceDomain(args[index + 1]);
                index++;
                break;
            case "--memory-write-log" when index + 1 < args.Length:
                memoryWriteLogPath = args[index + 1];
                index++;
                break;
            case "--memory-write-address" when index + 1 < args.Length:
                var (memoryWriteStart, memoryWriteEnd) = ParseAddressRange(args[index + 1]);
                memoryWriteAddressRange = new AddressRange(memoryWriteStart ?? 0, memoryWriteEnd ?? memoryWriteStart ?? uint.MaxValue);
                index++;
                break;
            case "--memory-write-limit" when index + 1 < args.Length && int.TryParse(args[index + 1], out var parsedMemoryWriteLimit):
                memoryWriteLimit = parsedMemoryWriteLimit;
                index++;
                break;
            case "--media" when index + 1 < args.Length:
                mediaPath = args[index + 1];
                index++;
                break;
            case "--stop-on-unmapped":
                stopOnUnmapped = true;
                break;
            case "--stop-on-device-domain" when index + 1 < args.Length:
                stopOnDeviceDomain = ParseDeviceDomain(args[index + 1]);
                index++;
                break;
            case "--initial-sp" when index + 1 < args.Length:
                initialStackPointer = ParseAddress(args[index + 1]);
                index++;
                break;
            case "--initial-sr" when index + 1 < args.Length:
                initialStatusRegister = ParseAddress(args[index + 1]);
                index++;
                break;
            default:
                throw new InvalidDataException($"Unknown or invalid run option: {args[index]}");
        }
    }

    if (instructionLimit == 0)
    {
        throw new InvalidDataException("--instructions must be greater than zero.");
    }

    if (traceTail < 0)
    {
        throw new InvalidDataException("--trace-tail must be zero or greater.");
    }

    if (traceLogLimit < 0)
    {
        throw new InvalidDataException("--trace-log-limit must be zero or greater.");
    }

    if (memoryWriteLimit < 0)
    {
        throw new InvalidDataException("--memory-write-limit must be zero or greater.");
    }

    var traceCapture = traceLogPath is null
        ? null
        : new DreamcastTraceCaptureOptions(traceStartPc, traceEndPc, traceLogLimit);
    var media = mediaPath is null
        ? null
        : DreamcastMediaImageLoader.LoadFromFile(mediaPath);
    var memoryWriteWatch = memoryWriteLogPath is null
        ? null
        : new DreamcastMemoryWriteWatch(
            memoryWriteAddressRange?.Start ?? 0,
            memoryWriteAddressRange?.End ?? uint.MaxValue,
            memoryWriteLimit);

    return new CliRunOptions(
        new DreamcastRunOptions(
            instructionLimit,
            traceTail,
            vblankInterval,
            controllerA,
            controllerAScript,
            traceCapture,
            controllerB,
            controllers.Count == 0 ? null : controllers,
            controllerScripts.Count == 0 ? null : controllerScripts,
            media,
            stopOnUnmapped,
            stopOnDeviceDomain,
            initialStackPointer,
            initialStatusRegister,
            MemoryWriteWatch: memoryWriteWatch),
        emitJson,
        framebufferDumpPath,
        framebufferWidth,
        framebufferHeight,
        audioWavPath,
        traceLogPath,
        deviceLogPath,
        deviceKind,
        deviceAddressRange,
        deviceDomain,
        memoryWriteLogPath);
}

static (int Width, int Height) ParseFramebufferSize(string text)
{
    var parts = text.Split('x', 2, StringSplitOptions.TrimEntries);
    if (parts.Length != 2 || !int.TryParse(parts[0], out var width) || !int.TryParse(parts[1], out var height) || width <= 0 || height <= 0)
    {
        throw new InvalidDataException("--framebuffer-size must use WIDTHxHEIGHT, for example 320x240.");
    }

    return (width, height);
}

static (uint? Start, uint? End) ParseAddressRange(string text)
{
    var separator = text.IndexOf('-');
    if (separator < 0)
    {
        var address = ParseAddress(text);
        return (address, address);
    }

    var start = string.IsNullOrWhiteSpace(text[..separator]) ? (uint?)null : ParseAddress(text[..separator]);
    var end = string.IsNullOrWhiteSpace(text[(separator + 1)..]) ? (uint?)null : ParseAddress(text[(separator + 1)..]);
    if (start is { } startValue && end is { } endValue && endValue < startValue)
    {
        throw new InvalidDataException("Address ranges must be ordered from low to high.");
    }

    return (start, end);
}

static uint ParseAddress(string text)
{
    var value = text.Trim();
    if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
    {
        value = value[2..];
        if (uint.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var parsedHex))
        {
            return parsedHex;
        }
    }
    else if (uint.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedDecimal))
    {
        return parsedDecimal;
    }

    throw new InvalidDataException($"Invalid address: {text}");
}

static string ParseDeviceDomain(string text)
{
    var normalized = text.Trim().ToLowerInvariant();
    return normalized switch
    {
        DreamcastDeviceDomainClassifier.Aica
            or DreamcastDeviceDomainClassifier.Asic
            or DreamcastDeviceDomainClassifier.Holly
            or DreamcastDeviceDomainClassifier.Maple
            or DreamcastDeviceDomainClassifier.Pvr
            or DreamcastDeviceDomainClassifier.Scif
            or DreamcastDeviceDomainClassifier.Sh4
            or DreamcastDeviceDomainClassifier.Tmu
            or DreamcastDeviceDomainClassifier.Unmapped
            or DreamcastDeviceDomainClassifier.Other => normalized,
        _ => throw new InvalidDataException($"Unknown device domain: {text}")
    };
}

static string FormatController(DreamcastControllerState state) =>
    $"buttons={state.Buttons}, ltrig={state.LeftTrigger}, rtrig={state.RightTrigger}, joy=({state.JoyX},{state.JoyY}), joy2=({state.Joy2X},{state.Joy2Y})";

static string FormatPvrTaLists(IReadOnlyList<DreamcastPvrTaListSummary> lists) =>
    string.Join(", ", lists.Select(list => $"{list.Region}:{list.ListTypeName ?? "none"} commands={list.CommandCount} headers={list.PolygonHeaderCount} vertices={list.VertexCount} ends={list.VertexEndOfStripCount}"));

static string FormatPvrTaStrips(IReadOnlyList<DreamcastPvrTaStripSummary> strips) =>
    string.Join(", ", strips.Select(strip => $"{strip.Region}:{strip.ListTypeName ?? "none"} vertices={strip.VertexCount} color={strip.Rgb565Hex}{FormatPvrTaStripMode(strip.HeaderPayload)} points={string.Join("/", strip.Vertices.Select(vertex => $"{vertex.X},{vertex.Y}"))}"));

static string FormatPvrTaSprites(IReadOnlyList<DreamcastPvrTaSpriteSummary> sprites) =>
    string.Join(", ", sprites.Select(sprite => $"{sprite.Region}:{sprite.ListTypeName ?? "none"} vertices={sprite.VertexCount} color={sprite.Rgb565Hex} argb={sprite.HeaderPayload.ArgbHex} tex={sprite.HeaderPayload.Mode1Fields.TextureEnabled} points={string.Join("/", sprite.Vertices.Select(vertex => $"{vertex.Name}:{vertex.X},{vertex.Y}:{FormatFloat(vertex.U)},{FormatFloat(vertex.V)}"))}"));

static string FormatPvrTaStripMode(DreamcastPvrTaPolygonHeaderPayloadSummary? payload) =>
    payload is null
        ? string.Empty
        : $" mode1={payload.Mode1Hex} depth={payload.Mode1Fields.DepthCompareName} cull={payload.Mode1Fields.CullingName} mode2={payload.Mode2Hex} blend={payload.Mode2Fields.BlendSrcName}/{payload.Mode2Fields.BlendDstName} alpha={payload.Mode2Fields.AlphaEnabled} mode3={payload.Mode3Hex} texBase={payload.Mode3Fields.TextureBaseHex} twid={!payload.Mode3Fields.NonTwiddled} pixel={payload.Mode3Fields.PixelFormatName}";

static string FormatPvrTaParameterHeaders(IReadOnlyList<DreamcastPvrTaParameterHeaderSummary> headers) =>
    string.Join(", ", headers
        .GroupBy(header => new
        {
            header.Region,
            header.Kind,
            header.ParameterType,
            header.ListTypeName,
            header.EndOfStrip,
            header.ExpectedPayloadWords,
            header.PolygonHeaderCommand?.ColorFormatName,
            header.PolygonHeaderCommand?.TextureEnabled,
            header.PolygonHeaderCommand?.Gouraud,
            header.PolygonHeaderCommand?.ClipModeName,
            header.PolygonHeaderCommand?.StripLengthName,
            header.PolygonHeaderCommand?.AutoStripLength
        })
        .Select(group =>
            $"{group.Key.Region}:{group.Key.Kind}x{group.Count()} type={group.Key.ParameterType?.ToString(CultureInfo.InvariantCulture) ?? "none"} list={group.Key.ListTypeName ?? "none"} end={group.Key.EndOfStrip} payload={group.Key.ExpectedPayloadWords?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}{FormatPvrTaPolygonCommand(group.First().PolygonHeaderCommand)}"));

static string FormatPvrTaPolygonCommand(DreamcastPvrTaPolygonHeaderCommandSummary? command) =>
    command is null
        ? string.Empty
        : $" poly=color={command.ColorFormatName} tex={command.TextureEnabled} gouraud={command.Gouraud} clip={command.ClipModeName} strip={command.StripLengthName} auto={command.AutoStripLength}";

static string FormatPvrTaStreamWrites(IReadOnlyList<DreamcastPvrTaStreamWriteSummary> writes) =>
    string.Join(", ", writes
        .GroupBy(write => new { write.Region, write.Role, write.ControlKind, write.PayloadWordName })
        .Select(group => $"{group.Key.Region}:{group.Key.Role}:{group.Key.ControlKind}{FormatPvrTaPayloadWordName(group.Key.PayloadWordName)}x{group.Count()}"));

static string FormatPvrTaPayloadWordName(string? payloadWordName) =>
    payloadWordName is null ? string.Empty : $":{payloadWordName}";

static string FormatPvrTaPolygonHeaderPayloads(IReadOnlyList<DreamcastPvrTaPolygonHeaderPayloadSummary> payloads) =>
    string.Join(", ", payloads.Select(payload =>
        $"{payload.Region}:{payload.ListTypeName ?? "none"} header={payload.HeaderValueHex} mode1={payload.Mode1Hex} depth={payload.Mode1Fields.DepthCompareName} cull={payload.Mode1Fields.CullingName} mode2={payload.Mode2Hex} blend={payload.Mode2Fields.BlendSrcName}/{payload.Mode2Fields.BlendDstName} alpha={payload.Mode2Fields.AlphaEnabled} fog={payload.Mode2Fields.FogTypeName} mode3={payload.Mode3Hex} texBase={payload.Mode3Fields.TextureBaseHex} twid={!payload.Mode3Fields.NonTwiddled} pixel={payload.Mode3Fields.PixelFormatName} vq={payload.Mode3Fields.VqEnabled} mip={payload.Mode3Fields.MipMapEnabled}"));

static string FormatPvrTaRealVertexPayloads(IReadOnlyList<DreamcastPvrTaRealVertexPayloadSummary> vertices) =>
    string.Join(", ", vertices
        .GroupBy(vertex => new { vertex.Region, vertex.ListTypeName, vertex.Rgb565Hex, vertex.ArgbHex })
        .Select(group => $"{group.Key.Region}:{group.Key.ListTypeName ?? "none"} vertices={group.Count()} points={string.Join("/", group.Select(vertex => $"{vertex.RoundedX},{vertex.RoundedY}"))} z={string.Join("/", group.Select(vertex => FormatFloat(vertex.Z)))} argb={group.Key.ArgbHex} rgb565={group.Key.Rgb565Hex} ends={group.Count(vertex => vertex.EndOfStrip)}"));

static string FormatGdromReads(IReadOnlyList<DreamcastGdromReadCommandSummary> reads) =>
    string.Join(", ", reads.Select(read =>
        $"sector={read.SectorHex ?? "none"} count={read.SectorCount?.ToString(CultureInfo.InvariantCulture) ?? "none"} dest={read.DestinationHex ?? "none"} bytes={read.BytesRead}/{read.BytesRequested} ok={read.Success} status={read.Status}"));

static string FormatGdromTocs(IReadOnlyList<DreamcastGdromTocCommandSummary> tocs) =>
    string.Join(", ", tocs.Select(toc =>
        $"buffer={toc.BufferAddressHex ?? "none"} first={toc.FirstTrack?.ToString(CultureInfo.InvariantCulture) ?? "none"} last={toc.LastTrack?.ToString(CultureInfo.InvariantCulture) ?? "none"} data={toc.DataTrackStartFadHex ?? "none"} leadout={toc.LeadoutFadHex ?? "none"} ok={toc.Success} status={toc.Status}"));

static string FormatGdromStatuses(IReadOnlyList<DreamcastGdromStatusCommandSummary> statuses) =>
    string.Join(", ", statuses
        .GroupBy(status => new { status.BufferAddressHex, status.StatusCode, status.StatusName, status.DiscType, status.DiscTypeName, status.Success, status.Status })
        .Select(group =>
            $"buffer={group.Key.BufferAddressHex} drive={group.Key.StatusCode}/{group.Key.StatusName} disc={group.Key.DiscType}/{group.Key.DiscTypeName} ok={group.Key.Success} status={group.Key.Status} x{group.Count()}"));

static string FormatGdromSectorModes(IReadOnlyList<DreamcastGdromSectorModeCommandSummary> modes) =>
    string.Join(", ", modes.Select(mode =>
        $"params={mode.ParameterAddressHex} request={mode.Request}/{mode.RequestName} part={mode.SectorPartHex} cdxa={mode.CdXa} size={mode.SectorSize} ok={mode.Success} status={mode.Status}"));

static string FormatGdromTracks(IReadOnlyList<DreamcastMediaTrackSummary> tracks) =>
    string.Join(", ", tracks.Select(track =>
        $"track={track.TrackNumber} start={track.StartFadHex} control={track.Control} sectors={track.SectorCount}"));

static string FormatFloat(float value) =>
    value.ToString("0.###", CultureInfo.InvariantCulture);

static DreamcastControllerState EffectiveControllerA(DreamcastRunOptions options, ulong instructionsExecuted) =>
    EffectiveController(options, 0x20, instructionsExecuted)
    ?? DreamcastControllerState.Neutral;

static ulong EffectiveControllerInstruction(DreamcastRunResult result) =>
    Math.Max(result.Cpu.InstructionsExecuted, result.Scheduler.HardwareAdvanceTicks);

static DreamcastControllerState? EffectiveController(DreamcastRunOptions options, byte address, ulong instructionsExecuted) =>
    options.ControllerScripts?.GetValueOrDefault(address)?.StateAt(instructionsExecuted)
    ?? (address == 0x20 ? options.ControllerAScript?.StateAt(instructionsExecuted) : null)
    ?? options.Controllers?.GetValueOrDefault(address)
    ?? (address == 0x40 ? options.ControllerB : null)
    ?? (address == 0x20 ? options.ControllerA : null);

static void PrintUsage()
{
    Console.WriteLine("Usage:");
    Console.WriteLine("  dcsharp inspect <file.elf>");
    Console.WriteLine("  dcsharp media inspect <path-to-media> [--scan-sectors count] [--json]");
    Console.WriteLine("  dcsharp media extract-boot <path-to-media> --out <path> [--scan-sectors count] [--json]");
    Console.WriteLine("  dcsharp media analyze-boot <path-to-media-or-boot-bin> [--out-descrambled path] [--scan-sectors count] [--json]");
    Console.WriteLine("  dcsharp media boot-smoke <path-to-media-or-boot-bin> [--layout auto|original|descrambled] [--scan-sectors count] [run options]");
    Console.WriteLine("  dcsharp run <file.elf> [--instructions count] [--trace-tail count] [--vblank-interval instructions] [--controller address:state] [--controller-script address:script] [--controller-a state] [--controller-b state] [--controller-a-script script] [--dump-framebuffer path.png] [--framebuffer-size 320x240] [--audio-wav path.wav] [--trace-log path] [--trace-pc start-end] [--device-log path] [--device-domain domain] [--device-kind kind] [--device-address start-end] [--memory-write-log path] [--memory-write-address start-end] [--memory-write-limit count] [--stop-on-unmapped] [--stop-on-device-domain domain] [--initial-sp address] [--initial-sr address] [--media path-to-media] [--json]");
    Console.WriteLine("  dcsharp fixtures <manifest.json> [--artifacts path] [--filter name] [--report-json path] [--validate-only] [--json]");
    Console.WriteLine("    Use --vblank-interval 0 to disable synthetic VBlank events.");
    Console.WriteLine("    Example controller state: --controller-a start,a,joyx=-16,ltrig=40");
    Console.WriteLine("    Example controller map entry: --controller b0:b,ltrig=7");
    Console.WriteLine("    Example controller script: --controller-script \"a0:0:none;200000:start,a\"");
    Console.WriteLine("    Framebuffer dumps currently use RGB565.");
    Console.WriteLine("    Audio WAV dumps currently synthesize modeled PCM16/PCM8 diagnostic playback only.");
}

static string? FindRepoRoot(string startPath)
{
    var directory = File.Exists(startPath)
        ? Directory.GetParent(startPath)
        : new DirectoryInfo(startPath);
    while (directory is not null)
    {
        if (File.Exists(Path.Combine(directory.FullName, "dcSharp.slnx")))
        {
            return directory.FullName;
        }

        directory = directory.Parent;
    }

    return null;
}

static string ResolveRepoPath(string repoRoot, string path) =>
    Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(repoRoot, path));

internal sealed record CliRunOptions(
    DreamcastRunOptions Emulation,
    bool EmitJson,
    string? FramebufferDumpPath,
    int FramebufferWidth,
    int FramebufferHeight,
    string? AudioWavPath,
    string? TraceLogPath,
    string? DeviceLogPath,
    MemoryAccessKind? DeviceKind,
    AddressRange? DeviceAddressRange,
    string? DeviceDomain,
    string? MemoryWriteLogPath);

internal sealed record BootExtractionCliReport(
    string MediaPath,
    string SourcePath,
    string OutputPath,
    string BootFile,
    string Title,
    string VolumeIdentifier,
    uint ExtentSector,
    string ExtentSectorHex,
    uint FileLength,
    int BytesWritten,
    IReadOnlyList<string> PriorAttempts);

internal sealed record BootSmokeCliReport(
    DreamcastBootBinaryAnalysis Analysis,
    string SelectedLayout,
    bool IpBinSeeded,
    IReadOnlyList<DreamcastMemoryRegionWriteSummary> MemoryRegionWrites,
    DreamcastRunSummary Summary);

internal sealed record AddressRange(uint Start, uint End)
{
    public bool Contains(uint address) => address >= Start && address <= End;
}

internal sealed record FixtureReport(
    string Name,
    bool Passed,
    DreamcastStopReason? StopReason,
    ulong? InstructionsExecuted,
    int? SerialBytes,
    ulong? VideoNonZeroBytes,
    int? PvrRegisterAccessCount,
    int? PvrTaCommandWriteCount,
    IReadOnlyList<DreamcastPvrTaListSummary>? PvrTaLists,
    IReadOnlyList<DreamcastPvrTaStripSummary>? PvrTaStrips,
    IReadOnlyList<DreamcastPvrTaSpriteSummary>? PvrTaSprites,
    IReadOnlyList<DreamcastPvrTaStreamWriteSummary>? RecentPvrTaStreamWrites,
    IReadOnlyList<DreamcastPvrTaPolygonHeaderPayloadSummary>? PvrTaPolygonHeaderPayloads,
    IReadOnlyList<DreamcastPvrTaRealVertexPayloadSummary>? PvrTaRealVertexPayloads,
    IReadOnlyList<DreamcastPvrTaParameterHeaderSummary>? RecentPvrTaParameterHeaders,
    int? AicaRegisterAccessCount,
    int? MapleTransferCount,
    int? MapleDeviceInfoCount,
    int? MapleGetConditionCount,
    string? TimerPendingEventCode,
    int? TimerPendingChannel,
    int? TimerPendingPriority,
    string? AsicPendingEventCode,
    int? AsicPendingLevel,
    ulong? VBlankEventsRaised,
    ulong? HardwareAdvanceTicks,
    ulong? HardwareAdvanceBatches,
    ulong? MaxHardwareAdvanceBatch,
    ulong? IdleAdvanceTicks,
    ulong? IdleAdvanceBatches,
    ulong? MaxIdleAdvanceBatch,
    ulong? IdleTimerWakeCount,
    ulong? IdleVBlankWakeCount,
    ulong? IdleInputWakeCount,
    ulong? CpuFastForwardInstructions,
    ulong? CpuFastForwardBatches,
    ulong? MaxCpuFastForwardBatch,
    ulong? ControllerScriptChanges,
    IReadOnlyList<string> Failures)
{
    public static FixtureReport FromResult(DreamcastFixtureCheckResult result) =>
        new(
            result.Name,
            result.Passed,
            result.Summary?.StopReason,
            result.Summary?.InstructionsExecuted,
            result.Summary?.SerialBytes,
            result.Summary?.Video.NonZeroBytes,
            result.Summary?.Video.PvrRegisterAccessCount,
            result.Summary?.Video.PvrTaCommandWriteCount,
            result.Summary?.Video.PvrTaLists,
            result.Summary?.Video.PvrTaStrips,
            result.Summary?.Video.PvrTaSprites,
            result.Summary?.Video.RecentPvrTaStreamWrites,
            result.Summary?.Video.PvrTaPolygonHeaderPayloads,
            result.Summary?.Video.PvrTaRealVertexPayloads,
            result.Summary?.Video.RecentPvrTaParameterHeaders,
            result.Summary?.Audio.RegisterAccessCount,
            result.Summary?.Maple.TransferCount,
            result.Summary?.Maple.DeviceInfoCount,
            result.Summary?.Maple.GetConditionCount,
            result.Summary?.Timer.PendingEventCodeHex,
            result.Summary?.Timer.PendingChannel,
            result.Summary?.Timer.PendingPriority,
            result.Summary?.Asic.PendingEventCodeHex,
            result.Summary?.Asic.PendingLevel,
            result.Summary?.Scheduler.VBlankEventsRaised,
            result.Summary?.Scheduler.HardwareAdvanceTicks,
            result.Summary?.Scheduler.HardwareAdvanceBatches,
            result.Summary?.Scheduler.MaxHardwareAdvanceBatch,
            result.Summary?.Scheduler.IdleAdvanceTicks,
            result.Summary?.Scheduler.IdleAdvanceBatches,
            result.Summary?.Scheduler.MaxIdleAdvanceBatch,
            result.Summary?.Scheduler.IdleTimerWakeCount,
            result.Summary?.Scheduler.IdleVBlankWakeCount,
            result.Summary?.Scheduler.IdleInputWakeCount,
            result.Summary?.Scheduler.CpuFastForwardInstructions,
            result.Summary?.Scheduler.CpuFastForwardBatches,
            result.Summary?.Scheduler.MaxCpuFastForwardBatch,
            result.Summary?.Scheduler.ControllerScriptChanges,
            result.Failures);
}

internal sealed record FixtureManifestValidationReport(
    string ManifestPath,
    string ArtifactDirectory,
    int FixtureCount,
    IReadOnlyList<string> FixtureNames);
