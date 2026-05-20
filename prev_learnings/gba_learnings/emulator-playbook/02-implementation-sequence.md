# Implementation Sequence

This sequence is based on what made `gbaSharp` move from "starts code" to "runs lots of retail software." The exact order will vary by console, but the milestones transfer well.

## 1. Research Before Coding

Collect primary and high-quality references first:

- official or leaked hardware manuals when legal to use,
- community technical references,
- known-good emulator source for behavior clues,
- test ROM suites,
- hardware behavior notes,
- file format and cartridge/media specs,
- timing tables,
- interrupt/DMA/video/audio diagrams.

Write down uncertainties. A note that says "unknown behavior here" is better than code that silently guesses.

## 2. Minimal Executable Core

Start with:

- ROM/media loading,
- reset state,
- memory map skeleton,
- CPU fetch/decode/execute for a small instruction subset,
- enough stepping to run tiny hand-written programs,
- unit tests for each instruction or instruction family.

Avoid starting with a GUI. A GUI makes progress feel real, but early emulator work needs command-line determinism.

## 3. CPU Correctness First

CPU bugs contaminate every other subsystem. Prioritize:

- decoding masks and undefined/reserved cases,
- flags,
- pipeline-visible PC behavior,
- branch/link behavior,
- load/store alignment rules,
- block transfers,
- exception entry/return,
- mode/state changes,
- multiply/divide or other multi-cycle instructions,
- user/supervisor register banking if present.

Use unit tests for exact register and flag outcomes. Add regression tests whenever a retail game exposes a new CPU edge case. The multiply-family quirk found in `gbaSharp` is a good example: generated retail code depended on hardware accepting an opcode pattern that a stricter decoder misclassified.

## 4. Memory Bus And Timing Skeleton

Implement the bus as a hardware component, not a byte array. It should own:

- address decode,
- mirrors,
- unmapped/open-bus reads,
- alignment and rotation rules,
- wait states,
- memory-mapped IO,
- cartridge/media windows,
- save/peripheral windows.

Timing can begin approximate, but the API should return or charge cycles from the start. Retrofitting wait states late is messy.

## 5. Interrupts, Timers, DMA

After basic CPU and memory tests pass, implement the mechanisms that make games alive:

- timers,
- interrupt enable/flags/master gates,
- interrupt priority and acknowledgement,
- DMA timing modes,
- DMA address increment/decrement/fixed/reload,
- DMA reentrancy rules,
- halt/stop/wait behavior.

DMA and interrupt bugs often present as invalid branches hundreds of frames later. Build trace hooks for them as you implement them.

## 6. Video Before Audio

A first renderer is essential for confidence. Implement visible output early:

- background/sprite layers or the console equivalent,
- palettes/color conversion,
- scrolling/windowing/mosaic/blending as applicable,
- debug rendering for individual layers,
- frame hashes.

Do not wait for perfect video to start compatibility testing. But do build visual diff tooling before trusting "looks okay."

## 7. Saves And Cartridge/Media Protocols

Save support is not just file IO. Implement the game-facing protocol:

- SRAM-like direct memory,
- flash command unlock/program/erase/bank-select flows,
- EEPROM or serial protocols,
- memory card or filesystem semantics,
- save-size detection and overrides,
- export/load with size validation.

Then build a save probe that writes through the emulated protocol, exports the save, reloads it into a fresh system, and verifies through the protocol again.

## 8. Firmware/BIOS Services

If games call firmware services, implement enough to match behavior:

- reset/init calls,
- wait/interrupt calls,
- memory copy/fill/decompression helpers,
- math helpers,
- checksum/version side effects,
- documented register clobbering.

For early compatibility, high-level emulation of firmware services can be acceptable. Keep it tested and explicit. Wrong side effects can break games in surprising places.

## 9. Compatibility Harness

Before broad retail testing, build a CLI that can:

- run one ROM to a frame or step limit,
- inject input by frame,
- stop on invalid PC or requested PC,
- print state,
- write screenshots/framebuffers,
- run standard compatibility phases,
- classify outcomes,
- write CSV reports.

The harness is part of the emulator project, not a side script. Without it, compatibility work becomes memory and vibes.

## 10. Curated Milestone, Then Full Library

Use layers:

1. CPU and hardware test ROMs.
2. A small smoke set of known retail games.
3. A curated milestone set of popular and technically demanding titles.
4. A representative save/peripheral set.
5. Full archive/library sweep.
6. Manual gameplay and reference comparisons.

Do not jump straight to full-library results. A full sweep gives lots of data, but without triage tools it mostly gives noise.

## 11. Frontend

Build the frontend once the core can boot and run enough software to be useful. Keep it simple first:

- open ROM,
- run/pause/reset,
- input mapping,
- framebuffer display,
- save load/write,
- optional debug layer display.

Frontend polish is valuable, but it should not block core correctness work.
