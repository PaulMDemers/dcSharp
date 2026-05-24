    .section .text
    .global _start

_start:
    mov.l vbr_value, r0
    ldc r0, vbr

    mov.l all_bits, r0
    mov.l asic_ack_a, r1
    mov.l r0, @r1

    mov #0, r0
    mov.l asic_irqd_a, r1
    mov.l r0, @r1

    mov.l vblank_bit, r0
    mov.l asic_irq9_a, r1
    mov.l r0, @r1

wait_for_handler:
    mov.l handled_ptr, r1
    mov.l @r1, r0
    tst r0, r0
    bt wait_for_handler
    nop

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
all_bits:
    .long 0xffffffff
vblank_bit:
    .long 0x00000008
asic_ack_a:
    .long 0xa05f6900
asic_irqd_a:
    .long 0xa05f6910
asic_irq9_a:
    .long 0xa05f6930
handled_ptr:
    .long handled
scif_tx:
    .long 0xffe8000c
message_ptr:
    .long message

    .org 0x600
interrupt_handler:
    mov #16, r3

pending_window:
    dt r3
    bf pending_window

    mov.l vblank_bit_for_handler, r0
    mov.l asic_ack_a_for_handler, r1
    mov.l r0, @r1

    mov #0, r0
    mov.l asic_irq9_a_for_handler, r1
    mov.l r0, @r1

    mov #1, r0
    mov.l handled_ptr_for_handler, r1
    mov.l r0, @r1

    rte
    nop

vblank_bit_for_handler:
    .long 0x00000008
asic_ack_a_for_handler:
    .long 0xa05f6900
asic_irq9_a_for_handler:
    .long 0xa05f6930
handled_ptr_for_handler:
    .long handled

handled:
    .long 0

message:
    .asciz "dcSharp interrupt nesting probe done\n"
