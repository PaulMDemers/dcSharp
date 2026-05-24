#include <kos.h>
#include <stdint.h>
#include <stdio.h>

KOS_INIT_FLAGS(INIT_DEFAULT);

#define PVR_REG(offset) (*(volatile uint32_t *)(0xa05f8000u + (offset)))
#define DCSHARP_PVR_TA_INPUT (*(volatile uint32_t *)0x10000000u)

static void write_words(const uint32_t *words, uint32_t count) {
    for(uint32_t index = 0; index < count; index++) {
        DCSHARP_PVR_TA_INPUT = words[index];
    }
}

int main(int argc, char **argv) {
    (void)argc;
    (void)argv;

    const uint32_t sprite[8] = {
        0xa0840000u,
        0x00000000u,
        0x00000000u,
        0x00000000u,
        0x3f800000u,
        0x40000000u,
        0x40400000u,
        0x40800000u
    };

    PVR_REG(0x0124) = 0x00100000u;
    PVR_REG(0x0128) = 0x00200000u;
    PVR_REG(0x012c) = 0x00101000u;
    PVR_REG(0x0130) = 0x00201000u;
    PVR_REG(0x0144) = 0x80000000u;

    write_words(sprite, 8);

    printf("dcSharp PVR sprite probe: header=0x%08lx param0=0x%08lx param3=0x%08lx ta_init=0x%08lx\n",
           (unsigned long)sprite[0],
           (unsigned long)sprite[4],
           (unsigned long)sprite[7],
           (unsigned long)PVR_REG(0x0144));

    return 0;
}
