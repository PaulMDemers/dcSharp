    .section .text
    .global _start

_start:
    mov.l vbr_value, r0
    ldc r0, vbr

    mov.l all_bits, r0
    mov.l asic_ack_a, r1
    mov.l r0, @r1

    mov.l maple_dma_bit, r0
    mov.l asic_irqb_a, r1
    mov.l r0, @r1

    mov.l dma_descriptor_physical, r0
    mov.l maple_dma_address, r1
    mov.l r0, @r1

    mov #1, r0
    mov.l maple_state, r1
    mov.l r0, @r1

wait_for_handler:
    mov.l handled_ptr, r1
    mov.l @r1, r0
    tst r0, r0
    bt wait_for_handler
    nop

    mov.l recorded_event_ptr, r1
    mov.l @r1, r0
    mov.l irqb_event, r2
    cmp/eq r2, r0
    bf print_failed

    mov.l receive_buffer_ptr, r1
    mov.b @r1, r0
    cmp/eq #5, r0
    bf print_failed

    mov.l message_ptr, r2
    bra print_message
    nop

print_failed:
    mov.l failed_message_ptr, r2

print_message:
    mov.l scif_tx, r1

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
maple_dma_bit:
    .long 0x00001000
irqb_event:
    .long 0x00000360
asic_ack_a:
    .long 0xa05f6900
asic_irqb_a:
    .long 0xa05f6920
maple_dma_address:
    .long 0xa05f6c04
maple_state:
    .long 0xa05f6c18
dma_descriptor_physical:
    .long dma_descriptor - 0x80000000
handled_ptr:
    .long handled
recorded_event_ptr:
    .long recorded_event
receive_buffer_ptr:
    .long receive_buffer
scif_tx:
    .long 0xffe8000c
message_ptr:
    .long message
failed_message_ptr:
    .long failed_message

    .org 0x600
interrupt_handler:
    mov.l intevt_for_handler, r1
    mov.l @r1, r0
    mov.l recorded_event_ptr_for_handler, r1
    mov.l r0, @r1

    mov.l maple_dma_bit_for_handler, r0
    mov.l asic_ack_a_for_handler, r1
    mov.l r0, @r1

    mov #0, r0
    mov.l asic_irqb_a_for_handler, r1
    mov.l r0, @r1

    mov #1, r0
    mov.l handled_ptr_for_handler, r1
    mov.l r0, @r1

    rte
    nop

    .align 2
intevt_for_handler:
    .long 0xff000028
recorded_event_ptr_for_handler:
    .long recorded_event
maple_dma_bit_for_handler:
    .long 0x00001000
asic_ack_a_for_handler:
    .long 0xa05f6900
asic_irqb_a_for_handler:
    .long 0xa05f6920
handled_ptr_for_handler:
    .long handled

    .align 5
dma_descriptor:
    .long 0x80000000
    .long receive_buffer - 0x80000000
    .long 0x00002001

    .align 5
receive_buffer:
    .space 128

handled:
    .long 0
recorded_event:
    .long 0

message:
    .asciz "dcSharp Maple IRQB accept probe done\n"
failed_message:
    .asciz "dcSharp Maple IRQB accept probe failed\n"
