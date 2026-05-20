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

        if (fixture.MinControllerScriptChanges is { } minControllerScriptChanges && summary.Scheduler.ControllerScriptChanges < minControllerScriptChanges)
        {
            failures.Add($"expected at least {minControllerScriptChanges} controller script changes, got {summary.Scheduler.ControllerScriptChanges}");
        }

        foreach (var expected in fixture.PvrTaCommands)
        {
            var count = summary.Video.PvrTaCommandKinds.SingleOrDefault(kind => string.Equals(kind.Kind, expected.Kind, StringComparison.Ordinal))?.Count ?? 0;
            if (count < expected.MinCount)
            {
                failures.Add($"expected at least {expected.MinCount} PVR TA {expected.Kind} commands, got {count}");
            }
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
}

public sealed record DreamcastFixtureCheckResult(
    string Name,
    string ArtifactPath,
    DreamcastRunSummary? Summary,
    IReadOnlyList<string> Failures)
{
    public bool Passed => Failures.Count == 0;
}
