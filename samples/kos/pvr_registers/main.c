#include <kos.h>
#include <stdint.h>
#include <stdio.h>

KOS_INIT_FLAGS(INIT_DEFAULT);

#define PVR_REG(offset) (*(volatile uint32_t *)(0xa05f8000u + (offset)))
#define DCSHARP_PVR_TA_INPUT (*(volatile uint32_t *)0x10000000u)
#define DCSHARP_PVR_TA_YUV (*(volatile uint32_t *)0x10800000u)

int main(int argc, char **argv) {
    (void)argc;
    (void)argv;

    PVR_REG(0x0044) = 0x00800000u;
    PVR_REG(0x0050) = 0x00000000u;
    PVR_REG(0x005c) = ((240u - 1u) << 10) | (320u - 1u);
    PVR_REG(0x0144) = 0x80000000u;

    DCSHARP_PVR_TA_INPUT = 0x80840000u;
    DCSHARP_PVR_TA_INPUT = 0xe0000000u;
    DCSHARP_PVR_TA_YUV = 0x00000001u;

    printf("dcSharp PVR register probe: fb_cfg=0x%08lx fb_size=0x%08lx ta_init=0x%08lx\n",
           (unsigned long)PVR_REG(0x0044),
           (unsigned long)PVR_REG(0x005c),
           (unsigned long)PVR_REG(0x0144));

    return 0;
}
