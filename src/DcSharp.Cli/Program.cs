using DcSharp.Core.Dreamcast.Memory;
using DcSharp.Core.Dreamcast.Input;
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
            Console.WriteLine($"  0x{step.Pc:X8}: 0x{step.Opcode:X4}  {step.Trace}");
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

static CliRunOptions ParseRunOptions(string[] args)
{
    ulong instructionLimit = 1_000;
    var traceTail = 16;
    ulong vblankInterval = 200_000;
    var emitJson = false;
    var controllerA = DreamcastControllerState.Neutral;
    DreamcastControllerScript? controllerAScript = null;

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
                controllerA = ParseControllerState(args[index + 1]);
                index++;
                break;
            case "--controller-a-script" when index + 1 < args.Length:
                controllerAScript = ParseControllerScript(args[index + 1]);
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

    return new CliRunOptions(new DreamcastRunOptions(instructionLimit, traceTail, vblankInterval, controllerA, controllerAScript), emitJson);
}

static DreamcastControllerScript ParseControllerScript(string text)
{
    var frames = new List<DreamcastControllerScriptFrame>();
    foreach (var rawFrame in text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
        var separator = rawFrame.IndexOf(':');
        if (separator <= 0)
        {
            throw new InvalidDataException("Controller script frames must use instruction:state syntax.");
        }

        if (!ulong.TryParse(rawFrame[..separator], out var instruction))
        {
            throw new InvalidDataException($"Invalid controller script instruction: {rawFrame[..separator]}");
        }

        frames.Add(new DreamcastControllerScriptFrame(instruction, ParseControllerState(rawFrame[(separator + 1)..])));
    }

    return new DreamcastControllerScript(frames.OrderBy(frame => frame.FromInstruction).ToArray());
}

static DreamcastControllerState ParseControllerState(string text)
{
    var buttons = DreamcastControllerButtons.None;
    byte leftTrigger = 0;
    byte rightTrigger = 0;
    sbyte joyX = 0;
    sbyte joyY = 0;
    sbyte joy2X = 0;
    sbyte joy2Y = 0;

    foreach (var rawToken in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
        var token = rawToken.Trim();
        var equals = token.IndexOf('=');
        if (equals < 0)
        {
            buttons |= ParseButton(token);
            continue;
        }

        var key = token[..equals].Trim().ToLowerInvariant();
        var value = token[(equals + 1)..].Trim();
        switch (key)
        {
            case "buttons":
                foreach (var button in value.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    buttons |= ParseButton(button);
                }
                break;
            case "ltrig":
            case "lt":
                leftTrigger = ParseByte(value, key);
                break;
            case "rtrig":
            case "rt":
                rightTrigger = ParseByte(value, key);
                break;
            case "joyx":
                joyX = ParseAxis(value, key);
                break;
            case "joyy":
                joyY = ParseAxis(value, key);
                break;
            case "joy2x":
                joy2X = ParseAxis(value, key);
                break;
            case "joy2y":
                joy2Y = ParseAxis(value, key);
                break;
            default:
                throw new InvalidDataException($"Unknown controller field: {key}");
        }
    }

    return new DreamcastControllerState(buttons, leftTrigger, rightTrigger, joyX, joyY, joy2X, joy2Y);
}

static DreamcastControllerButtons ParseButton(string text)
{
    var normalized = text.Replace("-", string.Empty, StringComparison.Ordinal).Replace("_", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
    return normalized switch
    {
        "a" => DreamcastControllerButtons.A,
        "b" => DreamcastControllerButtons.B,
        "c" => DreamcastControllerButtons.C,
        "d" => DreamcastControllerButtons.D,
        "x" => DreamcastControllerButtons.X,
        "y" => DreamcastControllerButtons.Y,
        "z" => DreamcastControllerButtons.Z,
        "start" => DreamcastControllerButtons.Start,
        "up" or "dpadup" => DreamcastControllerButtons.DPadUp,
        "down" or "dpaddown" => DreamcastControllerButtons.DPadDown,
        "left" or "dpadleft" => DreamcastControllerButtons.DPadLeft,
        "right" or "dpadright" => DreamcastControllerButtons.DPadRight,
        "dpad2up" => DreamcastControllerButtons.DPad2Up,
        "dpad2down" => DreamcastControllerButtons.DPad2Down,
        "dpad2left" => DreamcastControllerButtons.DPad2Left,
        "dpad2right" => DreamcastControllerButtons.DPad2Right,
        "none" => DreamcastControllerButtons.None,
        _ => throw new InvalidDataException($"Unknown controller button: {text}")
    };
}

static byte ParseByte(string text, string key)
{
    if (!byte.TryParse(text, out var value))
    {
        throw new InvalidDataException($"{key} must be between 0 and 255.");
    }

    return value;
}

static sbyte ParseAxis(string text, string key)
{
    if (!int.TryParse(text, out var parsed) || parsed is < -128 or > 127)
    {
        throw new InvalidDataException($"{key} must be between -128 and 127.");
    }

    return (sbyte)parsed;
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
    Console.WriteLine("  dcsharp run <file.elf> [--instructions count] [--trace-tail count] [--vblank-interval instructions] [--controller-a state] [--controller-a-script script] [--json]");
    Console.WriteLine("    Use --vblank-interval 0 to disable synthetic VBlank events.");
    Console.WriteLine("    Example controller state: --controller-a start,a,joyx=-16,ltrig=40");
    Console.WriteLine("    Example controller script: --controller-a-script \"0:none;200000:start,a\"");
}

internal sealed record CliRunOptions(DreamcastRunOptions Emulation, bool EmitJson);
