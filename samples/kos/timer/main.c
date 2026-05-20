#include <arch/timer.h>
#include <kos.h>
#include <stdint.h>
#include <stdio.h>

KOS_INIT_FLAGS(INIT_DEFAULT);

int main(int argc, char **argv) {
    (void)argc;
    (void)argv;

    printf("dcSharp KallistiOS timer probe start\n");

    for(int i = 0; i < 3; i++) {
        uint64_t before = timer_ms_gettime64();
        thd_sleep(16);
        uint64_t after = timer_ms_gettime64();

        printf("dcSharp timer tick %d elapsed %llu\n",
               i + 1,
               (unsigned long long)(after - before));
    }

    printf("dcSharp KallistiOS timer probe done\n");
    return 0;
}
