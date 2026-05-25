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

static void write_red_strip(void) {
    const uint32_t header[8] = {
        0x80840000u,
        0x00000000u,
        0x00000000u,
        0x00000000u,
        0x00000000u,
        0x00000000u,
        0x00000000u,
        0x00000000u
    };

    write_words(header, 8);
    write_pvr_vertex(0xe0000000u, 0x3f800000u, 0x3f800000u, 0xffff0000u);
    write_pvr_vertex(0xe0000000u, 0x40400000u, 0x3f800000u, 0xffff0000u);
    write_pvr_vertex(0xf0000000u, 0x3f800000u, 0x40400000u, 0xffff0000u);
}

static void write_green_sprite(void) {
    const uint32_t header[8] = {
        0xa0840000u,
        0x00000000u,
        0x00000000u,
        0x00000000u,
        0xff00ff00u,
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
        0x3f800000u,
        0x40400000u,
        0x3f800000u,
        0x40400000u,
        0x40400000u,
        0x00000000u,
        0x00000000u,
        0x00000000u,
        0x00000000u
    };

    write_words(header, 8);
    write_words(vertices, 16);
}

int main(int argc, char **argv) {
    (void)argc;
    (void)argv;

    PVR_REG(0x0124) = 0x00100000u;
    PVR_REG(0x0128) = 0x00200000u;
    PVR_REG(0x012c) = 0x00101000u;
    PVR_REG(0x0130) = 0x00201000u;
    PVR_REG(0x0144) = 0x80000000u;

    write_red_strip();
    write_green_sprite();

    printf("dcSharp PVR mixed strip sprite probe: strip_argb=0xffff0000 sprite_argb=0xff00ff00 ta_init=0x%08lx\n",
           (unsigned long)PVR_REG(0x0144));

    return 0;
}
