using DcSharp.Core.Dreamcast.Input;
using DcSharp.Core.Dreamcast.Video;
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
            MinMapleDmaBatches = 2,
            RequireNoMapleDescriptorLimitHits = true,
            MinVblankEvents = 2,
            MinHardwareAdvanceTicks = 100,
            MinHardwareAdvanceBatches = 20,
            MaxHardwareAdvanceBatch = 5,
            MinIdleAdvanceTicks = 10,
            MinIdleAdvanceBatches = 2,
            MaxIdleAdvanceBatch = 7,
            MinIdleTimerWakes = 1,
            MinIdleVBlankWakes = 1,
            MinIdleInputWakes = 1,
            MinCpuFastForwardInstructions = 10,
            MinCpuFastForwardBatches = 2,
            MaxCpuFastForwardBatch = 7,
            MinControllerScriptChanges = 1,
            RequireNoAsicPendingInterrupt = true,
            AsicPendingInterrupt = null,
            MinDeviceAccessDomains =
            {
                ["tmu"] = 3
            },
            AsicEventRegisters =
            [
                new DreamcastFixtureAsicEventRegisterExpectation
                {
                    Name = "A",
                    Ack = "0x00000000",
                    Irq9Mask = "0x00000008",
                    PendingIrq9 = "0x00000000"
                }
            ],
            PvrRegisters =
            {
                ["PVR_FB_CFG_1"] = "0x00800005"
            },
            PvrTaCommands =
            [
                new DreamcastFixturePvrTaCommandExpectation
                {
                    Kind = "PolygonHeader",
                    Region = "TA_INPUT",
                    ListTypeName = "OpaquePolygon",
                    EndOfStrip = false,
                    Value = "0x80840000"
                }
            ],
            PvrTaStreamWrites =
            [
                new DreamcastFixturePvrTaStreamWriteExpectation
                {
                    Role = "Control",
                    Region = "TA_INPUT",
                    Kind = "PolygonHeader",
                    Value = "0x80840000",
                    ControlKind = "PolygonHeader",
                    ControlValue = "0x80840000",
                    PayloadWordsRemaining = 7
                },
                new DreamcastFixturePvrTaStreamWriteExpectation
                {
                    Role = "Payload",
                    Region = "TA_INPUT",
                    Kind = "Unknown",
                    Value = "0x00000000",
                    ControlKind = "PolygonHeader",
                    ControlValue = "0x80840000",
                    PayloadWordIndex = 0,
                    PayloadWordsRemaining = 6,
                    PayloadWordName = "Mode1"
                }
            ],
            PvrTaParameterHeaders =
            [
                new DreamcastFixturePvrTaParameterHeaderExpectation
                {
                    Kind = "PolygonHeader",
                    Region = "TA_INPUT",
                    ParameterType = 4,
                    ListTypeName = "OpaquePolygon",
                    EndOfStrip = false,
                    Value = "0x80840000",
                    ExpectedPayloadWords = 7,
                    HasKnownPayloadLength = true,
                    Gouraud = false,
                    TextureEnabled = false,
                    ColorFormatName = "ArgbPacked",
                    ClipModeName = "Disabled",
                    StripLengthName = "Strip2",
                    AutoStripLength = true
                }
            ],
            PvrTaLists =
            [
                new DreamcastFixturePvrTaListExpectation
                {
                    Region = "TA_INPUT",
                    ListTypeName = "OpaquePolygon",
                    MinCommands = 1,
                    MinPolygonHeaders = 1
                }
            ],
            PvrTaStrips =
            [
                new DreamcastFixturePvrTaStripExpectation
                {
                    Region = "TA_INPUT",
                    ListTypeName = "OpaquePolygon",
                    Rgb565 = "0xF800",
                    MinVertices = 3,
                    Vertices =
                    [
                        new DreamcastFixturePvrTaVertexExpectation { X = 1, Y = 1 },
                        new DreamcastFixturePvrTaVertexExpectation { X = 2, Y = 1 },
                        new DreamcastFixturePvrTaVertexExpectation { X = 1, Y = 2 }
                    ]
                }
            ],
            AicaRegisters =
            {
                ["AICA_MASTER_VOLUME"] = "0x0000000F"
            },
            AicaChannels =
            [
                new DreamcastFixtureAicaChannelExpectation
                {
                    Channel = 0,
                    Control = "0x00008000",
                    SampleFormat = "Pcm16",
                    SampleAddress = "0x00001234",
                    LoopStart = "0x00000008",
                    LoopEnd = "0x00000040",
                    Pitch = "0x00001AC0",
                    Pan = 15,
                    Volume = 64,
                    Active = false,
                    KeyOn = false,
                    KeyOnExecute = true
                }
            ]
        };
        var summary = CreateSummary(
            vblankEvents: 2,
            hardwareTicks: 100,
            hardwareBatches: 20,
            maxHardwareBatch: 5,
            idleAdvanceTicks: 10,
            idleAdvanceBatches: 2,
            maxIdleAdvanceBatch: 7,
            idleTimerWakeCount: 1,
            idleVBlankWakeCount: 1,
            idleInputWakeCount: 1,
            cpuFastForwardInstructions: 10,
            cpuFastForwardBatches: 2,
            maxCpuFastForwardBatch: 7,
            controllerScriptChanges: 1,
            tmuDeviceAccesses: 3,
            mapleTransfers: 9,
            mapleDeviceInfoTransfers: 4,
            mapleGetConditionTransfers: 5,
            mapleDmaBatches: 2,
            asic: CreateAsicSummary(),
            pvrRegisters: [new DreamcastPvrRegisterValueSummary(0x0044, "0x0044", "PVR_FB_CFG_1", 0x00800005, "0x00800005")],
            pvrTaCommandWrites:
            [
                new DreamcastPvrTaCommandWriteSummary(
                    0x1000_0000,
                    "0x10000000",
                    "TA_INPUT",
                    "PolygonHeader",
                    0,
                    "OpaquePolygon",
                    false,
                    4,
                    0x8084_0000,
                    "0x80840000"),
                new DreamcastPvrTaCommandWriteSummary(
                    0x1000_0000,
                    "0x10000000",
                    "TA_INPUT",
                    "Unknown",
                    0,
                    "OpaquePolygon",
                    false,
                    4,
                    0x0000_0000,
                    "0x00000000")
            ],
            pvrTaStrips:
            [
                new DreamcastPvrTaStripSummary(
                    "TA_INPUT",
                    0,
                    "OpaquePolygon",
                    0x8084_0000,
                    "0x80840000",
                    null,
                    0xF800,
                    "0xF800",
                    3,
                    [
                        CreatePvrTaVertexSummary(1, 1),
                        CreatePvrTaVertexSummary(2, 1),
                        CreatePvrTaVertexSummary(1, 2, endOfStrip: true)
                    ])
            ],
            aicaRegisters: [new DreamcastAicaRegisterValueSummary(0x2800, "0x2800", "AICA_MASTER_VOLUME", null, 0x0000000F, "0x0000000F")],
            aicaChannels: [CreateAudioChannel()]);

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
            MinMapleDmaBatches = 2,
            RequireNoMapleDescriptorLimitHits = true,
            MinVblankEvents = 3,
            MinHardwareAdvanceTicks = 101,
            MinHardwareAdvanceBatches = 21,
            MaxHardwareAdvanceBatch = 4,
            MinIdleAdvanceTicks = 11,
            MinIdleAdvanceBatches = 3,
            MaxIdleAdvanceBatch = 6,
            MinIdleTimerWakes = 2,
            MinIdleVBlankWakes = 2,
            MinIdleInputWakes = 2,
            MinCpuFastForwardInstructions = 11,
            MinCpuFastForwardBatches = 3,
            MaxCpuFastForwardBatch = 6,
            MinControllerScriptChanges = 2,
            RequireNoAsicPendingInterrupt = true,
            AsicPendingInterrupt = new DreamcastFixtureAsicPendingInterruptExpectation
            {
                EventCode = "0x0360",
                Level = 11,
                LevelName = "IRQB",
                RegisterName = "A",
                Bit = 12,
                BitMask = "0x00001000"
            },
            MinDeviceAccessDomains =
            {
                ["tmu"] = 3
            },
            AsicEventRegisters =
            [
                new DreamcastFixtureAsicEventRegisterExpectation
                {
                    Name = "A",
                    Ack = "0x00000000",
                    Irq9Mask = "0x00000008"
                },
                new DreamcastFixtureAsicEventRegisterExpectation
                {
                    Name = "B",
                    Ack = "0x00000000"
                }
            ],
            PvrRegisters =
            {
                ["PVR_FB_CFG_1"] = "0x00800005",
                ["PVR_FB_SIZE"] = "0x00177D3F"
            },
            PvrTaCommands =
            [
                new DreamcastFixturePvrTaCommandExpectation
                {
                    Kind = "PolygonHeader",
                    Region = "TA_INPUT",
                    ListTypeName = "OpaquePolygon",
                    EndOfStrip = false,
                    Value = "0x80840001"
                }
            ],
            PvrTaStreamWrites =
            [
                new DreamcastFixturePvrTaStreamWriteExpectation
                {
                    Role = "Payload",
                    Region = "TA_INPUT",
                    Kind = "Unknown",
                    Value = "0x3F800000",
                    ControlKind = "Vertex",
                    ControlValue = "0xE0000000",
                    PayloadWordIndex = 0,
                    PayloadWordsRemaining = 6,
                    PayloadWordName = "Mode1"
                }
            ],
            PvrTaParameterHeaders =
            [
                new DreamcastFixturePvrTaParameterHeaderExpectation
                {
                    Kind = "PolygonHeader",
                    Region = "TA_INPUT",
                    ParameterType = 5,
                    ListTypeName = "OpaquePolygon",
                    Value = "0x80840001",
                    ExpectedPayloadWords = 7,
                    HasKnownPayloadLength = true,
                    TextureEnabled = true,
                    ColorFormatName = "FourFloats",
                    AutoStripLength = false
                }
            ],
            PvrTaPolygonHeaderPayloads =
            [
                new DreamcastFixturePvrTaPolygonHeaderPayloadExpectation
                {
                    Region = "TA_INPUT",
                    ListTypeName = "OpaquePolygon",
                    HeaderValue = "0x80840000",
                    Mode1 = "0x00000001",
                    DepthCompareName = "Less"
                }
            ],
            PvrTaLists =
            [
                new DreamcastFixturePvrTaListExpectation
                {
                    Region = "TA_INPUT",
                    ListTypeName = "OpaquePolygon",
                    MinCommands = 2,
                    MinVertices = 1
                },
                new DreamcastFixturePvrTaListExpectation
                {
                    Region = "TA_INPUT",
                    ListTypeName = "TranslucentPolygon",
                    MinCommands = 1
                }
            ],
            PvrTaStrips =
            [
                new DreamcastFixturePvrTaStripExpectation
                {
                    Region = "TA_INPUT",
                    ListTypeName = "OpaquePolygon",
                    Rgb565 = "0xF800",
                    MinVertices = 3,
                    Vertices =
                    [
                        new DreamcastFixturePvrTaVertexExpectation { X = 1, Y = 1 },
                        new DreamcastFixturePvrTaVertexExpectation { X = 2, Y = 1 },
                        new DreamcastFixturePvrTaVertexExpectation { X = 1, Y = 2 }
                    ]
                }
            ],
            AicaRegisters =
            {
                ["AICA_MASTER_VOLUME"] = "0x0000000F",
                ["AICA_MONITOR_CHANNEL"] = "0x00000001"
            },
            AicaChannels =
            [
                new DreamcastFixtureAicaChannelExpectation
                {
                    Channel = 0,
                    Pitch = "0x00001AC0",
                    Volume = 64
                },
                new DreamcastFixtureAicaChannelExpectation
                {
                    Channel = 1,
                    Control = "0x00008000"
                }
            ]
        };
        var summary = CreateSummary(
            vblankEvents: 2,
            hardwareTicks: 100,
            hardwareBatches: 20,
            maxHardwareBatch: 5,
            idleAdvanceTicks: 10,
            idleAdvanceBatches: 2,
            maxIdleAdvanceBatch: 7,
            idleTimerWakeCount: 1,
            idleVBlankWakeCount: 1,
            idleInputWakeCount: 1,
            cpuFastForwardInstructions: 10,
            cpuFastForwardBatches: 2,
            maxCpuFastForwardBatch: 7,
            controllerScriptChanges: 1,
            tmuDeviceAccesses: 2,
            mapleTransfers: 9,
            mapleDeviceInfoTransfers: 4,
            mapleGetConditionTransfers: 5,
            mapleDmaBatches: 1,
            mapleDescriptorLimitHits: 1,
            asic: CreateAsicSummary(pendingEventCode: 0x0320, pendingLevel: 9, ack: 0x00000008, irq9Mask: 0x00000008),
            pvrRegisters: [new DreamcastPvrRegisterValueSummary(0x0044, "0x0044", "PVR_FB_CFG_1", 0x00800006, "0x00800006")],
            pvrTaCommandWrites:
            [
                new DreamcastPvrTaCommandWriteSummary(
                    0x1000_0000,
                    "0x10000000",
                    "TA_INPUT",
                    "PolygonHeader",
                    0,
                    "OpaquePolygon",
                    false,
                    4,
                    0x8084_0000,
                    "0x80840000")
            ],
            aicaRegisters: [new DreamcastAicaRegisterValueSummary(0x2800, "0x2800", "AICA_MASTER_VOLUME", null, 0x0000000E, "0x0000000E")],
            aicaChannels: [CreateAudioChannel(pitch: 0x00001ABF, volume: 63)]);

        var failures = DreamcastFixtureRunner.Validate(fixture, summary);

        Assert.Contains("expected at least 3 scheduler VBlank events, got 2", failures);
        Assert.Contains("expected at least 101 hardware advance ticks, got 100", failures);
        Assert.Contains("expected at least 21 hardware advance batches, got 20", failures);
        Assert.Contains("expected max hardware advance batch at most 4, got 5", failures);
        Assert.Contains("expected at least 11 idle advance ticks, got 10", failures);
        Assert.Contains("expected at least 3 idle advance batches, got 2", failures);
        Assert.Contains("expected max idle advance batch at most 6, got 7", failures);
        Assert.Contains("expected at least 2 idle timer wakes, got 1", failures);
        Assert.Contains("expected at least 2 idle VBlank wakes, got 1", failures);
        Assert.Contains("expected at least 2 idle input wakes, got 1", failures);
        Assert.Contains("expected at least 11 CPU fast-forwarded instructions, got 10", failures);
        Assert.Contains("expected at least 3 CPU fast-forward batches, got 2", failures);
        Assert.Contains("expected max CPU fast-forward batch at most 6, got 7", failures);
        Assert.Contains("expected at least 2 controller script changes, got 1", failures);
        Assert.Contains("expected at least 10 Maple transfers, got 9", failures);
        Assert.Contains("expected at least 5 Maple DeviceInfo transfers, got 4", failures);
        Assert.Contains("expected at least 6 Maple GetCondition transfers, got 5", failures);
        Assert.Contains("expected at least 2 Maple DMA batches, got 1", failures);
        Assert.Contains("expected no Maple descriptor-limit hits, got 1", failures);
        Assert.Contains("expected at least 3 tmu device accesses, got 2", failures);
        Assert.Contains("expected no pending ASIC interrupt, got 0x0320 level 9", failures);
        Assert.Contains("ASIC pending interrupt event code expected 0x00000360, got 0x0320", failures);
        Assert.Contains("ASIC pending interrupt level expected 11, got 9", failures);
        Assert.Contains("ASIC pending interrupt level name expected IRQB, got IRQ9", failures);
        Assert.Contains("ASIC pending interrupt bit expected 12, got 3", failures);
        Assert.Contains("ASIC pending interrupt bit mask expected 0x00001000, got 0x00000008", failures);
        Assert.Contains("ASIC event register A ack expected 0x00000000, got 0x00000008", failures);
        Assert.Contains("missing ASIC event register: B", failures);
        Assert.Contains("PVR register PVR_FB_CFG_1 expected 0x00800005, got 0x00800006", failures);
        Assert.Contains("missing PVR register: PVR_FB_SIZE", failures);
        Assert.Contains("expected at least 1 PVR TA PolygonHeader region=TA_INPUT list=OpaquePolygon endOfStrip=False value=0x80840001 commands, got 0", failures);
        Assert.Contains("expected at least 1 PVR TA stream write role=Payload region=TA_INPUT kind=Unknown value=0x3F800000 controlKind=Vertex controlValue=0xE0000000 payloadWordIndex=0 payloadWordsRemaining=6 payloadWordName=Mode1 matches, got 0", failures);
        Assert.Contains("expected at least 1 PVR TA polygon header payload region=TA_INPUT list=OpaquePolygon headerValue=0x80840000 mode1=0x00000001 depthCompareName=Less matches, got 0", failures);
        Assert.Contains("expected at least 1 PVR TA parameter header kind=PolygonHeader region=TA_INPUT parameterType=5 list=OpaquePolygon value=0x80840001 expectedPayloadWords=7 hasKnownPayloadLength=True textureEnabled=True colorFormatName=FourFloats autoStripLength=False matches, got 0", failures);
        Assert.Contains("expected PVR TA list region=TA_INPUT list=OpaquePolygon to have at least 2 commands, got 1", failures);
        Assert.Contains("expected PVR TA list region=TA_INPUT list=OpaquePolygon to have at least 1 vertices, got 0", failures);
        Assert.Contains("missing PVR TA list region=TA_INPUT list=TranslucentPolygon", failures);
        Assert.Contains("expected at least 1 PVR TA strip region=TA_INPUT list=OpaquePolygon rgb565=0xF800 minVertices=3 vertices=1,1/2,1/1,2 matches, got 0", failures);
        Assert.Contains("AICA register AICA_MASTER_VOLUME expected 0x0000000F, got 0x0000000E", failures);
        Assert.Contains("missing AICA register: AICA_MONITOR_CHANNEL", failures);
        Assert.Contains("AICA channel 0 pitch expected 0x00001AC0, got 0x00001ABF", failures);
        Assert.Contains("AICA channel 0 volume expected 64, got 63", failures);
        Assert.Contains("missing AICA channel: 1", failures);
    }

    private static DreamcastRunSummary CreateSummary(
        ulong vblankEvents,
        ulong hardwareTicks,
        ulong hardwareBatches,
        ulong maxHardwareBatch,
        ulong controllerScriptChanges,
        ulong idleAdvanceTicks = 0,
        ulong idleAdvanceBatches = 0,
        ulong maxIdleAdvanceBatch = 0,
        ulong idleTimerWakeCount = 0,
        ulong idleVBlankWakeCount = 0,
        ulong idleInputWakeCount = 0,
        ulong cpuFastForwardInstructions = 0,
        ulong cpuFastForwardBatches = 0,
        ulong maxCpuFastForwardBatch = 0,
        int tmuDeviceAccesses = 0,
        int mapleTransfers = 0,
        int mapleDeviceInfoTransfers = 0,
        int mapleGetConditionTransfers = 0,
        int mapleDmaBatches = 0,
        int mapleDescriptorLimitHits = 0,
        DreamcastAsicSummary? asic = null,
        IReadOnlyList<DreamcastPvrRegisterValueSummary>? pvrRegisters = null,
        IReadOnlyList<DreamcastPvrTaCommandWriteSummary>? pvrTaCommandWrites = null,
        IReadOnlyList<DreamcastPvrTaStripSummary>? pvrTaStrips = null,
        IReadOnlyList<DreamcastAicaRegisterValueSummary>? aicaRegisters = null,
        IReadOnlyList<DreamcastAicaChannelSummary>? aicaChannels = null) =>
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
            asic ?? new DreamcastAsicSummary([], null, null, null, null),
            CreateVideoSummary(pvrRegisters, pvrTaCommandWrites, pvrTaStrips),
            new DreamcastAudioSummary(0, 0, 0, "0x00000000", aicaRegisters ?? [], 0, [], aicaChannels ?? [], aicaChannels?.Count(channel => channel.Active) ?? 0),
            new DreamcastMapleSummary(
                mapleTransfers,
                mapleDeviceInfoTransfers,
                mapleGetConditionTransfers,
                mapleDmaBatches,
                mapleDescriptorLimitHits,
                CreateMapleDmaBatches(mapleDmaBatches, mapleDescriptorLimitHits),
                []),
            new DreamcastSchedulerSummary(
                0,
                0,
                vblankEvents,
                hardwareTicks,
                hardwareBatches,
                maxHardwareBatch,
                idleAdvanceTicks,
                idleAdvanceBatches,
                maxIdleAdvanceBatch,
                idleTimerWakeCount,
                idleVBlankWakeCount,
                idleInputWakeCount,
                cpuFastForwardInstructions,
                cpuFastForwardBatches,
                maxCpuFastForwardBatch,
                controllerScriptChanges));

    private static DreamcastVideoSummary CreateVideoSummary(
        IReadOnlyList<DreamcastPvrRegisterValueSummary>? pvrRegisters,
        IReadOnlyList<DreamcastPvrTaCommandWriteSummary>? pvrTaCommandWrites,
        IReadOnlyList<DreamcastPvrTaStripSummary>? pvrTaStrips)
    {
        var taWrites = pvrTaCommandWrites ?? [];
        return new DreamcastVideoSummary(
            0,
            0,
            0,
            "0x00000000",
            null,
            null,
            [],
            pvrRegisters ?? [],
            0,
            [],
            taWrites.Count,
            taWrites,
            DreamcastPvrTaStreamDecoder.Decode(taWrites.Select(ToCommandWrite).ToArray())
                .Select(DreamcastPvrTaStreamWriteSummary.FromWrite)
                .ToArray(),
            DreamcastPvrTaPolygonHeaderPayloadDecoder.Decode(taWrites.Select(ToCommandWrite).ToArray())
                .Select(DreamcastPvrTaPolygonHeaderPayloadSummary.FromPayload)
                .ToArray(),
            DreamcastPvrTaRealVertexPayloadDecoder.Decode(taWrites.Select(ToCommandWrite).ToArray())
                .Select(DreamcastPvrTaRealVertexPayloadSummary.FromPayload)
                .ToArray(),
            taWrites.Select(DreamcastPvrTaParameterHeaderSummary.FromWriteSummary).ToArray(),
            CreatePvrTaLists(taWrites),
            pvrTaStrips ?? [],
            taWrites
                .GroupBy(write => write.Kind, StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => new DreamcastPvrTaCommandKindSummary(group.Key, group.Count()))
                .ToArray());
    }

    private static DreamcastPvrTaCommandWrite ToCommandWrite(DreamcastPvrTaCommandWriteSummary write) =>
        new(
            write.Address,
            write.AddressHex,
            write.Region,
            write.Kind,
            write.ListType,
            write.ListTypeName,
            write.EndOfStrip,
            write.Size,
            write.Value,
            write.ValueHex);

    private static DreamcastPvrTaVertexSummary CreatePvrTaVertexSummary(int x, int y, bool endOfStrip = false)
    {
        var xValue = (uint)x << 16;
        var yValue = (uint)y << 16;
        var controlValue = endOfStrip ? 0xF000_0000u : 0xE000_0000u;
        return new DreamcastPvrTaVertexSummary(
            x,
            y,
            1.0f,
            0x3F80_0000,
            "0x3F800000",
            endOfStrip,
            0xF800,
            "0xF800",
            controlValue,
            $"0x{controlValue:X8}",
            xValue,
            $"0x{xValue:X8}",
            yValue,
            $"0x{yValue:X8}",
            0x0000_F800,
            "0x0000F800");
    }

    private static IReadOnlyList<DreamcastPvrTaListSummary> CreatePvrTaLists(IReadOnlyList<DreamcastPvrTaCommandWriteSummary> taWrites) =>
        taWrites
            .GroupBy(write => new { write.Region, write.ListType, write.ListTypeName })
            .OrderBy(group => group.Key.Region, StringComparer.Ordinal)
            .ThenBy(group => group.Key.ListType ?? int.MaxValue)
            .ThenBy(group => group.Key.ListTypeName ?? string.Empty, StringComparer.Ordinal)
            .Select(group => new DreamcastPvrTaListSummary(
                group.Key.Region,
                group.Key.ListType,
                group.Key.ListTypeName,
                group.Count(),
                group.Count(write => string.Equals(write.Kind, "PolygonHeader", StringComparison.Ordinal)),
                group.Count(write => string.Equals(write.Kind, "Vertex", StringComparison.Ordinal)),
                group.Count(write => string.Equals(write.Kind, "VertexEndOfStrip", StringComparison.Ordinal))))
            .ToArray();

    private static IReadOnlyList<DreamcastMapleDmaBatchSummary> CreateMapleDmaBatches(int count, int descriptorLimitHits) =>
        Enumerable.Range(0, count)
            .Select(index => new DreamcastMapleDmaBatchSummary(
                0x8C02_0000u + ((uint)index * 12u),
                $"0x{0x8C02_0000u + ((uint)index * 12u):X8}",
                1,
                1,
                index >= descriptorLimitHits,
                index < descriptorLimitHits,
                0x8C02_0000u + ((uint)index * 12u),
                $"0x{0x8C02_0000u + ((uint)index * 12u):X8}"))
            .ToArray();

    private static DreamcastAsicSummary CreateAsicSummary(
        uint? pendingEventCode = null,
        int? pendingLevel = null,
        uint ack = 0,
        uint irq9Mask = 0x00000008) =>
        new(
            [
                new DreamcastAsicEventRegisterSummary(
                    0,
                    "A",
                    ack,
                    $"0x{ack:X8}",
                    irq9Mask,
                    $"0x{irq9Mask:X8}",
                    0,
                    "0x00000000",
                    0,
                    "0x00000000",
                    ack & irq9Mask,
                    $"0x{ack & irq9Mask:X8}",
                    0,
                    "0x00000000",
                    0,
                    "0x00000000")
            ],
            pendingEventCode,
            pendingEventCode is { } code ? $"0x{code:X4}" : null,
            pendingLevel,
            pendingEventCode is { } sourceCode && pendingLevel is { } sourceLevel
                ? new DreamcastAsicPendingInterruptSummary(sourceCode, $"0x{sourceCode:X4}", sourceLevel, "IRQ9", 0, "A", 3, 0x00000008, "0x00000008")
                : null);

    private static DreamcastAicaChannelSummary CreateAudioChannel(
        int channel = 0,
        uint control = 0x00008000,
        string sampleFormat = "Pcm16",
        uint sampleAddress = 0x00001234,
        uint loopStart = 0x00000008,
        uint loopEnd = 0x00000040,
        uint pitch = 0x00001AC0,
        byte pan = 15,
        byte volume = 64,
        bool active = false,
        bool keyOn = false,
        bool keyOnExecute = true) =>
        new(
            channel,
            control,
            $"0x{control:X8}",
            sampleFormat,
            false,
            sampleAddress,
            $"0x{sampleAddress:X8}",
            sampleAddress,
            $"0x{sampleAddress:X8}",
            loopStart,
            $"0x{loopStart:X8}",
            loopEnd,
            $"0x{loopEnd:X8}",
            pitch,
            $"0x{pitch:X8}",
            pan,
            volume,
            active,
            keyOn,
            keyOnExecute);
}
