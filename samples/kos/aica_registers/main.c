#include <kos.h>
#include <stdint.h>
#include <stdio.h>

KOS_INIT_FLAGS(INIT_DEFAULT);

#define AICA_REG(offset) (*(volatile uint32_t *)(0xa0700000u + (offset)))
#define AICA_REG8(offset) (*(volatile uint8_t *)(0xa0700000u + (offset)))
#define AICA_RAM(offset) (*(volatile uint32_t *)(0xa0800000u + (offset)))

int main(int argc, char **argv) {
    (void)argc;
    (void)argv;

    AICA_RAM(0x0000) = 0x11223344u;
    AICA_RAM(0x0004) = 0x55667788u;

    AICA_REG(0x0000) = 0x0000c000u;
    AICA_REG(0x0004) = 0x00001234u;
    AICA_REG(0x0008) = 0x00000008u;
    AICA_REG(0x000c) = 0x00000040u;
    AICA_REG(0x0018) = 0x00001ac0u;
    AICA_REG8(0x0024) = 0x0fu;
    AICA_REG8(0x0029) = 0x40u;
    AICA_REG(0x2800) = 0x0000000fu;

    printf("dcSharp AICA register probe: ctrl=0x%08lx sample=0x%08lx pitch=0x%08lx master=0x%08lx\n",
           (unsigned long)AICA_REG(0x0000),
           (unsigned long)AICA_REG(0x0004),
           (unsigned long)AICA_REG(0x0018),
           (unsigned long)AICA_REG(0x2800));

    return 0;
}
