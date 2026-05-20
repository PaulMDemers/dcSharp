using DcSharp.Core.Dreamcast.Memory;
using DcSharp.Core.Dreamcast.Input;
using DcSharp.Core.Dreamcast.Video;
using DcSharp.Core.Execution;
using DcSharp.Core.Fixtures;
using DcSharp.Core.Loading;
using DcSharp.Core.Media;
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

    Console.WriteLine($"Controller A: {FormatController(EffectiveControllerA(options.Emulation, result.Cpu.InstructionsExecuted))}");
    Console.WriteLine($"Video VRAM: nonzero={result.Video.NonZeroBytes}, checksum={result.Video.Fnv1A32Hex}, first={result.Video.FirstNonZeroOffsetHex ?? "none"}");
    Console.WriteLine($"Device accesses: {result.DeviceAccesses.Count}");
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
    var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };
    jsonOptions.Converters.Add(new JsonStringEnumConverter());

    Console.WriteLine(JsonSerializer.Serialize(summary, jsonOptions));
}

static int RunFixtures(string manifestPath, string[] args)
{
    var emitJson = false;
    string? artifactDirectoryOverride = null;
    for (var index = 0; index < args.Length; index++)
    {
        switch (args[index])
        {
            case "--json":
            case "--summary-json":
                emitJson = true;
                break;
            case "--artifacts" when index + 1 < args.Length:
                artifactDirectoryOverride = args[index + 1];
                index++;
                break;
            default:
                throw new InvalidDataException($"Unknown or invalid fixtures option: {args[index]}");
        }
    }

    var repoRoot = FindRepoRoot(Path.GetFullPath(manifestPath)) ?? Directory.GetCurrentDirectory();
    using var stream = File.OpenRead(manifestPath);
    var manifest = DreamcastFixtureManifest.Read(stream);
    var artifactDirectory = ResolveRepoPath(repoRoot, artifactDirectoryOverride ?? manifest.ArtifactDirectory);
    var results = new List<DreamcastFixtureCheckResult>();

    foreach (var fixture in manifest.Fixtures)
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

        results.Add(DreamcastFixtureRunner.Run(fixture, artifactPath));
    }

    if (emitJson)
    {
        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true
        };
        jsonOptions.Converters.Add(new JsonStringEnumConverter());
        Console.WriteLine(JsonSerializer.Serialize(results.Select(FixtureReport.FromResult).ToArray(), jsonOptions));
    }
    else
    {
        foreach (var result in results)
        {
            Console.WriteLine($"{(result.Passed ? "PASS" : "FAIL")} {result.Name}");
            if (result.Summary is not null)
            {
                Console.WriteLine($"  stop={result.Summary.StopReason}, instructions={result.Summary.InstructionsExecuted}, serial={result.Summary.SerialBytes}, videoNonZero={result.Summary.Video.NonZeroBytes}");
            }

            foreach (var failure in result.Failures)
            {
                Console.WriteLine($"  {failure}");
            }
        }

        Console.WriteLine($"Fixtures: {results.Count(result => result.Passed)}/{results.Count} passed");
    }

    return results.All(result => result.Passed) ? 0 : 1;
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

static CliRunOptions ParseRunOptions(string[] args)
{
    ulong instructionLimit = 1_000;
    var traceTail = 16;
    ulong vblankInterval = 200_000;
    var emitJson = false;
    var controllerA = DreamcastControllerState.Neutral;
    DreamcastControllerScript? controllerAScript = null;
    string? framebufferDumpPath = null;
    var framebufferWidth = 320;
    var framebufferHeight = 240;

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
            case "--controller-a-script" when index + 1 < args.Length:
                controllerAScript = DreamcastControllerStateParser.ParseScript(args[index + 1]);
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

    return new CliRunOptions(
        new DreamcastRunOptions(instructionLimit, traceTail, vblankInterval, controllerA, controllerAScript),
        emitJson,
        framebufferDumpPath,
        framebufferWidth,
        framebufferHeight);
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

static string FormatController(DreamcastControllerState state) =>
    $"buttons={state.Buttons}, ltrig={state.LeftTrigger}, rtrig={state.RightTrigger}, joy=({state.JoyX},{state.JoyY}), joy2=({state.Joy2X},{state.Joy2Y})";

static DreamcastControllerState EffectiveControllerA(DreamcastRunOptions options, ulong instructionsExecuted) =>
    options.ControllerAScript?.StateAt(instructionsExecuted)
    ?? options.ControllerA
    ?? DreamcastControllerState.Neutral;

static void PrintUsage()
{
    Console.WriteLine("Usage:");
    Console.WriteLine("  dcsharp inspect <file.elf>");
    Console.WriteLine("  dcsharp run <file.elf> [--instructions count] [--trace-tail count] [--vblank-interval instructions] [--controller-a state] [--controller-a-script script] [--dump-framebuffer path.png] [--framebuffer-size 320x240] [--json]");
    Console.WriteLine("  dcsharp fixtures <manifest.json> [--artifacts path] [--json]");
    Console.WriteLine("    Use --vblank-interval 0 to disable synthetic VBlank events.");
    Console.WriteLine("    Example controller state: --controller-a start,a,joyx=-16,ltrig=40");
    Console.WriteLine("    Example controller script: --controller-a-script \"0:none;200000:start,a\"");
    Console.WriteLine("    Framebuffer dumps currently use RGB565.");
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
    int FramebufferHeight);

internal sealed record FixtureReport(
    string Name,
    bool Passed,
    DreamcastStopReason? StopReason,
    ulong? InstructionsExecuted,
    int? SerialBytes,
    ulong? VideoNonZeroBytes,
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
            result.Failures);
}
