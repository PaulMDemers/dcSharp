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

static void write_sprite(uint32_t mode1, uint32_t mode2, uint32_t mode3, uint32_t argb, uint32_t textured) {
    const uint32_t auv = textured ? pack_uv(0.0f, 0.0f) : 0x00000000u;
    const uint32_t buv = textured ? pack_uv(1.0f, 0.0f) : 0x00000000u;
    const uint32_t cuv = textured ? pack_uv(1.0f, 1.0f) : 0x00000000u;
    const uint32_t header[8] = {
        textured ? 0xa0840001u : 0xa0840000u,
        mode1,
        mode2,
        mode3,
        argb,
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

    write_words(header, 8);
    write_words(vertices, 16);
}

int main(int argc, char **argv) {
    (void)argc;
    (void)argv;

    const uint32_t texture_base = 0x0000b000u;
    const uint32_t texture_mode1 = 0x02000000u;
    const uint32_t texture_mode2 = 0x94118000u;
    const uint32_t texture_mode3 = 0x1400b000u;
    const uint32_t auv = pack_uv(0.0f, 0.0f);
    const uint32_t cuv = pack_uv(1.0f, 1.0f);

    PVR_REG(0x0124) = 0x00100000u;
    PVR_REG(0x0128) = 0x00200000u;
    PVR_REG(0x012c) = 0x00101000u;
    PVR_REG(0x0130) = 0x00201000u;
    PVR_REG(0x0144) = 0x80000000u;

    DCSHARP_PVR_VRAM16(texture_base + 0u) = 0x8f00u;
    DCSHARP_PVR_VRAM16(texture_base + 14u) = 0x8f00u;
    DCSHARP_PVR_VRAM16(texture_base + (((4u * 8u) + 4u) * 2u)) = 0x8f00u;

    write_sprite(0x00000000u, 0x00000000u, 0x00000000u, 0xff00ff00u, 0u);
    write_sprite(texture_mode1, texture_mode2, texture_mode3, 0xffffffffu, 1u);

    printf("dcSharp PVR sprite texture alpha ARGB4444 probe: texture_base=0x%08lx texture_mode2=0x%08lx texture_mode3=0x%08lx auv=0x%08lx cuv=0x%08lx ta_init=0x%08lx\n",
           (unsigned long)texture_base,
           (unsigned long)texture_mode2,
           (unsigned long)texture_mode3,
           (unsigned long)auv,
           (unsigned long)cuv,
           (unsigned long)PVR_REG(0x0144));

    return 0;
}

