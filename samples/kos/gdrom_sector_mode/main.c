#include <kos.h>
#include <dc/cdrom.h>
#include <stdio.h>

KOS_INIT_FLAGS(INIT_DEFAULT);

int main(int argc, char **argv) {
    int rv;

    (void)argc;
    (void)argv;

    rv = cdrom_reinit_ex(CDROM_READ_DATA_AREA, 2048, 2048);

    printf("dcSharp GD-ROM sector mode rv=%d part=0x%x cdxa=%d size=%d\n",
           rv,
           CDROM_READ_DATA_AREA,
           2048,
           2048);

    if(rv != ERR_OK) {
        printf("dcSharp GD-ROM sector mode failed\n");
        return 1;
    }

    printf("dcSharp GD-ROM sector mode probe done\n");
    return 0;
}
