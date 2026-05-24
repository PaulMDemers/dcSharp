    .section .text
    .global _start

_start:
    mov.l vbr_value, r0
    ldc r0, vbr
    mov.l resume_slot_ptr, r1

    mov.l after_general_ptr, r0
    mov.l r0, @r1
    .word 0xfffd

after_general:
    mov.l after_slot_ptr, r0
    mov.l r0, @r1
    bra unused_branch_target
    .word 0xfffd

after_slot:
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

unused_branch_target:
    bra done
    nop

    .align 2
vbr_value:
    .long _start
resume_slot_ptr:
    .long resume_slot
after_general_ptr:
    .long after_general
after_slot_ptr:
    .long after_slot
scif_tx:
    .long 0xffe8000c
message_ptr:
    .long message

    .org 0x100
illegal_handler:
    mov.l resume_slot_ptr_for_handler, r0
    ldc.l @r0+, spc
    rte
    nop

resume_slot_ptr_for_handler:
    .long resume_slot
resume_slot:
    .long 0

message:
    .asciz "dcSharp illegal instruction probe done\n"
