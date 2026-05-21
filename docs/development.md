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
wsl -e bash tools/kos/build-sample.sh samples/kos/timer_callback
wsl -e bash tools/kos/build-sample.sh samples/kos/maple_controller
wsl -e bash tools/kos/build-sample.sh samples/kos/maple_controller_script
wsl -e bash tools/kos/build-sample.sh samples/kos/maple_controller_b
wsl -e bash tools/kos/build-sample.sh samples/kos/framebuffer
wsl -e bash tools/kos/build-sample.sh samples/kos/video_mode
wsl -e bash tools/kos/build-sample.sh samples/kos/pvr_registers
wsl -e bash tools/kos/build-sample.sh samples/kos/asic_irqb
wsl -e bash tools/kos/build-sample.sh samples/kos/asic_events
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
dotnet run --project src/DcSharp.Cli -- fixtures fixtures/kos.json --validate-only
dotnet run --project src/DcSharp.Cli -- fixtures fixtures/kos.json
dotnet run --project src/DcSharp.Cli -- fixtures fixtures/kos.json --filter input_idle
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
- `fixtures --validate-only` parses and validates a fixture manifest without requiring built ELF artifacts.
- `fixtures --filter <name>` runs or validates only fixtures whose names contain the filter text.
- `fixtures --report-json artifacts/reports/kos-fixtures.json` writes a structured fixture report while keeping the text summary on stdout.

The fixture manifest keeps sample paths, artifact names, instruction budgets, static `controllers`, instruction-indexed `controllerScripts`, and expected serial/video/audio checks together. Fixture text and JSON reports include Maple transfer counts plus scheduler VBlank, hardware tick, hardware batch, max batch, idle advance, idle wake, CPU fast-forward, and controller-script change diagnostics for timing comparisons. Manifests can also set optional Maple thresholds such as `minMapleTransfers`, `minMapleDeviceInfoTransfers`, `minMapleGetConditionTransfers`, `minMapleDmaBatches`, and `requireNoMapleDescriptorLimitHits`, scheduler thresholds such as `minVblankEvents`, `minHardwareAdvanceTicks`, `minHardwareAdvanceBatches`, `maxHardwareAdvanceBatch`, `minIdleAdvanceTicks`, `minIdleAdvanceBatches`, `maxIdleAdvanceBatch`, `minIdleTimerWakes`, `minIdleVBlankWakes`, `minIdleInputWakes`, `minCpuFastForwardInstructions`, `minCpuFastForwardBatches`, `maxCpuFastForwardBatch`, and `minControllerScriptChanges`, device-domain thresholds with `minDeviceAccessDomains`, ASIC expectations with `requireNoAsicPendingInterrupt`, `asicPendingInterrupt`, and `asicEventRegisters`, current PVR register values with `pvrRegisters`, current AICA register values with `aicaRegisters`, and decoded AICA channel state with `aicaChannels`. Use `--artifacts <path>` with the `fixtures` command when testing a different artifact directory.

`fixtures/kos.json` declares the local `fixtures/kos.schema.json` schema for editor validation and autocomplete. Keep the schema in sync whenever a new manifest field becomes part of the supported fixture contract.

KOS fixtures are usually unstripped. When `.symtab` or `.dynsym` is present, text and JSON summaries include nearest function names for stop PCs and trace-tail entries.

Generated framebuffer, trace, and device logs belong under `artifacts/` and stay out of git.

Run summaries also include scheduler diagnostics for synthetic VBlank events, hardware advancement ticks, coalesced hardware batches, max hardware batch size, idle-advance ticks/batches/wake reasons, CPU fast-forwarded instructions/batches, and controller-script state changes. The runner currently uses this batching after SH-4 `sleep` instructions, side-effect-free self-branch waits, and narrow taken backward `bt`/`bf` waits with read-only polling bodies to advance hardware directly to the next timer, enabled VBlank, or controller-script boundary. It also fast-forwards a narrow masked `dt`/`bf/s` counted-delay loop shape, including simple `nop` and `add #imm,rn` delay slots, while trace capture is disabled.

Structured run summaries include aggregate device-access counts by domain and access kind, plus recent device accesses. Device domains currently include `pvr`, `aica`, `maple`, `asic`, `holly`, `scif`, `tmu`, `sh4`, `unmapped`, and `other`.

ASIC summaries include current event ACK registers, IRQ9/IRQB/IRQD masks, per-level pending masks, and the currently deliverable ASIC interrupt event/level/source bit. Unit tests cover A/B/C event-bank source decoding and independent ACK clearing.

PVR summaries include current named register values plus recent register accesses. PVR TA writes are classified into diagnostic command kinds such as `PolygonHeader`, `Vertex`, `VertexEndOfStrip`, `SpriteHeader`, `ModifierVolume`, `UserClip`, and `YuvConverterData`, then grouped into TA list summaries by region/list type with header, vertex, and end-of-strip counts. Fixture `pvrTaCommands` entries can assert minimum counts by kind alone or add filters for `region`, `listTypeName`, `endOfStrip`, and exact command `value`.

AICA summaries include current named register values plus recent register accesses. Channel summaries decode sample format, loop enable, sample address, loop points, pitch, pan, volume, key-on state, and active channel count while remaining silence-safe.

Maple summaries capture DMA command/response names, destination labels, receive buffers, response sizes, decoded controller state for `GetCondition` responses, and per-DMA descriptor traversal diagnostics including malformed chains that hit the descriptor guard.

## Tests

Run normal tests:

```powershell
dotnet test dcSharp.slnx
```

Run the fast local check, including whitespace diff checks, fixture-manifest validation, and the unit suite:

```powershell
.\tools\check.ps1
```

GitHub CI runs the same fast path on `windows-latest`: restore, build, fixture-manifest validation, and the unit suite. It does not build KallistiOS samples or require generated ELF artifacts.

Run long KOS fixture checks:

```powershell
$env:DCSHARP_RUN_KOS_FIXTURES='1'
dotnet test dcSharp.slnx --filter DreamcastKosFixtureTests
```

Or run the same fast local check plus the full CLI fixture manifest:

```powershell
.\tools\check.ps1 -KosFixtures
```

Use `-FixtureFilter <name>` with `-KosFixtures` to run only matching CLI fixtures after the fast checks:

```powershell
.\tools\check.ps1 -KosFixtures -FixtureFilter input_idle
```

The fixture checks assume the corresponding ELF files already exist under `artifacts/kos/`. The test suite and CLI runner both read `fixtures/kos.json`.

## Current Fixture Expectations

- `dcsharp_minimal.elf`: reaches `main()` and exits through the firmware-exit trap.
- `dcsharp_probe.elf`: reaches default KOS `main()`, prints probe text, shuts down, and reports `ProgramExit`.
- `dcsharp_timer.elf`: wakes from `thd_sleep()`, prints timer ticks, shuts down, and reports `ProgramExit`.
- `dcsharp_timer_callback.elf`: chains the KOS TMU0 primary timer callback, observes three wakeups, shuts down, and reports `ProgramExit`.
- `dcsharp_maple_controller.elf`: detects `dcSharp Virtual Controller`, reads neutral or scripted input state, shuts down, and reports `ProgramExit`.
- `dcsharp_maple_controller_script.elf`: performs two raw Maple condition reads, observes a neutral first read and scripted second read, shuts down, and reports `ProgramExit`.
- `dcsharp_input_idle.elf`: performs two raw Maple condition reads around repeated SH-4 `sleep` idle points, observes a scripted controller transition, exposes idle input wake diagnostics, shuts down, and reports `ProgramExit`.
- `dcsharp_maple_controller_b.elf`: probes raw B0 Maple device-info and condition responses, covers absent B0 and configured B0 state, shuts down, and reports `ProgramExit`.
- `dcsharp_framebuffer.elf`: writes a 320x240 RGB565 quadrant pattern into VRAM, exposes non-zero VRAM diagnostics, shuts down, and reports `ProgramExit`.
- `dcsharp_video_mode.elf`: sets 640x480 RGB565 video mode, writes sentinel VRAM pixels, exposes PVR/video diagnostics, shuts down, and reports `ProgramExit`.
- `dcsharp_pvr_registers.elf`: writes named PVR framebuffer/TA registers plus TA command/YUV apertures, exposes PVR diagnostics, shuts down, and reports `ProgramExit`.
- `dcsharp_pvr_polygon.elf`: writes a minimal opaque polygon-style TA command sequence, exposes TA list/register diagnostics, shuts down, and reports `ProgramExit`.
- `dcsharp_asic_irqb.elf`: triggers a raw Maple DMA completion with ASIC IRQB enabled, leaves the decoded pending source observable, exits through the firmware-exit trap, and reports `FirmwareExit`.
- `dcsharp_asic_events.elf`: masks SH-4 interrupts, enables ASIC VBlank IRQ9, observes the raw ACK bit, clears it, disables the mask, shuts down, and reports `ProgramExit`.
- `dcsharp_vblank_idle.elf`: masks SH-4 interrupts, enables ASIC VBlank IRQ9, spins in a read-only ACK polling loop until synthetic VBlank, exposes idle VBlank wake diagnostics, clears the ACK bit, shuts down, and reports `ProgramExit`.
- `dcsharp_aica_registers.elf`: writes AICA channel/global registers and sound RAM, exposes silent audio diagnostics, shuts down, and reports `ProgramExit`.

## Commit Hygiene

Commit source, docs, KOS sample source, and tests. Do not commit generated artifacts, build outputs, downloaded BIOS/media, or generated traces.
