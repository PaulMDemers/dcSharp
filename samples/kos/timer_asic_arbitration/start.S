    .section .text
    .global _start

_start:
    mov.l vbr_value, r0
    ldc r0, vbr

    mov.l sr_masked, r0
    ldc r0, sr

    mov.l all_bits, r0
    mov.l asic_ack_a, r1
    mov.l r0, @r1

    mov #0, r0
    mov.l asic_irqd_a, r1
    mov.l r0, @r1

    mov.l timer_priority, r0
    mov.l ipra, r1
    mov.l r0, @r1

    mov.l timer_pending, r0
    mov.l tcr0, r1
    mov.l r0, @r1

    mov.l vblank_bit, r0
    mov.l asic_irq9_a, r1
    mov.l r0, @r1

wait_for_vblank:
    mov.l asic_ack_a, r1
    mov.l @r1, r0
    mov.l vblank_bit, r2
    and r2, r0
    tst r0, r0
    bt wait_for_vblank
    nop

    mov #0, r0
    ldc r0, sr

wait_for_handlers:
    mov.l done_ptr, r1
    mov.l @r1, r0
    tst r0, r0
    bt wait_for_handlers
    nop

    mov.l first_event_ptr, r2
    mov.l @r2, r0
    mov.l timer_event, r1
    cmp/eq r1, r0
    bf print_failed

    mov.l second_event_ptr, r2
    mov.l @r2, r0
    mov.l asic_event, r1
    cmp/eq r1, r0
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
sr_masked:
    .long 0x000000f0
all_bits:
    .long 0xffffffff
vblank_bit:
    .long 0x00000008
timer_priority:
    .long 0x0000a000
timer_pending:
    .long 0x00000120
timer_enabled:
    .long 0x00000020
timer_event:
    .long 0x00000400
asic_event:
    .long 0x00000320
ipra:
    .long 0xffd00004
tcr0:
    .long 0xffd80010
intevt:
    .long 0xff000028
asic_ack_a:
    .long 0xa05f6900
asic_irqd_a:
    .long 0xa05f6910
asic_irq9_a:
    .long 0xa05f6930
done_ptr:
    .long handlers_done
first_event_ptr:
    .long first_event
second_event_ptr:
    .long second_event
scif_tx:
    .long 0xffe8000c
message_ptr:
    .long message
failed_message_ptr:
    .long failed_message

    .org 0x600
interrupt_handler:
    mov.l handler_count_ptr, r1
    mov.l @r1, r0
    tst r0, r0
    bf second_interrupt

    mov.l intevt_for_handler, r1
    mov.l @r1, r0
    mov.l first_event_ptr_for_handler, r1
    mov.l r0, @r1

    mov.l timer_enabled_for_handler, r0
    mov.l tcr0_for_handler, r1
    mov.l r0, @r1

    mov #1, r0
    mov.l handler_count_ptr, r1
    mov.l r0, @r1

    rte
    nop

second_interrupt:
    mov.l intevt_for_handler, r1
    mov.l @r1, r0
    mov.l second_event_ptr_for_handler, r1
    mov.l r0, @r1

    mov.l vblank_bit_for_handler, r0
    mov.l asic_ack_a_for_handler, r1
    mov.l r0, @r1

    mov #0, r0
    mov.l asic_irq9_a_for_handler, r1
    mov.l r0, @r1

    mov #1, r0
    mov.l done_ptr_for_handler, r1
    mov.l r0, @r1

    mov #2, r0
    mov.l handler_count_ptr, r1
    mov.l r0, @r1

    rte
    nop

handler_count_ptr:
    .long handler_count
intevt_for_handler:
    .long 0xff000028
first_event_ptr_for_handler:
    .long first_event
second_event_ptr_for_handler:
    .long second_event
timer_enabled_for_handler:
    .long 0x00000020
tcr0_for_handler:
    .long 0xffd80010
vblank_bit_for_handler:
    .long 0x00000008
asic_ack_a_for_handler:
    .long 0xa05f6900
asic_irq9_a_for_handler:
    .long 0xa05f6930
done_ptr_for_handler:
    .long handlers_done

handler_count:
    .long 0
handlers_done:
    .long 0
first_event:
    .long 0
second_event:
    .long 0

message:
    .asciz "dcSharp timer ASIC arbitration probe done\n"
failed_message:
    .asciz "dcSharp timer ASIC arbitration probe failed\n"
