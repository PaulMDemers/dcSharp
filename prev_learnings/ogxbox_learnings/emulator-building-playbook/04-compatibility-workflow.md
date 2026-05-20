# Compatibility Workflow

## Use Buckets, Not Pass/Fail

Early compatibility is too nuanced for pass/fail. Use buckets such as:

- Image not parsed.
- Executable not loaded.
- CPU fault before subsystem init.
- Firmware/kernel call missing.
- File IO active.
- Device init active.
- Display commands decoded.
- Clear-only frame.
- Primitives counted, no fragments.
- Visible untextured geometry.
- Textured geometry.
- Input-responsive menu.
- Playable.

This helps choose fixes that affect clusters of software.

## The Loop

For each target:

1. Run with a fixed milestone command.
2. Record the stop reason and counters.
3. Inspect the newest relevant trace tail.
4. Identify whether the blocker is parser, CPU, HLE, file IO, device, graphics,
   input, timing, or host safety.
5. Make the smallest bounded fix.
6. Re-run the same command.
7. If the target advances, move the milestone forward.
8. If it does not advance, gather narrower evidence.

Do not change the run command while evaluating whether a fix worked unless the
old command no longer reaches the relevant milestone.

## Pick Clusters Over One-Offs

The best next fix usually affects a family:

- Several tests crash on the same unimplemented instruction.
- Multiple titles hang on the same syscall.
- Many images decode GPU commands but never draw.
- Several demos render flat color because texture state is missing.
- Multiple games read files but never advance because directory semantics are
  wrong.

One-off title fixes are useful later, but early project velocity comes from
cluster blockers.

## Maintain Smoke Sets

Keep several small recurring suites:

- **Unit tests:** fast, deterministic, no external assets.
- **Micro homebrew/test ROMs:** one feature each.
- **Demo set:** broader real software with smaller scope than retail games.
- **Retail smoke set:** a few representative commercial titles.
- **Known hard cases:** titles that exercise unusual hardware behavior.

The smoke set should be small enough to run often, even if the full suite is
slow.

## Compare Before And After

After each meaningful change, compare:

- Steps to milestone.
- First crash address.
- File read count and bytes.
- Device method count.
- Primitive and present counts.
- Frame fingerprint.
- Fragment probe hit/miss.
- Hot code list.

A fix that changes a screenshot but regresses file or GPU counters should be
treated carefully.

## Beware Early Stops

Stopping at the first primitive can be misleading:

- The first primitive may be a clear proxy, guard triangle, or setup artifact.
- A texture may not be bound yet.
- Later frames may be more representative.
- Synthetic/HLE primitives may increment counters even when not useful.

Use multiple stops when validating graphics:

- First primitive.
- First rasterized fragment.
- First visible frame.
- First textured fragment.
- Nth frame or Nth present.

## Document Known Tradeoffs

Every approximation should be documented with:

- What it does.
- Why it exists.
- Which tests it helps.
- Which tests might be distorted by it.
- What evidence would justify replacing it.

This keeps compatibility work honest.
