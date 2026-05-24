#include <arch/timer.h>
#include <kos.h>
#include <stdint.h>
#include <stdio.h>

KOS_INIT_FLAGS(INIT_DEFAULT);

#define DCSHARP_ASIC_ACK_A (*(volatile uint32_t *)0xa05f6900u)
#define DCSHARP_ASIC_IRQD_A (*(volatile uint32_t *)0xa05f6910u)
#define DCSHARP_ASIC_IRQ9_A (*(volatile uint32_t *)0xa05f6930u)
#define DCSHARP_SCIF_TX (*(volatile uint8_t *)0xffe8000cu)
#define ASIC_EVENT_VBLANK_BEGIN (1u << 3)

static volatile uint32_t callback_count = 0;
static timer_primary_callback_t previous_callback = NULL;

static void timer_callback(irq_context_t *context) {
    callback_count++;

    if(previous_callback != NULL) {
        previous_callback(context);
    }

    if(callback_count < 4) {
        timer_primary_wakeup(5);
    }
}

static void serial_write(const char *text) {
    while(*text != '\0') {
        DCSHARP_SCIF_TX = (uint8_t)*text++;
    }
}

int main(int argc, char **argv) {
    (void)argc;
    (void)argv;
    char line[160];

    printf("dcSharp timer+VBlank probe start\n");

    DCSHARP_ASIC_ACK_A = 0xffffffffu;
    DCSHARP_ASIC_IRQD_A = 0;
    DCSHARP_ASIC_IRQ9_A = ASIC_EVENT_VBLANK_BEGIN;

    previous_callback = timer_primary_set_callback(timer_callback);
    timer_primary_wakeup(5);

    uint32_t observed = 0;
    uint64_t deadline = timer_ms_gettime64() + 250;
    while(callback_count < 3 && timer_ms_gettime64() < deadline) {
        observed = DCSHARP_ASIC_ACK_A;
    }

    uint32_t before_clear = DCSHARP_ASIC_ACK_A;
    DCSHARP_ASIC_ACK_A = ASIC_EVENT_VBLANK_BEGIN;
    DCSHARP_ASIC_IRQ9_A = 0;
    uint32_t after_clear = DCSHARP_ASIC_ACK_A;

    timer_primary_set_callback(previous_callback);
    if(previous_callback != NULL) {
        timer_primary_wakeup(10);
    }

    snprintf(line,
             sizeof(line),
             "dcSharp timer+VBlank probe: callbacks=%lu observed=0x%08lx before_clear=0x%08lx after_clear=0x%08lx irq9=0x%08lx\n",
             (unsigned long)callback_count,
             (unsigned long)observed,
             (unsigned long)before_clear,
             (unsigned long)after_clear,
             (unsigned long)DCSHARP_ASIC_IRQ9_A);
    serial_write(line);
    serial_write("dcSharp timer+VBlank probe done\n");

    return callback_count >= 3 ? 0 : 1;
}
