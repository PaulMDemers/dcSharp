# Sonic Intro Milestone

Goal: use Sonic Adventure, Sonic Adventure 2, and Sonic Shuffle as the near-term retail canaries, with the first visible milestone being their intro sequences running far enough to produce GD-ROM streaming and PVR TA/render output.

## Current Baseline

Run with:

```powershell
tools\probe-sonic.ps1 -AssertKnownFrontiers
```

The current `main` baseline at a 50M instruction budget is:

| Title | Primary Local Media | Current Result | Frontier |
| --- | --- | --- | --- |
| Sonic Adventure | GDI | `UnsupportedInstruction`, `PC=0x8C000000`, 46,589,874 instructions | The game reads callback/table slot `0x8C6734C4`, gets `0x8C000000`, then jumps through it from `0x8C60493A`. |
| Sonic Adventure 2 | CUE | `UnsupportedInstruction`, `PC=0x8C000000`, 44,590,162 instructions | The game reads callback/table slot `0x8C17EC60`, gets `0x8C000000`, then jumps through it from `0x8C111EE6`. |
| Sonic Shuffle | GDI | `InstructionLimit`, `PC=0x8C02A0C0`, 50,000,000 instructions | The game spins in a `PVR_SYNC_STATUS` wait, polling `0xA05F810C` through helper `0x8C054D8A` and testing bit `0x2000`. |

All three currently have zero framebuffer output, no PVR TA geometry, and no successful GD-ROM read commands before these frontiers.

## Work Plan

1. Keep `tools\probe-sonic.ps1` as the quick regression loop while this milestone is active.
2. For Sonic Adventure and Sonic Adventure 2, trace the watched read/write history for `0x8C6734C4` and `0x8C17EC60`, then work backward to the callback/table initializer that should have populated those slots.
3. Compare Sonic Adventure GDI against its CUE fallback around IP.BIN work-area bytes, boot-directory state, and high-density media metadata before treating the zero callback slot as a CPU bug.
4. For Sonic Shuffle, characterize the `PVR_SYNC_STATUS` bit `0x2000` wait at `0x8C02A0B6-0x8C02A0C8`: confirm whether it is a scanline/VBlank/display status bit, then model it narrowly enough to clear the wait without lying to other retail paths.
5. After each title passes its current frontier, use GD-ROM command logs, PVR TA logs, and framebuffer snapshots to chase the next intro-sequence checkpoint.

## Success Criteria

The milestone is not complete when the games merely reach a higher instruction count. It is complete when the Sonic probes show at least one of:

- successful GD-ROM reads after the current frontiers,
- nonzero PVR TA command traffic,
- a nonblank framebuffer or intro/title visual output.
