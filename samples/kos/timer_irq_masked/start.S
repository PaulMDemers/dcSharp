    .section .text
    .global _start

_start:
    mov.l vbr_value, r0
    ldc r0, vbr

    mov.l sr_masked, r0
    ldc r0, sr

    mov.l timer_priority, r0
    mov.l ipra, r1
    mov.l r0, @r1

    mov.l timer_pending, r0
    mov.l tcr0, r1
    mov.l r0, @r1

    mov.l scif_tx, r1
    mov.l message_ptr, r2

write_loop:
    mov.b @r2+, r0
    cmp/eq #0, r0
    bt done
    mov.b r0, @r1
    bra write_loop
    nop

done:
    .word 0xffff

    .align 2
vbr_value:
    .long _start
sr_masked:
    .long 0x000000f0
timer_priority:
    .long 0x0000a000
timer_pending:
    .long 0x00000120
ipra:
    .long 0xffd00004
tcr0:
    .long 0xffd80010
scif_tx:
    .long 0xffe8000c
message_ptr:
    .long message

message:
    .asciz "dcSharp timer IRQ masked probe done\n"
