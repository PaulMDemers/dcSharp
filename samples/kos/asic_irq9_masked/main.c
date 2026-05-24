#include <arch/irq.h>
#include <kos.h>
#include <stdint.h>
#include <stdio.h>

KOS_INIT_FLAGS(INIT_IRQ | INIT_FS_DEV | INIT_LIBRARY | INIT_NO_DCLOAD | INIT_NO_SHUTDOWN);

#define DCSHARP_ASIC_ACK_A (*(volatile uint32_t *)0xa05f6900u)
#define DCSHARP_ASIC_IRQD_A (*(volatile uint32_t *)0xa05f6910u)
#define DCSHARP_ASIC_IRQ9_A (*(volatile uint32_t *)0xa05f6930u)
#define DCSHARP_SCIF_TX (*(volatile uint8_t *)0xffe8000cu)
#define ASIC_EVENT_VBLANK_BEGIN (1u << 3)

static uint32_t wait_for_vblank_ack(void) {
    volatile uint32_t *ack = &DCSHARP_ASIC_ACK_A;
    uint32_t mask = ASIC_EVENT_VBLANK_BEGIN;
    uint32_t observed;

    __asm__ volatile(
        "1:\n\t"
        "mov.l @%1,%0\n\t"
        "tst %2,%0\n\t"
        "bt 1b\n\t"
        : "=&r"(observed)
        : "r"(ack), "r"(mask)
        : "memory");

    return observed;
}

static void serial_write(const char *text) {
    while(*text != '\0') {
        DCSHARP_SCIF_TX = (uint8_t)*text++;
    }
}

int main(int argc, char **argv) {
    (void)argc;
    (void)argv;
    char line[128];

    irq_disable();
    DCSHARP_ASIC_ACK_A = 0xffffffffu;
    DCSHARP_ASIC_IRQD_A = 0;
    DCSHARP_ASIC_IRQ9_A = ASIC_EVENT_VBLANK_BEGIN;

    uint32_t observed = wait_for_vblank_ack();
    uint32_t pending = DCSHARP_ASIC_ACK_A;
    uint32_t mask = DCSHARP_ASIC_IRQ9_A;

    snprintf(line,
             sizeof(line),
             "dcSharp ASIC IRQ9 masked probe: observed=0x%08lx pending=0x%08lx irq9=0x%08lx\n",
             (unsigned long)observed,
             (unsigned long)pending,
             (unsigned long)mask);
    serial_write(line);
    fflush(stdout);
    serial_write("dcSharp ASIC IRQ9 masked probe complete\n");

    return 0;
}
