# dcSharp

dcSharp is a Sega Dreamcast emulator project in C#.

The first target is not retail-game compatibility. The first target is a deterministic, observable homebrew runner that can load legal KallistiOS-built fixtures, execute bounded SH-4 instruction budgets, and produce useful summaries when it fails.

## Current Setup

- C# solution: `dcSharp.slnx`
- Core library: `src/DcSharp.Core`
- CLI: `src/DcSharp.Cli`
- Desktop host: `src/DcSharp.Desktop`
- Tests: `tests/DcSharp.Tests`
- KallistiOS samples:
  - `samples/kos/hello`: default KOS init fixture that now reaches `main()` after Maple startup.
  - `samples/kos/minimal`: minimal init fixture that reaches `main()` and exits via the firmware-exit trap.
  - `samples/kos/trap_exception`: bare-metal SH fixture that executes `trapa`, returns through `rte`, and exposes exception registers.
  - `samples/kos/illegal_instruction`: bare-metal SH fixture that exercises general and slot illegal instruction exceptions.
  - `samples/kos/slot_illegal_branch`: bare-metal SH fixture that exercises a PC-changing branch in a delay slot.
  - `samples/kos/timer`: default KOS fixture that exercises `timer_ms_gettime64()` and `thd_sleep()`.
  - `samples/kos/timer_callback`: default KOS fixture that chains a TMU0 primary timer callback.
  - `samples/kos/timer_vblank`: default KOS fixture that observes TMU callback delivery while VBlank IRQ9 is enabled.
  - `samples/kos/maple_controller`: default KOS fixture that polls a virtual neutral controller.
  - `samples/kos/maple_controller_script`: raw Maple condition fixture that observes an instruction-indexed controller transition.
  - `samples/kos/input_idle`: raw Maple fixture that observes a scripted input transition across idle sleeps.
  - `samples/kos/maple_controller_b`: raw Maple fixture that probes optional B0 controller presence and state.
  - `samples/kos/framebuffer`: default KOS fixture that writes a RGB565 quadrant pattern to VRAM.
  - `samples/kos/video_mode`: default KOS fixture that sets 640x480 RGB565 mode and writes sentinel pixels.
  - `samples/kos/pvr_registers`: default KOS fixture that writes named PVR registers and TA command apertures.
  - `samples/kos/pvr_polygon`: default KOS fixture that writes a minimal opaque polygon-style TA command sequence.
  - `samples/kos/pvr_polygon_green`: default KOS fixture that writes a second opaque polygon preview with a different shape and color.
  - `samples/kos/pvr_real_polygon`: default KOS fixture that writes a real-shaped 32-byte polygon header and `pvr_vertex_t`-style vertices.
  - `samples/kos/pvr_real_modes`: default KOS fixture that writes a real-shaped polygon header with nonzero mode payload bits.
  - `samples/kos/asic_irqb`: minimal KOS fixture that leaves a Maple DMA ASIC IRQB source pending.
  - `samples/kos/asic_events`: default KOS fixture that observes and clears an ASIC VBlank event latch.
  - `samples/kos/asic_irq9_masked`: minimal KOS fixture that leaves VBlank IRQ9 pending while SH-4 interrupts are masked.
  - `samples/kos/interrupt_nesting`: bare-metal SH fixture that confirms `SR.BL` blocks nested IRQ9 delivery while a VBlank source remains pending in the interrupt handler.
  - `samples/kos/timer_asic_arbitration`: bare-metal SH fixture that records TMU0 before IRQ9 when both are pending and TMU0 has higher priority.
  - `samples/kos/vblank_idle`: default KOS fixture that waits for a synthetic VBlank through a read-only idle polling loop.
  - `samples/kos/aica_registers`: default KOS fixture that writes AICA channel/global registers and sound RAM.
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
wsl -e bash tools/kos/build-fixtures.sh
```

Or build individual samples while iterating:

```bash
wsl -e bash tools/kos/build-sample.sh samples/kos/hello
wsl -e bash tools/kos/build-sample.sh samples/kos/minimal
dotnet run --project src/DcSharp.Cli -- inspect artifacts/kos/dcsharp_probe.elf
dotnet run --project src/DcSharp.Cli -- run artifacts/kos/dcsharp_minimal.elf --instructions 14000000 --trace-tail 40
dotnet run --project src/DcSharp.Cli -- run artifacts/kos/dcsharp_probe.elf --instructions 50000000 --trace-tail 40
wsl -e bash tools/kos/build-sample.sh samples/kos/timer
dotnet run --project src/DcSharp.Cli -- run artifacts/kos/dcsharp_timer.elf --instructions 50000000 --trace-tail 40
wsl -e bash tools/kos/build-sample.sh samples/kos/timer_callback
dotnet run --project src/DcSharp.Cli -- run artifacts/kos/dcsharp_timer_callback.elf --instructions 50000000 --trace-tail 40
wsl -e bash tools/kos/build-sample.sh samples/kos/maple_controller
dotnet run --project src/DcSharp.Cli -- run artifacts/kos/dcsharp_maple_controller.elf --instructions 60000000 --trace-tail 40
wsl -e bash tools/kos/build-sample.sh samples/kos/maple_controller_script
dotnet run --project src/DcSharp.Cli -- run artifacts/kos/dcsharp_maple_controller_script.elf --instructions 70000000 --controller-script "a0:0:none;15000000:start,a,joyx=-12,joyy=13,ltrig=40,rtrig=80"
wsl -e bash tools/kos/build-sample.sh samples/kos/input_idle
dotnet run --project src/DcSharp.Cli -- run artifacts/kos/dcsharp_input_idle.elf --instructions 70000000 --controller-script "a0:0:none;15000000:start,a,joyx=-12,joyy=13,ltrig=40,rtrig=80"
wsl -e bash tools/kos/build-sample.sh samples/kos/maple_controller_b
dotnet run --project src/DcSharp.Cli -- run artifacts/kos/dcsharp_maple_controller_b.elf --instructions 70000000 --controller "b0:b,ltrig=7,rtrig=9,joyx=12,joyy=-13"
wsl -e bash tools/kos/build-sample.sh samples/kos/framebuffer
dotnet run --project src/DcSharp.Cli -- run artifacts/kos/dcsharp_framebuffer.elf --instructions 70000000 --trace-tail 40
wsl -e bash tools/kos/build-sample.sh samples/kos/video_mode
dotnet run --project src/DcSharp.Cli -- run artifacts/kos/dcsharp_video_mode.elf --instructions 70000000 --trace-tail 40
wsl -e bash tools/kos/build-sample.sh samples/kos/pvr_registers
dotnet run --project src/DcSharp.Cli -- run artifacts/kos/dcsharp_pvr_registers.elf --instructions 70000000 --trace-tail 40
wsl -e bash tools/kos/build-sample.sh samples/kos/pvr_polygon
dotnet run --project src/DcSharp.Cli -- run artifacts/kos/dcsharp_pvr_polygon.elf --instructions 70000000 --trace-tail 40
wsl -e bash tools/kos/build-sample.sh samples/kos/pvr_polygon_green
dotnet run --project src/DcSharp.Cli -- run artifacts/kos/dcsharp_pvr_polygon_green.elf --instructions 70000000 --trace-tail 40
wsl -e bash tools/kos/build-sample.sh samples/kos/asic_irqb
dotnet run --project src/DcSharp.Cli -- run artifacts/kos/dcsharp_asic_irqb.elf --instructions 30000000 --trace-tail 40
wsl -e bash tools/kos/build-sample.sh samples/kos/asic_events
dotnet run --project src/DcSharp.Cli -- run artifacts/kos/dcsharp_asic_events.elf --instructions 70000000 --trace-tail 40
wsl -e bash tools/kos/build-sample.sh samples/kos/vblank_idle
dotnet run --project src/DcSharp.Cli -- run artifacts/kos/dcsharp_vblank_idle.elf --instructions 70000000 --vblank-interval 50000 --trace-tail 40
wsl -e bash tools/kos/build-sample.sh samples/kos/interrupt_nesting
dotnet run --project src/DcSharp.Cli -- run artifacts/kos/dcsharp_interrupt_nesting.elf --instructions 1000000 --vblank-interval 5000 --trace-tail 24
wsl -e bash tools/kos/build-sample.sh samples/kos/timer_asic_arbitration
dotnet run --project src/DcSharp.Cli -- run artifacts/kos/dcsharp_timer_asic_arbitration.elf --instructions 1000000 --vblank-interval 5000 --trace-tail 32
wsl -e bash tools/kos/build-sample.sh samples/kos/aica_registers
dotnet run --project src/DcSharp.Cli -- run artifacts/kos/dcsharp_aica_registers.elf --instructions 70000000 --trace-tail 40
```

Run every manifest-listed KOS fixture and validate the expected stop reason, serial output, and device/video/audio expectations. The manifest declares `fixtures/kos.schema.json` for editor validation:

```bash
dotnet run --project src/DcSharp.Cli -- fixtures fixtures/kos.json --validate-only
dotnet run --project src/DcSharp.Cli -- fixtures fixtures/kos.json
dotnet run --project src/DcSharp.Cli -- fixtures fixtures/kos.json --filter input_idle
dotnet run --project src/DcSharp.Cli -- fixtures fixtures/kos.json --report-json artifacts/reports/kos-fixtures.json
```

The `run` command also accepts `--vblank-interval <instructions>`. Use `--vblank-interval 0` to disable the current synthetic VBlank source while debugging timing-sensitive behavior.

Dump the current RGB565 framebuffer snapshot to PNG:

```bash
dotnet run --project src/DcSharp.Cli -- run artifacts/kos/dcsharp_framebuffer.elf --instructions 70000000 --dump-framebuffer artifacts/video/framebuffer.png --framebuffer-size 320x240
```

Capture narrow trace/device logs while keeping the normal run summary readable:

```bash
dotnet run --project src/DcSharp.Cli -- run artifacts/kos/dcsharp_minimal.elf --instructions 14000000 --trace-log artifacts/logs/minimal-trace.txt --trace-pc 0x8C01B218-0x8C01B220 --device-log artifacts/logs/minimal-scif-writes.txt --device-domain scif --device-kind Write
```

Use `--controller` to map virtual controllers to Maple addresses. The older `--controller-a` and `--controller-b` shorthands still work:

```bash
dotnet run --project src/DcSharp.Cli -- run artifacts/kos/dcsharp_maple_controller_b.elf --instructions 70000000 --controller b0:b,ltrig=7,rtrig=9,joyx=12,joyy=-13
```

Use `--controller-script` for instruction-indexed controller changes. The older `--controller-a-script` shorthand still works for A0:

```bash
dotnet run --project src/DcSharp.Cli -- run artifacts/kos/dcsharp_maple_controller_script.elf --instructions 70000000 --controller-script "a0:0:none;15000000:start,a,joyx=-12,joyy=13,ltrig=40,rtrig=80"
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

Try the desktop host:

```powershell
dotnet run --project src/DcSharp.Desktop
```

The desktop app lets you pick an ELF and optional media file (`.bin`/`.cue`), set an instruction limit, and run the selected image through the same core runner used by the CLI.

For the usual fast local check, including whitespace diff checks, fixture-manifest validation, and the unit suite:

```powershell
.\tools\check.ps1
```

Run the fast check plus one matching KOS fixture while iterating:

```powershell
.\tools\check.ps1 -KosFixtures -FixtureFilter input_idle
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
- Fixture manifests can assert ASIC pending interrupt state and event ACK/mask registers.
- When ELF symbols are present, run summaries annotate stop PCs and trace-tail entries with nearest function names.
- The CLI can write bounded, PC-filtered trace logs and filtered device-access logs for focused debugging.
- Structured run summaries include aggregate device-access counts by domain and access kind, plus recent access details.
- ASIC event ACK registers, IRQ masks, pending masks, and deliverable ASIC interrupt event/level/source bit are captured in run summaries.
- Current PVR register values, PVR register writes, TA command writes, grouped TA lists, and assembled opaque TA strips are captured in the video summary with SDK-aligned names and first-pass TA command classification.
- A tiny fixture-backed PVR preview raster path turns assembled opaque TA polygon strips into RGB565 VRAM samples, including flat color, Gouraud vertex color interpolation, depth checks, alpha blending, and selected RGB565/ARGB texture sampling behavior; this is not a general renderer yet.
- Current AICA register values, register writes, sound RAM changes, decoded channel state, compressed/streamed sample metadata, pan/send balance diagnostics, silence-safe PCM playback sample/byte counters, and optional PCM16/PCM8 WAV dumps are captured without producing host audio by default.
- Maple DMA transfers are captured with command/response names, receive buffers, destination labels, response sizes, and controller state for condition reads.
- Scheduler summaries report synthetic VBlank count, next VBlank boundary, hardware ticks, coalesced hardware advancement batches, max batch size, idle advance batches, idle wake reasons, CPU fast-forward batches, and controller script changes.
- SH-4 `sleep`, side-effect-free self-branch waits, narrow read-only polling loops, controller-script wake boundaries, and masked counted idle loops use scheduler batching where the current fixtures expose safe patterns.
- Device logs can be filtered by named domains such as `pvr`, `aica`, `maple`, `scif`, `tmu`, and `unmapped`.
- SCIF serial writes are captured and printed by the CLI.
- The default KOS fixture gets through GD-ROM init, video setup, Maple scan, the probe's `main()` output, and KOS shutdown. The runner reports this terminal path as `ProgramExit` when KOS has emitted its exit banner and execution returns outside loaded executable code.
- The Maple controller fixtures see `dcSharp Virtual Controller`, read neutral or scripted controller state, validate instruction-indexed transitions across raw Maple condition reads and idle sleeps, and exercise absent/configured B0 behavior.
- The idle fixtures pin all current scheduler wake reasons: timer, VBlank, and input-script changes.
- The framebuffer fixture writes a 320x240 RGB565 pattern and exposes it through VRAM diagnostics.
- The CLI can emit structured JSON summaries for fixture regression checks and tooling.
- Legal/local media loading supports raw 2048-byte sector data, 2352-byte CD-sector payload extraction, simple CUE data-track selection, and GDI data tracks mapped by absolute LBA.
- GD-ROM firmware HLE read commands are captured in run summaries with media presence, sector size/count, requested LBA/count, destination, bytes read, success, and failure status.
- The WinForms desktop workbench can run selected ELF/media pairs, show serial/trace/device diagnostics, preview RGB565 VRAM, and derive framebuffer dimensions from `PVR_FB_SIZE` when a program configures it.

Next targets:

- Add focused KOS fixtures for more timer/interrupt edge cases.
- Build richer frame/input script formats around the instruction-indexed controller script model.
- Expand real PVR vertex parameter payload coverage beyond the current packed-color fixture shape.
- Extend silence-safe AICA playback timing into ADPCM decode/position stepping and richer audio dump diagnostics.

## Development Bias

Build the emulator as a diagnostics platform first:

- deterministic core before frontend polish
- bounded runs before long compatibility sweeps
- structured summaries before huge traces
- source-built homebrew fixtures before retail software
- reference emulators and manuals as evidence, not code to copy
