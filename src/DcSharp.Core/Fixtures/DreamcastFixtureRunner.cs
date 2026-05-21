using DcSharp.Core.Dreamcast.Input;
using DcSharp.Core.Execution;
using DcSharp.Core.Media;
using System.Globalization;

namespace DcSharp.Core.Fixtures;

public static class DreamcastFixtureRunner
{
    public static DreamcastFixtureCheckResult Run(DreamcastFixtureDefinition fixture, string artifactPath)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        using var stream = File.OpenRead(artifactPath);
        var options = CreateRunOptions(fixture);
        var result = new DreamcastRunner().Run(ElfFile.Read(stream), options);
        var summary = DreamcastRunSummary.FromResult(result, options);

        return new DreamcastFixtureCheckResult(fixture.Name, artifactPath, summary, Validate(fixture, summary));
    }

    public static DreamcastRunOptions CreateRunOptions(DreamcastFixtureDefinition fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        var controllerA = fixture.ControllerA is null
            ? null
            : DreamcastControllerStateParser.ParseState(fixture.ControllerA);
        var controllerB = fixture.ControllerB is null
            ? null
            : DreamcastControllerStateParser.ParseState(fixture.ControllerB);
        var controllers = fixture.Controllers.ToDictionary(
            entry => DreamcastControllerStateParser.ParseMapleAddress(entry.Key),
            entry => DreamcastControllerStateParser.ParseState(entry.Value));
        var controllerScript = fixture.ControllerAScript is null
            ? null
            : DreamcastControllerStateParser.ParseScript(fixture.ControllerAScript);
        var controllerScripts = fixture.ControllerScripts.ToDictionary(
            entry => DreamcastControllerStateParser.ParseMapleAddress(entry.Key),
            entry => DreamcastControllerStateParser.ParseScript(entry.Value));

        return new DreamcastRunOptions(
            fixture.Instructions,
            fixture.TraceTail,
            fixture.VblankInterval,
            controllerA,
            controllerScript,
            ControllerB: controllerB,
            Controllers: controllers.Count == 0 ? null : controllers,
            ControllerScripts: controllerScripts.Count == 0 ? null : controllerScripts);
    }

    public static IReadOnlyList<string> Validate(DreamcastFixtureDefinition fixture, DreamcastRunSummary summary)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        ArgumentNullException.ThrowIfNull(summary);

        var failures = new List<string>();
        if (summary.StopReason != fixture.ExpectedStopReason)
        {
            failures.Add($"expected stop {fixture.ExpectedStopReason}, got {summary.StopReason}");
        }

        foreach (var expected in fixture.SerialContains)
        {
            if (!summary.SerialText.Contains(expected, StringComparison.Ordinal))
            {
                failures.Add($"missing serial text: {expected}");
            }
        }

        if (fixture.RequireVideoNonZero && summary.Video.NonZeroBytes == 0)
        {
            failures.Add("expected non-zero video VRAM");
        }

        if (fixture.RequireNoAsicPendingInterrupt && summary.Asic.PendingEventCode is not null)
        {
            failures.Add($"expected no pending ASIC interrupt, got {summary.Asic.PendingEventCodeHex} level {summary.Asic.PendingLevel}");
        }

        if (fixture.AsicPendingInterrupt is { } expectedPending)
        {
            var pending = summary.Asic.PendingInterrupt;
            if (pending is null)
            {
                failures.Add("missing pending ASIC interrupt");
            }
            else
            {
                ValidateHex32(failures, "ASIC pending interrupt event code", expectedPending.EventCode, pending.EventCode, pending.EventCodeHex);
                ValidateInt(failures, "ASIC pending interrupt level", expectedPending.Level, pending.Level);
                ValidateString(failures, "ASIC pending interrupt level name", expectedPending.LevelName, pending.LevelName);
                ValidateString(failures, "ASIC pending interrupt register", expectedPending.RegisterName, pending.RegisterName);
                ValidateInt(failures, "ASIC pending interrupt bit", expectedPending.Bit, pending.Bit);
                ValidateHex32(failures, "ASIC pending interrupt bit mask", expectedPending.BitMask, pending.BitMask, pending.BitMaskHex);
            }
        }

        if (fixture.MinPvrRegisterAccesses is { } minPvrRegisterAccesses && summary.Video.PvrRegisterAccessCount < minPvrRegisterAccesses)
        {
            failures.Add($"expected at least {minPvrRegisterAccesses} PVR register accesses, got {summary.Video.PvrRegisterAccessCount}");
        }

        if (fixture.MinPvrTaCommandWrites is { } minPvrTaCommandWrites && summary.Video.PvrTaCommandWriteCount < minPvrTaCommandWrites)
        {
            failures.Add($"expected at least {minPvrTaCommandWrites} PVR TA writes, got {summary.Video.PvrTaCommandWriteCount}");
        }

        if (fixture.MinAicaRegisterAccesses is { } minAicaRegisterAccesses && summary.Audio.RegisterAccessCount < minAicaRegisterAccesses)
        {
            failures.Add($"expected at least {minAicaRegisterAccesses} AICA register accesses, got {summary.Audio.RegisterAccessCount}");
        }

        if (fixture.MinMapleTransfers is { } minMapleTransfers && summary.Maple.TransferCount < minMapleTransfers)
        {
            failures.Add($"expected at least {minMapleTransfers} Maple transfers, got {summary.Maple.TransferCount}");
        }

        if (fixture.MinMapleDeviceInfoTransfers is { } minMapleDeviceInfoTransfers && summary.Maple.DeviceInfoCount < minMapleDeviceInfoTransfers)
        {
            failures.Add($"expected at least {minMapleDeviceInfoTransfers} Maple DeviceInfo transfers, got {summary.Maple.DeviceInfoCount}");
        }

        if (fixture.MinMapleGetConditionTransfers is { } minMapleGetConditionTransfers && summary.Maple.GetConditionCount < minMapleGetConditionTransfers)
        {
            failures.Add($"expected at least {minMapleGetConditionTransfers} Maple GetCondition transfers, got {summary.Maple.GetConditionCount}");
        }

        if (fixture.MinMapleDmaBatches is { } minMapleDmaBatches && summary.Maple.DmaBatchCount < minMapleDmaBatches)
        {
            failures.Add($"expected at least {minMapleDmaBatches} Maple DMA batches, got {summary.Maple.DmaBatchCount}");
        }

        if (fixture.RequireNoMapleDescriptorLimitHits && summary.Maple.DescriptorLimitHitCount != 0)
        {
            failures.Add($"expected no Maple descriptor-limit hits, got {summary.Maple.DescriptorLimitHitCount}");
        }

        if (fixture.MinVblankEvents is { } minVblankEvents && summary.Scheduler.VBlankEventsRaised < minVblankEvents)
        {
            failures.Add($"expected at least {minVblankEvents} scheduler VBlank events, got {summary.Scheduler.VBlankEventsRaised}");
        }

        if (fixture.MinHardwareAdvanceTicks is { } minHardwareAdvanceTicks && summary.Scheduler.HardwareAdvanceTicks < minHardwareAdvanceTicks)
        {
            failures.Add($"expected at least {minHardwareAdvanceTicks} hardware advance ticks, got {summary.Scheduler.HardwareAdvanceTicks}");
        }

        if (fixture.MinHardwareAdvanceBatches is { } minHardwareAdvanceBatches && summary.Scheduler.HardwareAdvanceBatches < minHardwareAdvanceBatches)
        {
            failures.Add($"expected at least {minHardwareAdvanceBatches} hardware advance batches, got {summary.Scheduler.HardwareAdvanceBatches}");
        }

        if (fixture.MaxHardwareAdvanceBatch is { } maxHardwareAdvanceBatch && summary.Scheduler.MaxHardwareAdvanceBatch > maxHardwareAdvanceBatch)
        {
            failures.Add($"expected max hardware advance batch at most {maxHardwareAdvanceBatch}, got {summary.Scheduler.MaxHardwareAdvanceBatch}");
        }

        if (fixture.MinIdleAdvanceTicks is { } minIdleAdvanceTicks && summary.Scheduler.IdleAdvanceTicks < minIdleAdvanceTicks)
        {
            failures.Add($"expected at least {minIdleAdvanceTicks} idle advance ticks, got {summary.Scheduler.IdleAdvanceTicks}");
        }

        if (fixture.MinIdleAdvanceBatches is { } minIdleAdvanceBatches && summary.Scheduler.IdleAdvanceBatches < minIdleAdvanceBatches)
        {
            failures.Add($"expected at least {minIdleAdvanceBatches} idle advance batches, got {summary.Scheduler.IdleAdvanceBatches}");
        }

        if (fixture.MaxIdleAdvanceBatch is { } maxIdleAdvanceBatch && summary.Scheduler.MaxIdleAdvanceBatch > maxIdleAdvanceBatch)
        {
            failures.Add($"expected max idle advance batch at most {maxIdleAdvanceBatch}, got {summary.Scheduler.MaxIdleAdvanceBatch}");
        }

        if (fixture.MinIdleTimerWakes is { } minIdleTimerWakes && summary.Scheduler.IdleTimerWakeCount < minIdleTimerWakes)
        {
            failures.Add($"expected at least {minIdleTimerWakes} idle timer wakes, got {summary.Scheduler.IdleTimerWakeCount}");
        }

        if (fixture.MinIdleVBlankWakes is { } minIdleVBlankWakes && summary.Scheduler.IdleVBlankWakeCount < minIdleVBlankWakes)
        {
            failures.Add($"expected at least {minIdleVBlankWakes} idle VBlank wakes, got {summary.Scheduler.IdleVBlankWakeCount}");
        }

        if (fixture.MinIdleInputWakes is { } minIdleInputWakes && summary.Scheduler.IdleInputWakeCount < minIdleInputWakes)
        {
            failures.Add($"expected at least {minIdleInputWakes} idle input wakes, got {summary.Scheduler.IdleInputWakeCount}");
        }

        if (fixture.MinCpuFastForwardInstructions is { } minCpuFastForwardInstructions && summary.Scheduler.CpuFastForwardInstructions < minCpuFastForwardInstructions)
        {
            failures.Add($"expected at least {minCpuFastForwardInstructions} CPU fast-forwarded instructions, got {summary.Scheduler.CpuFastForwardInstructions}");
        }

        if (fixture.MinCpuFastForwardBatches is { } minCpuFastForwardBatches && summary.Scheduler.CpuFastForwardBatches < minCpuFastForwardBatches)
        {
            failures.Add($"expected at least {minCpuFastForwardBatches} CPU fast-forward batches, got {summary.Scheduler.CpuFastForwardBatches}");
        }

        if (fixture.MaxCpuFastForwardBatch is { } maxCpuFastForwardBatch && summary.Scheduler.MaxCpuFastForwardBatch > maxCpuFastForwardBatch)
        {
            failures.Add($"expected max CPU fast-forward batch at most {maxCpuFastForwardBatch}, got {summary.Scheduler.MaxCpuFastForwardBatch}");
        }

        if (fixture.MinControllerScriptChanges is { } minControllerScriptChanges && summary.Scheduler.ControllerScriptChanges < minControllerScriptChanges)
        {
            failures.Add($"expected at least {minControllerScriptChanges} controller script changes, got {summary.Scheduler.ControllerScriptChanges}");
        }

        foreach (var (domain, minCount) in fixture.MinDeviceAccessDomains)
        {
            var count = summary.DeviceAccessDomains.SingleOrDefault(access => string.Equals(access.Domain, domain, StringComparison.OrdinalIgnoreCase))?.Count ?? 0;
            if (count < minCount)
            {
                failures.Add($"expected at least {minCount} {domain} device accesses, got {count}");
            }
        }

        foreach (var expected in fixture.PvrTaCommands)
        {
            var count = summary.Video.PvrTaCommandKinds.SingleOrDefault(kind => string.Equals(kind.Kind, expected.Kind, StringComparison.Ordinal))?.Count ?? 0;
            if (count < expected.MinCount)
            {
                failures.Add($"expected at least {expected.MinCount} PVR TA {expected.Kind} commands, got {count}");
            }
        }

        foreach (var (registerName, expectedValueText) in fixture.PvrRegisters)
        {
            var register = summary.Video.PvrRegisters.SingleOrDefault(register => string.Equals(register.Name, registerName, StringComparison.Ordinal));
            if (register is null)
            {
                failures.Add($"missing PVR register: {registerName}");
                continue;
            }

            var expectedValue = ParseHex32(expectedValueText, registerName);
            if (register.Value != expectedValue)
            {
                failures.Add($"PVR register {registerName} expected 0x{expectedValue:X8}, got {register.ValueHex}");
            }
        }

        foreach (var expected in fixture.AsicEventRegisters)
        {
            var register = summary.Asic.EventRegisters.SingleOrDefault(register => string.Equals(register.Name, expected.Name, StringComparison.Ordinal));
            if (register is null)
            {
                failures.Add($"missing ASIC event register: {expected.Name}");
                continue;
            }

            ValidateHex32(failures, $"ASIC event register {expected.Name} ack", expected.Ack, register.Ack, register.AckHex);
            ValidateHex32(failures, $"ASIC event register {expected.Name} IRQ9 mask", expected.Irq9Mask, register.Irq9Mask, register.Irq9MaskHex);
            ValidateHex32(failures, $"ASIC event register {expected.Name} IRQB mask", expected.IrqBMask, register.IrqBMask, register.IrqBMaskHex);
            ValidateHex32(failures, $"ASIC event register {expected.Name} IRQD mask", expected.IrqDMask, register.IrqDMask, register.IrqDMaskHex);
            ValidateHex32(failures, $"ASIC event register {expected.Name} pending IRQ9", expected.PendingIrq9, register.PendingIrq9, register.PendingIrq9Hex);
            ValidateHex32(failures, $"ASIC event register {expected.Name} pending IRQB", expected.PendingIrqB, register.PendingIrqB, register.PendingIrqBHex);
            ValidateHex32(failures, $"ASIC event register {expected.Name} pending IRQD", expected.PendingIrqD, register.PendingIrqD, register.PendingIrqDHex);
        }

        foreach (var (registerName, expectedValueText) in fixture.AicaRegisters)
        {
            var register = summary.Audio.Registers.SingleOrDefault(register => string.Equals(register.Name, registerName, StringComparison.Ordinal));
            if (register is null)
            {
                failures.Add($"missing AICA register: {registerName}");
                continue;
            }

            var expectedValue = ParseHex32(expectedValueText, $"AICA register {registerName}");
            if (register.Value != expectedValue)
            {
                failures.Add($"AICA register {registerName} expected 0x{expectedValue:X8}, got {register.ValueHex}");
            }
        }

        foreach (var expected in fixture.AicaChannels)
        {
            var channel = summary.Audio.Channels.SingleOrDefault(channel => channel.Channel == expected.Channel);
            if (channel is null)
            {
                failures.Add($"missing AICA channel: {expected.Channel}");
                continue;
            }

            ValidateHex32(failures, $"AICA channel {expected.Channel} control", expected.Control, channel.Control, channel.ControlHex);
            ValidateString(failures, $"AICA channel {expected.Channel} sample format", expected.SampleFormat, channel.SampleFormat);
            ValidateHex32(failures, $"AICA channel {expected.Channel} sample address", expected.SampleAddress, channel.SampleAddress, channel.SampleAddressHex);
            ValidateHex32(failures, $"AICA channel {expected.Channel} loop start", expected.LoopStart, channel.LoopStart, channel.LoopStartHex);
            ValidateHex32(failures, $"AICA channel {expected.Channel} loop end", expected.LoopEnd, channel.LoopEnd, channel.LoopEndHex);
            ValidateHex32(failures, $"AICA channel {expected.Channel} pitch", expected.Pitch, channel.Pitch, channel.PitchHex);
            ValidateByte(failures, $"AICA channel {expected.Channel} pan", expected.Pan, channel.Pan);
            ValidateByte(failures, $"AICA channel {expected.Channel} volume", expected.Volume, channel.Volume);
            ValidateBool(failures, $"AICA channel {expected.Channel} active", expected.Active, channel.Active);
            ValidateBool(failures, $"AICA channel {expected.Channel} keyOn", expected.KeyOn, channel.KeyOn);
            ValidateBool(failures, $"AICA channel {expected.Channel} keyOnExecute", expected.KeyOnExecute, channel.KeyOnExecute);
        }

        foreach (var expected in fixture.VideoSamples)
        {
            var sample = summary.Video.Samples.SingleOrDefault(sample => string.Equals(sample.Name, expected.Name, StringComparison.Ordinal));
            if (sample is null)
            {
                failures.Add($"missing video sample: {expected.Name}");
                continue;
            }

            var expectedRgb565 = ParseHex16(expected.Rgb565, expected.Name);
            if (sample.Rgb565 != expectedRgb565)
            {
                failures.Add($"video sample {expected.Name} expected 0x{expectedRgb565:X4}, got {sample.Rgb565Hex}");
            }
        }

        return failures;
    }

    private static ushort ParseHex16(string text, string sampleName)
    {
        var value = text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? text[2..] : text;
        if (!ushort.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var parsed))
        {
            throw new InvalidDataException($"Video sample '{sampleName}' has invalid RGB565 value '{text}'.");
        }

        return parsed;
    }

    private static void ValidateHex32(List<string> failures, string label, string? expectedText, uint actual, string actualHex)
    {
        if (expectedText is null)
        {
            return;
        }

        var expected = ParseHex32(expectedText, label);
        if (actual != expected)
        {
            failures.Add($"{label} expected 0x{expected:X8}, got {actualHex}");
        }
    }

    private static void ValidateString(List<string> failures, string label, string? expected, string actual)
    {
        if (expected is not null && !string.Equals(actual, expected, StringComparison.Ordinal))
        {
            failures.Add($"{label} expected {expected}, got {actual}");
        }
    }

    private static void ValidateByte(List<string> failures, string label, byte? expected, byte actual)
    {
        if (expected is not null && actual != expected)
        {
            failures.Add($"{label} expected {expected}, got {actual}");
        }
    }

    private static void ValidateBool(List<string> failures, string label, bool? expected, bool actual)
    {
        if (expected is not null && actual != expected)
        {
            failures.Add($"{label} expected {expected}, got {actual}");
        }
    }

    private static void ValidateInt(List<string> failures, string label, int? expected, int actual)
    {
        if (expected is not null && actual != expected)
        {
            failures.Add($"{label} expected {expected}, got {actual}");
        }
    }

    private static uint ParseHex32(string text, string description)
    {
        var value = text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? text[2..] : text;
        if (!uint.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var parsed))
        {
            throw new InvalidDataException($"{description} has invalid hex value '{text}'.");
        }

        return parsed;
    }
}

public sealed record DreamcastFixtureCheckResult(
    string Name,
    string ArtifactPath,
    DreamcastRunSummary? Summary,
    IReadOnlyList<string> Failures)
{
    public bool Passed => Failures.Count == 0;
}
