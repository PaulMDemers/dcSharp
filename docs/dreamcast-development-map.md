# Dreamcast Development Map

This map turns the current Sonic frontiers, previous emulator lessons, public Dreamcast references, KallistiOS source, and reference-emulator workflow into a practical build plan.

## Short Answer

Yes, we can speed up development by implementing against reference manuals and legal homebrew fixtures in parallel with watching retail games. Retail traces should rank priorities and reveal integration bugs, but they should not be the only way we discover hardware behavior.

The productive loop is:

1. Use manuals and public docs to define the expected hardware contract.
2. Build a small KallistiOS or synthetic fixture that exercises that contract.
3. Add focused unit and fixture tests.
4. Re-run the retail frontier to prove the general model moved the game.
5. Compare against Flycast or captured reference frames when the behavior becomes visual, audible, or timing-sensitive.

## Evidence Ladder

Use the strongest available evidence first, and record which tier a behavior came from.

| Tier | Evidence | Use |
| --- | --- | --- |
| 1 | CPU/vendor manuals and official Dreamcast documentation | Opcode semantics, exceptions, registers, DMA, bus behavior, timing names, device command layouts. |
| 2 | KallistiOS docs and source | Legal SH-4 programs, open driver behavior, AICA queues, PVR command patterns, GD-ROM/Maple usage. |
| 3 | Public hardware notes and community reverse engineering | Dreamcast-specific addresses, practical quirks, boot/media details, unclear manual gaps. |
| 4 | Reference emulators, especially Flycast | Behavioral comparison, frame/audio targets, source-level architecture ideas without copying code. |
| 5 | Retail traces | Prioritization, integration validation, game-specific probes, frontier discovery. |
| 6 | Narrow HLE or fast-forward shims | Temporary compatibility bridges only when exact, trace-gated, documented, and regression-tested. |

## Source Inventory

- Renesas SH7750/SH7750R manuals and product docs: SH-4 CPU, MMU, caches, timers, DMA, interrupt controller, bus controller.
- Sega/Dreamcast official documentation indexes: Holly/PVR, AICA, Maple, GD-ROM, boot flow, system architecture.
- KallistiOS repository and docs: legal fixtures and open implementations for PVR, AICA, Maple, GD-ROM, timers, store queues, and BIOS-adjacent usage.
- KallistiOS AICA driver source: `aica_queue_t`, `aica_cmd_t`, queue wraparound, `process_ok`, `AICA_CMD_NONE`, `AICA_CMD_PING`, `AICA_CMD_CHAN`, `AICA_CMD_SYNC_CLOCK`, channel start/stop/update, command/response queues, and fixed AICA RAM layout.
- Flycast: active reference emulator for behavior, reference runs, and subsystem architecture study. Treat GPL source as reference material unless project licensing is intentionally changed.
- Existing dcSharp docs: `docs/prev-learnings-digest.md`, `docs/dreamcast-research.md`, `docs/long-probes-and-reference.md`, `docs/sonic-intro-milestone.md`, and `docs/roadmap.md`.

Useful links:

- https://www.renesas.com/en/products/sh7750r
- https://kos-docs.dreamcast.wiki/
- https://github.com/KallistiOS/KallistiOS
- https://dreamcast.wiki/Hardware_overview
- https://segaretro.org/Dreamcast_official_documentation
- https://mc.pp.se/dc/
- https://github.com/flyinghead/flycast

## Subsystem Map

### SH-4 Core

Current state: enough integer/FPU/control behavior exists for many KOS and retail paths, including exception and interrupt machinery, but retail render/audio paths keep exposing sharp edges.

Build next:

- Generate an opcode coverage matrix from the decoder and tests.
- Add manual-derived unit tests for unimplemented or high-risk instructions before games encounter them.
- Expand FPU tests around FPSCR rounding, exception cause/sticky flags, `FR` bank switching, `FIPR`, `FTRV`, `FSCA`, and denormal handling.
- Add cache/store-queue tests: `ocbi`, `ocbp`, `ocbwb`, `pref`, SQ address decoding, P4/store-queue TA flush behavior.
- Add MMU/TLB tests driven by the SH7750 manual and Sega Rally/WinCE frontiers.

Technique: keep scalar interpreter behavior plain and testable; optimize with trace-gated fast paths only after parity tests exist.

### Memory, Bus, DMA, And Interrupts

Current state: system RAM, VRAM, AICA RAM, boot regions, ASIC events, TMU, Maple DMA, G2 DMA, and GD-ROM DMA are partially modeled.

Build next:

- Create a single memory-map inventory with address ranges, backing storage, mirroring, side effects, and diagnostic domain names.
- Add generic DMA channel models where we currently have title-shaped behavior.
- Strengthen ASIC event lifecycle tests: latch, mask, ACK, interrupt priority, banked registers, nested interrupt blocking.
- Add bus timing only where software-visible: wait loops, interrupt cadence, DMA completion ordering, and device busy/ready windows.

Technique: most Dreamcast compatibility failures are protocol/order bugs before they are cycle bugs. Prefer event-order correctness and clear diagnostics over cycle counting.

### GD-ROM, BIOS, And Boot Flow

Current state: media parsing, CUE/GDI extraction, IP.BIN seeding, queued GD-ROM commands, TOC, sector reads, and some firmware HLE paths exist. Sonic Adventure still exits through a BIOS/system function path; Sonic Shuffle jumps into boot-area code.

Build next:

- Document each BIOS/system-function trap we HLE: inputs, outputs, terminal/nonterminal behavior, and observed titles.
- Add fixtures for IP.BIN work-area bytes, boot mode, soft-reset handoff, and high-RAM stack setup.
- Add media fixtures for more GDI/CUE edge cases: multi-session, data-track bias, 2048 vs 2352 sector reads, TOC variants.
- For Sonic Adventure, trace the system-function `r4=1` exit and the preceding GD-ROM status/read state.
- For Sonic Shuffle, trace why `0x8C0080FC/FE` stay set before the jump to `0x8C008300`.

Technique: boot/media bugs often masquerade as CPU bugs. Always compare loaded bytes, boot work-area state, GD-ROM command history, and destination buffers before changing CPU behavior.

### Maple

Current state: controller discovery, condition polling, scripts, DMA descriptor traversal, and IRQB basics exist.

Build next:

- Add VMU memory-card responses and minimal filesystem/status behavior.
- Add device hotplug/absent-device edge cases.
- Add broader accessory identity tables only when a title asks for them.
- Add reference summaries for Maple command sequences during Sonic and DOA2 boot.

Technique: Maple is packet-protocol work. Decode every command/response in summaries and build fixture packets before implementing broad accessory behavior.

### AICA And G2 Audio Path

Current state: silence-safe register/channel tracking, PCM/ADPCM diagnostics, G2 DMA, some AICA RAM upload paths, and many SA2-specific setup fast-forwards exist. The general AICA ARM/mailbox model is still the biggest structural gap.

Build next:

- Promote the KOS-style AICA command queue model into a generic AICA service:
  - detect valid queue headers;
  - honor `head`, `tail`, `size`, `valid`, `process_ok`, and `data`;
  - support wraparound packets;
  - clamp packet size to `AICA_CMD_MAX_SIZE`;
  - advance the KOS-style AICA millisecond clock from hardware ticks and obey timestamp delay against it;
  - process `NONE`, `PING`, `SYNC_CLOCK`, and `CHAN`;
  - mirror channel start/stop/update status into the channel area;
  - extend response packet generation beyond the current `PING`/`PONG` path where software expects it.
- Add KOS fixtures that submit each command type and verify queue tail movement, channel state, response behavior, and clock reset.
- Scan retail AICA RAM for queue-like structures and uploaded ARM driver markers, then report likely queue candidates. Queue snapshots now classify fixed and data-pointer-matched command/response queues and filter non-fixed candidates through forward data pointers plus pending packet-size sanity checks. AICA RAM region diagnostics now identify uploaded ARM/control/sample regions, and RAM access hotspots plus named field-event logs identify SH4-facing status fields. Current SA2 120M evidence shows an uploaded AICA program, no KOS-style mailbox queue, and the named hot custom status/mailbox candidate `SA2_AICA_STATUS_CANDIDATE` at AICA RAM `0x012400`: it is initially filled with `0x44504D44` (`DMPD`), observed as zero, written to `0x00000001`, then read repeatedly from `PC=0x8C16BF10`.
- Decide whether to add a minimal ARM7 interpreter later. For the Sonic intro milestone, a queue/service model is likely higher return than full ARM execution.
- Keep moving AICA-driver completion behavior into `DreamcastMemory` instead of SH-4 fast-forward bodies. The SA2 `EXEC` completion shim now resolves the AICA RAM completion word through `TryCompleteAicaDriverCompletionWord`, which is still title-shaped but puts the custom driver field mutation in the audio/memory subsystem where a future ARM/mailbox service can replace it.
- Use `driverFields` in run summaries to track current custom AICA driver state. The known SA2 `EXEC` and status candidates now report value, read/write counts, and last read/write PCs directly, which should make the next active-work/status-table producer easier to isolate.
- Route custom driver field writes through memory-owned helpers even when they are reached by SH-4 fast-forward paths. SA2's G2 PIO write shortcut now calls `TryWriteAicaRamDriverField` for known fields, keeping future replacement service behavior local to AICA memory instead of scattered through CPU shortcuts.

Technique: replace title-shaped waits with device-shaped mailbox completion. Keep the existing SA2 `EXEC` shim until the queue model proves it explains the same progress.

### PVR, TA, And Rendering

Current state: VRAM/registers, TA command logging, list grouping, primitive assembly, tiny previews, texture diagnostics, and DOA2 render-probe work exist. Sonic games have not produced TA traffic yet at the current frontiers.

Build next:

- Continue PVR development against KOS fixtures and DOA2, not by waiting for Sonic TA traffic.
- Build a PVR register/TA command table from manuals, KOS headers, and observed packets.
- Add fixture coverage for:
  - store-queue to TA paths;
  - opaque, punch-through, translucent lists;
  - modifier volumes and user clip;
  - sprite payload formats;
  - texture formats: RGB565, ARGB1555, ARGB4444, VQ, twiddled, mipmaps;
  - filtering, clamp/repeat/flip, blending, depth, culling.
- Keep the current software preview path as a diagnostic renderer, then later replace/augment it with a more faithful tile/list renderer.
- Use Flycast reference frames for visual checkpoints once dcSharp emits comparable frames.

Technique: rendering can progress in parallel with Sonic boot work. The moment Sonic reaches TA writes, the renderer should already have fixture-backed behavior.

### Scheduler And Timing

Current state: synthetic VBlank, TMU, idle fast-forward, scheduler wake boundaries, and several device completion events exist.

Build next:

- Centralize event scheduling contracts: VBlank, TMU, Maple DMA, GD-ROM DMA/command completion, G2 DMA, AICA queue service, PVR status windows.
- Add deterministic save-state snapshots at instruction/device boundaries so long retail probes can restart near frontiers.
- Add "run until condition" probes: first TA write, first framebuffer nonzero, first GD-ROM read after sector X, first AICA queue candidate, first unsupported opcode.

Technique: timing work should be externally observable. If a change does not alter a device-visible event, interrupt ordering, or wait-loop exit, it probably belongs behind a diagnostic flag until proven.

### Tooling And Workflow

Build next:

- Save states for retail probes, including CPU, memory, device, scheduler, media, and diagnostics state.
- Trace diff tooling: compare two runs by stop reason, PC, register set, device counters, and first divergent instruction/device access.
- Source/symbol support: keep KOS map/ELF symbols attached to traces; add retail region labels where inferred.
- Reference capture index: store local Flycast frame/audio capture metadata without committing generated media.
- Coverage dashboards: opcode coverage, device register coverage, fixture coverage, retail frontier coverage.
- Probe hygiene: fix stale assertions in scripts as frontiers move, and prefer artifact files over terminal scrollback.

## Best Techniques For This Project

### Use Three Lanes At Once

- Specification lane: manuals, KOS source, and small fixtures.
- Compatibility lane: retail traces and hot PC profiles.
- Reference lane: Flycast/manual captures for frames, audio, and high-level behavior.

The fastest work happens when a fix touches all three lanes: a manual-backed model, a legal fixture, and a retail frontier movement.

### Build Small Hardware Contracts

Avoid "make Sonic work" as an implementation unit. Convert it into smaller contracts:

- "AICA command queue tail advances when `process_ok=1` and timestamp is due."
- "GD-ROM queued command parameters are latched at send time."
- "ASIC IRQ9 dispatch acknowledges the accepted event and restores through `rte`."
- "TA store-queue flush produces the same command words as memory-mapped writes."

These become tests and survive after the title moves on.

### Treat Fast-Forwards As Temporary Lab Equipment

Fast-forwards are useful because a 200 MHz guest can spend enormous time in boring loops. Keep them:

- exact opcode/state/literal guarded;
- disabled under relevant trace/watch diagnostics;
- scheduler-clamped;
- covered by normal-vs-fast parity tests;
- documented as compatibility shortcuts, not hardware truth.

Retire or bypass them when the corresponding general hardware model exists.

### Prefer Artifacts Over Memory

Every meaningful probe should leave:

- output summary;
- profile;
- trace window or tail;
- device logs;
- memory snapshots for relevant ranges;
- reference comparison when visual/audio.

The artifact should answer "what changed?" without rerunning the title.

### Implement From Homebrew Outward

For each subsystem:

1. Synthetic unit tests for edge semantics.
2. Tiny KOS fixture.
3. Manifest expectation.
4. Retail probe.
5. Reference comparison if applicable.

This prevents retail games from becoming the only test suite.

## Current Sonic-Focused Build Map

### Sonic Adventure 2

State: best current canary. The focused long SA2 AICA/G2 ladder reaches the 500,068,800-instruction budget at `PC=0x8C1709F0` in the G2 DMA status-clear helper, with no TA traffic yet.

Next:

- Continue the AICA/G2 sound-driver path, but bias toward the generic AICA queue/service model.
- Add long probes with "first TA write" and "first later GD-ROM read" stop conditions.
- Keep shaving IP.BIN glyph/pattern-fill hotspots only when they block useful probe budgets.
- Once TA writes appear, switch attention to PVR command correctness and reference-frame comparison.

### Sonic Shuffle

State: reaches a successful GD-ROM read and nonzero framebuffer bytes, then jumps to `0x8C008300` containing `0x0000`.

Next:

- Trace producer/consumer history for IP.BIN work-area bytes `0x8C0080FC/FE`.
- Compare BIOS handoff state against reference and KOS boot assumptions.
- Verify GDI high-density extraction and boot-region writes before changing CPU or PVR behavior.

### Sonic Adventure

State: performs 8 successful GD-ROM DMA reads, then exits through BIOS/system-function-like path at `PC=0x8C0000E8` with `r4=1`.

Next:

- Trace the caller around `0x8C60434C-0x8C604350`.
- Capture GD-ROM command/status buffers, read destination contents, and boot work-area state.
- Determine whether system function `1` means a real disc/media error path, missing BIOS state, or a callback/table initialization gap.

## Near-Term Priority Order

1. Generic AICA queue/service model plus KOS command fixtures.
2. Save-state or restartable frontier snapshots for long retail probes.
3. Sonic Adventure BIOS/system-function trace and Sonic Shuffle boot-work trace.
4. PVR fixture expansion from manuals/KOS while Sonic still lacks TA traffic.
5. Trace-diff and coverage tooling to make each long probe cheaper to interpret.
6. Clean up stale Sonic probe assertions as frontiers move.

This keeps development moving even when a game is stuck: one lane improves the hardware model, one lane improves diagnostics, and one lane keeps pressure on the current retail frontier.
