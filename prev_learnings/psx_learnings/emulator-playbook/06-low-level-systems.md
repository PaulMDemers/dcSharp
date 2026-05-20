# Low-Level Systems

## CPU

CPU bring-up should include:

- instruction decode tests,
- branch and delay behavior,
- load delay or equivalent pipeline quirks,
- exceptions and return-from-exception,
- alignment faults,
- interrupt entry and masking,
- memory aliases,
- coprocessor or special-unit dispatch.

Retail software often relies on edge cases that synthetic tests miss. When an exception appears, dump EPC, cause, bad address, delay-slot state, and nearby registers.

## Bus And Memory

The bus should be boring and precise:

- map every documented region,
- implement mirrors and aliases,
- return stable values for unmapped reads,
- keep writes to unknown registers traceable,
- preserve device side effects,
- expose watch ranges.

Many "CPU bugs" are actually memory map mistakes.

## DMA

DMA is a compatibility multiplier. Implement:

- all channels, even if some are initially no-ops,
- direction and address increment/decrement,
- block and linked-list modes,
- interrupt behavior,
- request vs manual starts,
- channel priority where relevant,
- diagnostic summaries.

For linked-list graphics DMA, trace node count, first words, source address, command count delta, and guard conditions.

## Video Hardware

Start with a software renderer or equivalent exact path:

- command parser,
- video RAM or framebuffer model,
- display origin and mode,
- drawing area and offsets,
- uploads and copies,
- clears,
- rectangles,
- lines,
- polygons,
- textures and palettes,
- transparency or blending,
- mask or priority behavior.

Track both "submitted" and "actually wrote visible pixels." A primitive can be clipped, offscreen, transparent, degenerate, or drawn outside the active display.

## Geometry Or Math Units

If the console has a vector, geometry, DSP, or transform unit, do not leave it stubbed for long. Add:

- register read/write tests,
- common command implementations,
- saturation and flag behavior,
- representative title diagnostics,
- recent-operation summaries.

3D games can look like GPU failures when the real issue is transform math.

## Media

Disc, cartridge, tape, or other media systems need more than file reads:

- command/status behavior,
- response FIFO,
- data FIFO,
- timing,
- seek behavior,
- sector or block formats,
- raw vs cooked data,
- interrupts,
- DMA integration,
- buffering,
- audio/data multiplexing if present.

Log command parameters, current media position, active data bytes, queued buffers, and recent transfers.

## Audio

Audio is often deferred, but it affects compatibility:

- sound RAM,
- DMA transfers,
- key-on/key-off,
- envelopes,
- sample decode,
- IRQ behavior,
- streaming audio,
- frontend buffer pacing.

Even before perfect mixing, implement enough register behavior that games do not wait forever for audio state.

## Input And Storage

Input and save devices are small but high impact:

- controller polling protocol,
- status bits,
- IRQ timing,
- memory card or save media presence,
- "no device" behavior,
- stable defaults.

Some titles remain in firmware or menus if storage behavior is wrong. Make mounted/unmounted states explicit.

## Firmware And Boot

Firmware boot exposes real state. Be careful with:

- reset state,
- firmware callbacks,
- event tables,
- stack setup,
- executable load address,
- payload copy length,
- cache or instruction decode invalidation after loading code,
- handoff PC and registers.

If direct boot and firmware boot differ, dump the loaded executable payload and final bytes. Off-by-one payload copies can look like random game bugs.

