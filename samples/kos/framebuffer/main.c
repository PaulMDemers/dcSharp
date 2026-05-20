#include <kos.h>
#include <stdint.h>
#include <stdio.h>

#define WIDTH 320
#define HEIGHT 240

KOS_INIT_FLAGS(INIT_DEFAULT);

static uint16_t pixel_for(int x, int y) {
    if(x < WIDTH / 2 && y < HEIGHT / 2)
        return 0xf800;
    if(x >= WIDTH / 2 && y < HEIGHT / 2)
        return 0x07e0;
    if(x < WIDTH / 2)
        return 0x001f;

    return 0xffff;
}

int main(int argc, char **argv) {
    (void)argc;
    (void)argv;

    uint32_t checksum = 2166136261u;

    vid_set_mode(DM_320x240, PM_RGB565);

    for(int y = 0; y < HEIGHT; y++) {
        for(int x = 0; x < WIDTH; x++) {
            uint16_t pixel = pixel_for(x, y);
            vram_s[y * WIDTH + x] = pixel;
            checksum ^= pixel & 0xff;
            checksum *= 16777619u;
            checksum ^= pixel >> 8;
            checksum *= 16777619u;
        }
    }

    printf("dcSharp framebuffer probe: checksum=0x%08lx origin=0x%04x center=0x%04x corner=0x%04x\n",
           (unsigned long)checksum,
           vram_s[0],
           vram_s[(HEIGHT / 2) * WIDTH + (WIDTH / 2)],
           vram_s[(HEIGHT - 1) * WIDTH + (WIDTH - 1)]);

    return 0;
}
