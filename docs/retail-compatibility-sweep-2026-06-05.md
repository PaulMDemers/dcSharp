# Retail Compatibility Sweep - 2026-06-05

This sweep covers the local user-provided retail images under `retail_discs/`.
The disc images themselves are not repository artifacts.

## Method

- Built Release binaries with `dotnet build -c Release`.
- Ran broad smoke probes with:

```powershell
dotnet src\DcSharp.Cli\bin\Release\net10.0\DcSharp.Cli.dll media boot-smoke <descriptor> --scan-sectors 1024 --instructions 50000000 --trace-tail 0 --stop-on-unmapped --json
```

- Preferred GDI descriptors when available, then reran CUE fallbacks for GDI extraction failures. After the high-density GDI ISO-volume fix, the five formerly failing GDI descriptors were rerun from GDI.
- Captured focused trace tails for the distinct failure classes.

## Summary

- 11 retail titles were probed.
- 7 titles reached the 50M instruction cap without an unsupported opcode or unmapped stop.
- 0 titles in the current preferred-descriptor matrix stop on concrete unmapped device reads.
- 3 titles stopped on bad/unsupported execution after boot handoff or firmware callback behavior.
- 1 title exited through the BIOS CD-menu path.
- The GDI extraction blocker is fixed for the five affected descriptors. They now extract `1ST_READ.BIN` from the later high-density ISO9660 volume rather than the low-density session volume.

The generic smoke path did not observe GD-ROM sector reads or TA command writes for this new batch. That makes the next broad-compatibility work less about rendering first and more about remaining bootstrap layout selection and firmware callback/table state.

## Results

| Title | Descriptor Used | 50M Result | Frontier |
| --- | --- | --- | --- |
| Crazy Taxi (USA) | CUE | `InstructionLimit`, `PC=0xAC0047DA` | Fixed missing SH-4 `ldc r0,ssr`, then modeled the area-0 mirror/external-device read at `0xA30100C0`. Current frontier is a long cached-bootstrap loop polling SCIF status. |
| Dead or Alive 2 (USA) | CUE | `InstructionLimit`, `PC=0x8C129E4A` | Generic smoke reaches the budget; existing title-specific probes remain the deeper rendering path. |
| Gauntlet Legends (USA) | GDI | `InstructionLimit`, `PC=0x8C037A3A` | GDI extraction now succeeds from volume `GAUNT1`, boot extent `0x8595B`, 3,761,284 bytes. |
| The Grinch (USA) | GDI | `FirmwareExit`, `PC=0x8C0000E8` after 1,386,943 instructions | GDI extraction now succeeds from volume `GRINCH`; still requests BIOS CD menu, `function=2`. |
| Legacy of Kain - Soul Reaver (USA) | CUE | `InstructionLimit`, `PC=0x8C038934` | Reaches generic execution budget. |
| Rayman 2 - The Great Escape (USA) | CUE | `InstructionLimit`, `PC=0x8C0D18B0` | Reaches generic execution budget. |
| Sega Rally 2 (USA) | CUE | `InstructionLimit`, `PC=0x8C017968` at 50M instructions | WinCE `0WINCEOS.BIN` now follows the GD-ROM payload mapping: the first `0x800` bytes are skipped from the runtime payload, and the remaining image is loaded at `0x8C010000`. Sega Rally reaches real WinCE startup code, executes `ldtlb`, installs a low-virtual RAM mapping, handles the observed banked-register `ldc`, dispatches the first `WIN32.PerformCallBack` thunk, and now reaches the generic 50M budget inside a repeating WinCE scheduler/list-management path rather than faulting. Guarded WinCE scheduler fast-forwards collapse the stable timer-delta helper and scheduler return tail, so instruction-limit checkpoints can land a few opcodes earlier in the same loop than pre-fast-forward traces. |
| Sonic Adventure (USA, Rev A) | GDI | `UnsupportedInstruction` at `PC=0x8C000000` after 46,589,874 instructions | GDI extraction now succeeds from volume `SONIC_ADV`; unlike the CUE fallback, the GDI path exposes a zero-PC firmware/callback-style frontier. |
| Sonic Adventure 2 (USA) | CUE | `UnsupportedInstruction` at `PC=0x8C000000` after 44,590,162 instructions | Guest jumps through pointer `0x8C17EC60`, which currently contains zero; likely firmware callback/table state. |
| Sonic Shuffle (USA) | GDI | `InstructionLimit`, `PC=0x8C02A0C0` | GDI extraction now succeeds from volume `SONIC_SHUFFLE`; the CUE fallback now moves past modem and AICA RTC reads to a zero-PC firmware/callback-style frontier at 12,139,952 instructions. |
| Wetrix+ (USA) | GDI | `InstructionLimit`, `PC=0x8C16652C` | GDI extraction now succeeds from volume `WETRIXPLUS`, boot extent `0x85DB7`, 1,475,872 bytes. |

## Focused Findings

### SH-4 Control Register Decode

Crazy Taxi stopped at `0xAC004018` on opcode `0x403E` while executing cached bootstrap code:

```text
stc sr,r0
and r1,r0
ldc r0,ssr
```

The SH-4 control-register group includes saved status/program-counter transfers (`SSR`/`SPC`) alongside `SR`, `GBR`, and `VBR`. Implementing `ldc Rn,SSR`, `ldc Rn,SPC`, `stc SSR,Rn`, and `stc SPC,Rn` moves Crazy Taxi to the next device access frontier.

### GDI Extraction Gap

`media inspect` found Dreamcast boot sectors and `1ST_READ.BIN` names in the affected GDI images, but `boot-smoke` originally could not extract the boot file. The cause was ISO9660 volume selection: retail GDI dumps can contain a valid low-density session ISO without the game boot binary plus a later high-density ISO volume that actually contains `1ST_READ.BIN`. `Iso9660FileSystem` now tries later data-track volumes first.

Fixed GDI descriptors:

- Gauntlet Legends
- The Grinch
- Sonic Adventure
- Sonic Shuffle
- Wetrix+

### Device Frontiers

The first device-frontier pass retired the unmapped stops that were blocking Crazy Taxi and the Sonic Shuffle CUE fallback:

- Crazy Taxi: `0xA30100C0` normalizes through the documented `0x02000000-0x03FFFFFF` area-0 mirror to the `0x01000000-0x01FFFFFF` external-device window. Absent external devices now read as zero and log under the `external` domain.
- Sonic Shuffle CUE fallback: `0xA0600004` is in the `0x00600000-0x006007FF` modem window. Absent modem reads now return zero and log under the `modem` domain.
- Sonic Shuffle CUE fallback then reached `0xA0710000`, the AICA RTC control window. The AICA aperture now includes `0x00710000-0x0071000B`.

These changes make the current frontiers more specific: Crazy Taxi is waiting in cached bootstrap/SCIF code, and Sonic Shuffle CUE joins the zero-PC firmware/callback family instead of stopping on an unmapped read.

### WinCE Bootstrap Frontier

Sega Rally 2 uses `0WINCEOS.BIN` rather than the normal `1ST_READ.BIN` path. The analyzer now recognizes its WinCE-style header in the original layout:

- Header load address field: `0x0C010000`
- Entry offset field: `0x800`
- GD-ROM payload offset: `0x800`
- Suggested entry point: `0x8C010000`
- Entry trampoline target: `0x8C0120E0` / file offset `0x28E0`

The GD-ROM loader behavior matches the WinCE REIOS path used by Flycast: the header-sized first `0x800` bytes are parked separately, while the payload beginning at file offset `0x800` is loaded at `0x8C010000`. Running from IP.BIN now reaches the WinCE entry stub at the corrected payload base:

```text
0x8C010004: 0xD001  mov.l @(0x01,pc),r0 ; [0x8C01000C]=0x8C0120E0
0x8C010008: 0x402B  jmp @r0 ; target=0x8C0120E0
```

With the corrected file-offset mapping, target `0x8C0120E0` corresponds to file offset `0x28E0`, where executable startup code begins. The next observed WinCE bootstrap sequence writes `PTEH=0x00005800` and `PTEL=0x0C13194A`, executes `ldtlb`, and fetches through the resulting low virtual page (`0x00005B90 -> 0x0C131B90`). TLB entries now honor the SH-4 page-size bits, and SH-4 banked-register `ldc r14,r4_bank` is decoded for the following context setup.

The first odd negative thunk is `0xFFFFFD1F`, which decodes as `WIN32.PerformCallBack`. The observed `CALLBACKINFO` block at `0x8C137538` contains `hProc=0x0CEEEFE2`, `pfn=0x8C021FA0`, and `pvArg0=0x8C0116E0`; the preserved call context is `r5=0x8CEEE654`, `r6=1`, and `r7=0x8C137534`. A narrow WinCE HLE path now transfers control to that callback with `pvArg0` as the first argument and leaves `PR` pointing back to the original caller. The callback string at `0x8C0116E0` is the wide module/API sequence beginning with `coredll.dll`, `ExitThread`, `PSLNotify`, and `IsExiting`, so the callback is in the WinCE core module lookup path rather than the game-media path.

The next loader helper builds a runtime section table at `0x02010000`. With MMU address translation enabled, low virtual writes are now backed separately from the no-MMU area-0 external mirror, and the WinCE section table is matched back to the original loaded section descriptors. That lets the first mapped module entrypoint `0x01E324DC` read executable bytes from its loaded source at `0x8C0334DC` instead of decoding zero. A 12M Sega Rally probe now runs through that entrypoint and reaches the instruction budget back in the WinCE scheduler/timer path at `PC=0x8C017A02`, with no GD-ROM reads or TA work yet. A 25M enhanced scheduler snapshot confirms the module/list-root object is live at `0x8CEEE5F4`, records the mapped entrypoint at object offset `+0x5C`, and leaves the current-thread object live at `0x8CEEEE9C`, but the timer wheel slots remain empty and the scheduler returns through the same `0x8C0176A8-0x8C017A68` list-management loop. The second observed WinCE thunk is `CURPROC.Unused4` at `0xFFFFF9F9`, so the default zero return is treated as a named null-slot call rather than a likely missing HLE behavior. A caller trace shows the mapped entrypoint returns `r0=1`; the parent at `0x8C023416` branches through its nonzero-result path, reads the current-thread object field at `+0x48`, sees zero, and jumps toward `0x8C023690` before dropping into the scheduler. The `+0x48` field is derived from the nested source object at `0x8C1376C0 + 0x1C`. A focused 9.1M initializer trace shows the broad `0x8C02EBCA` write is just the earlier arena clear, while `0x8C01DD76` is the structured object initializer storing `r2=0` to `+0x1C`; the next frontier is therefore the initializer/caller inputs that decide whether that scheduler link should remain zero, not the null-slot syscall itself. A follow-up `--memory-write-changed-only` trace confirms the continuation calls are ordinary module/thread setup: `0x8C020B98` fills section metadata, `0x8C013F08` seeds current-thread fields, `0x8C01D654` clears the nested list object, `0x8C016584` queues the current thread in timer-wheel slot 1 with delta 1, and `0x8C017960` legitimately removes it before rolling `current-wait-delta` to 7 with every wheel slot empty. The guarded fast-forward path preserves the actual TMU counter read at `0x8C02DB32`, then skips only exact opcode/state-matched post-read helper and scheduler-return tails.

Use `--wince-syscall-log` on longer probes to capture odd negative WinCE API thunks without a broad trace. Use `--wince-scheduler-log` on the same runs to capture the compact final scheduler/KData field snapshot without disabling fast-forwards. The scheduler log now also captures the observed WinCE high-RAM allocation pages and low virtual module metadata page, then follows the current-thread, module/list-root, and captured nested object pointers to dump nonzero object words plus named key fields even when those fields are zero. A 25M scheduler snapshot stops at `PC=0x8C017A68` with `current-thread-object=0x8CEEEE9C`, `module-or-file-list-root=0x8CEEE5F4`, `runqueue-or-thread-list-next=1`, `scheduler-wait-active-flag=0`, `current-wait-delta=7`, `timer-wheel-max-delta=7`, `kernel-tick-total=1275`, and all eight timer-wheel slots empty; the followed module object contains `+0x50=0x03E30000`, `+0x5C=0x01E324DC`, `+0x78=0x01E30000`, `+0x7C=0x00021000`, and `+0xB0=0x03E4C0F8`. The nested current-thread source object at `0x8C1376C0` contains `+0x10=0x8CEEEE9C`, `+0x18=0x8C010000`, `+0x20=0x8C011924`, `+0x24=0x1F`, and section/module-like metadata at `+0x48..+0x64`, but `+0x1C` stays zero and therefore propagates to current-thread `+0x48`. A narrow trace of `0x8C02E228-0x8C02E248` shows the tick handler acknowledging TMU status at `0xFFD80010`, adding `25` to `kernel-tick-total` at `0x8C131888`, and writing `25` to `kernel-tick-delta` at `0x8C13188C`. Another trace of `0x8C016584-0x8C0165E8` and `0x8C0179B0-0x8C0179CC` shows one early object inserted into timer-wheel slot 1 (`0x8C136550/554`), removed at `0x8C017982/98A`, and then the scheduler repeatedly scanning empty slots 1 through 7 before preserving `current-wait-delta=7`. A changed-only version of that probe reduces the watched writes from a saturated 2048 rows to 34 real state changes and confirms that the slot-1 queue head/tail are reset to zero because the removed object has no next link. A changed-only plus distinct structural watch reduces the 12.5M scheduler write set to 11 rows: the wait-active flag toggles at `0x8C017900/0x8C017A0C`, slot 1 is cleared, the current object flag is demoted from `0x111` to `0x11`, `current-wait-delta` becomes `7`, and the scheduler pending tick delta at `0x8C1364F4` briefly becomes `0x19` at `0x8C01779C` before being cleared at `0x8C0178B4`. A focused `#9217214-#9217251` trace confirms `0x00005BC0` reads `kernel-tick-delta` from `0x8C13188C`, zeros that source, returns `0x19` in `r0`, and the scheduler adds that returned delta to `0x8C1364F4` before draining it through the empty `0x8C1365AC` list path. A branch probe with a synthetic nonzero `0x8C1365AC` entry decodes that path as a sorted scheduler-expired-list walk: if entry `+0x28` exceeds pending ticks, the scheduler subtracts the pending delta in place; otherwise it subtracts the entry delta from `0x8C1364F4`, removes the head through entry `+0x2C`, clears entry flag bit `0x0800`, and requeues callback-less entries through `0x8C016584` into the timer wheel. Exact write watches show `0x8C1365AC` is only touched by the `0x8C02EBDA` zero-fill in the 12.5M window. The live `0x8C01AAA8 -> 0x8C01633C` path resolves `0x0CEEE42A` to `0x8CEEE448`, queues `0x8C137554` into that object's bucket/priority `1`, marks current-thread flag `0x0200`, clears `0x8C136544`, and returns `0x102`; it is not the expired-list producer. A 50M follow-up records no `0x8C01B160-0x8C01B240` hits, no nonzero `0x8C1365AC` writes, and the same scheduler loop with `scheduler-wait-active-flag=1`, all timer-wheel slots empty, and `scheduler-expired-list-head=0`. The same run confirms that enabling memory-read watches can re-expose the IP.BIN `0x8C00909A` ASIC wait because that fast-forward is deliberately diagnostics-gated; without read/write watches, the wait is collapsed and the stable hot path remains the WinCE scheduler dispatch loop. A 9.3M callback-body trace shows the callback string walk probing the root at `0x8C131B24`, allocating a block at `0x8CEEE5F4` when that root is zero, returning `2` from `pfn=0x8C021FA0`, and then marking the current object with `0x200` while clearing `next-wait-delta`. The next trace target is another wake/runnable producer or the caller that should leave the current-thread object runnable, not the now-decoded priority-bucket helper or the unhit `0x8C01B160` literal user. A narrower attempted fast-forward of the `0x8C017780` empty-list/low-helper return path was measured and rejected: it skipped more guest instructions but added enough guard/write overhead to make the 50M wall-clock probe slower.

A changed-only current-thread/object write trace confirms the final `0x0211` current-thread flags are guest-driven rather than a stale queue-removal bug: initialization writes `0x0011`, timer-wheel enqueue at `0x8C0165D0` raises it to `0x0111`, scheduler removal at `0x8C0179A6` lowers it back to `0x0011`, and `0x8C01AB18` raises it to `0x0211` after the priority-bucket insert. The mapped module caller at `0x01E324DC-0x01E324FA` directly tests the return from `CURPROC.Unused4`: with the current zero return it takes the `bt 0x01E32586` path, converts the result to `r0=1`, and returns to `0x8C020660`. The parent then reads `current-thread +0x48` at `0x8CEEEEE4`; because that field remains zero it exits toward `0x8C023690` and the scheduler. A temporary nonzero-return experiment was measured and rejected; it exposes three subsequent `WIN32.CreateCrit` (`0xFFFFFD65`) calls that initialize critical-section objects at `0x01E4C0C0`, `0x01E4C014`, and `0x01E4C060`, but still reaches the same 50M scheduler/tick loop with no GD-ROM reads or TA work.

A one-shot `--memory-poke-pc` diagnostic now supports controlled branch experiments without patching emulator code. Forcing `0x8C1376DC` nonzero just before `0x8C017B7E` decodes the hidden constructor branch: it calls `0x8C017AC4`, receives `0x8CEEEE08`, stores that into `current-thread +0x48`, and zeroes fields at offsets `+0x00`, `+0x08`, and `+0x14` in the allocated object. The next parent path then passes the null test at `0x8C02343E-0x8C023442`, but compares region ids derived from source `+0x0C=0x02000000` and source `+0x20=0x8C011924`; after `shld #-25` those are `1` and `0x46`, so the parent takes `bf 0x8C023506` and returns to the scheduler. A second synthetic run that also forces source `+0x0C` to `0x8C010000` reaches a later scheduler loop and increases boot-binary writes from roughly 101 KiB to roughly 245 KiB in the short probe, but still records no GD-ROM reads or TA work. That makes the next real target the descriptor producer/loader semantics for the `0x02000000` low-virtual source field and the runnable/module state after the region-matched path, not a permanent forced nonzero `+0x1C` fix.

A no-poke producer trace narrows that descriptor diagnosis further: `0x8C01DD56-0x8C01DD58` computes `source +0x0C` as `(source byte 0 + 1) << 25`, so `0x02000000` is a deliberate guest descriptor value rather than a host translation artifact. The same initializer writes literal `0x8C011924` to `source +0x20`, hard-zeroes `source +0x1C`, and later computes `source +0x38 = source +0x0C + source +0x18 = 0x8E010000`. `--wince-scheduler-log` now reports this descriptor summary directly, including the region mismatch, derived-base match, and null copy-source field.

The region-matched synthetic branch has now been followed into its later steady state. With both `source +0x1C` and `source +0x0C=0x8C010000` poked before `0x8C017B7E`, Sega Rally reaches the context-switch/tick loop around `0x8C0123AE`, `0x8C0178EC`, `0x8C0179FC`, and `0x8C02DB24`. The final snapshot shows `current-thread-object=0x8CEEEE9C`, `kernel-tick-total` advancing, `scheduler-tail-state` advancing, wait-active set, empty timer-wheel slots, and no GD-ROM reads or TA work. The log now labels the transient dispatch/list words at `0x8C131AA0` and `0x8C131B20`, and suppresses `0x00C0C0C0` arena-fill objects that can otherwise resemble descriptors.

A producer/consumer trace splits that state from the actual missing queue source. `0x8C131AA0=0x8C012296` is installed once during cached kernel table setup at `0xAC013456`; the steady scheduler loop instead repeatedly reads `0x8C131AA4` and `0x8C131AA8`, both still zero. `0x8C131B20=0x8C136410` is initialized as part of a larger module/region record at `0x8C131B00` by `0x8C0295D0-0x8C0295F2`. A real no-poke consumer trace shows the module/list root is not globally missing: `0x8C022F4C` writes `0x8C131B24=0x8CEEE5F4`, and `0x8C01E5FA` later walks that root through the live module object containing the mapped entrypoint metadata. The corrected frontier is therefore the real producer/meaning of `0x8C131AA4/AA8`, plus the reason the synthetic region-matched path reaches a later loop with the module root absent, not the setup-only vector entry at `0x8C131AA0`.

### Bootstrap/Firmware Frontiers

- Sonic Adventure 2 jumps through a zero callback/table pointer to `0x8C000000`, which points at missing firmware initialization state or a callback-registration side effect.
- Sonic Adventure now does the same from the fixed GDI path, while the CUE fallback reaches the instruction budget. Compare CUE/GDI IP.BIN work-area and high-density media state before treating it as a CPU issue.
- Sonic Shuffle CUE now joins this family after modem/AICA RTC modeling, while Sonic Shuffle GDI reaches the instruction budget.
- The Grinch asks for the BIOS CD menu with system function `2`, which usually means the disc/authentication/media path did not satisfy the title's boot expectation.

## Next Work

1. Trace the real writer or state machine that should populate Sega Rally's `0x8C131AA4/AA8` dispatch state, and compare the real module-root write at `0x8C022F4C` against the synthetic region-matched route where `0x8C131B24` ends absent; the controlled `+0x1C`/region pokes expose later gates but are not fixes.
2. Trace Sonic Adventure, Sonic Adventure 2, and Sonic Shuffle CUE around their zero-PC firmware/callback frontiers and compare GDI versus CUE work-area state.
3. Re-run the full sweep after each fix and keep this report as the baseline.
