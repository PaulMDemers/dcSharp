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
- Support branches, delay slots, loads/stores, arithmetic, comparisons, basic system registers, `trapa` entry, modeled `0xFFFD` illegal-instruction exceptions, slot-illegal PC/SR-changing delay-slot exceptions, and `rte` return. Done for the current KOS fixtures, with bare-metal trap and illegal-instruction fixtures pinning exception diagnostics.
- Stop cleanly on unsupported opcode or exception. Done.
- Keep adding opcodes only when a fixture or focused unit test exposes the missing behavior.

## Milestone 3: KOS Homebrew Reaches Main

- Model enough boot/runtime expectations for `samples/kos/hello`. Done.
- Firmware HLE includes sysinfo/flash return-zero stubs, GD-ROM request handling, and named terminal system BIOS traps for reset/menu requests.
- Capture `printf` or serial-like output through an explicit diagnostic channel. Done: SCIF transmit writes are captured as serial bytes.
- Emit a run summary with final PC, executed instructions, memory faults, serial output, trace tail, and device accesses. Done for text and JSON CLI output.
- Current KOS probe frontier: default KOS init reaches `main()`, prints both probe messages, runs shutdown, and stops as `ProgramExit`.
- Symbolize stop PCs and trace-tail entries from ELF symbol tables when available. Done.
- Add bounded trace and device-access log filters for focused debugging. Done.

## Milestone 4: Timers, Interrupts, And Maple Probe

- Add scheduler clock and interrupt controller basics. Started with periodic VBlank, ASIC event masks, SH-4 external interrupt entry with `SR.BL`/`SR.IMASK`/delay-slot coverage, TMU countdown/reload, IPRA priority lookup, and TMU/ASIC pending-source arbitration.
- Build KOS fixtures for timer callbacks and controller polling. Started: `samples/kos/timer` uses `timer_ms_gettime64()` and `thd_sleep()`; `samples/kos/timer_callback` chains the TMU0 primary timer callback; `samples/kos/timer_vblank` observes TMU callback delivery while VBlank IRQ9 is enabled; `samples/kos/asic_irqb` leaves a Maple DMA IRQB source pending; `samples/kos/maple_irqb_accept` accepts and clears Maple DMA IRQB through the SH-4 external interrupt vector; `samples/kos/asic_events` observes and clears an ASIC VBlank event latch; `samples/kos/asic_irq9_masked` leaves VBlank IRQ9 pending while SH-4 interrupts are masked; `samples/kos/interrupt_nesting` confirms `SR.BL` blocks nested IRQ9 delivery while a VBlank source remains pending in the handler; `samples/kos/timer_asic_arbitration` records TMU0 before IRQ9 when both sources are pending and TMU0 has higher priority; `samples/kos/timer_irq_masked` leaves TMU0 pending while SH-4 interrupts are masked; `samples/kos/timer_irq_accept` accepts and clears a pending TMU0 interrupt through the external interrupt vector; `samples/kos/vblank_idle` observes an idle wake on synthetic VBlank; `samples/kos/maple_controller` detects a virtual controller; `samples/kos/maple_controller_script` observes an instruction-indexed input transition; `samples/kos/input_idle` observes an idle wake on an input-script change; `samples/kos/maple_controller_b` covers absent/configured B0 behavior.
- Keep KOS fixture expectations in a manifest and run them through a shared CLI/test validator. Done, including fixture filtering for targeted CLI and `tools/check.ps1` runs.
- Centralize TMU advancement and the synthetic VBlank pulse in `DreamcastEventScheduler`. Done.
- Expose scheduler event diagnostics in run summaries. Done.
- Expose ASIC event register and pending interrupt diagnostics in run summaries. Done, including decoded pending source register/bit and A/B/C event-bank ACK coverage.
- Add fixture expectations for ASIC event state. Started: manifests can assert no pending ASIC interrupt plus ACK/mask/pending event registers.
- Expose Maple DMA command/response diagnostics in run summaries. Done, including per-DMA descriptor traversal and descriptor-limit diagnostics.
- Expose aggregate device-access domain and access-kind diagnostics in run summaries. Done.
- Add fixture expectations for aggregate device-domain access counts. Done.
- Improve the scheduler to coalesce timer advancement. Done for skipped instruction-count calls, SH-4 `sleep` waits, side-effect-free self-branch waits, narrow read-only interruptible `bt`/`bf` polling waits, controller-script wake boundaries, and masked `dt`/`bf/s` counted-delay loops. Live fixtures now pin timer, VBlank, and input idle-wake reasons.
- Add frame/input scripts that vary Maple controller state over instruction time. Started with generic `--controller address:state` and `--controller-script address:script` mapping, compatibility A0/B0 shorthands, optional B0 controller configuration, raw Maple condition transition fixtures, and an input-idle wake fixture.

## Milestone 5: Video And Audio Bring-Up

- Start with framebuffer/register visibility. Started: PVR VRAM apertures are backed; `samples/kos/framebuffer` writes a RGB565 pattern and `samples/kos/video_mode` sets 640x480 RGB565 with sentinel pixels.
- Add the first tiny software preview path. Started: assembled opaque, punch-through, and translucent TA strips can draw small RGB565 triangle previews into VRAM for flat-color, Gouraud-color, and multi-triangle strip fixtures, with selected depth, blend, alpha discard, UV, filter, and texture format behavior.
- Dump the current RGB565 framebuffer snapshot to PNG for visual fixture checks. Done.
- Add PVR command logging before a full renderer. Started: current named PVR register values, named register accesses, TA command writes, grouped TA lists, and assembled opaque TA strips are captured in video summaries.
- Classify TA command writes before a full renderer. Started with first-word command kind, list type decoding, a real TA parameter decoder skeleton, a TA stream control/payload diagnostic view with named polygon/sprite/modifier/user-clip payload slots, real-shaped KOS polygon header/vertex and sprite-header/sprite-geometry fixtures with zero and nonzero mode payloads, a tiny diagnostic strip assembler that consumes fixture-only control/X/Y/color vertex packets across renderable lists, mixed-color Gouraud strip assembly for matching headers, immediate write-order strip/sprite preview composition, and tiny rectangular/skewed sprite quad previews with face-color plus non-twiddled/twiddled RGB565, ARGB4444-alpha, shading, and UV-mode texture paths.
- Add fixture expectations for PVR state. Started: manifests can assert current named PVR register values, RGB565 sentinel samples, TA command/list counts, and assembled TA strip matches.
- Add fixture expectations for AICA state. Started: manifests can assert current named register values plus decoded channel control, sample, compressed/streamed metadata, loop, pitch, pan/send, balance, volume, key-on fields, playback sample counters, and playback byte counters.
- Add silence-safe AICA register/channel tracking before audible output. Started: AICA MMIO, sound RAM writes, current named register values, decoded sample format/stride/compression metadata, loop state, key-on state, pan/send balance, touched channel snapshots, nominal 44.1 kHz PCM16/PCM8 playback position/loop and loop-end diagnostics, ADPCM packed playback counters, and optional PCM16/PCM8/ADPCM WAV dumps are captured; broader AICA DSP/ARM behavior remains future work.

## Milestone 6: Media And Broader Compatibility

- Parse legal/local media formats. Started: raw 2048-byte data, 2352-byte CD-sector payload extraction, simple CUE data-track selection, and GDI data-track mapping by absolute LBA, including generated multi-track GDI media with nontrivial data-track FADs, non-track-3 data tracks, 2352-byte source sectors, and GDI file offsets.
- Add GD-ROM sector read behavior. Started: firmware GD-ROM HLE can read sectors from the loaded media image into system RAM, KOS `cdrom_read_sectors()` PIO/DMA command layouts are wired to the HLE path, queued command ids now report completed/failed status with transferred byte counts, generated local media now includes a tiny nested ISO9660 filesystem, and summaries/fixtures record status, sector-mode, TOC, track mapping, and sector/count/destination/status diagnostics for loaded/no-media drive status, reinit sector-mode setup, successful TOC reads, TOC-discovered multi-track/2352-source reads, no-media TOC/read and out-of-range failures, successful raw single-sector, raw multi-sector, missing-file, root directory, nested directory, multi-sector file, and seek/EOF media access.
- Inspect retail-adjacent media without booting it. Started: `media inspect` reports raw/CUE/GDI geometry, CUE track layout, IP.BIN-style Dreamcast boot metadata when a readable boot sector is found, and adjacent CUE-directory boot candidates for odd dump layouts. `media extract-boot` adds a minimal ISO9660 reader and can extract the boot file named by IP.BIN from selected media, a usable adjacent candidate, or a split-track boot extent that lands in a later adjacent track file. `media analyze-boot` compares original and descrambled boot binary layouts, reports startup-stub/opcode heuristics, and can emit a descrambled candidate. `media boot-smoke` maps the selected raw boot binary at `0x8C010000`, seeds the full executable IP.BIN at `0x8C008000` for media inputs, rebuilds CUE directory fallback IP.BIN data from 2048-byte sector payloads, enters the IP.BIN bootstrap with BIOS-like SR/VBlank state, reports key boot-region writes, and can stop on unmapped or selected device-domain accesses to expose concrete emulator gaps.
- Ratchet local retail probes. Started: `tools/probe-retail.ps1` verifies current local-disc checkpoints, including DOA2 and Rayman progressing through IP.BIN framebuffer setup to the current system BIOS soft-reset checkpoint without unmapped stops and Legacy clearing the former `0x330441F0` corrupted-IP.BIN pointer/table blocker before reaching the IP.BIN framebuffer clear loop at `0x8C008374`.
- Run public redistributable demos and user-provided local images through the artifact pipeline.
