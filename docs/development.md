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

Build individual KOS samples:

```bash
wsl -e bash tools/kos/build-sample.sh samples/kos/minimal
wsl -e bash tools/kos/build-sample.sh samples/kos/hello
wsl -e bash tools/kos/build-sample.sh samples/kos/timer
wsl -e bash tools/kos/build-sample.sh samples/kos/maple_controller
wsl -e bash tools/kos/build-sample.sh samples/kos/framebuffer
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
```

Useful run options:

- `--instructions <count>` sets the execution budget.
- `--trace-tail <count>` controls how many final SH-4 steps are retained.
- `--vblank-interval <instructions>` controls the current synthetic VBlank cadence.
- `--vblank-interval 0` disables synthetic VBlank.
- `--controller-a start,a,joyx=-12,joyy=13,ltrig=40,rtrig=80` scripts a static controller state for Maple port A.
- `--controller-a-script "0:none;200000:start,a"` changes controller state at instruction-indexed boundaries.
- `--dump-framebuffer artifacts/video/framebuffer.png --framebuffer-size 320x240` writes the current RGB565 VRAM snapshot as a PNG.
- `--pixel-format rgb565` is accepted explicitly; RGB565 is currently the only framebuffer dump format.
- `--trace-log artifacts/logs/trace.txt --trace-pc 0x8C010000-0x8C010100 --trace-log-limit 4096` writes a bounded filtered SH-4 trace.
- `--device-log artifacts/logs/devices.txt --device-kind Write --device-address 0xFFE8000C` writes filtered device accesses.
- `--json` or `--summary-json` emits structured output for scripts and regression checks.

The fixture manifest keeps sample paths, artifact names, instruction budgets, and expected serial/video checks together. Use `--artifacts <path>` with the `fixtures` command when testing a different artifact directory.

KOS fixtures are usually unstripped. When `.symtab` or `.dynsym` is present, text and JSON summaries include nearest function names for stop PCs and trace-tail entries.

Generated framebuffer, trace, and device logs belong under `artifacts/` and stay out of git.

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
- `dcsharp_framebuffer.elf`: writes a 320x240 RGB565 quadrant pattern into VRAM, exposes non-zero VRAM diagnostics, shuts down, and reports `ProgramExit`.

## Commit Hygiene

Commit source, docs, KOS sample source, and tests. Do not commit generated artifacts, build outputs, downloaded BIOS/media, or generated traces.
