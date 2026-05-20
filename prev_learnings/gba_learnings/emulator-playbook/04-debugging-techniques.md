# Debugging Techniques

The core debugging pattern is: reduce a symptom to the first impossible hardware state. "PC became zero" is not the bug. The bug is the earlier event that made zero a plausible branch target.

## Start With A Repro Recipe

Every serious bug needs a recipe:

- ROM/media path or stable identifier,
- BIOS/firmware mode,
- save file and read/write mode,
- input script,
- frame/step/time limits,
- expected failure frame,
- expected PC or exception,
- command line.

If a failure cannot be reproduced from a command, it will waste time.

## Prefer Bounded Traces

Full traces become unreadable quickly. Better tools:

- trace only a PC/address range,
- trace only selected frames,
- keep the last N instructions before crash,
- stop on a specific PC hit count,
- watch reads/writes to a narrow address range,
- dump memory at crash,
- trace interrupts/DMA/system calls separately.

The useful question is usually not "what happened since boot?" It is "what wrote this bad value?" or "why did this branch target change?"

## Invalid PC Workflow

For invalid PC/control-flow bugs:

1. Stop on invalid PC and dump registers.
2. Use a trace tail to identify the final branch/load/return.
3. Identify the source register or memory slot used as the branch target.
4. Add a watchpoint on that source.
5. Narrow by frame range and address range.
6. Find the last writer.
7. Determine whether the writer was correct and the later reader was wrong, or vice versa.

Common causes:

- wrong interrupt return,
- wrong stack pointer or banked register,
- bad block transfer ordering,
- missing alignment/rotation behavior,
- bad DMA timing or reentrancy,
- generated code copied incorrectly,
- save/peripheral protocol returning unexpected data,
- firmware call side effects missing,
- game input script taking an unsupported path.

## Generated Code

Commercial software may generate, copy, decompress, or patch executable code in RAM. Treat RAM execution as normal.

When RAM code crashes:

- trace the DMA/copy/decompression that wrote it,
- dump the RAM code and source ROM bytes,
- check mirrors and alignment,
- check instruction decode against real hardware quirks,
- beware of data being intentionally decoded as an instruction family with reserved bits.

Do not assume an instruction is invalid just because it looks odd. Hardware may ignore bits that a clean decoder wants to reject.

## Interrupt Bugs

Interrupt bugs often look like random jumps.

Trace:

- interrupt request flags,
- interrupt enable flags,
- master enable,
- entry PC and mode,
- saved status/registers,
- handler address,
- return instruction,
- return target,
- flags after acknowledgement.

For no-firmware or HLE firmware paths, verify that the software-visible interrupt frame exactly matches what games expect. A mostly right interrupt trampoline can pass many games and fail one game hard.

## DMA Bugs

DMA bugs also masquerade as CPU bugs.

Trace:

- channel,
- timing mode,
- source/destination,
- count,
- width,
- control value,
- start frame/cycle,
- whether another DMA starts during a DMA-originated write.

Pay special attention to:

- immediate DMA triggered by writes to control registers,
- DMA reentrancy,
- address reload modes,
- repeat modes,
- FIFO/audio DMA,
- video-timed DMA,
- DMA to/from IO mirrors.

## Timing And Polling Bugs

Polling loops are compatibility sensors. If a game spins forever or times out:

- identify the polled address,
- identify the bit transition expected,
- trace who sets/clears it,
- verify event order,
- verify wait-state/cycle budget,
- compare against reference emulator behavior if possible.

Do not immediately "unstick" a loop with a hack. A stuck loop often points to a real missing event.

## Save/Peripheral Debugging

For save and peripheral protocols:

- log command state transitions,
- record raw bits/bytes,
- verify erased/default values,
- test readback through the protocol,
- distinguish protocol success from in-game progression,
- check marker detection against concrete library strings rather than loose substrings.

Loose save detection caused a real class of false classification risk in `gbaSharp`: bare marker-like strings can appear in ROM data and should not always decide the save backend.

## Renderer Debugging

Renderer bugs need visual and data tools:

- full framebuffer output,
- per-layer debug images,
- palette dumps,
- tile/map address logging,
- sprite counts,
- window/blend state,
- out-of-range guard assertions during development.

When a renderer crashes on retail software, suspect hardware wrapping/mirroring before assuming bad game data. Real hardware often wraps where arrays throw.

## Compare To References Carefully

Reference emulators are invaluable, but do not copy blindly.

Use them to answer:

- what is the register state at this frame?
- does this instruction execute or trap?
- what value does this read return?
- when does this interrupt fire?
- does this DMA start immediately?
- what does the framebuffer look like?

If two mature emulators disagree, the issue is likely hardware-subtle. Make a focused test before encoding behavior.

## Document The Signature

When you cannot finish a bug, leave a precise signature:

- title/media id,
- phase/input,
- frame,
- PC,
- last branch,
- bad source address/register,
- last writer if known,
- current hypothesis,
- next command to run.

This turns a paused investigation into a queue item instead of a lost thread.
