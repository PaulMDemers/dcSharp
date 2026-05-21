#include <arch/irq.h>
#include <kos.h>
#include <stdint.h>
#include <stdio.h>

KOS_INIT_FLAGS(INIT_DEFAULT);

#define DCSHARP_ASIC_ACK_A (*(volatile uint32_t *)0xa05f6900u)
#define DCSHARP_ASIC_IRQ9_A (*(volatile uint32_t *)0xa05f6930u)
#define ASIC_EVENT_VBLANK_BEGIN (1u << 3)

int main(int argc, char **argv) {
    (void)argc;
    (void)argv;

    printf("dcSharp ASIC event probe start\n");

    irq_mask_t old_irq = irq_disable();
    DCSHARP_ASIC_ACK_A = 0xffffffffu;
    DCSHARP_ASIC_IRQ9_A = ASIC_EVENT_VBLANK_BEGIN;

    uint32_t observed = 0;
    for(uint32_t spin = 0; spin < 2000000u; spin++) {
        observed = DCSHARP_ASIC_ACK_A;
        if((observed & ASIC_EVENT_VBLANK_BEGIN) != 0) {
            break;
        }
    }

    uint32_t before_clear = DCSHARP_ASIC_ACK_A;
    DCSHARP_ASIC_ACK_A = ASIC_EVENT_VBLANK_BEGIN;
    uint32_t after_clear = DCSHARP_ASIC_ACK_A;
    DCSHARP_ASIC_IRQ9_A = 0;
    irq_restore(old_irq);

    printf("dcSharp ASIC event probe: observed=0x%08lx before_clear=0x%08lx after_clear=0x%08lx irq9=0x%08lx\n",
           (unsigned long)observed,
           (unsigned long)before_clear,
           (unsigned long)after_clear,
           (unsigned long)DCSHARP_ASIC_IRQ9_A);

    return 0;
}
