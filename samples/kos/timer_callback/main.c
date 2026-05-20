#include <arch/timer.h>
#include <kos.h>
#include <stdint.h>
#include <stdio.h>

KOS_INIT_FLAGS(INIT_DEFAULT);

static volatile uint32_t callback_count = 0;
static timer_primary_callback_t previous_callback = NULL;

static void timer_callback(irq_context_t *context) {
    callback_count++;

    if(previous_callback != NULL) {
        previous_callback(context);
    }

    if(callback_count < 3) {
        timer_primary_wakeup(5);
    }
}

int main(int argc, char **argv) {
    (void)argc;
    (void)argv;

    printf("dcSharp KallistiOS timer callback probe start\n");

    previous_callback = timer_primary_set_callback(timer_callback);
    timer_primary_wakeup(5);

    uint64_t deadline = timer_ms_gettime64() + 250;
    while(callback_count < 3 && timer_ms_gettime64() < deadline) {
        /* Busy wait so TMU0 interrupt delivery is the behavior under test. */
    }

    timer_primary_set_callback(previous_callback);
    if(previous_callback != NULL) {
        timer_primary_wakeup(10);
    }

    printf("dcSharp timer callback count %lu\n", (unsigned long)callback_count);
    printf("dcSharp KallistiOS timer callback probe done\n");

    return callback_count >= 3 ? 0 : 1;
}
