#include <arch/irq.h>
#include <kos.h>
#include <stdint.h>
#include <stdio.h>
#include <string.h>

KOS_INIT_FLAGS(INIT_IRQ | INIT_FS_DEV | INIT_LIBRARY | INIT_NO_DCLOAD | INIT_NO_SHUTDOWN);

#define DCSHARP_ASIC_ACK_A (*(volatile uint32_t *)0xa05f6900u)
#define DCSHARP_ASIC_IRQB_A (*(volatile uint32_t *)0xa05f6920u)
#define DCSHARP_MAPLE_DMA_ADDRESS (*(volatile uint32_t *)0xa05f6c04u)
#define DCSHARP_MAPLE_STATE (*(volatile uint32_t *)0xa05f6c18u)
#define DCSHARP_MAPLE_STATE_DMA 1u
#define DCSHARP_SCIF_TX (*(volatile uint8_t *)0xffe8000cu)
#define DCSHARP_ASIC_EVENT_MAPLE_DMA (1u << 12)
#define DCSHARP_MAPLE_COMMAND_DEVICE_INFO 1u
#define DCSHARP_MAPLE_PORT_A_UNIT_0 0x20u

static uint32_t dma_descriptor[3] __attribute__((aligned(32)));
static uint8_t receive_buffer[128] __attribute__((aligned(32)));

static uint32_t physical_address(const void *ptr) {
    return ((uint32_t)(uintptr_t)ptr) & 0x1fffffffu;
}

static void serial_write(const char *text) {
    while(*text != '\0') {
        DCSHARP_SCIF_TX = (uint8_t)*text++;
    }
}

int main(int argc, char **argv) {
    (void)argc;
    (void)argv;

    irq_disable();
    memset(receive_buffer, 0, sizeof(receive_buffer));
    DCSHARP_ASIC_ACK_A = 0xffffffffu;
    DCSHARP_ASIC_IRQB_A = DCSHARP_ASIC_EVENT_MAPLE_DMA;

    dma_descriptor[0] = 0x80000000u;
    dma_descriptor[1] = physical_address(receive_buffer);
    dma_descriptor[2] = (DCSHARP_MAPLE_PORT_A_UNIT_0 << 8) | DCSHARP_MAPLE_COMMAND_DEVICE_INFO;

    DCSHARP_MAPLE_DMA_ADDRESS = physical_address(dma_descriptor);
    DCSHARP_MAPLE_STATE = DCSHARP_MAPLE_STATE_DMA;

    while((DCSHARP_MAPLE_STATE & DCSHARP_MAPLE_STATE_DMA) != 0) {
    }

    printf("dcSharp ASIC IRQB probe: response=0x%02x ack=0x%08lx irqb=0x%08lx\n",
           receive_buffer[0],
           (unsigned long)DCSHARP_ASIC_ACK_A,
           (unsigned long)DCSHARP_ASIC_IRQB_A);
    fflush(stdout);
    serial_write("dcSharp ASIC IRQB probe complete\n");

    return 0;
}
