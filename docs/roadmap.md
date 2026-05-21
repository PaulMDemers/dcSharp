# dcSharp Roadmap

## Milestone 0: Tools And Legal Fixtures

- KallistiOS installed and verified in WSL.
- Repo can build a tiny KOS ELF fixture.
- CLI can inspect ELF identity and Dreamcast plausibility.
- Docs define legal asset policy and reference sources.

## Milestone 1: Loader And Memory

- Parse ELF32 SH executable headers and program headers.
- Load segments into Dreamcast system RAM through P1/P2 address mirrors.
- Provide deterministic memory reads/writes with trace hooks.
- Add open-bus/unmapped-access diagnostics without crashing the host.
- CLI can produce a loader summary for a KOS ELF.

## Milestone 2: SH-4 Interpreter Slice

- Implement core integer/register model. Done for the current KOS fixtures.
- Decode 16-bit opcodes with trace formatting. Done for the current KOS fixtures.
- Support branches, delay slots, loads/stores, arithmetic, comparisons, and basic system registers. Done for the current KOS fixtures.
- Stop cleanly on unsupported opcode or exception. Done.
- Keep adding opcodes only when a fixture or focused unit test exposes the missing behavior.

## Milestone 3: KOS Homebrew Reaches Main

- Model enough boot/runtime expectations for `samples/kos/hello`. Done.
- Firmware HLE includes sysinfo/flash return-zero stubs, GD-ROM request handling, and a system BIOS exit trap.
- Capture `printf` or serial-like output through an explicit diagnostic channel. Done: SCIF transmit writes are captured as serial bytes.
- Emit a run summary with final PC, executed instructions, memory faults, serial output, trace tail, and device accesses. Done for text and JSON CLI output.
- Current KOS probe frontier: default KOS init reaches `main()`, prints both probe messages, runs shutdown, and stops as `ProgramExit`.
- Symbolize stop PCs and trace-tail entries from ELF symbol tables when available. Done.
- Add bounded trace and device-access log filters for focused debugging. Done.

## Milestone 4: Timers, Interrupts, And Maple Probe

- Add scheduler clock and interrupt controller basics. Started with periodic VBlank, ASIC event masks, SH-4 external interrupt entry, TMU countdown/reload, and IPRA priority lookup.
- Build KOS fixtures for timer callbacks and controller polling. Started: `samples/kos/timer` uses `timer_ms_gettime64()` and `thd_sleep()`; `samples/kos/timer_callback` chains the TMU0 primary timer callback; `samples/kos/asic_events` observes and clears an ASIC VBlank event latch; `samples/kos/maple_controller` detects a virtual controller; `samples/kos/maple_controller_script` observes an instruction-indexed input transition; `samples/kos/maple_controller_b` covers absent/configured B0 behavior.
- Keep KOS fixture expectations in a manifest and run them through a shared CLI/test validator. Done.
- Centralize TMU advancement and the synthetic VBlank pulse in `DreamcastEventScheduler`. Done.
- Expose scheduler event diagnostics in run summaries. Done.
- Expose ASIC event register and pending interrupt diagnostics in run summaries. Done, including decoded pending source register/bit.
- Add fixture expectations for ASIC event state. Started: manifests can assert no pending ASIC interrupt plus ACK/mask/pending event registers.
- Expose Maple DMA command/response diagnostics in run summaries. Done.
- Expose aggregate device-access domain and access-kind diagnostics in run summaries. Done.
- Add fixture expectations for aggregate device-domain access counts. Done.
- Improve the scheduler to coalesce timer advancement. Done for skipped instruction-count calls, SH-4 `sleep` waits, and side-effect-free self-branch waits; broader idle-loop detection can now build on the same hardware batch path.
- Add frame/input scripts that vary Maple controller state over instruction time. Started with generic `--controller address:state` and `--controller-script address:script` mapping, compatibility A0/B0 shorthands, optional B0 controller configuration, and a raw Maple condition transition fixture.

## Milestone 5: Video And Audio Bring-Up

- Start with framebuffer/register visibility. Started: PVR VRAM apertures are backed; `samples/kos/framebuffer` writes a RGB565 pattern and `samples/kos/video_mode` sets 640x480 RGB565 with sentinel pixels.
- Dump the current RGB565 framebuffer snapshot to PNG for visual fixture checks. Done.
- Add PVR command logging before a full renderer. Started: current named PVR register values, named register accesses, and TA command writes are captured in video summaries.
- Classify TA command writes before a full renderer. Started with first-word command kind and list type decoding.
- Add fixture expectations for PVR state. Started: manifests can assert current named PVR register values and RGB565 sentinel samples.
- Add fixture expectations for AICA state. Started: manifests can assert current named register values plus decoded channel control, sample, loop, pitch, pan, volume, and key-on fields.
- Add silence-safe AICA register/channel tracking before audible output. Started: AICA MMIO, sound RAM writes, current named register values, decoded sample format, loop state, key-on state, and touched channel snapshots are captured in audio summaries.

## Milestone 6: Media And Broader Compatibility

- Parse legal/local media formats.
- Add GD-ROM sector read behavior.
- Run public redistributable demos and user-provided local images through the artifact pipeline.
