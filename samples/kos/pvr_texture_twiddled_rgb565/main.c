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

static uint32_t twiddled_index(uint32_t x, uint32_t y) {
    uint32_t index = 0;

    for(uint32_t bit = 0; bit < 16; bit++) {
        index |= ((x >> bit) & 1u) << (bit * 2u);
        index |= ((y >> bit) & 1u) << ((bit * 2u) + 1u);
    }

    return index;
}

static void write_pvr_vertex(uint32_t flags, uint32_t x, uint32_t y, uint32_t u, uint32_t v) {
    const uint32_t words[8] = {
        flags,
        x,
        y,
        0x3f800000u,
        u,
        v,
        0xffffffffu,
        0x00000000u
    };

    write_words(words, 8);
}

int main(int argc, char **argv) {
    (void)argc;
    (void)argv;

    const uint32_t texture_base = 0x00002000u;
    const uint32_t header[8] = {
        0x80840008u,
        0x02000000u,
        0x00000000u,
        0x08002000u,
        0x00000000u,
        0x00000000u,
        0x00000000u,
        0x00000000u
    };

    PVR_REG(0x0124) = 0x00100000u;
    PVR_REG(0x0128) = 0x00200000u;
    PVR_REG(0x012c) = 0x00101000u;
    PVR_REG(0x0130) = 0x00201000u;
    PVR_REG(0x0144) = 0x80000000u;

    DCSHARP_PVR_VRAM16(texture_base + (twiddled_index(0u, 0u) * 2u)) = 0xf800u;
    DCSHARP_PVR_VRAM16(texture_base + (twiddled_index(7u, 0u) * 2u)) = 0x07e0u;
    DCSHARP_PVR_VRAM16(texture_base + (twiddled_index(0u, 7u) * 2u)) = 0x001fu;

    write_words(header, 8);
    write_pvr_vertex(0xe0000000u, 0x3f800000u, 0x3f800000u, 0x00000000u, 0x00000000u);
    write_pvr_vertex(0xe0000000u, 0x40000000u, 0x3f800000u, 0x3f800000u, 0x00000000u);
    write_pvr_vertex(0xf0000000u, 0x3f800000u, 0x40000000u, 0x00000000u, 0x3f800000u);

    printf("dcSharp PVR texture twiddled RGB565 probe: texture_base=0x%08lx mode3=0x%08lx ta_init=0x%08lx\n",
           (unsigned long)texture_base,
           (unsigned long)header[3],
           (unsigned long)PVR_REG(0x0144));

    return 0;
}
