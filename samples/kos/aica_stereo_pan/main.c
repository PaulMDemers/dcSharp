#include <kos.h>
#include <stdint.h>
#include <stdio.h>

KOS_INIT_FLAGS(INIT_DEFAULT);

#define AICA_REG(offset) (*(volatile uint32_t *)(0xa0700000u + (offset)))
#define AICA_REG8(offset) (*(volatile uint8_t *)(0xa0700000u + (offset)))
#define AICA_RAM16(offset) (*(volatile uint16_t *)(0xa0800000u + (offset)))

#define CH_OFFSET(channel, offset) (((channel) * 0x80u) + (offset))

static void setup_channel(uint32_t channel, uint32_t sample_base, uint8_t pan_send, uint8_t volume) {
    AICA_REG(CH_OFFSET(channel, 0x0004)) = sample_base;
    AICA_REG(CH_OFFSET(channel, 0x0008)) = 0x00000000u;
    AICA_REG(CH_OFFSET(channel, 0x000c)) = 0x00000020u;
    AICA_REG(CH_OFFSET(channel, 0x0018)) = 0x00000000u;
    AICA_REG8(CH_OFFSET(channel, 0x0024)) = pan_send;
    AICA_REG8(CH_OFFSET(channel, 0x0029)) = volume;
    AICA_REG(CH_OFFSET(channel, 0x0000)) = 0x0000c000u;
}

int main(int argc, char **argv) {
    (void)argc;
    (void)argv;

    for(uint32_t index = 0; index < 128; index++) {
        AICA_RAM16(index * 2u) = (uint16_t)(0x3000u + index);
    }

    AICA_REG(0x2800) = 0x0000000fu;
    setup_channel(0u, 0x00000000u, 0x10u, 0x30u);
    setup_channel(1u, 0x00000040u, 0x2fu, 0x60u);

    printf("dcSharp AICA stereo pan probe start ch0_pan=0x%02x ch1_pan=0x%02x ch0_vol=%u ch1_vol=%u\n",
           (unsigned int)AICA_REG8(CH_OFFSET(0u, 0x0024)),
           (unsigned int)AICA_REG8(CH_OFFSET(1u, 0x0024)),
           (unsigned int)AICA_REG8(CH_OFFSET(0u, 0x0029)),
           (unsigned int)AICA_REG8(CH_OFFSET(1u, 0x0029)));

    thd_sleep(20);

    printf("dcSharp AICA stereo pan probe done\n");
    return 0;
}
