    .section .text
    .global _start

_start:
    mov.l vbr_value, r0
    ldc r0, vbr

    mov.l timer_priority, r0
    mov.l ipra, r1
    mov.l r0, @r1

    mov.l timer_pending, r0
    mov.l tcr0, r1
    mov.l r0, @r1

wait_for_handler:
    mov.l handled_ptr, r1
    mov.l @r1, r0
    tst r0, r0
    bt wait_for_handler
    nop

    mov.l recorded_event_ptr, r1
    mov.l @r1, r0
    mov.l timer_event, r2
    cmp/eq r2, r0
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
timer_priority:
    .long 0x0000a000
timer_pending:
    .long 0x00000120
timer_event:
    .long 0x00000400
ipra:
    .long 0xffd00004
tcr0:
    .long 0xffd80010
handled_ptr:
    .long handled
recorded_event_ptr:
    .long recorded_event
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

    mov.l timer_enabled_for_handler, r0
    mov.l tcr0_for_handler, r1
    mov.l r0, @r1

    mov #1, r0
    mov.l handled_ptr_for_handler, r1
    mov.l r0, @r1

    rte
    nop

intevt_for_handler:
    .long 0xff000028
recorded_event_ptr_for_handler:
    .long recorded_event
timer_enabled_for_handler:
    .long 0x00000020
tcr0_for_handler:
    .long 0xffd80010
handled_ptr_for_handler:
    .long handled

handled:
    .long 0
recorded_event:
    .long 0

message:
    .asciz "dcSharp timer IRQ accept probe done\n"
failed_message:
    .asciz "dcSharp timer IRQ accept probe failed\n"
