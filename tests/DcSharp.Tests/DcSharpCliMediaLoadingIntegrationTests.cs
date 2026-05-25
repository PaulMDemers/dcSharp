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
        var debugAssembly = Path.Combine(repoRoot, "src", "DcSharp.Cli", "bin", "Debug", "net10.0", "DcSharp.Cli.dll");
        if (File.Exists(debugAssembly))
        {
            return debugAssembly;
        }

        var releaseAssembly = Path.Combine(repoRoot, "src", "DcSharp.Cli", "bin", "Release", "net10.0", "DcSharp.Cli.dll");
        if (File.Exists(releaseAssembly))
        {
            return releaseAssembly;
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
