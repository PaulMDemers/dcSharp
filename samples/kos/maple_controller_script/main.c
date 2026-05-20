#include <kos.h>
#include <stdint.h>
#include <stdio.h>
#include <string.h>

KOS_INIT_FLAGS(INIT_DEFAULT);

#define DCSHARP_MAPLE_DMA_ADDRESS ((volatile uint32_t *)0xA05F6C04)
#define DCSHARP_MAPLE_STATE       ((volatile uint32_t *)0xA05F6C18)
#define DCSHARP_MAPLE_STATE_DMA   1
#define MAPLE_COMMAND_GET_CONDITION 9
#define MAPLE_PORT_A_UNIT_0 0x20

static uint32_t dma_descriptor[3] __attribute__((aligned(32)));
static uint8_t receive_buffer[32] __attribute__((aligned(32)));

static uint32_t physical_address(const void *ptr) {
    return ((uint32_t)(uintptr_t)ptr) & 0x1fffffff;
}

static void read_controller_condition(void) {
    memset(receive_buffer, 0, sizeof(receive_buffer));

    dma_descriptor[0] = 0x80000000;
    dma_descriptor[1] = physical_address(receive_buffer);
    dma_descriptor[2] = ((uint32_t)MAPLE_PORT_A_UNIT_0 << 8) | MAPLE_COMMAND_GET_CONDITION;

    *DCSHARP_MAPLE_DMA_ADDRESS = physical_address(dma_descriptor);
    *DCSHARP_MAPLE_STATE = DCSHARP_MAPLE_STATE_DMA;

    while((*DCSHARP_MAPLE_STATE & DCSHARP_MAPLE_STATE_DMA) != 0) {
    }
}

static void print_condition(const char *label) {
    uint16_t raw_buttons = (uint16_t)(receive_buffer[8] | (receive_buffer[9] << 8));
    uint16_t pressed_buttons = (uint16_t)~raw_buttons;
    int joyx = (int)receive_buffer[12] - 128;
    int joyy = (int)receive_buffer[13] - 128;
    int ltrig = receive_buffer[11];
    int rtrig = receive_buffer[10];

    printf("dcSharp Maple controller script probe %s: buttons=0x%08lx joy=(%d,%d) triggers=(%d,%d)\n",
           label,
           (unsigned long)pressed_buttons,
           joyx,
           joyy,
           ltrig,
           rtrig);
}

int main(int argc, char **argv) {
    (void)argc;
    (void)argv;

    read_controller_condition();
    print_condition("first");

    for(volatile uint32_t spin = 0; spin < 1000000; spin++) {
    }

    read_controller_condition();
    print_condition("second");
    return 0;
}
