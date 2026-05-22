#include <kos.h>
#include <stdint.h>
#include <stdio.h>

KOS_INIT_FLAGS(INIT_DEFAULT);

#define PVR_REG(offset) (*(volatile uint32_t *)(0xa05f8000u + (offset)))
#define DCSHARP_PVR_TA_INPUT (*(volatile uint32_t *)0x10000000u)
#define DCSHARP_PVR_VRAM16(offset) (*(volatile uint16_t *)(0x05000000u + (offset)))

static void write_words(const uint32_t *words, uint32_t count) {
    for(uint32_t index = 0; index < count; index++) {
        DCSHARP_PVR_TA_INPUT = words[index];
    }
}

static void write_pvr_vertex(uint32_t flags, uint32_t x, uint32_t y, uint32_t u, uint32_t v, uint32_t argb) {
    const uint32_t words[8] = {
        flags,
        x,
        y,
        0x3f800000u,
        u,
        v,
        argb,
        0x00000000u
    };

    write_words(words, 8);
}

static void write_triangle(uint32_t mode1, uint32_t mode2, uint32_t mode3, uint32_t argb) {
    const uint32_t header[8] = {
        0x80840008u,
        mode1,
        mode2,
        mode3,
        0x00000000u,
        0x00000000u,
        0x00000000u,
        0x00000000u
    };

    write_words(header, 8);
    write_pvr_vertex(0xe0000000u, 0x3f800000u, 0x3f800000u, 0x00000000u, 0x00000000u, argb);
    write_pvr_vertex(0xe0000000u, 0x40000000u, 0x3f800000u, 0x3f800000u, 0x00000000u, argb);
    write_pvr_vertex(0xf0000000u, 0x3f800000u, 0x40000000u, 0x00000000u, 0x3f800000u, argb);
}

int main(int argc, char **argv) {
    (void)argc;
    (void)argv;

    const uint32_t texture_base = 0x00005000u;
    const uint32_t opaque_mode1 = 0x00000000u;
    const uint32_t opaque_mode2 = 0x00000000u;
    const uint32_t texture_mode1 = 0x02000000u;
    const uint32_t texture_mode2 = 0x94118000u;
    const uint32_t texture_mode3 = 0x14005000u;

    PVR_REG(0x0124) = 0x00100000u;
    PVR_REG(0x0128) = 0x00200000u;
    PVR_REG(0x012c) = 0x00101000u;
    PVR_REG(0x0130) = 0x00201000u;
    PVR_REG(0x0144) = 0x80000000u;

    DCSHARP_PVR_VRAM16(texture_base + 0u) = 0x8f00u;
    DCSHARP_PVR_VRAM16(texture_base + 14u) = 0x8f00u;
    DCSHARP_PVR_VRAM16(texture_base + (7u * 8u * 2u)) = 0x8f00u;

    write_triangle(opaque_mode1, opaque_mode2, 0x00000000u, 0xff00ff00u);
    write_triangle(texture_mode1, texture_mode2, texture_mode3, 0xffffffffu);

    printf("dcSharp PVR texture alpha ARGB4444 probe: texture_base=0x%08lx texture_mode2=0x%08lx texture_mode3=0x%08lx ta_init=0x%08lx\n",
           (unsigned long)texture_base,
           (unsigned long)texture_mode2,
           (unsigned long)texture_mode3,
           (unsigned long)PVR_REG(0x0144));

    return 0;
}
