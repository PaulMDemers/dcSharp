# Sonic Intro Milestone

Goal: use Sonic Adventure, Sonic Adventure 2, and Sonic Shuffle as the near-term retail canaries, with the first visible milestone being their intro sequences running far enough to produce sustained GD-ROM streaming and PVR TA/render output.

## Current Baseline

Run with:

```powershell
tools\probe-sonic.ps1 -AssertKnownFrontiers
```

The current baseline at a 50M instruction budget is:

| Title | Primary Local Media | Current Result | Frontier |
| --- | --- | --- | --- |
| Sonic Adventure | GDI | `FirmwareExit`, `PC=0x8C0000E8`, 46,680,428 instructions | The game performs 8 successful GD-ROM DMA reads, then reads the BIOS system vector at `0x8C0000E0`, gets `0x8C0000E8`, and jumps from `0x8C604350` with `r4=1`, matching the system menu/error function path. |
| Sonic Adventure 2 | CUE | `InstructionLimit`, `PC=0x8C135BF4`, 50,000,000 instructions | The game still performs 45 successful GD-ROM reads and 2 TOC requests, but now moves past the former `0xA05F688C`/G2 DMA busy frontier. The active 50M frontier is inside the SA2 AICA/G2 PIO upload helper while initializing the sound driver. |
| Sonic Shuffle | GDI | `UnsupportedInstruction`, `PC=0x8C008300`, 7,952,388 instructions | The game now performs a successful GD-ROM read and reaches nonzero framebuffer bytes, then reads IP.BIN work-area bytes `0x8C0080FC/FE == 1` and jumps from `0x8C014C50` to boot-area address `0x8C008300`, which currently contains `0x0000`. |

The old shared `0x8C000000` callback frontier is retired for this trio: the firmware HLE now treats it as the default no-op callback and returns to `PR` with `r0=0`. Sonic Shuffle's former `PVR_SYNC_STATUS` wait is also retired by exposing bit `0x2000` during the synthetic VBlank status window. Sonic Adventure 2 and Sonic Shuffle additionally depend on GD-ROM commands using the parameter words captured at `SEND_COMMAND` time instead of rereading mutable stack parameter blocks when `GDROM_MAINLOOP` executes the queued command.

Latest Sonic Adventure 2 audio/G2 progress:

- G2 DMA channel 0 now models the AICA DMA registers at `0xA05F7800/04/08/0C/14/18`, copies between system RAM and AICA RAM, clears the start bit, and raises ASIC event `0x000f`.
- The former `0xA05F7818` busy-poll now reads clear, moving a 100M run into SA2's AICA/G2 PIO helpers instead of stopping at `0x8C153CEE`.
- Narrow SA2 fast-forwards now cover the exact VRAM clear loop, the AICA PIO read-word helper, the AICA status wait that depends on unimplemented AICA ARM-side command completion, the AICA PIO write loop used to upload sound-driver data, one-shot external/modem G2 read probes, and the `0x8C1543A0` AICA word-read wrapper.
- A 300M SA2 probe reaches the same G2/AICA startup region around `0x8C170ABA`, with 45 GD-ROM reads and no TA writes yet. The AICA word-read wrapper skip raises CPU fast-forward coverage to roughly 263.6M instructions and removes `0x8C1356D8` from the top profile; the remaining top cluster is repeated AICA status/read traffic around `0x8C12F56x` and `0x8C1543A0`. The next work is to replace the compatibility shims with a more general AICA ARM/audio-driver handshake model or chase the next renderer gate once that traffic collapses.

## Work Plan

1. Keep `tools\probe-sonic.ps1 -AssertKnownFrontiers` as the quick regression loop while this milestone is active.
2. For Sonic Adventure 2, continue the AICA startup path: identify which AICA RAM status words are guest/ARM mailboxes, model the minimal command-completion semantics, then re-run higher budgets until either PVR TA traffic appears or a new renderer gate is exposed.
3. For Sonic Shuffle, trace why the boot work-area mode bytes at `0x8C0080FC/FE` remain `1` before the branch to `0x8C008300`; compare the path against BIOS handoff and soft-reset behavior before forcing any global boot byte.
4. For Sonic Adventure, trace the repeated sector `45166` reads and the state that leads to BIOS system function `1`; inspect the loaded read target, status buffers, and callback/table state around `0x8C60434C-0x8C604350`.
5. After each title passes its current frontier, use GD-ROM command logs, PVR TA logs, and framebuffer snapshots to chase the next intro-sequence checkpoint.

## Success Criteria

The milestone is not complete when the games merely reach a higher instruction count. It is complete when the Sonic probes show visible intro/title progress with at least one of:

- sustained successful GD-ROM reads after the current frontiers,
- nonzero PVR TA command traffic,
- a nonblank framebuffer or intro/title visual output.
