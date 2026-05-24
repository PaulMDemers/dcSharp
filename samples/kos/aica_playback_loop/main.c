#include <kos.h>
#include <stdint.h>
#include <stdio.h>

KOS_INIT_FLAGS(INIT_DEFAULT);

#define AICA_REG(offset) (*(volatile uint32_t *)(0xa0700000u + (offset)))
#define AICA_REG8(offset) (*(volatile uint8_t *)(0xa0700000u + (offset)))
#define AICA_RAM16(offset) (*(volatile uint16_t *)(0xa0800000u + (offset)))

int main(int argc, char **argv) {
    (void)argc;
    (void)argv;

    for(uint32_t index = 0; index < 64; index++) {
        AICA_RAM16(index * 2u) = (uint16_t)(0x2000u + index);
    }

    AICA_REG(0x0004) = 0x00000000u;
    AICA_REG(0x0008) = 0x00000008u;
    AICA_REG(0x000c) = 0x00000010u;
    AICA_REG(0x0018) = 0x00000000u;
    AICA_REG8(0x0024) = 0x0fu;
    AICA_REG8(0x0029) = 0x40u;
    AICA_REG(0x2800) = 0x0000000fu;
    AICA_REG(0x0000) = 0x0000c200u;

    printf("dcSharp AICA playback loop probe start ctrl=0x%08lx loop=%lu-%lu\n",
           (unsigned long)AICA_REG(0x0000),
           (unsigned long)AICA_REG(0x0008),
           (unsigned long)AICA_REG(0x000c));

    thd_sleep(20);

    printf("dcSharp AICA playback loop probe done\n");
    return 0;
}
