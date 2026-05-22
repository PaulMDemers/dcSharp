#include <kos.h>
#include <stdint.h>
#include <stdio.h>

KOS_INIT_FLAGS(INIT_DEFAULT);

#define PVR_REG(offset) (*(volatile uint32_t *)(0xa05f8000u + (offset)))
#define DCSHARP_PVR_TA_INPUT (*(volatile uint32_t *)0x10000000u)

static void write_vertex(uint32_t control, uint32_t x, uint32_t y, uint32_t rgb565) {
    DCSHARP_PVR_TA_INPUT = control;
    DCSHARP_PVR_TA_INPUT = x << 16;
    DCSHARP_PVR_TA_INPUT = y << 16;
    DCSHARP_PVR_TA_INPUT = rgb565;
}

int main(int argc, char **argv) {
    (void)argc;
    (void)argv;

    PVR_REG(0x0124) = 0x00100000u;
    PVR_REG(0x0128) = 0x00200000u;
    PVR_REG(0x012c) = 0x00101000u;
    PVR_REG(0x0130) = 0x00201000u;
    PVR_REG(0x0144) = 0x80000000u;

    DCSHARP_PVR_TA_INPUT = 0x80840000u;
    write_vertex(0xe0000000u, 1u, 1u, 0x0000f800u);
    write_vertex(0xe0000000u, 2u, 1u, 0x0000f800u);
    write_vertex(0xf0000000u, 1u, 2u, 0x0000f800u);

    printf("dcSharp PVR polygon probe: opb_start=0x%08lx vertbuf_start=0x%08lx ta_init=0x%08lx\n",
           (unsigned long)PVR_REG(0x0124),
           (unsigned long)PVR_REG(0x0128),
           (unsigned long)PVR_REG(0x0144));

    return 0;
}
