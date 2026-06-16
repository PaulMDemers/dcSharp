#include <kos.h>
#include <dc/cdrom.h>
#include <stdio.h>

KOS_INIT_FLAGS(INIT_DEFAULT);

#define DCSHARP_EXPECTED_DISC_GDROM 0x80

int main(int argc, char **argv) {
    int rv;
    int status = -99;
    int disc_type = -99;

    (void)argc;
    (void)argv;

    rv = cdrom_get_status(&status, &disc_type);

    printf("dcSharp GD-ROM status rv=%d status=%d disc=0x%02x\n",
           rv,
           status,
           disc_type);

    if(rv != ERR_OK) {
        printf("dcSharp GD-ROM status call failed\n");
        return 1;
    }

    if(status != CD_STATUS_STANDBY || disc_type != DCSHARP_EXPECTED_DISC_GDROM) {
        printf("dcSharp GD-ROM status mismatch\n");
        return 2;
    }

    printf("dcSharp GD-ROM status probe done\n");
    return 0;
}
