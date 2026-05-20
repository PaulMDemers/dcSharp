using DcSharp.Core.Execution;
using DcSharp.Core.Media;

namespace DcSharp.Tests;

public class DreamcastKosFixtureTests
{
    [Fact]
    public void MinimalKosFixtureReachesMainAndFirmwareExit()
    {
        if (!ShouldRunKosFixtures() || !TryOpenArtifact("dcsharp_minimal.elf", out var stream))
        {
            return;
        }

        using (stream)
        {
            var result = new DreamcastRunner().Run(ElfFile.Read(stream), new DreamcastRunOptions(InstructionLimit: 20_000_000, TraceTailLength: 8));
            var summary = DreamcastRunSummary.FromResult(result);

            Assert.Equal(DreamcastStopReason.FirmwareExit, summary.StopReason);
            Assert.Contains("dcSharp minimal KallistiOS probe", summary.SerialText);
        }
    }

    [Fact]
    public void DefaultKosFixtureReachesMainAndProgramExit()
    {
        if (!ShouldRunKosFixtures() || !TryOpenArtifact("dcsharp_probe.elf", out var stream))
        {
            return;
        }

        using (stream)
        {
            var result = new DreamcastRunner().Run(ElfFile.Read(stream), new DreamcastRunOptions(InstructionLimit: 60_000_000, TraceTailLength: 8));
            var summary = DreamcastRunSummary.FromResult(result);

            Assert.Equal(DreamcastStopReason.ProgramExit, summary.StopReason);
            Assert.Contains("dcSharp KallistiOS probe", summary.SerialText);
            Assert.Contains("arch: exit return code 0", summary.SerialText);
        }
    }

    [Fact]
    public void TimerKosFixtureSleepsAndProgramExits()
    {
        if (!ShouldRunKosFixtures() || !TryOpenArtifact("dcsharp_timer.elf", out var stream))
        {
            return;
        }

        using (stream)
        {
            var result = new DreamcastRunner().Run(ElfFile.Read(stream), new DreamcastRunOptions(InstructionLimit: 60_000_000, TraceTailLength: 8));
            var summary = DreamcastRunSummary.FromResult(result);

            Assert.Equal(DreamcastStopReason.ProgramExit, summary.StopReason);
            Assert.Contains("dcSharp timer tick 1 elapsed", summary.SerialText);
            Assert.Contains("dcSharp KallistiOS timer probe done", summary.SerialText);
        }
    }

    private static bool ShouldRunKosFixtures() =>
        string.Equals(Environment.GetEnvironmentVariable("DCSHARP_RUN_KOS_FIXTURES"), "1", StringComparison.Ordinal);

    private static bool TryOpenArtifact(string fileName, out FileStream stream)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "dcSharp.slnx")))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            stream = null!;
            return false;
        }

        var path = Path.Combine(directory.FullName, "artifacts", "kos", fileName);
        if (!File.Exists(path))
        {
            stream = null!;
            return false;
        }

        stream = File.OpenRead(path);
        return true;
    }
}
