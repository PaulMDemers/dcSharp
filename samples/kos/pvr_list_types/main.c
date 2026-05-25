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

static void write_pvr_vertex(uint32_t flags, uint32_t x, uint32_t y, uint32_t argb) {
    const uint32_t words[8] = {
        flags,
        x,
        y,
        0x3f800000u,
        0x00000000u,
        0x00000000u,
        argb,
        0x00000000u
    };

    write_words(words, 8);
}

static void write_triangle(uint32_t header_value, uint32_t mode2, uint32_t argb) {
    const uint32_t header[8] = {
        header_value,
        0x00000000u,
        mode2,
        0x00000000u,
        0x00000000u,
        0x00000000u,
        0x00000000u,
        0x00000000u
    };

    write_words(header, 8);
    write_pvr_vertex(0xe0000000u, 0x3f800000u, 0x3f800000u, argb);
    write_pvr_vertex(0xe0000000u, 0x40000000u, 0x3f800000u, argb);
    write_pvr_vertex(0xf0000000u, 0x3f800000u, 0x40000000u, argb);
}

int main(int argc, char **argv) {
    (void)argc;
    (void)argv;

    const uint32_t opaque_header = 0x80840000u;
    const uint32_t translucent_header = 0x82840000u;
    const uint32_t punch_header = 0x84840000u;
    const uint32_t blend_mode2 = 0x94100000u;

    PVR_REG(0x0124) = 0x00100000u;
    PVR_REG(0x0128) = 0x00200000u;
    PVR_REG(0x012c) = 0x00101000u;
    PVR_REG(0x0130) = 0x00201000u;
    PVR_REG(0x0144) = 0x80000000u;

    write_triangle(opaque_header, 0x00000000u, 0xff00ff00u);
    write_triangle(punch_header, 0x00000000u, 0x00ff0000u);
    write_triangle(punch_header, 0x00000000u, 0xff0000ffu);
    write_triangle(translucent_header, blend_mode2, 0x80ff0000u);

    printf("dcSharp PVR list types probe: opaque=0x%08lx translucent=0x%08lx punch=0x%08lx blend_mode2=0x%08lx ta_init=0x%08lx\n",
           (unsigned long)opaque_header,
           (unsigned long)translucent_header,
           (unsigned long)punch_header,
           (unsigned long)blend_mode2,
           (unsigned long)PVR_REG(0x0144));

    return 0;
}

