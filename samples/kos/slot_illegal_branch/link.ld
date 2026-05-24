OUTPUT_ARCH(sh)
ENTRY(_start)

SECTIONS
{
    . = 0x8c010000;
    .text : { *(.text*) }
    .rodata : { *(.rodata*) }
}
