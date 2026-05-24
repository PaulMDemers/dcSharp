#include <kos.h>
#include <stdint.h>
#include <stdio.h>

KOS_INIT_FLAGS(INIT_DEFAULT);

#define AICA_REG(offset) (*(volatile uint32_t *)(0xa0700000u + (offset)))
#define AICA_REG8(offset) (*(volatile uint8_t *)(0xa0700000u + (offset)))
#define AICA_RAM8(offset) (*(volatile uint8_t *)(0xa0800000u + (offset)))

int main(int argc, char **argv) {
    (void)argc;
    (void)argv;

    for(uint32_t index = 0; index < 64; index++) {
        AICA_RAM8(0x20u + index) = (uint8_t)(0x80u + index);
    }

    AICA_REG(0x0004) = 0x00000020u;
    AICA_REG(0x0008) = 0x00000004u;
    AICA_REG(0x000c) = 0x00000020u;
    AICA_REG(0x0018) = 0x00000000u;
    AICA_REG8(0x0024) = 0x2au;
    AICA_REG8(0x0029) = 0x50u;
    AICA_REG(0x2800) = 0x0000000fu;
    AICA_REG(0x0000) = 0x0000c300u;

    printf("dcSharp AICA ADPCM metadata probe start ctrl=0x%08lx loop=%lu-%lu pan=0x%02x vol=%u\n",
           (unsigned long)AICA_REG(0x0000),
           (unsigned long)AICA_REG(0x0008),
           (unsigned long)AICA_REG(0x000c),
           (unsigned int)AICA_REG8(0x0024),
           (unsigned int)AICA_REG8(0x0029));

    thd_sleep(20);

    printf("dcSharp AICA ADPCM metadata probe done\n");
    return 0;
}
