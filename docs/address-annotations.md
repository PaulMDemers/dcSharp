# Address Annotations

Retail probes often expose raw Dreamcast addresses before we know enough to give the problem a subsystem name. dcSharp now has a small built-in known-address catalog to make those probe outputs easier to read.

## Address Classes

- `0x8C...`: SH-4 cached virtual addresses. These are usually guest code or system RAM data.
- `0x0C...`: physical/system RAM aliases used by loaders and memory diagnostics.
- `0xA05F....`: Dreamcast MMIO, including ASIC, G2 DMA, and PVR/TA registers.
- `0xA070....`: AICA register window.
- `0xA080....`: uncached AICA sound RAM alias.
- `opcode=0x....`: raw 16-bit SH-4 instruction word at the current PC.
- `PR=0x........`: SH-4 procedure return register, usually the return address for `jsr`/`bsr`.

## Where Labels Appear

Known labels supplement ELF symbols in:

- text run summaries and trace tails,
- JSON `DreamcastRunSummary` output,
- trace logs,
- FPU/FPSCR/CPU snapshot logs,
- PC profile logs,
- the desktop app summary and trace tab.

Example trace line:

```text
0x8C15B21A: 0x7E34  add #52,r14 ; SA2.G2DmaStatusSetFunction+0x1A [SA2 code]
```

Example JSON fields:

```json
{
  "pcHex": "0x8C15B21A",
  "knownAddress": {
    "name": "SA2.G2DmaStatusSetFunction",
    "category": "SA2 code",
    "display": "SA2.G2DmaStatusSetFunction+0x1A"
  }
}
```

## Extending The Catalog

Add entries in `src/DcSharp.Core/Execution/DreamcastKnownAddressCatalog.cs`.

Use:

- `Entry.Point(address, name, category, description)` for single globals, vectors, or exact trap points.
- `Entry.Range(start, endInclusive, name, category, description)` for functions, loops, helpers, tables, and MMIO blocks.

Naming guidance:

- Prefer `Title.Subsystem.Purpose`, for example `SA2.G2DmaStatusSetFunction`.
- Use categories like `SA2 code`, `SA2 data`, `MMIO`, `AICA RAM`, `Firmware`, or `IP.BIN code`.
- Keep descriptions behavior-oriented: what the guest code or hardware region does, not just where it lives.
- Add labels after a PC appears repeatedly in traces, docs, fast-forward guards, or compatibility probes.

This catalog is intentionally not a substitute for ELF symbols, hardware manuals, or a full disassembler database. It is a lightweight bridge from raw frontier addresses to names that make compatibility work easier to discuss and continue.
