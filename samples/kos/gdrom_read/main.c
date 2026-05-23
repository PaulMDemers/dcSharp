#include <kos.h>
#include <dc/cdrom.h>
#include <stdint.h>
#include <stdio.h>
#include <string.h>

KOS_INIT_FLAGS(INIT_DEFAULT);

#define DCSHARP_GDROM_LBA 45000

static uint8_t sector_buffer[2048] __attribute__((aligned(32)));

int main(int argc, char **argv) {
    int status;

    (void)argc;
    (void)argv;

    memset(sector_buffer, 0, sizeof(sector_buffer));
    status = cdrom_read_sectors(sector_buffer, DCSHARP_GDROM_LBA, 1);

    printf("dcSharp GD-ROM read probe status=%d\n", status);
    printf("dcSharp GD-ROM sentinel=%02x%02x%02x%02x\n",
           sector_buffer[0],
           sector_buffer[1],
           sector_buffer[2],
           sector_buffer[3]);

    if(status != ERR_OK) {
        printf("dcSharp GD-ROM read failed\n");
        return 1;
    }

    if(memcmp(sector_buffer, "DCSH", 4) != 0) {
        printf("dcSharp GD-ROM sentinel mismatch\n");
        return 2;
    }

    printf("dcSharp GD-ROM read probe done\n");
    return 0;
}
