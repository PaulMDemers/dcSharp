using DcSharp.Core.Dreamcast.Input;
using DcSharp.Core.Execution;
using DcSharp.Core.Fixtures;

namespace DcSharp.Tests;

public class DreamcastFixtureRunnerTests
{
    [Fact]
    public void CreateRunOptionsParsesControllerB()
    {
        var options = DreamcastFixtureRunner.CreateRunOptions(new DreamcastFixtureDefinition
        {
            Name = "controller_b",
            Artifact = "controller_b.elf",
            Instructions = 1,
            ControllerB = "b,ltrig=7"
        });

        var controllerB = Assert.IsType<DreamcastControllerState>(options.ControllerB);
        Assert.Equal(DreamcastControllerButtons.B, controllerB.Buttons);
        Assert.Equal(7, controllerB.LeftTrigger);
    }

    [Fact]
    public void CreateRunOptionsParsesControllerMap()
    {
        var options = DreamcastFixtureRunner.CreateRunOptions(new DreamcastFixtureDefinition
        {
            Name = "controller_map",
            Artifact = "controller_map.elf",
            Instructions = 1,
            Controllers =
            {
                ["a0"] = "start",
                ["b0"] = "b,ltrig=7"
            }
        });

        var controllers = Assert.IsAssignableFrom<IReadOnlyDictionary<byte, DreamcastControllerState>>(options.Controllers);
        Assert.Equal(DreamcastControllerButtons.Start, controllers[0x20].Buttons);
        Assert.Equal(DreamcastControllerButtons.B, controllers[0x40].Buttons);
        Assert.Equal(7, controllers[0x40].LeftTrigger);
    }

    [Fact]
    public void CreateRunOptionsParsesControllerScriptMap()
    {
        var options = DreamcastFixtureRunner.CreateRunOptions(new DreamcastFixtureDefinition
        {
            Name = "controller_script_map",
            Artifact = "controller_script_map.elf",
            Instructions = 1,
            ControllerScripts =
            {
                ["b0"] = "0:none;10:b,ltrig=7"
            }
        });

        var scripts = Assert.IsAssignableFrom<IReadOnlyDictionary<byte, DreamcastControllerScript>>(options.ControllerScripts);
        Assert.Equal(DreamcastControllerButtons.None, scripts[0x40].StateAt(0).Buttons);
        Assert.Equal(DreamcastControllerButtons.B, scripts[0x40].StateAt(10).Buttons);
        Assert.Equal(7, scripts[0x40].StateAt(10).LeftTrigger);
    }

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
            MinControllerScriptChanges = 1,
            MinDeviceAccessDomains =
            {
                ["tmu"] = 3
            },
            PvrRegisters =
            {
                ["PVR_FB_CFG_1"] = "0x00800005"
            }
        };
        var summary = CreateSummary(
            vblankEvents: 2,
            hardwareTicks: 100,
            hardwareBatches: 20,
            maxHardwareBatch: 5,
            controllerScriptChanges: 1,
            tmuDeviceAccesses: 3,
            mapleTransfers: 9,
            mapleDeviceInfoTransfers: 4,
            mapleGetConditionTransfers: 5,
            pvrRegisters: [new DreamcastPvrRegisterValueSummary(0x0044, "0x0044", "PVR_FB_CFG_1", 0x00800005, "0x00800005")]);

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
            MinControllerScriptChanges = 2,
            MinDeviceAccessDomains =
            {
                ["tmu"] = 3
            },
            PvrRegisters =
            {
                ["PVR_FB_CFG_1"] = "0x00800005",
                ["PVR_FB_SIZE"] = "0x00177D3F"
            }
        };
        var summary = CreateSummary(
            vblankEvents: 2,
            hardwareTicks: 100,
            hardwareBatches: 20,
            maxHardwareBatch: 5,
            controllerScriptChanges: 1,
            tmuDeviceAccesses: 2,
            mapleTransfers: 9,
            mapleDeviceInfoTransfers: 4,
            mapleGetConditionTransfers: 5,
            pvrRegisters: [new DreamcastPvrRegisterValueSummary(0x0044, "0x0044", "PVR_FB_CFG_1", 0x00800006, "0x00800006")]);

        var failures = DreamcastFixtureRunner.Validate(fixture, summary);

        Assert.Contains("expected at least 3 scheduler VBlank events, got 2", failures);
        Assert.Contains("expected at least 101 hardware advance ticks, got 100", failures);
        Assert.Contains("expected at least 21 hardware advance batches, got 20", failures);
        Assert.Contains("expected max hardware advance batch at most 4, got 5", failures);
        Assert.Contains("expected at least 2 controller script changes, got 1", failures);
        Assert.Contains("expected at least 10 Maple transfers, got 9", failures);
        Assert.Contains("expected at least 5 Maple DeviceInfo transfers, got 4", failures);
        Assert.Contains("expected at least 6 Maple GetCondition transfers, got 5", failures);
        Assert.Contains("expected at least 3 tmu device accesses, got 2", failures);
        Assert.Contains("PVR register PVR_FB_CFG_1 expected 0x00800005, got 0x00800006", failures);
        Assert.Contains("missing PVR register: PVR_FB_SIZE", failures);
    }

    private static DreamcastRunSummary CreateSummary(
        ulong vblankEvents,
        ulong hardwareTicks,
        ulong hardwareBatches,
        ulong maxHardwareBatch,
        ulong controllerScriptChanges,
        int tmuDeviceAccesses = 0,
        int mapleTransfers = 0,
        int mapleDeviceInfoTransfers = 0,
        int mapleGetConditionTransfers = 0,
        IReadOnlyList<DreamcastPvrRegisterValueSummary>? pvrRegisters = null) =>
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
            tmuDeviceAccesses,
            tmuDeviceAccesses == 0 ? [] : [new DreamcastDeviceAccessDomainSummary("tmu", tmuDeviceAccesses)],
            [],
            [],
            0,
            string.Empty,
            [],
            new DreamcastControllerSummary(DreamcastControllerButtons.None, "None", 0, 0, 0, 0, 0, 0),
            new DreamcastVideoSummary(0, 0, 0, "0x00000000", null, null, [], pvrRegisters ?? [], 0, [], 0, [], []),
            new DreamcastAudioSummary(0, 0, 0, "0x00000000", 0, [], [], 0),
            new DreamcastMapleSummary(mapleTransfers, mapleDeviceInfoTransfers, mapleGetConditionTransfers, []),
            new DreamcastSchedulerSummary(0, 0, vblankEvents, hardwareTicks, hardwareBatches, maxHardwareBatch, controllerScriptChanges));
}
