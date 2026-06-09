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
| Sonic Adventure 2 | CUE | `InstructionLimit`, `PC=0x8C135C10`, 50,000,000 instructions | The game still performs 45 successful GD-ROM reads and 2 TOC requests, but now moves past the former `0xA05F688C`/G2 DMA busy frontier. The active 50M frontier is inside the SA2 AICA/G2 PIO upload helper while initializing the sound driver. |
| Sonic Shuffle | GDI | `UnsupportedInstruction`, `PC=0x8C008300`, 7,952,388 instructions | The game now performs a successful GD-ROM read and reaches nonzero framebuffer bytes, then reads IP.BIN work-area bytes `0x8C0080FC/FE == 1` and jumps from `0x8C014C50` to boot-area address `0x8C008300`, which currently contains `0x0000`. |

The old shared `0x8C000000` callback frontier is retired for this trio: the firmware HLE now treats it as the default no-op callback and returns to `PR` with `r0=0`. Sonic Shuffle's former `PVR_SYNC_STATUS` wait is also retired by exposing bit `0x2000` during the synthetic VBlank status window. Sonic Adventure 2 and Sonic Shuffle additionally depend on GD-ROM commands using the parameter words captured at `SEND_COMMAND` time instead of rereading mutable stack parameter blocks when `GDROM_MAINLOOP` executes the queued command.

Latest Sonic Adventure 2 audio/G2 progress:

- G2 DMA channel 0 now models the AICA DMA registers at `0xA05F7800/04/08/0C/14/18`, copies between system RAM and AICA RAM, clears the start bit, and raises ASIC event `0x000f`.
- The former `0xA05F7818` busy-poll now reads clear, moving a 100M run into SA2's AICA/G2 PIO helpers instead of stopping at `0x8C153CEE`.
- Narrow SA2 fast-forwards now cover the exact VRAM clear loop, the post-load `0x8C10052A` word clear and `0x8C100554` byte clear over system RAM, the AICA PIO read-word helper, the AICA status wait that depends on unimplemented AICA ARM-side command completion, the AICA PIO write loop used to upload sound-driver data, one-shot external/modem G2 read probes, the `0x8C1543A0` AICA word-read wrapper, the `0x8C16BF10` AICA byte-read adapter, the AICA work-queue no-work poll, the IRQ-side `0x8C153A90` AICA work-poll wrapper, the outer `0x8C12F7F2-0x8C12F802` AICA work-poll loop, the G2 DMA status clear/set helpers around `0x8C1709E0` and `0x8C170A98`, the follow-up `EXEC` completion wait that polls the AICA work-structure mailbox at `[*0x8C1833A4 + 0x80] + 0xD8`, the IP.BIN zero-bit glyph walker around `0x8C0089F6-0x8C008A74`, the IP.BIN glyph draw helper at `0x8C008AD0-0x8C008B08`, the set-bit glyph draw tail at `0x8C008A3E-0x8C008A4C`, the empty pointer-table scan around `0x8C135CD8-0x8C135D06`, the trace-gated `0x8C10F440-0x8C10F446` cache-invalidate loop, the `0x8C134F3E-0x8C134F76` record-hash scan, the full SA2 byte-copy helper entry at `0x8C134CA8-0x8C134CBE`, the AICA no-work slot scan/cleanup pair around `0x8C15B604-0x8C15B690` and `0x8C15B83C-0x8C15B860`, the AICA name-call bridge and loop tail around `0x8C15B91A-0x8C15B936`, the AICA channel setup bridge at `0x8C15C4DE-0x8C15C564`, the AICA post-setup flag tail at `0x8C15C57E-0x8C15C5AA`, the AICA descriptor-copy helper at `0x8C15C622-0x8C15C680`, the inactive AICA channel tail at `0x8C15C780-0x8C15C856`, the AICA channel flag return tail at `0x8C15C5AC-0x8C15C5DA`, and the `0x8C023E20` PRS-style asset decompressor used after the 882,688-byte DMA read.
- The latest 120M no-trace SA2 profile reaches `PC=0x8C15AFA6` with 47/47 GD-ROM reads and no TA writes. CPU fast-forward has risen to `116,779,086`; the `0x8C10F440` cache loop, `0x8C134F3E` record-hash scan, `0x8C134CAA-0x8C134CBE` byte-copy body, `0x8C008AD2-0x8C008B08` IP.BIN draw-helper body, and `0x8C008A40-0x8C008A4A` set-bit draw-call tail are retired from the hot profile. The next profile-guided targets are mostly the IP.BIN set-bit prefix/math helpers around `0x8C008A14-0x8C008A3E` and `0x8C0098CC`, plus remaining helper trigger PCs such as `0x8C134CA8`, `0x8C15B918`, `0x8C15B92E`, and `0x8C15C57C`.
- A 500M post-PRS SA2 probe reaches later disc loading with 47 GD-ROM reads, including an 882,688-byte DMA read to `0x0C428800` and a 61,440-byte read to `0x0C300000`, but still has no TA writes. The old `0x8C12F56x`/`0x8C1543A0` `EXEC` wait collapses; `0xA08000F8` now contains `EXEC`; the old `0x8C023E34-0x8C023E88` decompressor cluster, `0x8C10052A-0x8C10055C` RAM-clear cluster, G2 DMA status set/clear helpers, IRQ-side `0x8C153A90` AICA work-poll body, and active `0x8C16B58E` AICA work-helper frontier are retired from the current higher-budget wall. CPU fast-forward is about 496.7M instructions. The current 500M frontier is `PC=0x8C15BF8C`, in a later SA2 sound/asset update path that still has no PVR TA traffic. The next work is to identify which remaining AICA work-structure fields are standing in for ARM/audio-driver completion so this becomes device behavior rather than another one-off skip.

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
