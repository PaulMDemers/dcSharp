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

## Milestone 4: Timers, Interrupts, And Maple Probe

- Add scheduler clock and interrupt controller basics. Started with periodic VBlank, ASIC event masks, SH-4 external interrupt entry, TMU countdown/reload, and IPRA priority lookup.
- Build KOS fixtures for timer callbacks and controller polling. Started: `samples/kos/timer` uses `timer_ms_gettime64()` and `thd_sleep()` and now completes; `samples/kos/maple_controller` detects a virtual controller and observes static scripted input.
- Centralize TMU advancement and the synthetic VBlank pulse in `DreamcastEventScheduler`. Done.
- Improve the scheduler to coalesce timer advancement and expose event diagnostics.
- Add frame/input scripts that vary Maple controller state over instruction time. Started with `--controller-a-script`.

## Milestone 5: Video And Audio Bring-Up

- Start with framebuffer/register visibility. Started: PVR VRAM apertures are backed and `samples/kos/framebuffer` writes a RGB565 pattern with run-summary samples.
- Add PVR command logging before a full renderer.
- Add silence-safe AICA register/channel tracking before audible output.

## Milestone 6: Media And Broader Compatibility

- Parse legal/local media formats.
- Add GD-ROM sector read behavior.
- Run public redistributable demos and user-provided local images through the artifact pipeline.
