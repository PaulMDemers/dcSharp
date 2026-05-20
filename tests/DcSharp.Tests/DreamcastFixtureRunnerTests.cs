using DcSharp.Core.Dreamcast.Input;
using DcSharp.Core.Execution;
using DcSharp.Core.Fixtures;

namespace DcSharp.Tests;

public class DreamcastFixtureRunnerTests
{
    [Fact]
    public void ValidateAcceptsSchedulerExpectationsAtThreshold()
    {
        var fixture = new DreamcastFixtureDefinition
        {
            ExpectedStopReason = DreamcastStopReason.ProgramExit,
            MinMapleTransfers = 9,
            MinMapleDeviceInfoTransfers = 4,
            MinMapleGetConditionTransfers = 5,
            MinVblankEvents = 2,
            MinHardwareAdvanceTicks = 100,
            MinHardwareAdvanceBatches = 20,
            MaxHardwareAdvanceBatch = 5,
            MinControllerScriptChanges = 1
        };
        var summary = CreateSummary(
            vblankEvents: 2,
            hardwareTicks: 100,
            hardwareBatches: 20,
            maxHardwareBatch: 5,
            controllerScriptChanges: 1,
            mapleTransfers: 9,
            mapleDeviceInfoTransfers: 4,
            mapleGetConditionTransfers: 5);

        var failures = DreamcastFixtureRunner.Validate(fixture, summary);

        Assert.Empty(failures);
    }

    [Fact]
    public void ValidateReportsSchedulerExpectationFailures()
    {
        var fixture = new DreamcastFixtureDefinition
        {
            ExpectedStopReason = DreamcastStopReason.ProgramExit,
            MinMapleTransfers = 10,
            MinMapleDeviceInfoTransfers = 5,
            MinMapleGetConditionTransfers = 6,
            MinVblankEvents = 3,
            MinHardwareAdvanceTicks = 101,
            MinHardwareAdvanceBatches = 21,
            MaxHardwareAdvanceBatch = 4,
            MinControllerScriptChanges = 2
        };
        var summary = CreateSummary(
            vblankEvents: 2,
            hardwareTicks: 100,
            hardwareBatches: 20,
            maxHardwareBatch: 5,
            controllerScriptChanges: 1,
            mapleTransfers: 9,
            mapleDeviceInfoTransfers: 4,
            mapleGetConditionTransfers: 5);

        var failures = DreamcastFixtureRunner.Validate(fixture, summary);

        Assert.Contains("expected at least 3 scheduler VBlank events, got 2", failures);
        Assert.Contains("expected at least 101 hardware advance ticks, got 100", failures);
        Assert.Contains("expected at least 21 hardware advance batches, got 20", failures);
        Assert.Contains("expected max hardware advance batch at most 4, got 5", failures);
        Assert.Contains("expected at least 2 controller script changes, got 1", failures);
        Assert.Contains("expected at least 10 Maple transfers, got 9", failures);
        Assert.Contains("expected at least 5 Maple DeviceInfo transfers, got 4", failures);
        Assert.Contains("expected at least 6 Maple GetCondition transfers, got 5", failures);
    }

    private static DreamcastRunSummary CreateSummary(
        ulong vblankEvents,
        ulong hardwareTicks,
        ulong hardwareBatches,
        ulong maxHardwareBatch,
        ulong controllerScriptChanges,
        int mapleTransfers = 0,
        int mapleDeviceInfoTransfers = 0,
        int mapleGetConditionTransfers = 0) =>
        new(
            DreamcastStopReason.ProgramExit,
            string.Empty,
            0,
            0,
            "0x00000000",
            0,
            "0x00000000",
            0,
            "0x00000000",
            null,
            null,
            null,
            null,
            null,
            new DreamcastLoadSummary(0, "0x00000000", 0, "0x00000000", 0, 0, [], 0),
            0,
            [],
            0,
            string.Empty,
            [],
            new DreamcastControllerSummary(DreamcastControllerButtons.None, "None", 0, 0, 0, 0, 0, 0),
            new DreamcastVideoSummary(0, 0, 0, "0x00000000", null, null, [], 0, [], 0, [], []),
            new DreamcastAudioSummary(0, 0, 0, "0x00000000", 0, [], [], 0),
            new DreamcastMapleSummary(mapleTransfers, mapleDeviceInfoTransfers, mapleGetConditionTransfers, []),
            new DreamcastSchedulerSummary(0, 0, vblankEvents, hardwareTicks, hardwareBatches, maxHardwareBatch, controllerScriptChanges));
}
