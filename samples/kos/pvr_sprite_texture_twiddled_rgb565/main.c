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

static uint32_t pack_uv(float u, float v) {
    union {
        float f;
        uint32_t u;
    } up;
    union {
        float f;
        uint32_t u;
    } vp;

    up.f = u;
    vp.f = v;
    return (up.u & 0xffff0000u) | ((vp.u >> 16) & 0x0000ffffu);
}

static uint32_t twiddled_index(uint32_t x, uint32_t y) {
    uint32_t index = 0;

    for(uint32_t bit = 0; bit < 16; bit++) {
        index |= ((x >> bit) & 1u) << (bit * 2u);
        index |= ((y >> bit) & 1u) << ((bit * 2u) + 1u);
    }

    return index;
}

int main(int argc, char **argv) {
    (void)argc;
    (void)argv;

    const uint32_t texture_base = 0x0000a000u;
    const uint32_t auv = pack_uv(0.0f, 0.0f);
    const uint32_t buv = pack_uv(1.0f, 0.0f);
    const uint32_t cuv = pack_uv(1.0f, 1.0f);
    const uint32_t header[8] = {
        0xa0840001u,
        0x02000000u,
        0x00018000u,
        0x0800a000u,
        0xffffffffu,
        0x00000000u,
        0x00000000u,
        0x00000000u
    };
    const uint32_t vertices[16] = {
        0xf0000000u,
        0x3f800000u,
        0x3f800000u,
        0x3f800000u,
        0x40400000u,
        0x3f800000u,
        0x3f800000u,
        0x40400000u,
        0x40400000u,
        0x3f800000u,
        0x3f800000u,
        0x40400000u,
        0x00000000u,
        auv,
        buv,
        cuv
    };

    PVR_REG(0x0124) = 0x00100000u;
    PVR_REG(0x0128) = 0x00200000u;
    PVR_REG(0x012c) = 0x00101000u;
    PVR_REG(0x0130) = 0x00201000u;
    PVR_REG(0x0144) = 0x80000000u;

    DCSHARP_PVR_VRAM16(texture_base + (twiddled_index(0u, 0u) * 2u)) = 0xf800u;
    DCSHARP_PVR_VRAM16(texture_base + (twiddled_index(7u, 0u) * 2u)) = 0x07e0u;
    DCSHARP_PVR_VRAM16(texture_base + (twiddled_index(4u, 4u) * 2u)) = 0xffffu;
    DCSHARP_PVR_VRAM16(texture_base + (twiddled_index(7u, 7u) * 2u)) = 0x001fu;

    write_words(header, 8);
    write_words(vertices, 16);

    printf("dcSharp PVR sprite texture twiddled RGB565 probe: texture_base=0x%08lx mode3=0x%08lx auv=0x%08lx cuv=0x%08lx ta_init=0x%08lx\n",
           (unsigned long)texture_base,
           (unsigned long)header[3],
           (unsigned long)auv,
           (unsigned long)cuv,
           (unsigned long)PVR_REG(0x0144));

    return 0;
}

