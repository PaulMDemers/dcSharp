#include <kos.h>
#include <dc/cdrom.h>
#include <stdint.h>
#include <stdio.h>
#include <string.h>

#define DCSHARP_GDROM_BAD_LBA 45020
#define SECTOR_SIZE 2048

KOS_INIT_FLAGS(INIT_DEFAULT);

static uint8_t sector_buffer[SECTOR_SIZE] __attribute__((aligned(32)));

int main(int argc, char **argv) {
    int status;
    unsigned unchanged = 0;
    unsigned index;

    (void)argc;
    (void)argv;

    memset(sector_buffer, 0x5a, sizeof(sector_buffer));
    status = cdrom_read_sectors(sector_buffer, DCSHARP_GDROM_BAD_LBA, 1);

    for(index = 0; index < sizeof(sector_buffer); index++) {
        if(sector_buffer[index] == 0x5a) {
            unchanged++;
        }
    }

    printf("dcSharp GD-ROM out-of-range status=%d unchanged=%u first=0x%02x last=0x%02x\n",
           status,
           unchanged,
           sector_buffer[0],
           sector_buffer[SECTOR_SIZE - 1]);

    if(status == ERR_OK) {
        printf("dcSharp GD-ROM out-of-range unexpectedly succeeded\n");
        return 1;
    }

    if(unchanged != SECTOR_SIZE) {
        printf("dcSharp GD-ROM out-of-range buffer changed\n");
        return 2;
    }

    printf("dcSharp GD-ROM out-of-range probe done\n");
    return 0;
}
