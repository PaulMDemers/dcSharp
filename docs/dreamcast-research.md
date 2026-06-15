# Dreamcast Research Notes

## Hardware Shape

The Dreamcast is centered on a little-endian Hitachi/Renesas SH-4 CPU at 200 MHz, with 16 MB system RAM, 8 MB video RAM, and 2 MB AICA audio RAM. The graphics path is NEC PowerVR2/CLX2 through Holly, with a tile accelerator and tile-based deferred rendering rather than a simple immediate framebuffer GPU. Audio is Yamaha AICA, including an ARM7-side processor and 64-channel PCM/ADPCM playback.

Primary bring-up subsystems:

- SH-4 interpreter: integer ops, branches, delay slots, status registers, FPU, exceptions, cache/MMU later.
- Memory map and buses: system RAM, boot ROM/flash hooks, PVR/Holly registers, AICA, Maple, GD-ROM/G1/G2 paths.
- PVR path: start with framebuffer/register visibility, then tile accelerator command parsing, texture memory, ISP/TSP behavior.
- AICA path: start with MMIO/register traces and silence-safe audio, then ARM7/AICA memory, channels, DMA, interrupts.
- Maple: controller discovery, input polling, VMU state, hotplug semantics.
- GD-ROM/media: GDI/CDI/CHD/CUE parsing policy, sector reads, BIOS/syscall boundaries.

## SDK And Fixture Strategy

KallistiOS is the best first fixture source because it is actively used, permissively licensed, and ships examples. The official path is to build or install a `sh-elf` toolchain, source `environ.sh`, and build KOS examples. The current WSL setup uses a prebuilt GCC 15.1.0 + KallistiOS 2.2.1 package because this machine's WSL apt path requires a sudo password for missing source-build packages.

Fixture ladder:

1. Synthetic ELF/header fixtures in C# tests.
2. Tiny KOS samples from `samples/kos`.
3. Focused KOS probes for CPU exceptions, timers, Maple input, framebuffer writes, DMA, and PVR commands.
4. Public redistributable demos/utilities, inventoried outside git unless source-licensed.
5. User-provided retail firmware/media only as local ignored artifacts.

## Reference Emulators And Tools

Use mature projects to learn what to measure, not to copy implementation into this codebase:

- Flycast: active GPL-2.0 Dreamcast/Naomi/Atomiswave emulator; useful for architecture, compatibility behavior, and automated reference runs.
- lxdream/lxdream-nitro: older GPL-2.0 C emulator family, useful for simple homebrew/dev-console behaviors.
- WashingtonDC, MAME, Deecy: useful cross-checks when behavior differs or a subsystem needs a second opinion.
- Redream/Demul/nullDC: useful as black-box behavior references, with source availability varying.

## Legal Boundaries

Do not commit commercial games, BIOS/flash dumps, proprietary SDK files, extracted assets, generated captures from commercial software, or local emulator binaries. Keep those in ignored `artifacts/` or another local-only path. When a behavior comes from a reference run, record whether it is documented, observed, or inferred.

## Useful Sources

- [KallistiOS GitHub](https://github.com/KallistiOS/KallistiOS)
- [KallistiOS documentation](https://kos-docs.dreamcast.wiki/)
- [Getting started with Dreamcast development](https://dreamcast.wiki/Getting_Started_with_Dreamcast_development)
- [DreamSDK for Windows](https://github.com/dreamsdk/dreamsdk)
- [Prebuilt Dreamcast toolchain release](https://github.com/drpaneas/dreamcast-toolchain-builds/releases/tag/gcc15.1.0-kos2.2.1)
- [Dreamcast hardware overview](https://dreamcast.wiki/Hardware_overview)
- [Dreamcast official documentation index](https://segaretro.org/Dreamcast_official_documentation)
- [Marcus Comstedt Dreamcast programming notes](https://mc.pp.se/dc/)
- [SH-4 CPU notes](https://mc.pp.se/dc/cpu.html)
- [PowerVR notes](https://mc.pp.se/dc/pvr.html)
- [Dreamcast emulator list](https://dreamcast.wiki/Dreamcast_emulators)
- [Flycast source](https://github.com/flyinghead/flycast)
- [lxdream source](https://github.com/lxdream/lxdream)

See also [Dreamcast Development Map](dreamcast-development-map.md) for the current subsystem build plan and development workflow.
