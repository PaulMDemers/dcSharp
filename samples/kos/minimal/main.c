#include <kos.h>
#include <stdio.h>

KOS_INIT_FLAGS(INIT_IRQ | INIT_FS_DEV | INIT_LIBRARY | INIT_NO_DCLOAD | INIT_NO_SHUTDOWN);

int main(int argc, char **argv) {
    (void)argc;
    (void)argv;

    dbglog(DBG_INFO, "dcSharp minimal KallistiOS probe via dbglog\n");
    printf("dcSharp minimal KallistiOS probe\n");

    return 0;
}
