#include <kos.h>
#include <stdio.h>

KOS_INIT_FLAGS(INIT_DEFAULT);

int main(int argc, char **argv) {
    (void)argc;
    (void)argv;

    dbglog(DBG_INFO, "dcSharp KallistiOS probe via dbglog\n");
    dbglog(DBG_INFO, "If this appears, the emulator reached a legal homebrew main().\n");

    printf("dcSharp KallistiOS probe\n");
    printf("If this runs, the emulator reached a legal homebrew main().\n");

    return 0;
}
