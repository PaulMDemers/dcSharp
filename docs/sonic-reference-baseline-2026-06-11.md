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

## IP.BIN Byte-Fetch To Dispatch Chain

The glyph byte-fetch prefix at `0x8C0089DC-0x8C0089F4` now fast-forwards through the pointer increment, source-byte cache update, bit-count reset, and loop-tail branch back into the existing dispatch aggregate. The dispatch helper also has an entry-mode path so the prefix can continue through set-bit and zero-bit chains without interpreting the next `0x8C0089F6` trigger.

The 50M probe `sa2-ipbin-bytefetch-chain-50m-20260612-015122` stayed at `PC=0x8C15C7F4`, kept `PVR registers=2255`, and still had no GD-ROM reads. CPU fast-forward rose from `48,760,250` to `48,784,454`; `0x8C0089F6` profile hits fell from `5,732` to `4,249`, and the byte-fetch body `0x8C0089DE-0x8C0089F4` plus the loop-tail redispatch `0x8C008A70-0x8C008A74` dropped out of the top profile. The remaining `0x8C0089DC` count is the single trigger instruction for each fetched glyph byte.

## IP.BIN Byte-Exit To Fetch Chain

The byte-exhausted glyph tail at `0x8C008A76-0x8C008A88` now fast-forwards the geometry limit compare and branch back to the byte-fetch prefix when the current glyph cell index is still inside the `width * height` limit. With enough budget it can continue through the next byte-fetch and dispatch chain, while exact-budget callers still stop at the next byte-fetch trigger.

The 50M probe `sa2-ipbin-byteexit-chain-50m-20260612-015550` stayed at `PC=0x8C15C7F4`, kept `PVR registers=2255`, and still had no GD-ROM reads. CPU fast-forward rose from `48,784,454` to `48,799,284`; `0x8C008A78-0x8C008A88` and `0x8C0089DC` dropped out of the top profile, leaving `0x8C008A76` as the single byte-exit trigger at `1,530` hits.

## AICA Descriptor No-Event Chain

The `0x8C15BED8` descriptor-update prologue can now opportunistically continue through the setup, pointer advance, second/third descriptor word, and counter-return helpers on the observed no-event path. The aggregate ratchets forward: if a later descriptor guard does not match, it returns the already-applied safe prefix and lets normal execution resume at the next instruction boundary.

The 50M probe `sa2-aica-descriptor-aggregate-50m-20260612-114807` stayed at `PC=0x8C15C7F4`, kept `PVR registers=2255`, and still had no GD-ROM reads. CPU fast-forward rose from `48,799,284` to `48,802,350`, and CPU fast-forward batches dropped from `43,455` to `42,227`; the middle descriptor triggers `0x8C15BF1C`, `0x8C15BF72`, and `0x8C15BFC2` dropped out of the top profile. The remaining exposed descriptor pressure is the `0x8C15BED8` aggregate trigger plus `0x8C15C08C` for paths that reach the counter-return helper directly.

## G2 PIO Read Helper Prologue Coverage

The `0x8C1356D8` G2 PIO read helper prologue can now bridge directly into the existing read-word or external-read helper fast-forwards when the body is fully eligible. It refuses ineligible bodies rather than consuming only the prologue, preserving the larger helper batches.

The 50M probe `sa2-g2-pio-read-prologue-tight-50m-20260612-115530` stayed at `PC=0x8C15C7F4`, kept `PVR registers=2255`, and still had no GD-ROM reads. CPU fast-forward rose slightly from `48,802,350` to `48,802,386`, while CPU fast-forward batches stayed flat at `42,227`; this is narrow guarded coverage rather than a visible compatibility frontier move.

## AICA Register Pair Read Wrapper

The `0x8C110A08` wrapper now folds its two-word AICA register read through the G2 PIO helper, combines the high half of `0xA0710000` with the low half of `0xA0710004`, and restores directly to the caller. The shortcut is signature-gated against the wrapper and G2 helper bodies and is disabled under memory watches.

The 50M probe `sa2-aica-register-pair-wrapper-50m-20260612-123310` stayed at `PC=0x8C15C7F4`, kept `PVR registers=2255`, and still had no GD-ROM reads. CPU fast-forward rose from `48,802,386` to `49,080,818`, and CPU fast-forward batches dropped from `42,227` to `39,763`. The aggregate entry `0x8C110A08` remains visible as the call trigger, while the body now collapses enough to expose the next clusters around `0x8C110938`, `0x8C10FE8E`, `0x8C14D15A`, and the remaining AICA name/descriptor triggers.

## AICA Register Pair Pointer Loop

The `0x8C11092A` pointer loop now fast-forwards repeated calls into the AICA register-pair wrapper when the output pointer span is contiguous and the retail wrapper/helper signatures match. Each folded iteration writes the combined AICA register value and preserves the helper counters, then exits at the existing post-loop compare boundary.

The 50M probe `sa2-aica-register-pointer-loop-50m-20260612-124750` stayed at `PC=0x8C15C7F4`, kept `PVR registers=2255`, and still had no GD-ROM reads. CPU fast-forward rose from `49,080,818` to `49,091,870`, and CPU fast-forward batches dropped from `39,763` to `39,149`. The `0x8C110938/0x8C11093A` pointer-loop compare/branch cluster dropped out of the top profile; the next exposed clusters remain the interrupt callback scan around `0x8C14D15A`, the SR-protected callback dispatch around `0x8C10FE8E`, and the AICA name/slot/descriptor triggers.
