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
    Console.WriteLine($"  Windows CE header: {candidate.HasWindowsCeHeader}");
    if (candidate.WindowsCePayloadOffsetHex is not null)
    {
        Console.WriteLine($"  Windows CE GD-ROM payload offset: {candidate.WindowsCePayloadOffsetHex}");
    }

    Console.WriteLine($"  Suggested entry: {candidate.SuggestedEntryPointHex}{(candidate.WindowsCeEntryOffsetHex is null ? string.Empty : $" (offset {candidate.WindowsCeEntryOffsetHex})")}");
    if (candidate.WindowsCeEntryJumpTargetHex is not null)
    {
        Console.WriteLine($"  Windows CE entry jump target: {candidate.WindowsCeEntryJumpTargetHex}{(candidate.WindowsCeEntryJumpTargetFileOffsetHex is null ? string.Empty : $" (file offset {candidate.WindowsCeEntryJumpTargetFileOffsetHex})")}");
        if (candidate.WindowsCeEntryJumpTargetOpcodeCount is not null)
        {
            Console.WriteLine($"  Windows CE jump target sample: {candidate.WindowsCeEntryJumpTargetRecognizedOpcodeCount}/{candidate.WindowsCeEntryJumpTargetOpcodeCount} ({candidate.WindowsCeEntryJumpTargetFirstWordsHex})");
        }
    }

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
    var selectedCandidate = selectedLayout == "descrambled" ? analysis.Descrambled : analysis.Original;
    var bootPayloadOffset = selectedLayout == "original" && selectedCandidate.WindowsCePayloadOffset is { } windowsCePayloadOffset
        ? windowsCePayloadOffset
        : 0;
    var bootEntryPoint = selectedCandidate.SuggestedEntryPoint;
    var bootBytes = selectedLayout == "descrambled"
        ? DreamcastBootScrambler.Descramble(data)
        : bootPayloadOffset == 0
            ? data
            : data[(int)bootPayloadOffset..];
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
                SoftResetEntryPoint = bootEntryPoint,
                SeedInitialVBlank = options.SeedInitialVBlankOverride ?? enterIpBin,
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
        enterIpBin ? ipBinEntryPoint : bootEntryPoint);

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

    if (options.PvrTaLogPath is not null)
    {
        DumpPvrTaLog(result, options);
    }

    if (options.PvrTaSpriteLogPath is not null)
    {
        DumpPvrTaSpriteLog(result, options);
    }

    if (options.PvrTaSpriteTextureSampleLogPath is not null)
    {
        DumpPvrTaSpriteTextureSampleLog(result, options);
    }

    if (options.PvrTaTextureModeLogPath is not null)
    {
        DumpPvrTaTextureModeLog(result, options);
    }

    if (options.PvrTaModeTableLogPath is not null)
    {
        DumpPvrTaModeTableLog(result, options);
    }

    if (options.PvrTaSpriteSourceLogPath is not null)
    {
        DumpPvrTaSpriteSourceLog(result, options);
    }

    if (options.PvrTaSpriteSqLogPath is not null)
    {
        DumpPvrTaSpriteSqLog(result, options);
    }

    if (options.StoreQueueFlushLogPath is not null)
    {
        DumpStoreQueueFlushLog(result, options);
    }

    if (options.FpuAnomalyLogPath is not null)
    {
        DumpFpuAnomalyLog(result, options.FpuAnomalyLogPath);
    }

    if (options.FpuWriteLogPath is not null)
    {
        DumpFpuWriteLog(result, options.FpuWriteLogPath);
    }

    if (options.FpscrLogPath is not null)
    {
        DumpFpscrLog(result, options.FpscrLogPath);
    }

    if (options.FpuSnapshotLogPath is not null)
    {
        DumpFpuSnapshotLog(result, options.FpuSnapshotLogPath);
    }

    if (options.CpuSnapshotLogPath is not null)
    {
        DumpCpuSnapshotLog(result, options.CpuSnapshotLogPath);
    }

    if (options.FpuMemoryLogPath is not null)
    {
        DumpFpuMemoryLog(result, options.FpuMemoryLogPath);
    }

    if (options.PcProfileLogPath is not null)
    {
        DumpPcProfileLog(result, options.PcProfileLogPath);
    }

    if (options.WindowsCeSyscallLogPath is not null)
    {
        DumpWindowsCeSyscallLog(result, options.WindowsCeSyscallLogPath);
    }

    if (options.WindowsCeSchedulerLogPath is not null)
    {
        DumpWindowsCeSchedulerLog(result, options.WindowsCeSchedulerLogPath);
    }

    if (options.DeviceLogPath is not null)
    {
        DumpDeviceLog(result, options);
    }

    if (options.MemoryWriteLogPath is not null)
    {
        DumpMemoryWriteLog(result, options.MemoryWriteLogPath);
    }

    if (options.MemoryReadLogPath is not null)
    {
        DumpMemoryReadLog(result, options.MemoryReadLogPath);
    }

    if (options.MemorySnapshotLogPath is not null)
    {
        DumpMemorySnapshotLog(result, options.MemorySnapshotLogPath);
    }

    var summary = DreamcastRunSummary.FromResult(result, options.Emulation);
    if (options.EmitJson)
    {
        Console.WriteLine(SerializeJson(new BootSmokeCliReport(analysis, selectedLayout, bootPayloadOffset, ipBin is not null, result.MemoryRegionWrites, summary)));
        return;
    }

    Console.WriteLine($"Source: {analysis.SourcePath}");
    Console.WriteLine($"Source kind: {analysis.SourceKind}");
    Console.WriteLine($"Selected layout: {selectedLayout}");
    Console.WriteLine($"Analyzer recommendation: {analysis.RecommendedLayout}");
    Console.WriteLine($"Load address: {analysis.LoadAddressHex}");
    Console.WriteLine($"Boot payload offset: 0x{bootPayloadOffset:X}");
    Console.WriteLine($"Boot entry: 0x{bootEntryPoint:X8}");
    Console.WriteLine($"IP.BIN seeded: {ipBin is not null}");
    Console.WriteLine($"Bytes loaded: {bootBytes.Length}");
    Console.WriteLine($"Instructions: {result.Cpu.InstructionsExecuted}");
    Console.WriteLine($"PC: 0x{result.Cpu.Pc:X8}");
    Console.WriteLine($"PR: 0x{result.Cpu.Pr:X8}");
    Console.WriteLine($"SR: 0x{result.Cpu.Sr:X8}");
    Console.WriteLine($"FPSCR: {FormatFpscr(result.Cpu.Fpscr)}");
    PrintGeneralRegisters(result.Cpu);
    Console.WriteLine($"Stopped: {result.StopReason}");
    Console.WriteLine($"Detail: {result.StopDetail}");
    PrintSoftResetCheckpoint(result);
    Console.WriteLine($"Device accesses: {result.DeviceAccesses.Count}");
    Console.WriteLine($"FPU anomalies: {result.FpuAnomalies.Count}");
    Console.WriteLine($"FPU writes: {result.FpuRegisterWrites.Count}");
    Console.WriteLine($"FPSCR events: {result.FpscrEvents.Count}");
    Console.WriteLine($"FPU snapshots: {result.FpuSnapshots.Count}");
    Console.WriteLine($"CPU snapshots: {result.CpuSnapshots.Count}");
    Console.WriteLine($"FPU memory transfers: {result.FpuMemoryTransfers.Count}");
    Console.WriteLine($"PC profile entries: {result.PcProfile.Count}");
    Console.WriteLine($"Watched memory writes: {result.WatchedMemoryWrites.Count}");
    Console.WriteLine($"Watched memory reads: {result.WatchedMemoryReads.Count}");
    Console.WriteLine($"Serial bytes: {result.SerialOutput.Count}");
    PrintVideoActivity(summary.Video);
    PrintRuntimeScheduling(summary);
    var gdrom = result.Gdrom ?? DreamcastGdromSnapshot.Empty;
    Console.WriteLine($"GD-ROM: media={gdrom.HasMedia}, reads={gdrom.ReadCommands.Count}, ok={gdrom.ReadCommands.Count(command => command.Success)}, failed={gdrom.ReadCommands.Count(command => !command.Success)}, tocs={gdrom.TocCommands.Count}");
    PrintGdromActivity(gdrom);
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

static void PrintRuntimeScheduling(DreamcastRunSummary summary)
{
    var scheduler = summary.Scheduler;
    Console.WriteLine($"Scheduler: vblanks={scheduler.VBlankEventsRaised}, nextVBlank={scheduler.NextVBlankInstruction}, hardwareTicks={scheduler.HardwareAdvanceTicks}, hardwareBatches={scheduler.HardwareAdvanceBatches}, maxHardwareBatch={scheduler.MaxHardwareAdvanceBatch}, idleTicks={scheduler.IdleAdvanceTicks}, idleBatches={scheduler.IdleAdvanceBatches}, maxIdleBatch={scheduler.MaxIdleAdvanceBatch}, idleWakes=timer:{scheduler.IdleTimerWakeCount}/vblank:{scheduler.IdleVBlankWakeCount}/input:{scheduler.IdleInputWakeCount}, cpuFastForward={scheduler.CpuFastForwardInstructions}, cpuFastForwardBatches={scheduler.CpuFastForwardBatches}, maxCpuFastForward={scheduler.MaxCpuFastForwardBatch}, inputChanges={scheduler.ControllerScriptChanges}");

    var timerSource = summary.Timer.PendingInterrupt is { } pendingTimer
        ? $", channel={pendingTimer.Channel}, priority={pendingTimer.Priority}"
        : string.Empty;
    Console.WriteLine($"TMU: pending={summary.Timer.PendingEventCodeHex ?? "none"}{timerSource}");

    var asicSource = summary.Asic.PendingInterrupt is { } pendingAsic
        ? $", source={pendingAsic.RegisterName}:bit{pendingAsic.Bit}, mask={pendingAsic.BitMaskHex}, levelName={pendingAsic.LevelName}"
        : string.Empty;
    Console.WriteLine($"ASIC: pending={summary.Asic.PendingEventCodeHex ?? "none"}, level={summary.Asic.PendingLevel?.ToString(CultureInfo.InvariantCulture) ?? "none"}{asicSource}");
}

static void PrintVideoActivity(DreamcastVideoSummary video)
{
    Console.WriteLine($"Video VRAM: nonzero={video.NonZeroBytes}, checksum={video.Fnv1A32Hex}, first={video.FirstNonZeroOffsetHex ?? "none"}");
    Console.WriteLine($"PVR: registers={video.PvrRegisterAccessCount}, taWrites={video.PvrTaCommandWriteCount}, taStrips={video.PvrTaStrips.Count}, taSprites={video.PvrTaSprites.Count}{FormatPvrTaSpriteCounts(video)}");
    if (video.PvrDisplay.HasConfiguredState)
    {
        Console.WriteLine($"PVR display: {FormatPvrDisplay(video.PvrDisplay)}");
    }

    Console.WriteLine($"PVR TA diag: {FormatPvrTaDiagnostics(video.PvrTaDiagnostics)}");
    if (video.PvrTaLists.Count > 0)
    {
        Console.WriteLine($"PVR TA lists: {FormatPvrTaLists(video.PvrTaLists)}");
    }

    if (video.PvrTaStrips.Count > 0)
    {
        Console.WriteLine($"PVR TA strips: {FormatPvrTaStrips(video.PvrTaStrips)}");
    }

    if (video.PvrTaSprites.Count > 0)
    {
        Console.WriteLine($"PVR TA sprite sources: {FormatPvrTaSpriteSourceGroups(video.PvrTaSpriteSourceGroups)}");
        Console.WriteLine($"PVR TA sprite shapes: {FormatPvrTaSpriteShapeGroups(video.PvrTaSpriteShapeGroups)}");
        Console.WriteLine($"PVR TA sprites: {FormatPvrTaSprites(video.PvrTaSprites.TakeLast(8).ToArray())}");
    }
}

static void PrintGdromActivity(DreamcastGdromSnapshot gdrom)
{
    var readSectors = gdrom.ReadCommands
        .Where(read => read.Sector is not null)
        .GroupBy(read => read.Sector!.Value)
        .OrderBy(group => group.Key)
        .Select(group => $"{group.Key.ToString(CultureInfo.InvariantCulture)}x{group.Count()}")
        .ToArray();
    if (readSectors.Length > 0)
    {
        Console.WriteLine($"  GD-ROM read sectors: unique={readSectors.Length}, {string.Join(", ", readSectors.Take(8))}");
    }

    foreach (var activity in gdrom.CommandActivities.TakeLast(8))
    {
        Console.WriteLine($"  GD-ROM command: op={activity.Operation}, id={activity.CommandId?.ToString(CultureInfo.InvariantCulture) ?? "none"}, cmd={activity.CommandHex ?? "none"}/{activity.CommandName ?? "none"}, params={activity.ParameterAddressHex ?? "none"}, statusBuffer={activity.StatusAddressHex ?? "none"}, response={activity.Response?.ToString(CultureInfo.InvariantCulture) ?? "none"}/{activity.ResponseName ?? "none"}, words={activity.Status0?.ToString(CultureInfo.InvariantCulture) ?? "none"},{activity.Status1?.ToString(CultureInfo.InvariantCulture) ?? "none"},{activity.TransferredBytes?.ToString(CultureInfo.InvariantCulture) ?? "none"},{activity.AtaStatus?.ToString(CultureInfo.InvariantCulture) ?? "none"}, status={activity.Status}");
    }

    foreach (var status in gdrom.StatusCommands.TakeLast(4))
    {
        Console.WriteLine($"  GD-ROM status: buffer=0x{status.BufferAddress:X8}, drive={status.StatusCode}/{status.StatusName}, disc={status.DiscType}/{status.DiscTypeName}, ok={status.Success}, status={status.Status}");
    }

    foreach (var read in gdrom.ReadCommands.TakeLast(8))
    {
        Console.WriteLine($"  GD-ROM read: sector={read.Sector?.ToString(CultureInfo.InvariantCulture) ?? "none"}, count={read.SectorCount?.ToString(CultureInfo.InvariantCulture) ?? "none"}, dest={read.DestinationHex ?? "none"}, bytes={read.BytesRead}/{read.BytesRequested}, ok={read.Success}, status={read.Status}");
    }

    foreach (var toc in gdrom.TocCommands.TakeLast(4))
    {
        Console.WriteLine($"  GD-ROM TOC: buffer={toc.BufferAddress?.ToString(CultureInfo.InvariantCulture) ?? "none"}, first={toc.FirstTrack?.ToString(CultureInfo.InvariantCulture) ?? "none"}, last={toc.LastTrack?.ToString(CultureInfo.InvariantCulture) ?? "none"}, data={toc.DataTrackStartFadHex ?? "none"}, leadout={toc.LeadoutFadHex ?? "none"}, ok={toc.Success}, status={toc.Status}");
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

    if (options.PvrTaLogPath is not null)
    {
        DumpPvrTaLog(result, options);
    }

    if (options.PvrTaSpriteLogPath is not null)
    {
        DumpPvrTaSpriteLog(result, options);
    }

    if (options.PvrTaSpriteTextureSampleLogPath is not null)
    {
        DumpPvrTaSpriteTextureSampleLog(result, options);
    }

    if (options.PvrTaTextureModeLogPath is not null)
    {
        DumpPvrTaTextureModeLog(result, options);
    }

    if (options.PvrTaModeTableLogPath is not null)
    {
        DumpPvrTaModeTableLog(result, options);
    }

    if (options.PvrTaSpriteSourceLogPath is not null)
    {
        DumpPvrTaSpriteSourceLog(result, options);
    }

    if (options.PvrTaSpriteSqLogPath is not null)
    {
        DumpPvrTaSpriteSqLog(result, options);
    }

    if (options.StoreQueueFlushLogPath is not null)
    {
        DumpStoreQueueFlushLog(result, options);
    }

    if (options.FpuAnomalyLogPath is not null)
    {
        DumpFpuAnomalyLog(result, options.FpuAnomalyLogPath);
    }

    if (options.FpuWriteLogPath is not null)
    {
        DumpFpuWriteLog(result, options.FpuWriteLogPath);
    }

    if (options.FpscrLogPath is not null)
    {
        DumpFpscrLog(result, options.FpscrLogPath);
    }

    if (options.FpuSnapshotLogPath is not null)
    {
        DumpFpuSnapshotLog(result, options.FpuSnapshotLogPath);
    }

    if (options.CpuSnapshotLogPath is not null)
    {
        DumpCpuSnapshotLog(result, options.CpuSnapshotLogPath);
    }

    if (options.FpuMemoryLogPath is not null)
    {
        DumpFpuMemoryLog(result, options.FpuMemoryLogPath);
    }

    if (options.PcProfileLogPath is not null)
    {
        DumpPcProfileLog(result, options.PcProfileLogPath);
    }

    if (options.WindowsCeSyscallLogPath is not null)
    {
        DumpWindowsCeSyscallLog(result, options.WindowsCeSyscallLogPath);
    }

    if (options.DeviceLogPath is not null)
    {
        DumpDeviceLog(result, options);
    }

    if (options.MemoryWriteLogPath is not null)
    {
        DumpMemoryWriteLog(result, options.MemoryWriteLogPath);
    }

    if (options.MemoryReadLogPath is not null)
    {
        DumpMemoryReadLog(result, options.MemoryReadLogPath);
    }

    if (options.MemorySnapshotLogPath is not null)
    {
        DumpMemorySnapshotLog(result, options.MemorySnapshotLogPath);
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
    Console.WriteLine($"FPSCR: {FormatFpscr(result.Cpu.Fpscr)}");
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

    var videoSummary = DreamcastVideoSummary.FromSnapshot(result.Video);
    Console.WriteLine($"Video VRAM: nonzero={result.Video.NonZeroBytes}, checksum={result.Video.Fnv1A32Hex}, first={result.Video.FirstNonZeroOffsetHex ?? "none"}");
    Console.WriteLine($"PVR: registers={result.Video.PvrRegisterAccesses.Count}, taWrites={result.Video.PvrTaCommandWrites.Count}, taStrips={videoSummary.PvrTaStrips.Count}, taSprites={videoSummary.PvrTaSprites.Count}{FormatPvrTaSpriteCounts(videoSummary)}");
    if (videoSummary.PvrDisplay.HasConfiguredState)
    {
        Console.WriteLine($"PVR display: {FormatPvrDisplay(videoSummary.PvrDisplay)}");
    }

    Console.WriteLine($"PVR TA diag: {FormatPvrTaDiagnostics(videoSummary.PvrTaDiagnostics)}");
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
        Console.WriteLine($"PVR TA sprite sources: {FormatPvrTaSpriteSourceGroups(videoSummary.PvrTaSpriteSourceGroups)}");
        Console.WriteLine($"PVR TA sprite shapes: {FormatPvrTaSpriteShapeGroups(videoSummary.PvrTaSpriteShapeGroups)}");
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
    Console.WriteLine($"FPU anomalies: {result.FpuAnomalies.Count}");
    Console.WriteLine($"FPU writes: {result.FpuRegisterWrites.Count}");
    Console.WriteLine($"FPSCR events: {result.FpscrEvents.Count}");
    Console.WriteLine($"FPU snapshots: {result.FpuSnapshots.Count}");
    Console.WriteLine($"CPU snapshots: {result.CpuSnapshots.Count}");
    Console.WriteLine($"FPU memory transfers: {result.FpuMemoryTransfers.Count}");
    Console.WriteLine($"PC profile entries: {result.PcProfile.Count}");
    Console.WriteLine($"Watched memory writes: {result.WatchedMemoryWrites.Count}");
    Console.WriteLine($"Watched memory reads: {result.WatchedMemoryReads.Count}");
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

    var readSectors = gdrom.ReadCommands
        .Where(read => read.Sector is not null)
        .GroupBy(read => read.Sector!.Value)
        .OrderBy(group => group.Key)
        .Select(group => $"{group.Key.ToString(CultureInfo.InvariantCulture)}x{group.Count()}")
        .ToArray();
    if (readSectors.Length > 0)
    {
        Console.WriteLine($"  GD-ROM read sectors: unique={readSectors.Length}, {string.Join(", ", readSectors.Take(8))}");
    }

    foreach (var activity in gdrom.CommandActivities.TakeLast(8))
    {
        Console.WriteLine($"  GD-ROM command: op={activity.Operation}, id={activity.CommandId?.ToString(CultureInfo.InvariantCulture) ?? "none"}, cmd={activity.CommandHex ?? "none"}/{activity.CommandName ?? "none"}, params={activity.ParameterAddressHex ?? "none"}, statusBuffer={activity.StatusAddressHex ?? "none"}, response={activity.Response?.ToString(CultureInfo.InvariantCulture) ?? "none"}/{activity.ResponseName ?? "none"}, words={activity.Status0?.ToString(CultureInfo.InvariantCulture) ?? "none"},{activity.Status1?.ToString(CultureInfo.InvariantCulture) ?? "none"},{activity.TransferredBytes?.ToString(CultureInfo.InvariantCulture) ?? "none"},{activity.AtaStatus?.ToString(CultureInfo.InvariantCulture) ?? "none"}, status={activity.Status}");
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
                Console.WriteLine($"  stop={result.Summary.StopReason}, instructions={result.Summary.InstructionsExecuted}, serial={result.Summary.SerialBytes}, videoNonZero={result.Summary.Video.NonZeroBytes}, pvrRegs={result.Summary.Video.PvrRegisterAccessCount}, taWrites={result.Summary.Video.PvrTaCommandWriteCount}, taStrips={result.Summary.Video.PvrTaStrips.Count}, taSprites={result.Summary.Video.PvrTaSprites.Count}{FormatPvrTaSpriteCounts(result.Summary.Video)}, aicaRegs={result.Summary.Audio.RegisterAccessCount}, mapleTransfers={result.Summary.Maple.TransferCount}, mapleDmaBatches={result.Summary.Maple.DmaBatchCount}, mapleDescriptorLimitHits={result.Summary.Maple.DescriptorLimitHitCount}, gdromStatuses={result.Summary.Gdrom.StatusCommandCount}, gdromSectorModes={result.Summary.Gdrom.SectorModeCommandCount}, gdromTocs={result.Summary.Gdrom.TocCommandCount}, gdromReads={result.Summary.Gdrom.ReadCommandCount}, gdromBytes={result.Summary.Gdrom.BytesRead}, timerPending={result.Summary.Timer.PendingEventCodeHex ?? "none"}, asicPending={result.Summary.Asic.PendingEventCodeHex ?? "none"}, vblanks={scheduler.VBlankEventsRaised}, schedulerTicks={scheduler.HardwareAdvanceTicks}, schedulerBatches={scheduler.HardwareAdvanceBatches}, maxSchedulerBatch={scheduler.MaxHardwareAdvanceBatch}, idleTicks={scheduler.IdleAdvanceTicks}, idleBatches={scheduler.IdleAdvanceBatches}, maxIdleBatch={scheduler.MaxIdleAdvanceBatch}, idleWakes=timer:{scheduler.IdleTimerWakeCount}/vblank:{scheduler.IdleVBlankWakeCount}/input:{scheduler.IdleInputWakeCount}, cpuFastForward={scheduler.CpuFastForwardInstructions}, cpuFastForwardBatches={scheduler.CpuFastForwardBatches}, maxCpuFastForward={scheduler.MaxCpuFastForwardBatch}, inputChanges={scheduler.ControllerScriptChanges}");
                if (result.Summary.Video.PvrTaLists.Count > 0)
                {
                    Console.WriteLine($"  pvrTaDiag={FormatPvrTaDiagnostics(result.Summary.Video.PvrTaDiagnostics)}");
                    Console.WriteLine($"  pvrTaLists={FormatPvrTaLists(result.Summary.Video.PvrTaLists)}");
                }

                if (result.Summary.Video.PvrTaStrips.Count > 0)
                {
                    Console.WriteLine($"  pvrTaStrips={FormatPvrTaStrips(result.Summary.Video.PvrTaStrips)}");
                }

                if (result.Summary.Video.PvrTaSprites.Count > 0)
                {
                    Console.WriteLine($"  pvrTaSpriteSources={FormatPvrTaSpriteSourceGroups(result.Summary.Video.PvrTaSpriteSourceGroups)}");
                    Console.WriteLine($"  pvrTaSpriteShapes={FormatPvrTaSpriteShapeGroups(result.Summary.Video.PvrTaSpriteShapeGroups)}");
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
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
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
    var byteOffset = FramebufferDumpByteOffset(result.Video, options.FramebufferWidth, options.FramebufferHeight);
    DreamcastFramebufferPngWriter.WriteRgb565Png(stream, result.Video.Vram.AsSpan(byteOffset), options.FramebufferWidth, options.FramebufferHeight);
}

static int FramebufferDumpByteOffset(DreamcastVideoSnapshot video, int width, int height)
{
    var requiredBytes = checked(width * height * 2);
    var renderAddress = PvrRegisterValue(video, "PVR_RENDER_ADDR");
    var framebufferAddress = PvrRegisterValue(video, "PVR_FB_ADDR");
    foreach (var candidate in new[] { renderAddress, framebufferAddress })
    {
        if (candidate is not { } address || address == 0)
        {
            continue;
        }

        var offset = (int)(address & 0x00FF_FFFFu);
        if (offset >= 0 && offset + requiredBytes <= video.Vram.Length)
        {
            return offset;
        }
    }

    return 0;
}

static uint? PvrRegisterValue(DreamcastVideoSnapshot video, string name) =>
    video.PvrRegisters.FirstOrDefault(register => string.Equals(register.Name, name, StringComparison.Ordinal))?.Value;

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
        writer.WriteLine($"#{step.Instruction}: 0x{step.Pc:X8}: 0x{step.Opcode:X4}  {step.Trace}{symbolText}");
    }
}

static void DumpPvrTaLog(DreamcastRunResult result, CliRunOptions options)
{
    using var writer = CreateTextLog(options.PvrTaLogPath!);
    DreamcastPvrTaLogWriter.WriteText(writer, result.Video.PvrTaCommandWrites, options.PvrTaLogLimit);
}

static void DumpPvrTaSpriteLog(DreamcastRunResult result, CliRunOptions options)
{
    using var writer = CreateTextLog(options.PvrTaSpriteLogPath!);
    var videoSummary = DreamcastVideoSummary.FromSnapshot(result.Video);
    DreamcastPvrTaSpriteLogWriter.WriteText(
        writer,
        videoSummary.PvrTaSprites,
        options.PvrTaSpriteLogLimit,
        options.PvrTaSpriteStatus);
}

static void DumpPvrTaSpriteTextureSampleLog(DreamcastRunResult result, CliRunOptions options)
{
    using var writer = CreateTextLog(options.PvrTaSpriteTextureSampleLogPath!);
    var videoSummary = DreamcastVideoSummary.FromSnapshot(result.Video);
    DreamcastPvrTaSpriteTextureSampleTraceWriter.WriteText(
        writer,
        videoSummary.PvrTaSprites,
        result.Video.Vram,
        options.PvrTaSpriteTextureSampleLogLimit,
        options.PvrTaSpriteStatus);
}

static void DumpPvrTaTextureModeLog(DreamcastRunResult result, CliRunOptions options)
{
    using var writer = CreateTextLog(options.PvrTaTextureModeLogPath!);
    var videoSummary = DreamcastVideoSummary.FromSnapshot(result.Video);
    DreamcastPvrTaTextureModeTraceWriter.WriteText(
        writer,
        videoSummary.PvrTaSprites,
        result.Video.Vram,
        options.PvrTaTextureModeLogLimit,
        options.PvrTaSpriteStatus);
}

static void DumpPvrTaModeTableLog(DreamcastRunResult result, CliRunOptions options)
{
    using var writer = CreateTextLog(options.PvrTaModeTableLogPath!);
    var videoSummary = DreamcastVideoSummary.FromSnapshot(result.Video);
    DreamcastPvrTaModeTableTraceWriter.WriteText(
        writer,
        videoSummary.PvrTaSprites,
        result.WatchedMemoryReads,
        result.WatchedMemoryWrites,
        options.PvrTaModeTableLogLimit,
        options.PvrTaSpriteStatus);
}

static void DumpPvrTaSpriteSourceLog(DreamcastRunResult result, CliRunOptions options)
{
    using var writer = CreateTextLog(options.PvrTaSpriteSourceLogPath!);
    var videoSummary = DreamcastVideoSummary.FromSnapshot(result.Video);
    DreamcastPvrTaSpriteSourceTraceWriter.WriteText(
        writer,
        videoSummary.PvrTaSprites,
        options.PvrTaSpriteSourceLogLimit,
        options.PvrTaSpriteStatus);
}

static void DumpPvrTaSpriteSqLog(DreamcastRunResult result, CliRunOptions options)
{
    using var writer = CreateTextLog(options.PvrTaSpriteSqLogPath!);
    var videoSummary = DreamcastVideoSummary.FromSnapshot(result.Video);
    DreamcastPvrTaSpriteStoreQueueTraceWriter.WriteText(
        writer,
        videoSummary.PvrTaSprites,
        result.WatchedMemoryWrites,
        options.PvrTaSpriteSqLogLimit,
        options.PvrTaSpriteStatus);
}

static void DumpStoreQueueFlushLog(DreamcastRunResult result, CliRunOptions options)
{
    using var writer = CreateTextLog(options.StoreQueueFlushLogPath!);
    DreamcastStoreQueueFlushTraceWriter.WriteText(
        writer,
        result.Video.StoreQueueFlushes,
        options.StoreQueueFlushLogLimit);
}

static string FormatFpscr(uint value)
{
    var summary = Sh4FpscrSummary.FromValue(value);
    return $"{summary.ValueHex} ({summary.Display})";
}

static void DumpFpuAnomalyLog(DreamcastRunResult result, string path)
{
    using var writer = CreateTextLog(path);
    foreach (var anomaly in result.FpuAnomalies)
    {
        var symbol = DreamcastSymbolSummary.FromSymbol(result.Load.FindNearestSymbol(anomaly.Pc), anomaly.Pc);
        var symbolText = symbol is null ? string.Empty : $" ; {symbol.Display}";
        writer.WriteLine(
            $"#{anomaly.Instruction}: {anomaly.PcHex}: {anomaly.OpcodeHex}  {anomaly.Trace} ; {anomaly.Register} {anomaly.OldValueHex}->{anomaly.NewValueHex} {anomaly.Kind}, fpscr={FormatFpscr(anomaly.Fpscr)}{symbolText}");
    }
}

static void DumpFpuWriteLog(DreamcastRunResult result, string path)
{
    using var writer = CreateTextLog(path);
    foreach (var write in result.FpuRegisterWrites)
    {
        var symbol = DreamcastSymbolSummary.FromSymbol(result.Load.FindNearestSymbol(write.Pc), write.Pc);
        var symbolText = symbol is null ? string.Empty : $" ; {symbol.Display}";
        writer.WriteLine(
            $"#{write.Instruction}: {write.PcHex}: {write.OpcodeHex}  {write.Trace} ; {write.Register} {write.OldValueHex}->{write.NewValueHex}, fpscr={FormatFpscr(write.Fpscr)}{symbolText}");
    }
}

static void DumpFpscrLog(DreamcastRunResult result, string path)
{
    using var writer = CreateTextLog(path);
    foreach (var fpscrEvent in result.FpscrEvents)
    {
        var symbol = DreamcastSymbolSummary.FromSymbol(result.Load.FindNearestSymbol(fpscrEvent.Pc), fpscrEvent.Pc);
        var symbolText = symbol is null ? string.Empty : $" ; {symbol.Display}";
        writer.WriteLine(
            $"#{fpscrEvent.Instruction}: {fpscrEvent.PcHex}: {fpscrEvent.OpcodeHex}  {fpscrEvent.Trace} ; fpscr {FormatFpscr(fpscrEvent.OldValue)}->{FormatFpscr(fpscrEvent.NewValue)} {fpscrEvent.Kind}{symbolText}");
    }
}

static void DumpFpuSnapshotLog(DreamcastRunResult result, string path)
{
    using var writer = CreateTextLog(path);
    foreach (var snapshot in result.FpuSnapshots)
    {
        var symbol = DreamcastSymbolSummary.FromSymbol(result.Load.FindNearestSymbol(snapshot.Pc), snapshot.Pc);
        var symbolText = symbol is null ? string.Empty : $" ; {symbol.Display}";
        writer.WriteLine(
            $"#{snapshot.Instruction}: {snapshot.PcHex}: {snapshot.OpcodeHex}  {snapshot.Trace}{symbolText}");
        writer.WriteLine($"  fpscr={FormatFpscr(snapshot.Fpscr)}, fpul={snapshot.FpulHex}, pr={snapshot.PrHex}, r15={snapshot.R15Hex}");
        writer.WriteLine($"  fr={FormatFpuSnapshotBank("fr", snapshot.Fr)}");
        writer.WriteLine($"  xf={FormatFpuSnapshotBank("xf", snapshot.Xf)}");
    }
}

static void DumpCpuSnapshotLog(DreamcastRunResult result, string path)
{
    using var writer = CreateTextLog(path);
    foreach (var snapshot in result.CpuSnapshots)
    {
        var symbol = DreamcastSymbolSummary.FromSymbol(result.Load.FindNearestSymbol(snapshot.Pc), snapshot.Pc);
        var symbolText = symbol is null ? string.Empty : $" ; {symbol.Display}";
        var state = snapshot.State;
        writer.WriteLine(
            $"#{snapshot.Instruction}: {snapshot.PcHex}: {snapshot.OpcodeHex}  {snapshot.Trace}{symbolText}");
        writer.WriteLine($"  r0-r7={FormatRegisterRange(state.R, 0, 8)}");
        writer.WriteLine($"  r8-r15={FormatRegisterRange(state.R, 8, 8)}");
        writer.WriteLine($"  pr=0x{state.Pr:X8}, sr=0x{state.Sr:X8}, gbr=0x{state.Gbr:X8}, vbr=0x{state.Vbr:X8}");
        writer.WriteLine($"  fpscr={FormatFpscr(state.Fpscr)}, pc=0x{state.Pc:X8}, spc=0x{state.Spc:X8}, ssr=0x{state.Ssr:X8}, tra=0x{state.Tra:X8}, expevt=0x{state.Expevt:X8}, intevt=0x{state.Intevt:X8}");
    }
}

static string FormatFpuSnapshotBank(string prefix, IReadOnlyList<uint> values)
{
    var builder = new StringBuilder();
    for (var index = 0; index < values.Count; index++)
    {
        if (index > 0)
        {
            builder.Append(',');
        }

        builder.Append($"{prefix}{index}=0x{values[index]:X8}");
    }

    return builder.ToString();
}

static void DumpFpuMemoryLog(DreamcastRunResult result, string path)
{
    using var writer = CreateTextLog(path);
    foreach (var transfer in result.FpuMemoryTransfers)
    {
        var symbol = DreamcastSymbolSummary.FromSymbol(result.Load.FindNearestSymbol(transfer.Pc), transfer.Pc);
        var symbolText = symbol is null ? string.Empty : $" ; {symbol.Display}";
        var value = transfer.ValueHighHex is { } high
            ? $"{transfer.ValueHex},{high}"
            : transfer.ValueHex;
        writer.WriteLine(
            $"#{transfer.Instruction}: {transfer.PcHex}: {transfer.OpcodeHex}  {transfer.Trace} ; {transfer.Direction} {transfer.Register}, addr={transfer.AddressHex}, size={transfer.Size}, value={value}, fpscr={FormatFpscr(transfer.Fpscr)}{symbolText}");
    }
}

static void DumpPcProfileLog(DreamcastRunResult result, string path)
{
    using var writer = CreateTextLog(path);
    var total = result.PcProfile.Aggregate(0UL, (sum, entry) => sum + entry.Count);
    foreach (var entry in result.PcProfile)
    {
        var symbol = DreamcastSymbolSummary.FromSymbol(result.Load.FindNearestSymbol(entry.Pc), entry.Pc);
        var symbolText = symbol is null ? string.Empty : $" ; {symbol.Display}";
        var percent = total == 0
            ? 0
            : (double)entry.Count * 100 / total;
        writer.WriteLine($"{entry.PcHex}: count={entry.Count}, percent={percent:F2}{symbolText}");
    }
}

static void DumpWindowsCeSyscallLog(DreamcastRunResult result, string path)
{
    using var writer = CreateTextLog(path);
    foreach (var step in result.TraceLog.Where(step => (step.Pc & 1) != 0 && (step.Pc & 0xFFFF_0000) == 0xFFFF_0000))
    {
        var symbol = DreamcastSymbolSummary.FromSymbol(result.Load.FindNearestSymbol(step.Pc), step.Pc);
        var symbolText = symbol is null ? string.Empty : $" ; {symbol.Display}";
        writer.WriteLine($"#{step.Instruction}: 0x{step.Pc:X8}: 0x{step.Opcode:X4}  {step.Trace}{symbolText}");
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
        var pc = access.Pc is { } accessPc ? $", pc=0x{accessPc:X8}" : string.Empty;
        writer.WriteLine($"{access.Kind}: domain={DreamcastDeviceDomainClassifier.Classify(access)}, addr=0x{access.Address:X8}, size={access.Size}, value=0x{access.Value:X8}{pc}");
    }
}

static void DumpMemoryWriteLog(DreamcastRunResult result, string path)
{
    using var writer = CreateTextLog(path);
    foreach (var access in result.WatchedMemoryWrites)
    {
        var pc = access.Pc is { } watchedPc ? $", pc=0x{watchedPc:X8}" : string.Empty;
        var previous = access.PreviousValue is { } previousValue ? $", previous=0x{previousValue:X8}" : string.Empty;
        var producer = access.Opcode is null ? string.Empty : $", {DreamcastMemoryAccessProducerFormatter.Format(access)}";
        writer.WriteLine($"{access.Kind}: addr=0x{access.Address:X8}, size={access.Size}, value=0x{access.Value:X8}{previous}{pc}{producer}");
    }
}

static void DumpMemoryReadLog(DreamcastRunResult result, string path)
{
    using var writer = CreateTextLog(path);
    foreach (var access in result.WatchedMemoryReads)
    {
        var pc = access.Pc is { } watchedPc ? $", pc=0x{watchedPc:X8}" : string.Empty;
        var producer = access.Opcode is null ? string.Empty : $", {DreamcastMemoryAccessProducerFormatter.Format(access)}";
        writer.WriteLine($"{access.Kind}: addr=0x{access.Address:X8}, size={access.Size}, value=0x{access.Value:X8}{pc}{producer}");
    }
}

static void DumpMemorySnapshotLog(DreamcastRunResult result, string path)
{
    if (result.FinalMemorySnapshot is null)
    {
        throw new InvalidDataException("Final memory snapshot was not captured for this run.");
    }

    using var writer = CreateTextLog(path);
    DreamcastMemorySnapshotLogWriter.WriteText(writer, result.FinalMemorySnapshot);
}

static IReadOnlyList<WindowsCeSchedulerSnapshotField> WindowsCeSchedulerSnapshotFields() =>
[
    new(0x8C13_1884u, "kernel-data+0x1884"),
    new(0x8C13_1888u, "kernel-tick-total"),
    new(0x8C13_188Cu, "kernel-tick-delta"),
    new(0x8C13_1894u, "current-thread-object"),
    new(0x8C13_1898u, "kernel-data+0x1898"),
    new(0x8C13_1AA4u, "scheduler-dispatch-state"),
    new(0x8C13_1AA8u, "scheduler-dispatch-next"),
    new(0x8C13_1B24u, "module-or-file-list-root"),
    new(0x8C13_1D14u, "timer-wheel-max-delta"),
    new(0x8C13_1D4Cu, "callback-allocation-slot"),
    new(0x8C13_64F4u, "scheduler-pending-tick-delta"),
    new(0x8C13_64FCu, "runqueue-or-thread-list-next"),
    new(0x8C13_6524u, "scheduler-wait-active-flag"),
    new(0x8C13_6540u, "current-wait-delta"),
    new(0x8C13_6544u, "next-wait-delta"),
    new(0x8C13_6548u, "timer-wheel-slot0-head"),
    new(0x8C13_654Cu, "timer-wheel-slot0-tail"),
    new(0x8C13_6550u, "timer-wheel-slot1-head"),
    new(0x8C13_6554u, "timer-wheel-slot1-tail"),
    new(0x8C13_6558u, "timer-wheel-slot2-head"),
    new(0x8C13_655Cu, "timer-wheel-slot2-tail"),
    new(0x8C13_6560u, "timer-wheel-slot3-head"),
    new(0x8C13_6564u, "timer-wheel-slot3-tail"),
    new(0x8C13_6568u, "timer-wheel-slot4-head"),
    new(0x8C13_656Cu, "timer-wheel-slot4-tail"),
    new(0x8C13_6570u, "timer-wheel-slot5-head"),
    new(0x8C13_6574u, "timer-wheel-slot5-tail"),
    new(0x8C13_6578u, "timer-wheel-slot6-head"),
    new(0x8C13_657Cu, "timer-wheel-slot6-tail"),
    new(0x8C13_6580u, "timer-wheel-slot7-head"),
    new(0x8C13_6584u, "timer-wheel-slot7-tail"),
    new(0x8C13_659Cu, "wake-or-time-state"),
    new(0x8C13_65ACu, "scheduler-expired-list-head"),
    new(0x8C13_6664u, "scheduler-tail-state")
];

static IReadOnlyList<AddressRange> WindowsCeSchedulerSnapshotRanges() =>
[
    new(0x8C13_1880u, 0x8C13_18A0u),
    new(0x8C13_1AA0u, 0x8C13_1AB0u),
    new(0x8C13_1B20u, 0x8C13_1B28u),
    new(0x8C13_1D10u, 0x8C13_1D50u),
    new(0x8C13_64F0u, 0x8C13_6668u),
    new(0x8C13_7000u, 0x8C13_77FFu),
    new(0x8C13_8A60u, 0x8C13_8ADFu),
    new(0x8CEE_E000u, 0x8CEE_EFFFu),
    new(0x01E4_C000u, 0x01E4_CFFFu)
];

static IReadOnlyList<WindowsCeSchedulerPointerSnapshot> WindowsCeSchedulerPointerSnapshots() =>
[
    new(0x8C13_1894u, "current-thread-object", 0x100u),
    new(0x8C13_1B24u, "module-or-file-list-root", 0x120u)
];

static IReadOnlyList<uint> WindowsCeSchedulerNestedPointerOffsets() =>
[
    0x0Cu,
    0x10u,
    0x1Cu,
    0x20u,
    0x24u,
    0x34u,
    0x48u,
    0xACu,
    0xB0u
];

static IReadOnlyList<WindowsCeSchedulerObjectKeyField> WindowsCeSchedulerObjectKeyFields() =>
[
    new(0x00Cu, "link-or-source-a"),
    new(0x010u, "link-or-source-b"),
    new(0x014u, "state-or-flags"),
    new(0x018u, "base-or-entry"),
    new(0x01Cu, "wait-link-or-copy-source"),
    new(0x020u, "handler-or-list"),
    new(0x024u, "priority-or-flags"),
    new(0x034u, "owner-or-thread"),
    new(0x038u, "derived-base"),
    new(0x048u, "metadata-or-thread-copy"),
    new(0x04Cu, "metadata-flags"),
    new(0x054u, "metadata-block"),
    new(0x058u, "metadata-tag"),
    new(0x05Cu, "mapped-entry-or-size"),
    new(0x060u, "mapped-base"),
    new(0x064u, "mapped-size")
];

static void DumpWindowsCeSchedulerLog(DreamcastRunResult result, string path)
{
    if (result.FinalMemorySnapshot is null)
    {
        throw new InvalidDataException("Final memory snapshot was not captured for this run.");
    }

    using var writer = CreateTextLog(path);
    writer.WriteLine("# Windows CE scheduler snapshot");
    writer.WriteLine(string.Create(CultureInfo.InvariantCulture, $"# instructions={result.Cpu.InstructionsExecuted} pc=0x{result.Cpu.Pc:X8}"));
    writer.WriteLine("# columns: address label value signed");

    foreach (var field in WindowsCeSchedulerSnapshotFields())
    {
        if (TryReadSnapshotUInt32(result.FinalMemorySnapshot, field.Address, out var value))
        {
            writer.WriteLine(string.Create(CultureInfo.InvariantCulture, $"0x{field.Address:X8} {field.Label} value=0x{value:X8} signed={(int)value}"));
        }
        else
        {
            writer.WriteLine(string.Create(CultureInfo.InvariantCulture, $"0x{field.Address:X8} {field.Label} value=unavailable"));
        }
    }

    writer.WriteLine("# pointer snapshots");
    foreach (var pointer in WindowsCeSchedulerPointerSnapshots())
    {
        DumpWindowsCeSchedulerPointerSnapshot(writer, result.FinalMemorySnapshot, pointer);
    }
}

static bool TryReadSnapshotUInt32(DreamcastMemorySnapshot snapshot, uint address, out uint value)
{
    foreach (var range in snapshot.Ranges)
    {
        if (address < range.StartAddress
            || address + 3u > range.CapturedEndAddress)
        {
            continue;
        }

        var offset = (int)(address - range.StartAddress);
        value = (uint)(range.Bytes[offset]
            | (range.Bytes[offset + 1] << 8)
            | (range.Bytes[offset + 2] << 16)
            | (range.Bytes[offset + 3] << 24));
        return true;
    }

    value = 0;
    return false;
}

static void DumpWindowsCeSchedulerPointerSnapshot(
    TextWriter writer,
    DreamcastMemorySnapshot snapshot,
    WindowsCeSchedulerPointerSnapshot pointer)
{
    if (!TryReadSnapshotUInt32(snapshot, pointer.SourceAddress, out var target))
    {
        writer.WriteLine(string.Create(CultureInfo.InvariantCulture, $"0x{pointer.SourceAddress:X8} {pointer.Label} target=unavailable"));
        return;
    }

    if (target == 0)
    {
        writer.WriteLine(string.Create(CultureInfo.InvariantCulture, $"0x{pointer.SourceAddress:X8} {pointer.Label} target=0x00000000 (null)"));
        return;
    }

    writer.WriteLine(string.Create(CultureInfo.InvariantCulture, $"0x{pointer.SourceAddress:X8} {pointer.Label} target=0x{target:X8} bytes=0x{pointer.ByteCount:X}"));
    DumpWindowsCeSchedulerObjectWords(writer, snapshot, target, pointer.ByteCount, "  ");
    DumpWindowsCeSchedulerNestedPointerSnapshots(writer, snapshot, pointer, target);
}

static bool DumpWindowsCeSchedulerObjectWords(
    TextWriter writer,
    DreamcastMemorySnapshot snapshot,
    uint target,
    uint byteCount,
    string indent)
{
    var wroteAny = false;
    var keyFields = WindowsCeSchedulerObjectKeyFields();
    for (uint offset = 0; offset + 4u <= byteCount; offset += 4u)
    {
        if (!TryReadSnapshotUInt32(snapshot, target + offset, out var value))
        {
            continue;
        }

        var keyField = keyFields.FirstOrDefault(field => field.Offset == offset);
        if (value == 0 && keyField is null)
        {
            continue;
        }

        var label = keyField is null ? string.Empty : $" {keyField.Label}";
        writer.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{indent}+0x{offset:X3}{label} addr=0x{target + offset:X8} value=0x{value:X8} signed={(int)value}"));
        wroteAny = true;
    }

    if (!wroteAny)
    {
        writer.WriteLine($"{indent}(no nonzero captured words)");
    }

    return wroteAny;
}

static void DumpWindowsCeSchedulerNestedPointerSnapshots(
    TextWriter writer,
    DreamcastMemorySnapshot snapshot,
    WindowsCeSchedulerPointerSnapshot pointer,
    uint target)
{
    var visitedTargets = new HashSet<uint> { target };
    foreach (var offset in WindowsCeSchedulerNestedPointerOffsets())
    {
        if (offset + 4u > pointer.ByteCount
            || !TryReadSnapshotUInt32(snapshot, target + offset, out var nestedTarget)
            || nestedTarget == 0
            || !visitedTargets.Add(nestedTarget)
            || !TryReadSnapshotUInt32(snapshot, nestedTarget, out _))
        {
            continue;
        }

        writer.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  nested +0x{offset:X3} target=0x{nestedTarget:X8} bytes=0x80"));
        DumpWindowsCeSchedulerObjectWords(writer, snapshot, nestedTarget, 0x80u, "    ");
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
    var framebufferWidth = 640;
    var framebufferHeight = 480;
    string? audioWavPath = null;
    string? traceLogPath = null;
    string? pvrTaLogPath = null;
    var pvrTaLogLimit = 4096;
    string? pvrTaSpriteLogPath = null;
    var pvrTaSpriteLogLimit = 4096;
    string? pvrTaSpriteTextureSampleLogPath = null;
    var pvrTaSpriteTextureSampleLogLimit = 256;
    string? pvrTaTextureModeLogPath = null;
    var pvrTaTextureModeLogLimit = 256;
    string? pvrTaModeTableLogPath = null;
    var pvrTaModeTableLogLimit = 256;
    string? pvrTaSpriteSourceLogPath = null;
    var pvrTaSpriteSourceLogLimit = 256;
    string? pvrTaSpriteSqLogPath = null;
    var pvrTaSpriteSqLogLimit = 256;
    string? storeQueueFlushLogPath = null;
    var storeQueueFlushLogLimit = 256;
    string? pvrTaSpriteStatus = null;
    uint? traceStartPc = null;
    uint? traceEndPc = null;
    ulong? traceStartInstruction = null;
    ulong? traceEndInstruction = null;
    var tracePcRanges = new List<AddressRange>();
    var traceLogLimit = 4096;
    string? fpuAnomalyLogPath = null;
    var fpuAnomalyLimit = 4096;
    var fpuAnomalyKind = DreamcastFpuAnomalyKind.All;
    ulong? fpuAnomalyStartInstruction = null;
    ulong? fpuAnomalyEndInstruction = null;
    string? fpuAnomalyRegister = null;
    var fpuAnomalyDistinct = false;
    string? fpuWriteLogPath = null;
    var fpuWriteLimit = 4096;
    var fpuWriteRegisters = new List<string>();
    ulong? fpuWriteStartInstruction = null;
    ulong? fpuWriteEndInstruction = null;
    string? fpscrLogPath = null;
    var fpscrLimit = 4096;
    ulong? fpscrStartInstruction = null;
    ulong? fpscrEndInstruction = null;
    string? fpuSnapshotLogPath = null;
    var fpuSnapshotLimit = 4096;
    var fpuSnapshotPcRanges = new List<AddressRange>();
    ulong? fpuSnapshotStartInstruction = null;
    ulong? fpuSnapshotEndInstruction = null;
    string? cpuSnapshotLogPath = null;
    var cpuSnapshotLimit = 4096;
    var cpuSnapshotPcRanges = new List<AddressRange>();
    ulong? cpuSnapshotStartInstruction = null;
    ulong? cpuSnapshotEndInstruction = null;
    string? fpuMemoryLogPath = null;
    var fpuMemoryLimit = 4096;
    var fpuMemoryRegisters = new List<string>();
    ulong? fpuMemoryStartInstruction = null;
    ulong? fpuMemoryEndInstruction = null;
    var fpuMemoryAddressRanges = new List<AddressRange>();
    var fpuMemoryPcRanges = new List<AddressRange>();
    string? pcProfileLogPath = null;
    var pcProfileLimit = 256;
    ulong? pcProfileStartInstruction = null;
    ulong? pcProfileEndInstruction = null;
    string? windowsCeSyscallLogPath = null;
    var windowsCeSyscallLogLimit = 256;
    string? windowsCeSchedulerLogPath = null;
    string? deviceLogPath = null;
    MemoryAccessKind? deviceKind = null;
    AddressRange? deviceAddressRange = null;
    string? deviceDomain = null;
    string? memoryWriteLogPath = null;
    AddressRange? memoryWriteAddressRange = null;
    var memoryWriteAddressRanges = new List<AddressRange>();
    AddressRange? memoryWritePcRange = null;
    var memoryWritePcRanges = new List<AddressRange>();
    var memoryWriteLimit = 4096;
    var memoryWriteChangedOnly = false;
    var memoryWriteDistinct = false;
    string? memoryReadLogPath = null;
    AddressRange? memoryReadAddressRange = null;
    var memoryReadAddressRanges = new List<AddressRange>();
    AddressRange? memoryReadPcRange = null;
    var memoryReadPcRanges = new List<AddressRange>();
    var memoryReadLimit = 4096;
    string? memorySnapshotLogPath = null;
    var memorySnapshotRanges = new List<AddressRange>();
    var memorySnapshotMaxBytes = 4096;
    var memoryPokesOnPc = new List<DreamcastMemoryPokeOnPc>();
    string? mediaPath = null;
    var stopOnUnmapped = false;
    string? stopOnDeviceDomain = null;
    var initialStackPointer = 0x8D00_0000u;
    var initialStatusRegister = 0u;
    bool? seedInitialVBlank = null;

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
            case "--seed-initial-vblank":
                seedInitialVBlank = true;
                break;
            case "--no-initial-vblank":
                seedInitialVBlank = false;
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
            case "--pvr-ta-log" when index + 1 < args.Length:
                pvrTaLogPath = args[index + 1];
                index++;
                break;
            case "--pvr-ta-log-limit" when index + 1 < args.Length && int.TryParse(args[index + 1], out var parsedPvrTaLogLimit):
                pvrTaLogLimit = parsedPvrTaLogLimit;
                index++;
                break;
            case "--pvr-ta-sprite-log" when index + 1 < args.Length:
                pvrTaSpriteLogPath = args[index + 1];
                index++;
                break;
            case "--pvr-ta-sprite-log-limit" when index + 1 < args.Length && int.TryParse(args[index + 1], out var parsedPvrTaSpriteLogLimit):
                pvrTaSpriteLogLimit = parsedPvrTaSpriteLogLimit;
                index++;
                break;
            case "--pvr-ta-sprite-texture-sample-log" when index + 1 < args.Length:
                pvrTaSpriteTextureSampleLogPath = args[index + 1];
                index++;
                break;
            case "--pvr-ta-sprite-texture-sample-log-limit" when index + 1 < args.Length && int.TryParse(args[index + 1], out var parsedPvrTaSpriteTextureSampleLogLimit):
                pvrTaSpriteTextureSampleLogLimit = parsedPvrTaSpriteTextureSampleLogLimit;
                index++;
                break;
            case "--pvr-ta-texture-mode-log" when index + 1 < args.Length:
                pvrTaTextureModeLogPath = args[index + 1];
                index++;
                break;
            case "--pvr-ta-texture-mode-log-limit" when index + 1 < args.Length && int.TryParse(args[index + 1], out var parsedPvrTaTextureModeLogLimit):
                pvrTaTextureModeLogLimit = parsedPvrTaTextureModeLogLimit;
                index++;
                break;
            case "--pvr-ta-mode-table-log" when index + 1 < args.Length:
                pvrTaModeTableLogPath = args[index + 1];
                index++;
                break;
            case "--pvr-ta-mode-table-log-limit" when index + 1 < args.Length && int.TryParse(args[index + 1], out var parsedPvrTaModeTableLogLimit):
                pvrTaModeTableLogLimit = parsedPvrTaModeTableLogLimit;
                index++;
                break;
            case "--pvr-ta-sprite-source-log" when index + 1 < args.Length:
                pvrTaSpriteSourceLogPath = args[index + 1];
                index++;
                break;
            case "--pvr-ta-sprite-source-log-limit" when index + 1 < args.Length && int.TryParse(args[index + 1], out var parsedPvrTaSpriteSourceLogLimit):
                pvrTaSpriteSourceLogLimit = parsedPvrTaSpriteSourceLogLimit;
                index++;
                break;
            case "--pvr-ta-sprite-sq-log" when index + 1 < args.Length:
                pvrTaSpriteSqLogPath = args[index + 1];
                index++;
                break;
            case "--pvr-ta-sprite-sq-log-limit" when index + 1 < args.Length && int.TryParse(args[index + 1], out var parsedPvrTaSpriteSqLogLimit):
                pvrTaSpriteSqLogLimit = parsedPvrTaSpriteSqLogLimit;
                index++;
                break;
            case "--store-queue-flush-log" when index + 1 < args.Length:
                storeQueueFlushLogPath = args[index + 1];
                index++;
                break;
            case "--store-queue-flush-log-limit" when index + 1 < args.Length && int.TryParse(args[index + 1], out var parsedStoreQueueFlushLogLimit):
                storeQueueFlushLogLimit = parsedStoreQueueFlushLogLimit;
                index++;
                break;
            case "--pvr-ta-sprite-status" when index + 1 < args.Length:
                pvrTaSpriteStatus = ParsePvrTaSpriteStatus(args[index + 1]);
                index++;
                break;
            case "--trace-pc" when index + 1 < args.Length:
                (traceStartPc, traceEndPc) = ParseAddressRange(args[index + 1]);
                tracePcRanges.Add(new AddressRange(traceStartPc ?? 0, traceEndPc ?? traceStartPc ?? uint.MaxValue));
                index++;
                break;
            case "--trace-instruction" when index + 1 < args.Length:
                (traceStartInstruction, traceEndInstruction) = ParseInstructionRange(args[index + 1]);
                index++;
                break;
            case "--trace-log-limit" when index + 1 < args.Length && int.TryParse(args[index + 1], out var parsedTraceLogLimit):
                traceLogLimit = parsedTraceLogLimit;
                index++;
                break;
            case "--fpu-anomaly-log" when index + 1 < args.Length:
                fpuAnomalyLogPath = args[index + 1];
                index++;
                break;
            case "--fpu-anomaly-limit" when index + 1 < args.Length && int.TryParse(args[index + 1], out var parsedFpuAnomalyLimit):
                fpuAnomalyLimit = parsedFpuAnomalyLimit;
                index++;
                break;
            case "--fpu-anomaly-kind" when index + 1 < args.Length:
                fpuAnomalyKind = ParseFpuAnomalyKind(args[index + 1]);
                index++;
                break;
            case "--fpu-anomaly-instruction" when index + 1 < args.Length:
                (fpuAnomalyStartInstruction, fpuAnomalyEndInstruction) = ParseInstructionRange(args[index + 1]);
                index++;
                break;
            case "--fpu-anomaly-register" when index + 1 < args.Length:
                fpuAnomalyRegister = args[index + 1];
                index++;
                break;
            case "--fpu-anomaly-distinct":
                fpuAnomalyDistinct = true;
                break;
            case "--fpu-write-log" when index + 1 < args.Length:
                fpuWriteLogPath = args[index + 1];
                index++;
                break;
            case "--fpu-write-limit" when index + 1 < args.Length && int.TryParse(args[index + 1], out var parsedFpuWriteLimit):
                fpuWriteLimit = parsedFpuWriteLimit;
                index++;
                break;
            case "--fpu-write-register" when index + 1 < args.Length:
                fpuWriteRegisters.Add(args[index + 1]);
                index++;
                break;
            case "--fpu-write-instruction" when index + 1 < args.Length:
                (fpuWriteStartInstruction, fpuWriteEndInstruction) = ParseInstructionRange(args[index + 1]);
                index++;
                break;
            case "--fpscr-log" when index + 1 < args.Length:
                fpscrLogPath = args[index + 1];
                index++;
                break;
            case "--fpscr-limit" when index + 1 < args.Length && int.TryParse(args[index + 1], out var parsedFpscrLimit):
                fpscrLimit = parsedFpscrLimit;
                index++;
                break;
            case "--fpscr-instruction" when index + 1 < args.Length:
                (fpscrStartInstruction, fpscrEndInstruction) = ParseInstructionRange(args[index + 1]);
                index++;
                break;
            case "--fpu-snapshot-log" when index + 1 < args.Length:
                fpuSnapshotLogPath = args[index + 1];
                index++;
                break;
            case "--fpu-snapshot-limit" when index + 1 < args.Length && int.TryParse(args[index + 1], out var parsedFpuSnapshotLimit):
                fpuSnapshotLimit = parsedFpuSnapshotLimit;
                index++;
                break;
            case "--fpu-snapshot-pc" when index + 1 < args.Length:
                var (snapshotStart, snapshotEnd) = ParseAddressRange(args[index + 1]);
                fpuSnapshotPcRanges.Add(new AddressRange(snapshotStart ?? 0, snapshotEnd ?? snapshotStart ?? uint.MaxValue));
                index++;
                break;
            case "--fpu-snapshot-instruction" when index + 1 < args.Length:
                (fpuSnapshotStartInstruction, fpuSnapshotEndInstruction) = ParseInstructionRange(args[index + 1]);
                index++;
                break;
            case "--cpu-snapshot-log" when index + 1 < args.Length:
                cpuSnapshotLogPath = args[index + 1];
                index++;
                break;
            case "--cpu-snapshot-limit" when index + 1 < args.Length && int.TryParse(args[index + 1], out var parsedCpuSnapshotLimit):
                cpuSnapshotLimit = parsedCpuSnapshotLimit;
                index++;
                break;
            case "--cpu-snapshot-pc" when index + 1 < args.Length:
                var (cpuSnapshotStart, cpuSnapshotEnd) = ParseAddressRange(args[index + 1]);
                cpuSnapshotPcRanges.Add(new AddressRange(cpuSnapshotStart ?? 0, cpuSnapshotEnd ?? cpuSnapshotStart ?? uint.MaxValue));
                index++;
                break;
            case "--cpu-snapshot-instruction" when index + 1 < args.Length:
                (cpuSnapshotStartInstruction, cpuSnapshotEndInstruction) = ParseInstructionRange(args[index + 1]);
                index++;
                break;
            case "--fpu-memory-log" when index + 1 < args.Length:
                fpuMemoryLogPath = args[index + 1];
                index++;
                break;
            case "--fpu-memory-limit" when index + 1 < args.Length && int.TryParse(args[index + 1], out var parsedFpuMemoryLimit):
                fpuMemoryLimit = parsedFpuMemoryLimit;
                index++;
                break;
            case "--fpu-memory-register" when index + 1 < args.Length:
                fpuMemoryRegisters.Add(args[index + 1]);
                index++;
                break;
            case "--fpu-memory-instruction" when index + 1 < args.Length:
                (fpuMemoryStartInstruction, fpuMemoryEndInstruction) = ParseInstructionRange(args[index + 1]);
                index++;
                break;
            case "--fpu-memory-address" when index + 1 < args.Length:
                var (fpuMemoryStart, fpuMemoryEnd) = ParseAddressRange(args[index + 1]);
                fpuMemoryAddressRanges.Add(new AddressRange(fpuMemoryStart ?? 0, fpuMemoryEnd ?? fpuMemoryStart ?? uint.MaxValue));
                index++;
                break;
            case "--fpu-memory-pc" when index + 1 < args.Length:
                var (fpuMemoryStartPc, fpuMemoryEndPc) = ParseAddressRange(args[index + 1]);
                fpuMemoryPcRanges.Add(new AddressRange(fpuMemoryStartPc ?? 0, fpuMemoryEndPc ?? fpuMemoryStartPc ?? uint.MaxValue));
                index++;
                break;
            case "--pc-profile-log" when index + 1 < args.Length:
                pcProfileLogPath = args[index + 1];
                index++;
                break;
            case "--pc-profile-limit" when index + 1 < args.Length && int.TryParse(args[index + 1], out var parsedPcProfileLimit):
                pcProfileLimit = parsedPcProfileLimit;
                index++;
                break;
            case "--pc-profile-instruction" when index + 1 < args.Length:
                (pcProfileStartInstruction, pcProfileEndInstruction) = ParseInstructionRange(args[index + 1]);
                index++;
                break;
            case "--wince-syscall-log" when index + 1 < args.Length:
                windowsCeSyscallLogPath = args[index + 1];
                index++;
                break;
            case "--wince-syscall-log-limit" when index + 1 < args.Length && int.TryParse(args[index + 1], out var parsedWindowsCeSyscallLogLimit):
                windowsCeSyscallLogLimit = parsedWindowsCeSyscallLogLimit;
                index++;
                break;
            case "--wince-scheduler-log" when index + 1 < args.Length:
                windowsCeSchedulerLogPath = args[index + 1];
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
                memoryWriteAddressRanges.Add(memoryWriteAddressRange);
                index++;
                break;
            case "--memory-write-pc" when index + 1 < args.Length:
                var (memoryWritePcStart, memoryWritePcEnd) = ParseAddressRange(args[index + 1]);
                memoryWritePcRange = new AddressRange(memoryWritePcStart ?? 0, memoryWritePcEnd ?? memoryWritePcStart ?? uint.MaxValue);
                memoryWritePcRanges.Add(memoryWritePcRange);
                index++;
                break;
            case "--memory-write-limit" when index + 1 < args.Length && int.TryParse(args[index + 1], out var parsedMemoryWriteLimit):
                memoryWriteLimit = parsedMemoryWriteLimit;
                index++;
                break;
            case "--memory-write-changed-only":
                memoryWriteChangedOnly = true;
                break;
            case "--memory-write-distinct":
                memoryWriteDistinct = true;
                break;
            case "--memory-read-log" when index + 1 < args.Length:
                memoryReadLogPath = args[index + 1];
                index++;
                break;
            case "--memory-read-address" when index + 1 < args.Length:
                var (memoryReadStart, memoryReadEnd) = ParseAddressRange(args[index + 1]);
                memoryReadAddressRange = new AddressRange(memoryReadStart ?? 0, memoryReadEnd ?? memoryReadStart ?? uint.MaxValue);
                memoryReadAddressRanges.Add(memoryReadAddressRange);
                index++;
                break;
            case "--memory-read-pc" when index + 1 < args.Length:
                var (memoryReadPcStart, memoryReadPcEnd) = ParseAddressRange(args[index + 1]);
                memoryReadPcRange = new AddressRange(memoryReadPcStart ?? 0, memoryReadPcEnd ?? memoryReadPcStart ?? uint.MaxValue);
                memoryReadPcRanges.Add(memoryReadPcRange);
                index++;
                break;
            case "--memory-read-limit" when index + 1 < args.Length && int.TryParse(args[index + 1], out var parsedMemoryReadLimit):
                memoryReadLimit = parsedMemoryReadLimit;
                index++;
                break;
            case "--memory-snapshot-log" when index + 1 < args.Length:
                memorySnapshotLogPath = args[index + 1];
                index++;
                break;
            case "--memory-snapshot-address" when index + 1 < args.Length:
                var (memorySnapshotStart, memorySnapshotEnd) = ParseAddressRange(args[index + 1]);
                memorySnapshotRanges.Add(new AddressRange(memorySnapshotStart ?? 0, memorySnapshotEnd ?? memorySnapshotStart ?? uint.MaxValue));
                index++;
                break;
            case "--memory-snapshot-max-bytes" when index + 1 < args.Length && int.TryParse(args[index + 1], out var parsedMemorySnapshotMaxBytes):
                memorySnapshotMaxBytes = parsedMemorySnapshotMaxBytes;
                index++;
                break;
            case "--memory-poke-pc" when index + 1 < args.Length:
                memoryPokesOnPc.Add(ParseMemoryPokeOnPc(args[index + 1]));
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

    if (pvrTaLogLimit < 0)
    {
        throw new InvalidDataException("--pvr-ta-log-limit must be zero or greater.");
    }

    if (pvrTaSpriteLogLimit < 0)
    {
        throw new InvalidDataException("--pvr-ta-sprite-log-limit must be zero or greater.");
    }

    if (pvrTaSpriteTextureSampleLogLimit < 0)
    {
        throw new InvalidDataException("--pvr-ta-sprite-texture-sample-log-limit must be zero or greater.");
    }

    if (pvrTaTextureModeLogLimit < 0)
    {
        throw new InvalidDataException("--pvr-ta-texture-mode-log-limit must be zero or greater.");
    }

    if (pvrTaModeTableLogLimit < 0)
    {
        throw new InvalidDataException("--pvr-ta-mode-table-log-limit must be zero or greater.");
    }

    if (pvrTaSpriteSourceLogLimit < 0)
    {
        throw new InvalidDataException("--pvr-ta-sprite-source-log-limit must be zero or greater.");
    }

    if (pvrTaSpriteSqLogLimit < 0)
    {
        throw new InvalidDataException("--pvr-ta-sprite-sq-log-limit must be zero or greater.");
    }

    if (storeQueueFlushLogLimit < 0)
    {
        throw new InvalidDataException("--store-queue-flush-log-limit must be zero or greater.");
    }

    if (fpuAnomalyLimit < 0)
    {
        throw new InvalidDataException("--fpu-anomaly-limit must be zero or greater.");
    }

    if (fpuWriteLimit < 0)
    {
        throw new InvalidDataException("--fpu-write-limit must be zero or greater.");
    }

    if (fpscrLimit < 0)
    {
        throw new InvalidDataException("--fpscr-limit must be zero or greater.");
    }

    if (fpuSnapshotLimit < 0)
    {
        throw new InvalidDataException("--fpu-snapshot-limit must be zero or greater.");
    }

    if (cpuSnapshotLimit < 0)
    {
        throw new InvalidDataException("--cpu-snapshot-limit must be zero or greater.");
    }

    if (fpuMemoryLimit < 0)
    {
        throw new InvalidDataException("--fpu-memory-limit must be zero or greater.");
    }

    if (pcProfileLimit < 0)
    {
        throw new InvalidDataException("--pc-profile-limit must be zero or greater.");
    }

    if (windowsCeSyscallLogLimit < 0)
    {
        throw new InvalidDataException("--wince-syscall-log-limit must be zero or greater.");
    }

    if (memoryWriteLimit < 0)
    {
        throw new InvalidDataException("--memory-write-limit must be zero or greater.");
    }

    if (memoryReadLimit < 0)
    {
        throw new InvalidDataException("--memory-read-limit must be zero or greater.");
    }

    if (memorySnapshotMaxBytes < 0)
    {
        throw new InvalidDataException("--memory-snapshot-max-bytes must be zero or greater.");
    }

    if (memorySnapshotLogPath is not null && memorySnapshotRanges.Count == 0)
    {
        throw new InvalidDataException("--memory-snapshot-log requires at least one --memory-snapshot-address range.");
    }

    var needsTraceCapture = traceLogPath is not null || windowsCeSyscallLogPath is not null;
    var effectiveTraceLogLimit = windowsCeSyscallLogPath is null
        ? traceLogLimit
        : traceLogPath is null
            ? windowsCeSyscallLogLimit
            : Math.Max(traceLogLimit, windowsCeSyscallLogLimit);
    if (windowsCeSyscallLogPath is not null
        && (traceLogPath is null || traceStartPc is not null || traceEndPc is not null || tracePcRanges.Count > 0))
    {
        tracePcRanges.Add(new AddressRange(0xFFFF_0000u, 0xFFFF_FFFFu));
    }

    var traceCapture = !needsTraceCapture
        ? null
        : new DreamcastTraceCaptureOptions(
            traceStartPc,
            traceEndPc,
            effectiveTraceLogLimit,
            tracePcRanges.Count == 0
                ? null
                : tracePcRanges.Select(range => new DreamcastTracePcRange(range.Start, range.End)).ToArray(),
            traceStartInstruction,
            traceEndInstruction);
    var media = mediaPath is null
        ? null
        : DreamcastMediaImageLoader.LoadFromFile(mediaPath);
    var needsSpriteSqWrites = pvrTaSpriteSqLogPath is not null;
    var spriteSqWriteLimit = needsSpriteSqWrites
        ? Math.Max(memoryWriteLimit, Math.Max(1, pvrTaSpriteSqLogLimit) * 32)
        : memoryWriteLimit;
    var defaultSpriteSqAddressRange = needsSpriteSqWrites && memoryWriteAddressRanges.Count == 0 && memoryWriteAddressRange is null
        ? new AddressRange(0xE000_0000u, 0xE000_005Fu)
        : memoryWriteAddressRange;
    var memoryWriteWatch = memoryWriteLogPath is null && !needsSpriteSqWrites
        ? null
        : new DreamcastMemoryWriteWatch(
            defaultSpriteSqAddressRange?.Start ?? 0,
            defaultSpriteSqAddressRange?.End ?? uint.MaxValue,
            spriteSqWriteLimit,
            memoryWriteAddressRanges.Count == 0
                ? null
                : memoryWriteAddressRanges.Select(range => new DreamcastMemoryAddressRange(range.Start, range.End)).ToArray(),
            memoryWritePcRange?.Start,
            memoryWritePcRange?.End,
            memoryWritePcRanges.Count == 0
                ? null
                : memoryWritePcRanges.Select(range => new DreamcastMemoryAddressRange(range.Start, range.End)).ToArray(),
            memoryWriteChangedOnly,
            memoryWriteDistinct);
    var memoryReadWatch = memoryReadLogPath is null
        ? null
        : new DreamcastMemoryReadWatch(
            memoryReadAddressRange?.Start ?? 0,
            memoryReadAddressRange?.End ?? uint.MaxValue,
            memoryReadLimit,
            memoryReadAddressRanges.Count == 0
                ? null
                : memoryReadAddressRanges.Select(range => new DreamcastMemoryAddressRange(range.Start, range.End)).ToArray(),
            memoryReadPcRange?.Start,
            memoryReadPcRange?.End,
            memoryReadPcRanges.Count == 0
                ? null
                : memoryReadPcRanges.Select(range => new DreamcastMemoryAddressRange(range.Start, range.End)).ToArray());
    var finalMemorySnapshotRanges = new List<AddressRange>(memorySnapshotRanges);
    if (windowsCeSchedulerLogPath is not null)
    {
        finalMemorySnapshotRanges.AddRange(WindowsCeSchedulerSnapshotRanges());
    }

    var finalMemorySnapshot = finalMemorySnapshotRanges.Count == 0
        ? null
        : new DreamcastFinalMemorySnapshotOptions(
            finalMemorySnapshotRanges.Select(range => new DreamcastMemoryAddressRange(range.Start, range.End)).ToArray(),
            memorySnapshotMaxBytes);

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
            MemoryWriteWatch: memoryWriteWatch,
            MemoryReadWatch: memoryReadWatch,
            FpuAnomalyCapture: fpuAnomalyLogPath is null ? null : new DreamcastFpuAnomalyCaptureOptions(fpuAnomalyLimit, fpuAnomalyKind, fpuAnomalyStartInstruction, fpuAnomalyEndInstruction, fpuAnomalyRegister, fpuAnomalyDistinct),
            FpuRegisterWatch: fpuWriteLogPath is null ? null : new DreamcastFpuRegisterWatchOptions(
                fpuWriteLimit,
                fpuWriteRegisters.Count == 1 ? fpuWriteRegisters[0] : null,
                fpuWriteStartInstruction,
                fpuWriteEndInstruction,
                fpuWriteRegisters.Count > 1 ? fpuWriteRegisters.ToArray() : null),
            FpscrWatch: fpscrLogPath is null ? null : new DreamcastFpscrWatchOptions(fpscrLimit, fpscrStartInstruction, fpscrEndInstruction),
            FpuSnapshotCapture: fpuSnapshotLogPath is null ? null : new DreamcastFpuSnapshotCaptureOptions(
                fpuSnapshotLimit,
                fpuSnapshotPcRanges.Count == 0
                    ? null
                    : fpuSnapshotPcRanges.Select(range => new DreamcastTracePcRange(range.Start, range.End)).ToArray(),
                fpuSnapshotStartInstruction,
                fpuSnapshotEndInstruction),
            CpuSnapshotCapture: cpuSnapshotLogPath is null ? null : new DreamcastCpuSnapshotCaptureOptions(
                cpuSnapshotLimit,
                cpuSnapshotPcRanges.Count == 0
                    ? null
                    : cpuSnapshotPcRanges.Select(range => new DreamcastTracePcRange(range.Start, range.End)).ToArray(),
                cpuSnapshotStartInstruction,
                cpuSnapshotEndInstruction),
            FpuMemoryWatch: fpuMemoryLogPath is null ? null : new DreamcastFpuMemoryWatchOptions(
                fpuMemoryLimit,
                fpuMemoryRegisters.Count == 1 ? fpuMemoryRegisters[0] : null,
                fpuMemoryStartInstruction,
                fpuMemoryEndInstruction,
                fpuMemoryAddressRanges.Count == 0
                    ? null
                    : fpuMemoryAddressRanges.Select(range => new DreamcastMemoryAddressRange(range.Start, range.End)).ToArray(),
                fpuMemoryRegisters.Count > 1 ? fpuMemoryRegisters.ToArray() : null,
                fpuMemoryPcRanges.Count == 0
                    ? null
                    : fpuMemoryPcRanges.Select(range => new DreamcastTracePcRange(range.Start, range.End)).ToArray()),
            PcProfile: pcProfileLogPath is null ? null : new DreamcastPcProfileOptions(pcProfileLimit, pcProfileStartInstruction, pcProfileEndInstruction),
            FinalMemorySnapshot: finalMemorySnapshot,
            MemoryPokesOnPc: memoryPokesOnPc.Count == 0 ? null : memoryPokesOnPc.ToArray(),
            SeedInitialVBlank: seedInitialVBlank == true),
        seedInitialVBlank,
        emitJson,
        framebufferDumpPath,
        framebufferWidth,
        framebufferHeight,
        audioWavPath,
        traceLogPath,
        pvrTaLogPath,
        pvrTaLogLimit,
        pvrTaSpriteLogPath,
        pvrTaSpriteLogLimit,
        pvrTaSpriteTextureSampleLogPath,
        pvrTaSpriteTextureSampleLogLimit,
        pvrTaTextureModeLogPath,
        pvrTaTextureModeLogLimit,
        pvrTaModeTableLogPath,
        pvrTaModeTableLogLimit,
        pvrTaSpriteSourceLogPath,
        pvrTaSpriteSourceLogLimit,
        pvrTaSpriteSqLogPath,
        pvrTaSpriteSqLogLimit,
        storeQueueFlushLogPath,
        storeQueueFlushLogLimit,
        pvrTaSpriteStatus,
        fpuAnomalyLogPath,
        fpuWriteLogPath,
        fpscrLogPath,
        fpuSnapshotLogPath,
        cpuSnapshotLogPath,
        fpuMemoryLogPath,
        pcProfileLogPath,
        windowsCeSyscallLogPath,
        windowsCeSchedulerLogPath,
        deviceLogPath,
        deviceKind,
        deviceAddressRange,
        deviceDomain,
        memoryWriteLogPath,
        memoryReadLogPath,
        memorySnapshotLogPath);
}

static (int Width, int Height) ParseFramebufferSize(string text)
{
    var parts = text.Split('x', 2, StringSplitOptions.TrimEntries);
    if (parts.Length != 2 || !int.TryParse(parts[0], out var width) || !int.TryParse(parts[1], out var height) || width <= 0 || height <= 0)
    {
        throw new InvalidDataException("--framebuffer-size must use WIDTHxHEIGHT, for example 640x480.");
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

static DreamcastMemoryPokeOnPc ParseMemoryPokeOnPc(string text)
{
    var parts = text.Split(':', StringSplitOptions.TrimEntries);
    if (parts.Length != 3 || parts.Any(string.IsNullOrWhiteSpace))
    {
        throw new InvalidDataException("--memory-poke-pc must use PC:ADDRESS:VALUE, for example 0x8C010002:0x8C00C000:0x12345678.");
    }

    return new DreamcastMemoryPokeOnPc(ParseAddress(parts[0]), ParseAddress(parts[1]), ParseAddress(parts[2]));
}

static (ulong? Start, ulong? End) ParseInstructionRange(string text)
{
    var separator = text.IndexOf('-');
    if (separator < 0)
    {
        var instruction = ParseUnsigned64(text, "instruction");
        return (instruction, instruction);
    }

    var start = string.IsNullOrWhiteSpace(text[..separator]) ? (ulong?)null : ParseUnsigned64(text[..separator], "instruction");
    var end = string.IsNullOrWhiteSpace(text[(separator + 1)..]) ? (ulong?)null : ParseUnsigned64(text[(separator + 1)..], "instruction");
    if (start is { } startValue && end is { } endValue && endValue < startValue)
    {
        throw new InvalidDataException("Instruction ranges must be ordered from low to high.");
    }

    return (start, end);
}

static ulong ParseUnsigned64(string text, string valueName)
{
    var value = text.Trim();
    if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
    {
        value = value[2..];
        if (ulong.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var parsedHex))
        {
            return parsedHex;
        }
    }
    else if (ulong.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedDecimal))
    {
        return parsedDecimal;
    }

    throw new InvalidDataException($"Invalid {valueName}: {text}");
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
            or DreamcastDeviceDomainClassifier.External
            or DreamcastDeviceDomainClassifier.Holly
            or DreamcastDeviceDomainClassifier.Maple
            or DreamcastDeviceDomainClassifier.Modem
            or DreamcastDeviceDomainClassifier.Pvr
            or DreamcastDeviceDomainClassifier.Scif
            or DreamcastDeviceDomainClassifier.Sh4
            or DreamcastDeviceDomainClassifier.Tmu
            or DreamcastDeviceDomainClassifier.Unmapped
            or DreamcastDeviceDomainClassifier.Other => normalized,
        _ => throw new InvalidDataException($"Unknown device domain: {text}")
    };
}

static DreamcastFpuAnomalyKind ParseFpuAnomalyKind(string text)
{
    var normalized = text.Trim().ToLowerInvariant();
    return normalized switch
    {
        "all" => DreamcastFpuAnomalyKind.All,
        "nan" => DreamcastFpuAnomalyKind.NaN,
        "infinity" or "inf" => DreamcastFpuAnomalyKind.Infinity,
        _ => throw new InvalidDataException($"Unknown FPU anomaly kind: {text}")
    };
}

static string ParsePvrTaSpriteStatus(string text)
{
    var normalized = text.Trim().ToLowerInvariant();
    return normalized switch
    {
        "renderable" => "renderable",
        "degenerate" => "degenerate",
        "nonfinite" => "nonfinite",
        "all" => throw new InvalidDataException("--pvr-ta-sprite-status filters one status; omit it to include all sprites."),
        _ => throw new InvalidDataException($"Unknown PVR TA sprite status: {text}")
    };
}

static string FormatController(DreamcastControllerState state) =>
    $"buttons={state.Buttons}, ltrig={state.LeftTrigger}, rtrig={state.RightTrigger}, joy=({state.JoyX},{state.JoyY}), joy2=({state.Joy2X},{state.Joy2Y})";

static string FormatPvrTaLists(IReadOnlyList<DreamcastPvrTaListSummary> lists) =>
    string.Join(", ", lists.Select(list => $"{list.Region}:{list.ListTypeName ?? "none"} commands={list.CommandCount} headers={list.PolygonHeaderCount} vertices={list.VertexCount} ends={list.VertexEndOfStripCount}"));

static string FormatPvrTaStrips(IReadOnlyList<DreamcastPvrTaStripSummary> strips) =>
    string.Join(", ", strips.Select(strip => $"{strip.Region}:{strip.ListTypeName ?? "none"} vertices={strip.VertexCount} color={strip.Rgb565Hex}{FormatPvrTaStripMode(strip.HeaderPayload)} points={string.Join("/", strip.Vertices.Select(vertex => $"{vertex.X},{vertex.Y}"))}"));

static string FormatPvrTaSprites(IReadOnlyList<DreamcastPvrTaSpriteSummary> sprites) =>
    string.Join(", ", sprites.Select(sprite => $"{sprite.Region}:{sprite.ListTypeName ?? "none"} vertices={sprite.VertexCount} color={sprite.Rgb565Hex} argb={sprite.HeaderPayload.ArgbHex} tex={sprite.HeaderPayload.Mode1Fields.TextureEnabled} cmdTex={sprite.HeaderPayload.HasTexturePayload} effectiveTex={sprite.HeaderPayload.EffectiveTextureEnabled} uv16={HasPvrTaSpritePackedUv(sprite.HeaderValue)} preview={FormatPvrTaSpritePreviewStatus(sprite)}{FormatPvrTaSpriteSource(sprite)} points={string.Join("/", sprite.Vertices.Select(FormatPvrTaSpriteVertex))}"));

static string FormatPvrTaSpriteCounts(DreamcastVideoSummary video) =>
    video.PvrTaSprites.Count == 0
        ? string.Empty
        : $", taSpritePreview=renderable:{video.PvrTaRenderableSpriteCount}/degenerate:{video.PvrTaDegenerateSpriteCount}/nonfinite:{video.PvrTaNonfiniteSpriteCount}";

static string FormatPvrTaSpriteSourceGroups(IReadOnlyList<DreamcastPvrTaSpriteSourceGroupSummary> groups) =>
    string.Join(", ", groups
        .Take(8)
        .Select(group => $"{group.PreviewStatus}:{group.Count} pc=h:{group.HeaderInstructionPcHex ?? "-"}/c:{group.ControlInstructionPcHex ?? "-"}/p:{group.PayloadInstructionPcRangeHex}"));

static string FormatPvrTaSpriteShapeGroups(IReadOnlyList<DreamcastPvrTaSpriteShapeGroupSummary> groups) =>
    string.Join(", ", groups
        .Take(8)
        .Select(group => $"{group.PreviewStatus}:{group.Count} list={group.ListTypeName ?? "none"} color={group.Rgb565Hex}/argb={group.ArgbHex} tex={group.TextureEnabled} cmdTex={group.TexturePayload} uv16={group.Uv16Bit} size={group.WidthBucket}x{group.HeightBucket} rawW={FormatFloat(group.MinWidth)}/{FormatFloat(group.AverageWidth)}/{FormatFloat(group.MaxWidth)} rawH={FormatFloat(group.MinHeight)}/{FormatFloat(group.AverageHeight)}/{FormatFloat(group.MaxHeight)} fallbackPx={group.MinFallbackPixels}/{FormatFloat(group.AverageFallbackPixels)}/{group.MaxFallbackPixels} pc=h:{group.HeaderInstructionPcHex ?? "-"}/c:{group.ControlInstructionPcHex ?? "-"}/p:{group.PayloadInstructionPcRangeHex}"));

static string FormatPvrTaDiagnostics(DreamcastPvrTaDiagnosticsSummary diagnostics)
{
    var textures = diagnostics.TextureModes.Count == 0
        ? "none"
        : string.Join("/", diagnostics.TextureModes.Take(4).Select(FormatPvrTaTextureMode));
    return
        $"previewW={diagnostics.PreviewWidth} fbNonZero={diagnostics.FramebufferNonZeroBytes} first={diagnostics.FirstNonZeroOffsetHex ?? "none"} checksum={diagnostics.FramebufferChecksumHex} " +
        $"prims=strips:{diagnostics.StripCount}/tris:{diagnostics.StripTriangleCount}/sprites:{diagnostics.SpriteCount} " +
        $"stripDrops=short:{diagnostics.DroppedShortStripCount}/zero:{diagnostics.DroppedZeroColorPrimitiveCount}/mixed:{diagnostics.DroppedMixedFlatColorStripCount} " +
        $"sprites=renderable:{diagnostics.RenderableSpriteCount}/degenerate:{diagnostics.DegenerateSpriteCount}/nonfinite:{diagnostics.NonfiniteSpriteCount} " +
        $"spriteRender={FormatPvrPreviewRenderStats(diagnostics.PreviewRenderStats)} " +
        $"bounds=all:{FormatPvrTaBounds(diagnostics.CombinedBounds)} strips:{FormatPvrTaBounds(diagnostics.StripBounds)} sprites:{FormatPvrTaBounds(diagnostics.SpriteBounds)} " +
        $"clipRisk=x<0:{diagnostics.CombinedBounds.NegativeXCount}/x>=w:{diagnostics.CombinedBounds.RightClippedCount}/y<0:{diagnostics.CombinedBounds.NegativeYCount} " +
        $"zeroExtent=w:{diagnostics.CombinedBounds.ZeroWidthCount}/h:{diagnostics.CombinedBounds.ZeroHeightCount} " +
        $"tex={textures}";
}

static string FormatPvrPreviewRenderStats(DreamcastPvrPreviewRenderStatsSummary stats) =>
    $"calls:{stats.SpriteCalls}/attempts:{stats.PixelWriteAttempts}/written:{stats.PixelsWritten}/unique:{stats.UniquePixelsWritten}/zero:{stats.ZeroRgbWritePixels}/alpha:{stats.AlphaBlendedPixels}/texSample:{stats.TextureSampledPixels}/texA0:{stats.ZeroAlphaTexturePixels}/punchReject:{stats.PunchThroughRejectedPixels}/fallback:{stats.SubpixelFallbacks}/oob:{stats.OutOfBoundsWritePixels}";

static string FormatPvrTaBounds(DreamcastPvrTaBoundsSummary bounds) =>
    bounds.HasBounds
        ? $"{FormatNullableFloat(bounds.MinX)},{FormatNullableFloat(bounds.MinY)}-{FormatNullableFloat(bounds.MaxX)},{FormatNullableFloat(bounds.MaxY)}({bounds.SourceCount})"
        : "none";

static string FormatNullableFloat(float? value) =>
    value is { } concreteValue ? FormatFloat(concreteValue) : "-";

static string FormatPvrTaTextureMode(DreamcastPvrTaTextureModeGroupSummary mode) =>
    $"{mode.PrimitiveKind}:{mode.ListTypeName ?? "none"}:{(mode.TextureEnabled ? "tex" : "flat")}:vq={mode.VqEnabled}:mip={mode.MipMapEnabled}:twid={!mode.NonTwiddled}:pix={mode.PixelFormatName}x{mode.Count}";

static string FormatPvrDisplay(DreamcastPvrDisplaySummary display)
{
    var addresses =
        $"fb={display.FramebufferAddressHex ?? "-"}"
        + $"/il={display.InterlacedFramebufferAddressHex ?? "-"}"
        + $" render={display.RenderAddressHex ?? "-"}"
        + $"/alt={display.AlternateRenderAddressHex ?? "-"}";
    var ranges =
        $"clip={display.PixelClipX?.Display ?? "-"}x{display.PixelClipY?.Display ?? "-"}";
    var sizes =
        $"fbSize={display.FramebufferSizeHex ?? "-"} bitmap={display.BitmapXHex ?? "-"}x{display.BitmapYHex ?? "-"}";
    var config =
        $"cfg={display.FramebufferConfig1Hex ?? "-"}/{display.FramebufferConfig2Hex ?? "-"}"
        + $" video={display.VideoConfigHex ?? "-"} scaler={display.ScalerConfigHex ?? "-"} palette={display.PaletteConfigHex ?? "-"}";
    return $"{addresses} {ranges} {sizes} {config}";
}

static string FormatPvrTaSpritePreviewStatus(DreamcastPvrTaSpriteSummary sprite) =>
    sprite.HasRenderablePreviewArea
        ? "renderable"
        : sprite.HasFinitePreviewCoordinates ? "degenerate" : "nonfinite";

static string FormatPvrTaSpriteSource(DreamcastPvrTaSpriteSummary sprite) =>
    sprite.HeaderInstructionPcHex is null
    && sprite.ControlInstructionPcHex is null
    && sprite.FirstPayloadInstructionPcHex is null
    && sprite.LastPayloadInstructionPcHex is null
        ? string.Empty
        : $" pc=h:{sprite.HeaderInstructionPcHex ?? "-"}/c:{sprite.ControlInstructionPcHex ?? "-"}/p:{FormatPvrTaSpritePayloadPcRange(sprite)}";

static string FormatPvrTaSpritePayloadPcRange(DreamcastPvrTaSpriteSummary sprite) =>
    sprite.FirstPayloadInstructionPcHex == sprite.LastPayloadInstructionPcHex
        ? sprite.FirstPayloadInstructionPcHex ?? "-"
        : $"{sprite.FirstPayloadInstructionPcHex ?? "-"}-{sprite.LastPayloadInstructionPcHex ?? "-"}";

static string FormatPvrTaSpriteVertex(DreamcastPvrTaSpriteVertexSummary vertex)
{
    var formatted = $"{vertex.Name}:{vertex.X},{vertex.Y}:{FormatFloat(vertex.U)},{FormatFloat(vertex.V)}";
    return vertex.HasFinitePosition
        ? formatted
        : $"{formatted}[raw={vertex.XValueHex},{vertex.YValueHex},z={vertex.ZValueHex}]";
}

static bool HasPvrTaSpritePackedUv(uint headerValue) =>
    (headerValue & 0x0000_0001u) != 0;

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
    Console.WriteLine("  dcsharp run <file.elf> [--instructions count] [--trace-tail count] [--vblank-interval instructions] [--seed-initial-vblank] [--no-initial-vblank] [--controller address:state] [--controller-script address:script] [--controller-a state] [--controller-b state] [--controller-a-script script] [--dump-framebuffer path.png] [--framebuffer-size 640x480] [--audio-wav path.wav] [--trace-log path] [--trace-pc start-end] [--trace-instruction start-end] [--pvr-ta-log path] [--pvr-ta-log-limit count] [--pvr-ta-sprite-log path] [--pvr-ta-sprite-log-limit count] [--pvr-ta-sprite-texture-sample-log path] [--pvr-ta-sprite-texture-sample-log-limit count] [--pvr-ta-texture-mode-log path] [--pvr-ta-texture-mode-log-limit count] [--pvr-ta-mode-table-log path] [--pvr-ta-mode-table-log-limit count] [--pvr-ta-sprite-source-log path] [--pvr-ta-sprite-source-log-limit count] [--pvr-ta-sprite-sq-log path] [--pvr-ta-sprite-sq-log-limit count] [--store-queue-flush-log path] [--store-queue-flush-log-limit count] [--pvr-ta-sprite-status renderable|degenerate|nonfinite] [--fpu-anomaly-log path] [--fpu-anomaly-limit count] [--fpu-anomaly-kind all|nan|infinity] [--fpu-anomaly-instruction start-end] [--fpu-anomaly-register frN|xfN] [--fpu-anomaly-distinct] [--fpu-write-log path] [--fpu-write-limit count] [--fpu-write-register frN|xfN] [--fpu-write-instruction start-end] [--fpscr-log path] [--fpscr-limit count] [--fpscr-instruction start-end] [--fpu-snapshot-log path] [--fpu-snapshot-limit count] [--fpu-snapshot-pc start-end] [--fpu-snapshot-instruction start-end] [--cpu-snapshot-log path] [--cpu-snapshot-limit count] [--cpu-snapshot-pc start-end] [--cpu-snapshot-instruction start-end] [--fpu-memory-log path] [--fpu-memory-limit count] [--fpu-memory-register frN|drN] [--fpu-memory-instruction start-end] [--fpu-memory-address start-end] [--fpu-memory-pc start-end] [--pc-profile-log path] [--pc-profile-limit count] [--pc-profile-instruction start-end] [--wince-syscall-log path] [--wince-syscall-log-limit count] [--wince-scheduler-log path] [--device-log path] [--device-domain domain] [--device-kind kind] [--device-address start-end] [--memory-write-log path] [--memory-write-address start-end] [--memory-write-pc start-end] [--memory-write-limit count] [--memory-write-changed-only] [--memory-write-distinct] [--memory-read-log path] [--memory-read-address start-end] [--memory-read-pc start-end] [--memory-read-limit count] [--memory-snapshot-log path] [--memory-snapshot-address start-end] [--memory-snapshot-max-bytes count] [--stop-on-unmapped] [--stop-on-device-domain domain] [--initial-sp address] [--initial-sr address] [--media path-to-media] [--json]");
    Console.WriteLine("    --trace-pc, --fpu-snapshot-pc, --cpu-snapshot-pc, --fpu-memory-address, --fpu-memory-pc, --memory-write-address, --memory-write-pc, --memory-read-address, --memory-read-pc, and --memory-snapshot-address may be repeated for multiple ranges. --fpu-write-register and --fpu-memory-register may be repeated for multiple registers. --trace-instruction, --fpu-anomaly-instruction, --fpu-write-instruction, --fpscr-instruction, --fpu-snapshot-instruction, --cpu-snapshot-instruction, --fpu-memory-instruction, and --pc-profile-instruction accept N, START-END, START-, or -END.");
    Console.WriteLine("    --memory-poke-pc accepts PC:ADDRESS:VALUE, applies a one-shot 32-bit diagnostic patch before the matching PC executes, and may be repeated.");
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
    bool? SeedInitialVBlankOverride,
    bool EmitJson,
    string? FramebufferDumpPath,
    int FramebufferWidth,
    int FramebufferHeight,
    string? AudioWavPath,
    string? TraceLogPath,
    string? PvrTaLogPath,
    int PvrTaLogLimit,
    string? PvrTaSpriteLogPath,
    int PvrTaSpriteLogLimit,
    string? PvrTaSpriteTextureSampleLogPath,
    int PvrTaSpriteTextureSampleLogLimit,
    string? PvrTaTextureModeLogPath,
    int PvrTaTextureModeLogLimit,
    string? PvrTaModeTableLogPath,
    int PvrTaModeTableLogLimit,
    string? PvrTaSpriteSourceLogPath,
    int PvrTaSpriteSourceLogLimit,
    string? PvrTaSpriteSqLogPath,
    int PvrTaSpriteSqLogLimit,
    string? StoreQueueFlushLogPath,
    int StoreQueueFlushLogLimit,
    string? PvrTaSpriteStatus,
    string? FpuAnomalyLogPath,
    string? FpuWriteLogPath,
    string? FpscrLogPath,
    string? FpuSnapshotLogPath,
    string? CpuSnapshotLogPath,
    string? FpuMemoryLogPath,
    string? PcProfileLogPath,
    string? WindowsCeSyscallLogPath,
    string? WindowsCeSchedulerLogPath,
    string? DeviceLogPath,
    MemoryAccessKind? DeviceKind,
    AddressRange? DeviceAddressRange,
    string? DeviceDomain,
    string? MemoryWriteLogPath,
    string? MemoryReadLogPath,
    string? MemorySnapshotLogPath);

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
    uint BootPayloadOffset,
    bool IpBinSeeded,
    IReadOnlyList<DreamcastMemoryRegionWriteSummary> MemoryRegionWrites,
    DreamcastRunSummary Summary);

internal sealed record AddressRange(uint Start, uint End)
{
    public bool Contains(uint address) => address >= Start && address <= End;
}

internal sealed record WindowsCeSchedulerSnapshotField(uint Address, string Label);

internal sealed record WindowsCeSchedulerPointerSnapshot(uint SourceAddress, string Label, uint ByteCount);

internal sealed record WindowsCeSchedulerObjectKeyField(uint Offset, string Label);

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
