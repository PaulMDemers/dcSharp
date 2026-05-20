# dcSharp Development Runbook

This project is currently optimized around legal KallistiOS fixtures, deterministic CLI runs, and small emulator slices that can be regression-tested.

## Toolchain

KallistiOS is installed in WSL:

```bash
~/kos
~/kos-ports
~/sh-elf
```

Verify the SDK:

```bash
wsl -e bash tools/kos/verify-kos.sh
```

## Build Fixtures

Build every sample referenced by `fixtures/kos.json`:

```bash
wsl -e bash tools/kos/build-fixtures.sh
```

Build individual KOS samples:

```bash
wsl -e bash tools/kos/build-sample.sh samples/kos/minimal
wsl -e bash tools/kos/build-sample.sh samples/kos/hello
wsl -e bash tools/kos/build-sample.sh samples/kos/timer
wsl -e bash tools/kos/build-sample.sh samples/kos/maple_controller
wsl -e bash tools/kos/build-sample.sh samples/kos/maple_controller_script
wsl -e bash tools/kos/build-sample.sh samples/kos/maple_controller_b
wsl -e bash tools/kos/build-sample.sh samples/kos/framebuffer
wsl -e bash tools/kos/build-sample.sh samples/kos/pvr_registers
wsl -e bash tools/kos/build-sample.sh samples/kos/aica_registers
```

Generated ELF files are copied to `artifacts/kos/`, which is intentionally ignored by git.

## Run Fixtures

Text summary:

```bash
dotnet run --project src/DcSharp.Cli -- run artifacts/kos/dcsharp_probe.elf --instructions 50000000 --trace-tail 40
```

JSON summary:

```bash
dotnet run --project src/DcSharp.Cli -- run artifacts/kos/dcsharp_timer.elf --instructions 50000000 --trace-tail 40 --json
```

Manifest regression run:

```bash
dotnet run --project src/DcSharp.Cli -- fixtures fixtures/kos.json
dotnet run --project src/DcSharp.Cli -- fixtures fixtures/kos.json --report-json artifacts/reports/kos-fixtures.json
```

Useful run options:

- `--instructions <count>` sets the execution budget.
- `--trace-tail <count>` controls how many final SH-4 steps are retained.
- `--vblank-interval <instructions>` controls the current synthetic VBlank cadence.
- `--vblank-interval 0` disables synthetic VBlank.
- `--controller a0:start,a,joyx=-12,joyy=13,ltrig=40,rtrig=80` maps a static controller state to a Maple address.
- `--controller-script "a0:0:none;200000:start,a"` maps an instruction-indexed controller script to a Maple address.
- `--controller-a start,a,joyx=-12,joyy=13,ltrig=40,rtrig=80` and `--controller-b b,ltrig=7` are compatibility shorthands for A0 and B0.
- `--controller-a-script "0:none;200000:start,a"` is a compatibility shorthand for A0 scripts.
- `--dump-framebuffer artifacts/video/framebuffer.png --framebuffer-size 320x240` writes the current RGB565 VRAM snapshot as a PNG.
- `--pixel-format rgb565` is accepted explicitly; RGB565 is currently the only framebuffer dump format.
- `--trace-log artifacts/logs/trace.txt --trace-pc 0x8C010000-0x8C010100 --trace-log-limit 4096` writes a bounded filtered SH-4 trace.
- `--device-log artifacts/logs/devices.txt --device-domain pvr --device-kind Write` writes filtered device accesses.
- `--json` or `--summary-json` emits structured output for scripts and regression checks.
- `fixtures --report-json artifacts/reports/kos-fixtures.json` writes a structured fixture report while keeping the text summary on stdout.

The fixture manifest keeps sample paths, artifact names, instruction budgets, static `controllers`, instruction-indexed `controllerScripts`, and expected serial/video checks together. Fixture text and JSON reports include Maple transfer counts plus scheduler VBlank, hardware tick, hardware batch, max batch, and controller-script change diagnostics for timing comparisons. Manifests can also set optional Maple thresholds such as `minMapleTransfers`, `minMapleDeviceInfoTransfers`, and `minMapleGetConditionTransfers`, plus scheduler thresholds such as `minVblankEvents`, `minHardwareAdvanceTicks`, `minHardwareAdvanceBatches`, `maxHardwareAdvanceBatch`, and `minControllerScriptChanges`. Use `--artifacts <path>` with the `fixtures` command when testing a different artifact directory.

KOS fixtures are usually unstripped. When `.symtab` or `.dynsym` is present, text and JSON summaries include nearest function names for stop PCs and trace-tail entries.

Generated framebuffer, trace, and device logs belong under `artifacts/` and stay out of git.

Run summaries also include scheduler diagnostics for synthetic VBlank events, hardware advancement ticks, coalesced hardware batches, max hardware batch size, and controller-script state changes.

Device domains currently include `pvr`, `aica`, `maple`, `asic`, `holly`, `scif`, `tmu`, `sh4`, `unmapped`, and `other`.

PVR TA writes are classified into diagnostic command kinds such as `PolygonHeader`, `Vertex`, `VertexEndOfStrip`, `SpriteHeader`, `ModifierVolume`, `UserClip`, and `YuvConverterData`.

AICA channel summaries decode sample format, loop enable, sample address, key-on state, and active channel count while remaining silence-safe.

Maple summaries capture DMA command/response names, destination labels, receive buffers, response sizes, and decoded controller state for `GetCondition` responses.

## Tests

Run normal tests:

```powershell
dotnet test dcSharp.slnx
```

Run long KOS fixture checks:

```powershell
$env:DCSHARP_RUN_KOS_FIXTURES='1'
dotnet test dcSharp.slnx --filter DreamcastKosFixtureTests
```

The fixture checks assume the corresponding ELF files already exist under `artifacts/kos/`. The test suite and CLI runner both read `fixtures/kos.json`.

## Current Fixture Expectations

- `dcsharp_minimal.elf`: reaches `main()` and exits through the firmware-exit trap.
- `dcsharp_probe.elf`: reaches default KOS `main()`, prints probe text, shuts down, and reports `ProgramExit`.
- `dcsharp_timer.elf`: wakes from `thd_sleep()`, prints timer ticks, shuts down, and reports `ProgramExit`.
- `dcsharp_maple_controller.elf`: detects `dcSharp Virtual Controller`, reads neutral or scripted input state, shuts down, and reports `ProgramExit`.
- `dcsharp_maple_controller_script.elf`: performs two raw Maple condition reads, observes a neutral first read and scripted second read, shuts down, and reports `ProgramExit`.
- `dcsharp_maple_controller_b.elf`: probes raw B0 Maple device-info and condition responses, covers absent B0 and configured B0 state, shuts down, and reports `ProgramExit`.
- `dcsharp_framebuffer.elf`: writes a 320x240 RGB565 quadrant pattern into VRAM, exposes non-zero VRAM diagnostics, shuts down, and reports `ProgramExit`.
- `dcsharp_pvr_registers.elf`: writes named PVR framebuffer/TA registers plus TA command/YUV apertures, exposes PVR diagnostics, shuts down, and reports `ProgramExit`.
- `dcsharp_aica_registers.elf`: writes AICA channel/global registers and sound RAM, exposes silent audio diagnostics, shuts down, and reports `ProgramExit`.

## Commit Hygiene

Commit source, docs, KOS sample source, and tests. Do not commit generated artifacts, build outputs, downloaded BIOS/media, or generated traces.
