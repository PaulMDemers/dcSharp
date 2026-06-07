using System.Diagnostics;

namespace DcSharp.Tests;

public class DcSharpCliMediaLoadingIntegrationTests
{
    [Fact]
    public void RunCommandAccepts2048SectorMedia()
    {
        var repoRoot = FindRepoRoot();
        var cli = FindCliAssembly(repoRoot);
        var elfPath = Path.Combine(repoRoot, "artifacts", "kos", "dcsharp_minimal.elf");
        var mediaDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(mediaDirectory);
        var mediaPath = Path.Combine(mediaDirectory, "media.bin");

        try
        {
            File.WriteAllBytes(mediaPath, [.. Enumerable.Repeat((byte)0x11, 2048 * 2)]);

            var result = RunCli(
                cli,
                repoRoot,
                "run",
                elfPath,
                "--media",
                mediaPath,
                "--instructions",
                "8",
                "--trace-tail",
                "0");

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("Loaded: yes", result.StandardOutput);
        }
        finally
        {
            Directory.Delete(mediaDirectory, recursive: true);
        }
    }

    [Fact]
    public void RunCommandAcceptsCueWithMode1Track()
    {
        var repoRoot = FindRepoRoot();
        var cli = FindCliAssembly(repoRoot);
        var elfPath = Path.Combine(repoRoot, "artifacts", "kos", "dcsharp_minimal.elf");
        var mediaDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(mediaDirectory);
        var trackPath = Path.Combine(mediaDirectory, "track.bin");
        var cuePath = Path.Combine(mediaDirectory, "game.cue");

        try
        {
            var track = new byte[2352];
            track[16] = 0xAA;
            track[17] = 0xBB;
            track[18] = 0xCC;
            track[19] = 0xDD;
            File.WriteAllBytes(trackPath, track);
            File.WriteAllText(
                cuePath,
                $$"""
                REM cue
                FILE "track.bin" BINARY
                  TRACK 01 MODE1/2352
                    INDEX 01 00:00:00
                """);

            var result = RunCli(
                cli,
                repoRoot,
                "run",
                elfPath,
                "--media",
                cuePath,
                "--instructions",
                "8",
                "--trace-tail",
                "0");

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("Loaded: yes", result.StandardOutput);
        }
        finally
        {
            Directory.Delete(mediaDirectory, recursive: true);
        }
    }

    [Fact]
    public void RunCommandRejectsMissingMediaFile()
    {
        var repoRoot = FindRepoRoot();
        var cli = FindCliAssembly(repoRoot);
        var elfPath = Path.Combine(repoRoot, "artifacts", "kos", "dcsharp_minimal.elf");
        var mediaDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(mediaDirectory);
        var missingMediaPath = Path.Combine(mediaDirectory, "does-not-exist.bin");

        try
        {
            var result = RunCli(
                cli,
                repoRoot,
                "run",
                elfPath,
                "--media",
                missingMediaPath,
                "--instructions",
                "8",
                "--trace-tail",
                "0");

            Assert.Equal(2, result.ExitCode);
            Assert.Contains($"dcsharp: Media file not found: {missingMediaPath}", result.StandardError);
        }
        finally
        {
            Directory.Delete(mediaDirectory, recursive: true);
        }
    }

    [Fact]
    public void RunCommandRejectsCueWithoutModeDataTrack()
    {
        var repoRoot = FindRepoRoot();
        var cli = FindCliAssembly(repoRoot);
        var elfPath = Path.Combine(repoRoot, "artifacts", "kos", "dcsharp_minimal.elf");
        var mediaDirectory = Path.Combine(Path.GetTempPath(), "dcsharp cli test");
        Directory.CreateDirectory(mediaDirectory);
        var trackPath = Path.Combine(mediaDirectory, "audio track.bin");
        var cuePath = Path.Combine(mediaDirectory, "game cue.cue");

        try
        {
            File.WriteAllBytes(trackPath, new byte[2352]);
            File.WriteAllText(
                cuePath,
                $$"""
                FILE "audio track.bin" BINARY
                  TRACK 01 AUDIO
                    INDEX 01 00:00:00
                """);

            var result = RunCli(
                cli,
                repoRoot,
                "run",
                elfPath,
                "--media",
                cuePath,
                "--instructions",
                "8",
                "--trace-tail",
                "0");

            Assert.Equal(2, result.ExitCode);
            Assert.Contains($"dcsharp: No mode1/mode2 data track found in cue sheet '{cuePath}'.", result.StandardError);
        }
        finally
        {
            Directory.Delete(mediaDirectory, recursive: true);
        }
    }

    [Fact]
    public void MediaInspectCommandReportsDreamcastBootSector()
    {
        var repoRoot = FindRepoRoot();
        var cli = FindCliAssembly(repoRoot);
        var mediaDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(mediaDirectory);
        var trackPath = Path.Combine(mediaDirectory, "track.bin");
        var cuePath = Path.Combine(mediaDirectory, "game.cue");

        try
        {
            File.WriteAllBytes(trackPath, CreateCdSector(CreateBootSector("1ST_READ.BIN", "CLI MEDIA TEST")));
            File.WriteAllText(
                cuePath,
                $$"""
                FILE "track.bin" BINARY
                  TRACK 03 MODE1/2352
                    INDEX 01 00:00:00
                """);

            var result = RunCli(cli, repoRoot, "media", "inspect", cuePath);

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("Dreamcast boot sector: found", result.StandardOutput);
            Assert.Contains("Boot file: 1ST_READ.BIN", result.StandardOutput);
            Assert.Contains("Title: CLI MEDIA TEST", result.StandardOutput);
        }
        finally
        {
            Directory.Delete(mediaDirectory, recursive: true);
        }
    }

    [Fact]
    public void MediaInspectCommandReportsCueDirectoryBootCandidates()
    {
        var repoRoot = FindRepoRoot();
        var cli = FindCliAssembly(repoRoot);
        var mediaDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(mediaDirectory);
        var cueDataPath = Path.Combine(mediaDirectory, "game.bin");
        var track3Path = Path.Combine(mediaDirectory, "game (Track 3).bin");
        var cuePath = Path.Combine(mediaDirectory, "game.cue");

        try
        {
            File.WriteAllBytes(cueDataPath, CreateCdSector([0x00]));
            File.WriteAllBytes(track3Path, CreateCdSector(CreateBootSector("1ST_READ.BIN", "CLI CANDIDATE")));
            File.WriteAllText(
                cuePath,
                $$"""
                FILE "{{Path.GetFileName(cueDataPath)}}" BINARY
                  TRACK 01 MODE2/2352
                    INDEX 01 00:00:00
                """);

            var result = RunCli(cli, repoRoot, "media", "inspect", cuePath);

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("Dreamcast boot sector: not found", result.StandardOutput);
            Assert.Contains("CUE directory boot candidates:", result.StandardOutput);
            Assert.Contains(track3Path, result.StandardOutput);
            Assert.Contains("Boot file: 1ST_READ.BIN", result.StandardOutput);
            Assert.Contains("Title: CLI CANDIDATE", result.StandardOutput);
        }
        finally
        {
            Directory.Delete(mediaDirectory, recursive: true);
        }
    }

    [Fact]
    public void MediaExtractBootCommandWritesBootFile()
    {
        var repoRoot = FindRepoRoot();
        var cli = FindCliAssembly(repoRoot);
        var mediaDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(mediaDirectory);
        var mediaPath = Path.Combine(mediaDirectory, "game.bin");
        var outputPath = Path.Combine(mediaDirectory, "1ST_READ.BIN");

        try
        {
            File.WriteAllBytes(mediaPath, CreateBootableIsoImage("1ST_READ.BIN", "CLI BOOT"u8.ToArray()));

            var result = RunCli(cli, repoRoot, "media", "extract-boot", mediaPath, "--out", outputPath);

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("Boot file: 1ST_READ.BIN", result.StandardOutput);
            Assert.Contains($"Output: {outputPath}", result.StandardOutput);
            Assert.Equal("CLI BOOT"u8.ToArray(), File.ReadAllBytes(outputPath));
        }
        finally
        {
            Directory.Delete(mediaDirectory, recursive: true);
        }
    }

    [Fact]
    public void MediaAnalyzeBootCommandReportsStartupStub()
    {
        var repoRoot = FindRepoRoot();
        var cli = FindCliAssembly(repoRoot);
        var mediaDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(mediaDirectory);
        var bootPath = Path.Combine(mediaDirectory, "1ST_READ.BIN");
        var descrambledPath = Path.Combine(mediaDirectory, "1ST_READ.descrambled.bin");

        try
        {
            File.WriteAllBytes(bootPath, CreateDreamcastStartupStubBinary());

            var result = RunCli(cli, repoRoot, "media", "analyze-boot", bootPath, "--out-descrambled", descrambledPath);

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("Source kind: binary-file", result.StandardOutput);
            Assert.Contains("Recommended layout: original", result.StandardOutput);
            Assert.Contains("Dreamcast startup stub: True", result.StandardOutput);
            Assert.True(File.Exists(descrambledPath));
        }
        finally
        {
            Directory.Delete(mediaDirectory, recursive: true);
        }
    }

    [Fact]
    public void MediaBootSmokeCommandRunsRawBootBinary()
    {
        var repoRoot = FindRepoRoot();
        var cli = FindCliAssembly(repoRoot);
        var mediaDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(mediaDirectory);
        var bootPath = Path.Combine(mediaDirectory, "1ST_READ.BIN");

        try
        {
            File.WriteAllBytes(bootPath, CreateNopBootBinary());

            var result = RunCli(cli, repoRoot, "media", "boot-smoke", bootPath, "--instructions", "3", "--trace-tail", "2");

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("Selected layout: original", result.StandardOutput);
            Assert.Contains("Load address: 0x8C010000", result.StandardOutput);
            Assert.Contains("Bytes loaded: 16", result.StandardOutput);
            Assert.Contains("Stopped: InstructionLimit", result.StandardOutput);
            Assert.Contains("PC: 0x8C010006", result.StandardOutput);
            Assert.Contains("R0-R7: R0=0x00000000 R1=0x00000000 R2=0x00000000 R3=0x00000000 R4=0x00000000 R5=0x00000000 R6=0x00000000 R7=0x00000000", result.StandardOutput);
            Assert.Contains("R8-R15: R8=0x00000000 R9=0x00000000 R10=0x00000000 R11=0x00000000 R12=0x00000000 R13=0x00000000 R14=0x00000000 R15=0x8D000000", result.StandardOutput);
            Assert.Contains("Boot region writes:", result.StandardOutput);
        }
        finally
        {
            Directory.Delete(mediaDirectory, recursive: true);
        }
    }

    [Fact]
    public void MediaBootSmokeCommandWritesWindowsCeSyscallLog()
    {
        var repoRoot = FindRepoRoot();
        var cli = FindCliAssembly(repoRoot);
        var mediaDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(mediaDirectory);
        var bootPath = Path.Combine(mediaDirectory, "0WINCEOS.BIN");
        var logPath = Path.Combine(mediaDirectory, "wince.txt");

        try
        {
            File.WriteAllBytes(bootPath, CreateWindowsCeSleepThunkBootBinary());

            var result = RunCli(
                cli,
                repoRoot,
                "media",
                "boot-smoke",
                bootPath,
                "--instructions",
                "4",
                "--trace-tail",
                "0",
                "--wince-syscall-log",
                logPath);

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("Stopped: InstructionLimit", result.StandardOutput);
            var log = File.ReadAllText(logPath);
            Assert.Contains("0xFFFFFD5D", log);
            Assert.Contains("firmware wince hle WIN32.Sleep", log);
        }
        finally
        {
            Directory.Delete(mediaDirectory, recursive: true);
        }
    }

    [Fact]
    public void MediaBootSmokeCommandWritesWindowsCeSchedulerLog()
    {
        var repoRoot = FindRepoRoot();
        var cli = FindCliAssembly(repoRoot);
        var mediaDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(mediaDirectory);
        var bootPath = Path.Combine(mediaDirectory, "0WINCEOS.BIN");
        var logPath = Path.Combine(mediaDirectory, "scheduler.txt");

        try
        {
            File.WriteAllBytes(bootPath, CreateNopBootBinary());

            var result = RunCli(
                cli,
                repoRoot,
                "media",
                "boot-smoke",
                bootPath,
                "--instructions",
                "1",
                "--trace-tail",
                "0",
                "--wince-scheduler-log",
                logPath);

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("Stopped: InstructionLimit", result.StandardOutput);
            var log = File.ReadAllText(logPath);
            Assert.Contains("# Windows CE scheduler snapshot", log);
            Assert.Contains("0x8C131894 current-thread-object value=0x00000000 signed=0", log);
            Assert.Contains("0x8C131AA0 scheduler-dispatch-entry value=0x00000000 signed=0", log);
            Assert.Contains("0x8C131AA4 scheduler-dispatch-state value=0x00000000 signed=0", log);
            Assert.Contains("0x8C131B20 module-or-file-list-link value=0x00000000 signed=0", log);
            Assert.Contains("0x8C131B24 module-or-file-list-root value=0x00000000 signed=0", log);
            Assert.Contains("0x8C131D4C callback-allocation-slot value=0x00000000 signed=0", log);
            Assert.Contains("0x8C136540 current-wait-delta value=0x00000000 signed=0", log);
            Assert.Contains("0x8C131894 current-thread-object target=0x00000000 (null)", log);
            Assert.Contains("0x8C131B24 module-or-file-list-root target=0x00000000 (null)", log);
        }
        finally
        {
            Directory.Delete(mediaDirectory, recursive: true);
        }
    }

    [Fact]
    public void MediaBootSmokeCommandWritesWindowsCeSchedulerKeyZeroFields()
    {
        var repoRoot = FindRepoRoot();
        var cli = FindCliAssembly(repoRoot);
        var mediaDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(mediaDirectory);
        var bootPath = Path.Combine(mediaDirectory, "0WINCEOS.BIN");
        var logPath = Path.Combine(mediaDirectory, "scheduler.txt");

        try
        {
            File.WriteAllBytes(bootPath, CreateWindowsCeSchedulerPointerBootBinary());

            var result = RunCli(
                cli,
                repoRoot,
                "media",
                "boot-smoke",
                bootPath,
                "--instructions",
                "6",
                "--trace-tail",
                "0",
                "--wince-scheduler-log",
                logPath);

            Assert.Equal(0, result.ExitCode);
            var log = File.ReadAllText(logPath);
            Assert.Contains("0x8C131894 current-thread-object target=0x8C1376C0 bytes=0x100", log);
            Assert.Contains("+0x01C wait-link-or-copy-source addr=0x8C1376DC value=0x00000000 signed=0", log);
            Assert.Contains("+0x048 metadata-or-thread-copy addr=0x8C137708 value=0x00000000 signed=0", log);
        }
        finally
        {
            Directory.Delete(mediaDirectory, recursive: true);
        }
    }

    [Fact]
    public void MediaBootSmokeCommandDecodesWindowsCeDescriptorFields()
    {
        var repoRoot = FindRepoRoot();
        var cli = FindCliAssembly(repoRoot);
        var mediaDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(mediaDirectory);
        var bootPath = Path.Combine(mediaDirectory, "0WINCEOS.BIN");
        var logPath = Path.Combine(mediaDirectory, "scheduler.txt");

        try
        {
            File.WriteAllBytes(bootPath, CreateWindowsCeSchedulerDescriptorBootBinary());

            var result = RunCli(
                cli,
                repoRoot,
                "media",
                "boot-smoke",
                bootPath,
                "--instructions",
                "12",
                "--trace-tail",
                "0",
                "--wince-scheduler-log",
                logPath);

            Assert.Equal(0, result.ExitCode);
            var log = File.ReadAllText(logPath);
            Assert.Contains("descriptor-region-check base-or-region=0x02000000 region=0x01 handler=0x8C011924 region=0x46 match=False", log);
            Assert.Contains("descriptor-derived-base base-or-region=0x02000000 runtime-base=0x8C010000 expected=0x8E010000 recorded=0x8E010000 match=True", log);
            Assert.Contains("descriptor-copy-source value=0x00000000 null=True", log);
        }
        finally
        {
            Directory.Delete(mediaDirectory, recursive: true);
        }
    }

    [Fact]
    public void MediaBootSmokeCommandIgnoresFilledWindowsCeDescriptorLookalike()
    {
        var repoRoot = FindRepoRoot();
        var cli = FindCliAssembly(repoRoot);
        var mediaDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(mediaDirectory);
        var bootPath = Path.Combine(mediaDirectory, "0WINCEOS.BIN");
        var logPath = Path.Combine(mediaDirectory, "scheduler.txt");

        try
        {
            File.WriteAllBytes(
                bootPath,
                CreateWindowsCeSchedulerDescriptorBootBinary(runtimeBase: 0x00C0_C0C0, handler: 0x00C0_C0C0, derivedBase: 0x00C0_C0C0));

            var result = RunCli(
                cli,
                repoRoot,
                "media",
                "boot-smoke",
                bootPath,
                "--instructions",
                "12",
                "--trace-tail",
                "0",
                "--wince-scheduler-log",
                logPath);

            Assert.Equal(0, result.ExitCode);
            var log = File.ReadAllText(logPath);
            Assert.Contains("+0x018 base-or-entry addr=0x8C1376D8 value=0x00C0C0C0 signed=12632256", log);
            Assert.DoesNotContain("descriptor-region-check", log);
            Assert.DoesNotContain("descriptor-derived-base", log);
            Assert.DoesNotContain("descriptor-copy-source", log);
        }
        finally
        {
            Directory.Delete(mediaDirectory, recursive: true);
        }
    }

    [Fact]
    public void MediaBootSmokeCommandSeedsIpBinForCueInput()
    {
        var repoRoot = FindRepoRoot();
        var cli = FindCliAssembly(repoRoot);
        var mediaDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(mediaDirectory);
        var trackPath = Path.Combine(mediaDirectory, "track.bin");
        var cuePath = Path.Combine(mediaDirectory, "game.cue");

        try
        {
            File.WriteAllBytes(trackPath, ToCdSectors(CreateBootableIsoImage("1ST_READ.BIN", CreateNopBootBinary())));
            File.WriteAllText(
                cuePath,
                $$"""
                FILE "{{Path.GetFileName(trackPath)}}" BINARY
                  TRACK 03 MODE1/2352
                    INDEX 01 00:00:00
                """);

            var result = RunCli(cli, repoRoot, "media", "boot-smoke", cuePath, "--instructions", "1", "--trace-tail", "0");

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("Source kind: media-extracted", result.StandardOutput);
            Assert.Contains("IP.BIN seeded: True", result.StandardOutput);
            Assert.Contains("Stopped: InstructionLimit", result.StandardOutput);
        }
        finally
        {
            Directory.Delete(mediaDirectory, recursive: true);
        }
    }

    [Fact]
    public void MediaBootSmokeCommandSeedsIpBinFromCueDirectoryCandidate()
    {
        var repoRoot = FindRepoRoot();
        var cli = FindCliAssembly(repoRoot);
        var mediaDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(mediaDirectory);
        var cueDataPath = Path.Combine(mediaDirectory, "game.bin");
        var trackPath = Path.Combine(mediaDirectory, "game (Track 3).bin");
        var cuePath = Path.Combine(mediaDirectory, "game.cue");

        try
        {
            File.WriteAllBytes(cueDataPath, CreateCdSector([0x00]));
            File.WriteAllBytes(trackPath, ToCdSectors(CreateBootableIsoImage("1ST_READ.BIN", CreateNopBootBinary())));
            File.WriteAllText(
                cuePath,
                $$"""
                FILE "{{Path.GetFileName(cueDataPath)}}" BINARY
                  TRACK 01 MODE2/2352
                    INDEX 01 00:00:00
                """);

            var result = RunCli(cli, repoRoot, "media", "boot-smoke", cuePath, "--instructions", "1", "--trace-tail", "0");

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("Source kind: media-extracted", result.StandardOutput);
            Assert.Contains("IP.BIN seeded: True", result.StandardOutput);
        }
        finally
        {
            Directory.Delete(mediaDirectory, recursive: true);
        }
    }

    [Fact]
    public void MediaBootSmokeCommandRebuildsCueDirectoryIpBinFromSectorPayloads()
    {
        var repoRoot = FindRepoRoot();
        var cli = FindCliAssembly(repoRoot);
        var mediaDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(mediaDirectory);
        var cueDataPath = Path.Combine(mediaDirectory, "game.bin");
        var trackPath = Path.Combine(mediaDirectory, "game (Track 3).bin");
        var cuePath = Path.Combine(mediaDirectory, "game.cue");

        try
        {
            var iso = CreateBootableIsoImage("1ST_READ.BIN", CreateNopBootBinary());
            CreateIpBinThatBranchesToSecondSector().CopyTo(iso.AsSpan(0, 16 * 2048));
            File.WriteAllBytes(cueDataPath, CreateCdSector([0x00]));
            File.WriteAllBytes(trackPath, ToCdSectors(iso));
            File.WriteAllText(
                cuePath,
                $$"""
                FILE "{{Path.GetFileName(cueDataPath)}}" BINARY
                  TRACK 01 MODE2/2352
                    INDEX 01 00:00:00
                """);

            var result = RunCli(cli, repoRoot, "media", "boot-smoke", cuePath, "--instructions", "3", "--trace-tail", "0");

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("IP.BIN seeded: True", result.StandardOutput);
            Assert.Contains("Stopped: InstructionLimit", result.StandardOutput);
            Assert.Contains("PC: 0x8C008902", result.StandardOutput);
        }
        finally
        {
            Directory.Delete(mediaDirectory, recursive: true);
        }
    }

    [Fact]
    public void MediaBootSmokeCommandCanStopOnUnmappedAccess()
    {
        var repoRoot = FindRepoRoot();
        var cli = FindCliAssembly(repoRoot);
        var mediaDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(mediaDirectory);
        var bootPath = Path.Combine(mediaDirectory, "1ST_READ.BIN");

        try
        {
            File.WriteAllBytes(bootPath, CreateUnmappedReadBootBinary());

            var result = RunCli(
                cli,
                repoRoot,
                "media",
                "boot-smoke",
                bootPath,
                "--instructions",
                "10",
                "--trace-tail",
                "4",
                "--stop-on-unmapped");

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("Stopped: DeviceAccessStop", result.StandardOutput);
            Assert.Contains("Stopped on UnmappedRead at 0x08000010", result.StandardOutput);
        }
        finally
        {
            Directory.Delete(mediaDirectory, recursive: true);
        }
    }

    [Fact]
    public void RunCommandWritesAudioWav()
    {
        var repoRoot = FindRepoRoot();
        var cli = FindCliAssembly(repoRoot);
        var elfPath = Path.Combine(repoRoot, "artifacts", "kos", "dcsharp_minimal.elf");
        var outputDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(outputDirectory);
        var wavPath = Path.Combine(outputDirectory, "audio.wav");

        try
        {
            var result = RunCli(
                cli,
                repoRoot,
                "run",
                elfPath,
                "--audio-wav",
                wavPath,
                "--instructions",
                "8",
                "--trace-tail",
                "0");

            Assert.Equal(0, result.ExitCode);
            Assert.True(File.Exists(wavPath));
            var header = File.ReadAllBytes(wavPath);
            Assert.True(header.Length >= 44);
            Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(header, 0, 4));
            Assert.Equal("WAVE", System.Text.Encoding.ASCII.GetString(header, 8, 4));
        }
        finally
        {
            Directory.Delete(outputDirectory, recursive: true);
        }
    }

    private static string FindCliAssembly(string repoRoot)
    {
        var releaseAssembly = Path.Combine(repoRoot, "src", "DcSharp.Cli", "bin", "Release", "net10.0", "DcSharp.Cli.dll");
        if (File.Exists(releaseAssembly))
        {
            return releaseAssembly;
        }

        var debugAssembly = Path.Combine(repoRoot, "src", "DcSharp.Cli", "bin", "Debug", "net10.0", "DcSharp.Cli.dll");
        if (File.Exists(debugAssembly))
        {
            return debugAssembly;
        }

        throw new FileNotFoundException("Could not find built CLI assembly.", "DcSharp.Cli.dll");
    }

    private static (int ExitCode, string StandardOutput, string StandardError) RunCli(string cliAssembly, string workingDirectory, params string[] args)
    {
        var quotedArgs = string.Join(" ", args.Select(EscapeArgument));
        var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"\"{cliAssembly}\" {quotedArgs}",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        process.Start();
        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return (process.ExitCode, stdout, stderr);
    }

    private static string EscapeArgument(string value) =>
        value.Contains(' ') || value.Contains('"') || value.Contains('\'')
            ? $"\"{value.Replace("\"", "\\\"")}\""
            : value;

    private static byte[] CreateCdSector(byte[] payload)
    {
        var sector = new byte[2352];
        Array.Copy(payload, 0, sector, 16, payload.Length);
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

    private static byte[] CreateBootableIsoImage(string bootFile, byte[] bootData)
    {
        var image = new byte[2048 * 24];
        Array.Copy(CreateBootSector(bootFile, "CLI ISO TEST"), image, 2048);

        var pvd = image.AsSpan(16 * 2048, 2048);
        pvd[0] = 1;
        System.Text.Encoding.ASCII.GetBytes("CD001").CopyTo(pvd[1..]);
        pvd[6] = 1;
        WriteAscii(image, (16 * 2048) + 40, 32, "CLI TEST ISO");
        WriteDirectoryRecord(pvd, 156, 20, 2048, 0x02, [0]);

        var directory = image.AsSpan(20 * 2048, 2048);
        var offset = 0;
        offset += WriteDirectoryRecord(directory, offset, 20, 2048, 0x02, [0]);
        offset += WriteDirectoryRecord(directory, offset, 20, 2048, 0x02, [1]);
        WriteDirectoryRecord(directory, offset, 21, (uint)bootData.Length, 0x00, System.Text.Encoding.ASCII.GetBytes($"{bootFile};1"));
        Array.Copy(bootData, 0, image, 21 * 2048, bootData.Length);
        return image;
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

    private static byte[] CreateNopBootBinary()
    {
        var data = new byte[16];
        for (var offset = 0; offset < data.Length; offset += 2)
        {
            data[offset] = 0x09;
            data[offset + 1] = 0x00;
        }

        return data;
    }

    private static byte[] CreateWindowsCeSleepThunkBootBinary() =>
    [
        0x01, 0xD0, // mov.l @(0x01,pc),r0
        0x0B, 0x40, // jsr @r0
        0x09, 0x00, // nop
        0x09, 0x00, // nop
        0x5D, 0xFD, 0xFF, 0xFF, // 0xFFFFFD5D
        0x09, 0x00,
        0x09, 0x00
    ];

    private static byte[] CreateWindowsCeSchedulerPointerBootBinary() =>
    [
        0x02, 0xD1, // mov.l @(0x02,pc),r1
        0x03, 0xD2, // mov.l @(0x03,pc),r2
        0x22, 0x21, // mov.l r2,@r1
        0x05, 0xA0, // bra after literals
        0x09, 0x00, // nop
        0x09, 0x00, // literal alignment
        0x94, 0x18, 0x13, 0x8C, // 0x8C131894
        0xC0, 0x76, 0x13, 0x8C, // 0x8C1376C0
        0x09, 0x00,
        0x09, 0x00
    ];

    private static byte[] CreateWindowsCeSchedulerDescriptorBootBinary(
        uint baseOrRegion = 0x0200_0000,
        uint runtimeBase = 0x8C01_0000,
        uint handler = 0x8C01_1924,
        uint derivedBase = 0x8E01_0000)
    {
        var data = new byte[0x50];
        WriteUInt16LittleEndian(data, 0x00, 0xD10B); // mov.l @(0x0B,pc),r1 ; current-thread pointer slot
        WriteUInt16LittleEndian(data, 0x02, 0xD20C); // mov.l @(0x0C,pc),r2 ; descriptor object
        WriteUInt16LittleEndian(data, 0x04, 0x2122); // mov.l r2,@r1
        WriteUInt16LittleEndian(data, 0x06, 0x6123); // mov r2,r1
        WriteUInt16LittleEndian(data, 0x08, 0xD30B); // mov.l @(0x0B,pc),r3 ; base-or-region
        WriteUInt16LittleEndian(data, 0x0A, 0x1133); // mov.l r3,@(0x3,r1)
        WriteUInt16LittleEndian(data, 0x0C, 0xD30B); // mov.l @(0x0B,pc),r3 ; runtime base
        WriteUInt16LittleEndian(data, 0x0E, 0x1136); // mov.l r3,@(0x6,r1)
        WriteUInt16LittleEndian(data, 0x10, 0xD30B); // mov.l @(0x0B,pc),r3 ; handler
        WriteUInt16LittleEndian(data, 0x12, 0x1138); // mov.l r3,@(0x8,r1)
        WriteUInt16LittleEndian(data, 0x14, 0xD30B); // mov.l @(0x0B,pc),r3 ; derived base
        WriteUInt16LittleEndian(data, 0x16, 0x113E); // mov.l r3,@(0xE,r1)
        WriteUInt32LittleEndian(data, 0x30, 0x8C13_1894);
        WriteUInt32LittleEndian(data, 0x34, 0x8C13_76C0);
        WriteUInt32LittleEndian(data, 0x38, baseOrRegion);
        WriteUInt32LittleEndian(data, 0x3C, runtimeBase);
        WriteUInt32LittleEndian(data, 0x40, handler);
        WriteUInt32LittleEndian(data, 0x44, derivedBase);
        return data;
    }

    private static byte[] CreateUnmappedReadBootBinary() =>
    [
        0x01, 0xD1, // mov.l @(0x01,pc),r1
        0x10, 0x60, // mov.b @r1,r0
        0xFE, 0xAF, // bra 0x8C010004
        0x09, 0x00, // nop
        0x10, 0x00, 0x00, 0x08
    ];

    private static byte[] CreateIpBinThatBranchesToSecondSector()
    {
        var ipBin = new byte[16 * 2048];
        CreateBootSector("1ST_READ.BIN", "CLI IP.BIN").CopyTo(ipBin, 0);
        ipBin[0x300] = 0xFE; // bra 0x8C008900
        ipBin[0x301] = 0xA2;
        ipBin[0x302] = 0x09; // nop
        ipBin[0x303] = 0x00;
        ipBin[0x900] = 0x07; // mov #7,r0
        ipBin[0x901] = 0xE0;
        return ipBin;
    }

    private static byte[] ToCdSectors(byte[] isoImage)
    {
        var sectors = isoImage.Length / 2048;
        var raw = new byte[sectors * 2352];
        for (var sector = 0; sector < sectors; sector++)
        {
            Array.Copy(isoImage, sector * 2048, raw, (sector * 2352) + 16, 2048);
        }

        return raw;
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

    private static void WriteUInt16LittleEndian(byte[] destination, int offset, ushort value) =>
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(destination.AsSpan(offset, 2), value);

    private static void WriteUInt32LittleEndian(byte[] destination, int offset, uint value) =>
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(destination.AsSpan(offset, 4), value);

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "dcSharp.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find repository root.");
    }
}
