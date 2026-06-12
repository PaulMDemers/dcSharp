# Sonic Reference Baseline - 2026-06-11

This note captures the first Flycast reference frames and normalized dcSharp SA2 long-probe checkpoints taken after syncing the root Dreamcast BIOS files into Flycast v2.6.

## Flycast Reference Frames

Reference PNGs are generated artifacts under `artifacts\reference-frames\` and are intentionally not committed.

| Game | Capture | Result |
| --- | --- | --- |
| Sonic Adventure | `sa1-bios-8s.png` | Dreamcast BIOS swirl. |
| Sonic Adventure 2 | `sa2-bios-8s-dpi.png` | Dreamcast BIOS/title startup frame. |
| Sonic Shuffle | `shuffle-bios-8s.png` | Dreamcast BIOS swirl. |
| Sonic Adventure | `sa1-reference-35s.png` | Opening city video. |
| Sonic Adventure 2 | `sa2-reference-35s.png` | Title-logo fade. |
| Sonic Shuffle | `shuffle-reference-35s.png` | Opening video. |

Capture command shape:

```powershell
powershell -ExecutionPolicy Bypass -File tools\capture-flycast-frame.ps1 -Game SA2 -DelaySeconds 35 -OutputPath artifacts\reference-frames\sa2-reference-35s.png
```

## dcSharp SA2 Checkpoints

All probes used the Release CLI, `tools\probe-long.ps1`, `TraceTail=16` or `24`, and profile logging enabled. Outputs are generated under `artifacts\long-probes\`.

| Budget | Elapsed | PC | Stop | PVR regs | GD-ROM reads | CPU fast-forward | Artifact |
| ---: | ---: | --- | --- | ---: | ---: | ---: | --- |
| 50M | 11s | `0x8C15C7F4` | `InstructionLimit` | 2,255 | 0 | 48,723,337 | `sa2-baseline-50m-20260611-141554` |
| 120M | 1m28s | `0x8C15B77A` | `InstructionLimit` | 23,656 | 0 | 105,512,407 | `sa2-baseline-120m-20260611-141614` |
| 300M | 4m17s | `0x8C15BF8E` | `InstructionLimit` | 78,685 | 0 | 251,541,116 | `sa2-baseline-300m-20260611-141751` |
| 1B | 12m21s | `0x8C14C884` | `InstructionLimit` | 292,687 | 0 | 819,430,926 | `sa2-1b-20260611-130319` |

The same hot AICA/name-channel cluster dominates all checkpoints:

- `0x8C15B92E`
- `0x8C15C564`
- `0x8C15C56E`
- `0x8C15B8EC`
- `0x8C15B938`
- `0x8C15B68C-0x8C15B68E`

The current same-runner baseline does not reach game GD-ROM reads within 1B instructions. The next implementation pass should therefore stay focused on the AICA work/name/channel setup loop and the mailbox/driver-completion model rather than jumping to PVR TA rendering yet.

## AICA Mailbox Step

The first mailbox-shaped implementation now services valid KOS-style SH4-to-AICA command queues in AICA RAM. It is dirty-gated on writes to the command queue or clock word, consumes bounded packets, honors timestamp-delayed commands, mirrors channel start/update/stop state into the KOS channel status area, and resets the AICA clock for `AICA_CMD_SYNC_CLOCK`.

The SA2 50M checkpoint remained at `PC=0x8C15C7F4` with no GD-ROM reads after this change, and the dirty gate restored the 50M probe runtime to roughly the pre-mailbox baseline.

## AICA Bridge/Post-Setup Fold

The active-channel descriptor-return aggregate now opportunistically folds the return bridge and post-setup return tail when the live code and stack frame match the known SA2 shape. The legacy 92-instruction partial aggregate is still preserved when the post-setup tail is not safe to consume.

The 50M probe `sa2-c564-post-50m-20260611-174756` stayed at `PC=0x8C15C7F4`, kept `PVR registers=2255`, and still had no GD-ROM reads. Its profile moved `0x8C15C56E` out of the top hotspots; the next visible AICA pressure is the remaining `0x8C15C57C` / `0x8C15C5DA` / status-update chain.

## AICA Zero-Mask Descriptor/Status Fold

The zero-mask descriptor-copy path now folds the `0x8C15C680` prologue through descriptor copy, status bridge, and status helper, stopping at `0x8C15C69A` when the slot byte transition requires the event epilogue to run normally. It still consumes the no-event epilogue when that path is proven safe.

The 50M probe `sa2-zeromask-dispatch-50m-20260611-182503` stayed at `PC=0x8C15C7F4`, kept `PVR registers=2255`, and still had no GD-ROM reads. The scheduler reported fewer CPU fast-forward batches (`69,207` vs `71,658` in the earlier same-day checkpoint), and `0x8C15C5DA`, `0x8C15C622`, and `0x8C15C694` dropped out of the top profile. `0x8C15C680` remains as the aggregate entry point.

## AICA Name-Call Active Descriptor Fold

The name-call/channel-setup aggregate now reuses the active-channel descriptor-return core when the next instruction is the `0x8C15C564` active setup entry. Exact-budget callers still stop at `0x8C15C564`; larger budgets can continue through descriptor copy and post-setup return.

The 50M probe `sa2-name-active-descriptor-50m-20260611-184714` stayed at `PC=0x8C15C7F4`, kept `PVR registers=2255`, and still had no GD-ROM reads. CPU fast-forward batches dropped to `60,021`, and `0x8C15C564` dropped out of the top profile. The dominant remaining SA2 AICA pressure is now the outer name loop entry at `0x8C15B92E` plus group/slot scan entries around `0x8C15B8EC`, `0x8C15B938`, and `0x8C15B604`.

## AICA One-Step Name Loop Chain

The active descriptor/post-setup return core now opportunistically consumes one following `0x8C15B92E` name-loop tail and next active setup. The nested setup deliberately stops after its descriptor return so the chain stays bounded and easy to reason about.

The 50M probe `sa2-b92e-chain-50m-20260611-222052` stayed at `PC=0x8C15C7F4`, kept `PVR registers=2255`, and still had no GD-ROM reads. CPU fast-forward batches dropped again to `56,346`, and `0x8C15B92E` dropped from `11,024` to `7,349` profile hits.

## AICA Group Tail To Descriptor Chain

The `0x8C15B938` group-tail loop-back can now consume the following `0x8C15B8EC` descriptor-head aggregate and opportunistically continue into the name/setup path when the state supports it. The exit case still uses the narrow group-tail fast-forward.

The 50M probe `sa2-group-tail-chain-50m-20260611-222702` stayed at `PC=0x8C15C7F4`, kept `PVR registers=2255`, and still had no GD-ROM reads. CPU fast-forward batches dropped to `55,121`, and `0x8C15B8EC` dropped out of the top 40 profile entries. The next exposed fold is the `0x8C15B92E` name-loop exit into the same group-tail continuation, which should reduce `0x8C15B938`.

## AICA Name Loop Exit To Group Tail Chain

The `0x8C15B92E` name-loop exit path now consumes the following `0x8C15B938` group-tail loop-back, descriptor-head aggregate, and opportunistic name/setup continuation. The non-exit name-loop path remains separate so its trace gates stay narrow.

The 50M probe `sa2-b92e-exit-group-chain-50m-20260611-223058` stayed at `PC=0x8C15C7F4`, kept `PVR registers=2255`, and still had no GD-ROM reads. CPU fast-forward batches dropped to `53,896`, and `0x8C15B938` dropped out of the top 40 profile entries. The next exposed SA2 AICA pressure is the slot-scan tail around `0x8C15B68C/0x8C15B68E` and the remaining setup triggers around `0x8C15B918`/`0x8C15C4DC`.

## AICA Name Zero-Mask Setup Chain

The `0x8C15B918` name-call path now has a zero-mask aggregate that continues through channel setup, the `0x8C15C572` descriptor-copy bridge, the `0x8C15C680` descriptor-copy helper, and the no-event epilogue when the retail global base pointer and slot transition match the existing zero-mask guards.

The 50M probe `sa2-name-zeromask-setup-50m-20260611-225538` stayed at `PC=0x8C15C7F4`, kept `PVR registers=2255`, and still had no GD-ROM reads. CPU fast-forward batches dropped to `49,607`; `0x8C15C4DC`, `0x8C15C572`, and `0x8C15C680` dropped out of the top profile. The remaining exposed triggers in this cluster are `0x8C15B918` and `0x8C15C57C`, plus the older slot tail around `0x8C15B68C/0x8C15B68E`.

## AICA Post-Setup Return To Name Chain

The standalone `0x8C15C57C` post-setup return aggregate now opportunistically continues through one following `0x8C15B92E` name-loop tail and next active setup when the restored frame matches the existing bounded name-loop chain.

The 50M probe `sa2-postsetup-name-chain-50m-20260611-232357` stayed at `PC=0x8C15C7F4`, kept `PVR registers=2255`, and still had no GD-ROM reads. CPU fast-forward batches dropped to `48,995`; `0x8C15B92E` fell from `7,349` to `5,512` profile hits. `0x8C15C57C` remains exposed for paths that cannot safely enter the next name-loop setup.

## AICA Name Loop Zero-Mask Tail Coverage

The zero-mask setup aggregate now explicitly requires a zero channel mask, so active masks fall through to the active setup aggregate and keep the deeper active descriptor/name-loop continuation. The `0x8C15B92E` name-loop tail also has a zero-mask continuation, including the post-setup return path that restores directly to the loop tail.

The 50M probe `sa2-postsetup-zeromask-chain-50m-20260611-233257` stayed at `PC=0x8C15C7F4`, kept `PVR registers=2255`, and still had no GD-ROM reads. CPU fast-forward batches and the top profile were unchanged at this checkpoint (`48,995` batches, `0x8C15B92E` still at `5,512` hits), so this is guarded coverage for paths not reached by the current 50M frontier rather than a visible speedup.

## IP.BIN Set-Bit To Zero-Tail Chain

The set-bit glyph draw prefix now uses extra budget to continue from the post-draw loop tail back into the following zero-bit dispatch when the remaining byte bits are clear. Exact-budget callers still stop at the old loop-tail boundary, while larger budgets can consume the trailing zero-bit span and land at the byte-exhausted path.

The 50M probe `sa2-ipbin-setbit-zero-chain-50m-20260611-233836` stayed at `PC=0x8C15C7F4`, kept `PVR registers=2255`, and still had no GD-ROM reads. CPU fast-forward batches dropped from `48,995` to `48,288`, and `0x8C0089F6` profile hits fell from `6,439` to `5,732`; `0x8C008A14` remains at `4,833` hits and is now the main IP.BIN glyph trigger left to fold.

## IP.BIN Dispatch To Set-Bit Chain

The glyph bit-dispatch aggregate now uses extra budget to consume the following set-bit draw prefix directly when the current bit is set. The shared set-bit core still preserves the exact-budget standalone `0x8C008A14` behavior, but dispatch callers can now draw the set pixel, run the loop tail, and consume a following zero-tail span in one bounded aggregate.

The 50M probe `sa2-ipbin-dispatch-setbit-chain-50m-20260612-001016` stayed at `PC=0x8C15C7F4`, kept `PVR registers=2255`, and still had no GD-ROM reads. CPU fast-forward batches dropped from `48,288` to `43,455`, and `0x8C008A14` dropped out of the top profile entirely. The leading IP.BIN glyph trigger is now `0x8C0089F6` at `5,732` hits, followed by the SA2 AICA name-loop and slot-scan clusters.
