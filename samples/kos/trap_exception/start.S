    .section .text
    .global _start

_start:
    mov.l vbr_value, r0
    ldc r0, vbr
    trapa #0x2a

after_trap:
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
scif_tx:
    .long 0xffe8000c
message_ptr:
    .long message

    .org 0x100
trap_handler:
    rte
    nop

message:
    .asciz "dcSharp raw trap probe done\n"
