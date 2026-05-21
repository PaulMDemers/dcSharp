#include <kos.h>
#include <stdint.h>
#include <stdio.h>

#define WIDTH 640
#define CENTER_INDEX ((240 * WIDTH) + 320)

KOS_INIT_FLAGS(INIT_DEFAULT);

int main(int argc, char **argv) {
    (void)argc;
    (void)argv;

    vid_set_mode(DM_640x480, PM_RGB565);

    vram_s[0] = 0x001f;
    vram_s[1] = 0x07e0;
    vram_s[2] = 0xf800;
    vram_s[CENTER_INDEX] = 0xffff;

    printf("dcSharp video mode probe: origin=0x%04x one=0x%04x two=0x%04x center=0x%04x\n",
           vram_s[0],
           vram_s[1],
           vram_s[2],
           vram_s[CENTER_INDEX]);

    return 0;
}
