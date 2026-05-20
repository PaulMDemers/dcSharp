# dcSharp

dcSharp is a Sega Dreamcast emulator project in C#.

The first target is not retail-game compatibility. The first target is a deterministic, observable homebrew runner that can load legal KallistiOS-built fixtures, execute bounded SH-4 instruction budgets, and produce useful summaries when it fails.

## Current Setup

- C# solution: `dcSharp.slnx`
- Core library: `src/DcSharp.Core`
- CLI: `src/DcSharp.Cli`
- Tests: `tests/DcSharp.Tests`
- KallistiOS samples:
  - `samples/kos/hello`: default KOS init fixture that now reaches `main()` after Maple startup.
  - `samples/kos/minimal`: minimal init fixture that reaches `main()` and exits via the firmware-exit trap.
  - `samples/kos/timer`: default KOS fixture that exercises `timer_ms_gettime64()` and `thd_sleep()`.
  - `samples/kos/maple_controller`: default KOS fixture that polls a virtual neutral controller.
  - `samples/kos/framebuffer`: default KOS fixture that writes a RGB565 quadrant pattern to VRAM.
  - `samples/kos/pvr_registers`: default KOS fixture that writes named PVR registers and TA command apertures.
- Generated artifacts: `artifacts/` (ignored)

KallistiOS is installed in WSL using the prebuilt GCC 15.1.0/KOS 2.2.1 toolchain layout:

```bash
~/kos
~/kos-ports
~/sh-elf
```

Verify it:

```bash
wsl -e bash tools/kos/verify-kos.sh
```

Build the legal Dreamcast probe fixtures:

```bash
wsl -e bash tools/kos/build-sample.sh samples/kos/hello
wsl -e bash tools/kos/build-sample.sh samples/kos/minimal
dotnet run --project src/DcSharp.Cli -- inspect artifacts/kos/dcsharp_probe.elf
dotnet run --project src/DcSharp.Cli -- run artifacts/kos/dcsharp_minimal.elf --instructions 14000000 --trace-tail 40
dotnet run --project src/DcSharp.Cli -- run artifacts/kos/dcsharp_probe.elf --instructions 50000000 --trace-tail 40
wsl -e bash tools/kos/build-sample.sh samples/kos/timer
dotnet run --project src/DcSharp.Cli -- run artifacts/kos/dcsharp_timer.elf --instructions 50000000 --trace-tail 40
wsl -e bash tools/kos/build-sample.sh samples/kos/maple_controller
dotnet run --project src/DcSharp.Cli -- run artifacts/kos/dcsharp_maple_controller.elf --instructions 60000000 --trace-tail 40
wsl -e bash tools/kos/build-sample.sh samples/kos/framebuffer
dotnet run --project src/DcSharp.Cli -- run artifacts/kos/dcsharp_framebuffer.elf --instructions 70000000 --trace-tail 40
wsl -e bash tools/kos/build-sample.sh samples/kos/pvr_registers
dotnet run --project src/DcSharp.Cli -- run artifacts/kos/dcsharp_pvr_registers.elf --instructions 70000000 --trace-tail 40
```

Run every manifest-listed KOS fixture and validate the expected stop reason, serial output, and video samples:

```bash
dotnet run --project src/DcSharp.Cli -- fixtures fixtures/kos.json
```

The `run` command also accepts `--vblank-interval <instructions>`. Use `--vblank-interval 0` to disable the current synthetic VBlank source while debugging timing-sensitive behavior.

Dump the current RGB565 framebuffer snapshot to PNG:

```bash
dotnet run --project src/DcSharp.Cli -- run artifacts/kos/dcsharp_framebuffer.elf --instructions 70000000 --dump-framebuffer artifacts/video/framebuffer.png --framebuffer-size 320x240
```

Capture narrow trace/device logs while keeping the normal run summary readable:

```bash
dotnet run --project src/DcSharp.Cli -- run artifacts/kos/dcsharp_minimal.elf --instructions 14000000 --trace-log artifacts/logs/minimal-trace.txt --trace-pc 0x8C01B218-0x8C01B220 --device-log artifacts/logs/minimal-scif-writes.txt --device-kind Write --device-address 0xFFE8000C
```

Use `--controller-a` to script the virtual controller on Maple port A:

```bash
dotnet run --project src/DcSharp.Cli -- run artifacts/kos/dcsharp_maple_controller.elf --instructions 60000000 --controller-a start,a,joyx=-12,joyy=13,ltrig=40,rtrig=80
```

Use `--controller-a-script` for instruction-indexed controller changes:

```bash
dotnet run --project src/DcSharp.Cli -- run artifacts/kos/dcsharp_maple_controller.elf --instructions 60000000 --controller-a-script "0:none;1:start,a,joyx=-12,joyy=13,ltrig=40,rtrig=80"
```

Use `--json` or `--summary-json` to emit a machine-readable run summary:

```bash
dotnet run --project src/DcSharp.Cli -- run artifacts/kos/dcsharp_timer.elf --instructions 50000000 --trace-tail 40 --json
```

The default test suite keeps long KOS fixture runs opt-in. To exercise the built artifacts end to end:

```powershell
$env:DCSHARP_RUN_KOS_FIXTURES='1'
dotnet test dcSharp.slnx
```

Current state:

- ELF loading maps KOS-built SH-4 binaries into Dreamcast RAM.
- The SH-4 interpreter covers enough integer, control, exception-return, register-bank, and FPU behavior to run a minimal KOS program through `main()`.
- Low-RAM firmware vectors include return-zero sysinfo/flash stubs, a GD-ROM HLE dispatcher, and a non-returning system BIOS exit trap.
- The external ASIC/Maple slice includes stateful event registers, periodic VBlank events, Maple DMA completion, one virtual neutral controller on port A, and IRQ entry through KOS's normal interrupt vector.
- The SH-4 TMU slice includes TSTR, TCOR/TCNT countdown, TCR underflow/interrupt bits, and IPRA priority lookup; the timer fixture now wakes from `thd_sleep()`.
- PVR VRAM is backed for the 32-bit and 64-bit apertures, and run summaries include a checksum, non-zero byte count, first changed offset, and RGB565 samples.
- The CLI can dump the current RGB565 VRAM snapshot to a PNG file for quick visual fixture checks.
- KOS fixture expectations live in `fixtures/kos.json`, and the CLI can run the manifest as a compact regression suite.
- When ELF symbols are present, run summaries annotate stop PCs and trace-tail entries with nearest function names.
- The CLI can write bounded, PC-filtered trace logs and filtered device-access logs for focused debugging.
- PVR register and TA command writes are captured in the video summary with SDK-aligned register names.
- SCIF serial writes are captured and printed by the CLI.
- The default KOS fixture gets through GD-ROM init, video setup, Maple scan, the probe's `main()` output, and KOS shutdown. The runner reports this terminal path as `ProgramExit` when KOS has emitted its exit banner and execution returns outside loaded executable code.
- The Maple controller fixture sees `dcSharp Virtual Controller` and reads neutral or scripted controller state.
- The framebuffer fixture writes a 320x240 RGB565 pattern and exposes it through VRAM diagnostics.
- The CLI can emit structured JSON summaries for fixture regression checks and tooling.

Next targets:

- Improve the central event scheduler so it can coalesce timer advancement and report scheduled event diagnostics.
- Add focused KOS fixtures for timer callbacks.
- Build richer frame/input script formats around the instruction-indexed controller script model.
- Promote device diagnostics into structured summaries so regressions can be compared without scanning huge traces.

## Development Bias

Build the emulator as a diagnostics platform first:

- deterministic core before frontend polish
- bounded runs before long compatibility sweeps
- structured summaries before huge traces
- source-built homebrew fixtures before retail software
- reference emulators and manuals as evidence, not code to copy
