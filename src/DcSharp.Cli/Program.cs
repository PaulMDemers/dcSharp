using DcSharp.Core.Dreamcast.Memory;
using DcSharp.Core.Execution;
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

    if (options.EmitJson)
    {
        WriteJsonRunSummary(result);
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

    if (result.TraceTail.Count > 0)
    {
        Console.WriteLine("Trace tail:");
        foreach (var step in result.TraceTail)
        {
            Console.WriteLine($"  0x{step.Pc:X8}: 0x{step.Opcode:X4}  {step.Trace}");
        }
    }
}

static void WriteJsonRunSummary(DreamcastRunResult result)
{
    var summary = DreamcastRunSummary.FromResult(result);
    var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };
    jsonOptions.Converters.Add(new JsonStringEnumConverter());

    Console.WriteLine(JsonSerializer.Serialize(summary, jsonOptions));
}

static CliRunOptions ParseRunOptions(string[] args)
{
    ulong instructionLimit = 1_000;
    var traceTail = 16;
    ulong vblankInterval = 200_000;
    var emitJson = false;

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

    return new CliRunOptions(new DreamcastRunOptions(instructionLimit, traceTail, vblankInterval), emitJson);
}

static void PrintUsage()
{
    Console.WriteLine("Usage:");
    Console.WriteLine("  dcsharp inspect <file.elf>");
    Console.WriteLine("  dcsharp run <file.elf> [--instructions count] [--trace-tail count] [--vblank-interval instructions] [--json]");
    Console.WriteLine("    Use --vblank-interval 0 to disable synthetic VBlank events.");
}

internal sealed record CliRunOptions(DreamcastRunOptions Emulation, bool EmitJson);
