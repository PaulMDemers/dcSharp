#include <kos.h>
#include <dc/cdrom.h>
#include <stdint.h>
#include <stdio.h>
#include <string.h>

KOS_INIT_FLAGS(INIT_DEFAULT);

#define DCSHARP_GDROM_FIRST_TRACK 3
#define DCSHARP_GDROM_LAST_TRACK 4
#define DCSHARP_GDROM_DATA_FAD 45150
#define DCSHARP_GDROM_LEADOUT 45170

static CDROM_TOC toc __attribute__((aligned(32)));
static uint8_t sector_buffer[2048] __attribute__((aligned(32)));

int main(int argc, char **argv) {
    int toc_status;
    int read_status;
    uint32_t located_track;

    (void)argc;
    (void)argv;

    memset(&toc, 0, sizeof(toc));
    memset(sector_buffer, 0, sizeof(sector_buffer));

    toc_status = cdrom_read_toc(&toc, false);
    located_track = cdrom_locate_data_track(&toc);
    read_status = cdrom_read_sectors(sector_buffer, located_track, 1);

    printf("dcSharp GD-ROM multitrack toc=%d read=%d first=%lu last=%lu located=%lu leadout=%lu sentinel=%02x%02x%02x%02x\n",
           toc_status,
           read_status,
           (unsigned long)TOC_TRACK(toc.first),
           (unsigned long)TOC_TRACK(toc.last),
           (unsigned long)located_track,
           (unsigned long)TOC_LBA(toc.leadout_sector),
           sector_buffer[0],
           sector_buffer[1],
           sector_buffer[2],
           sector_buffer[3]);

    if(toc_status != ERR_OK || read_status != ERR_OK) {
        printf("dcSharp GD-ROM multitrack read failed\n");
        return 1;
    }

    if(TOC_TRACK(toc.first) != DCSHARP_GDROM_FIRST_TRACK ||
       TOC_TRACK(toc.last) != DCSHARP_GDROM_LAST_TRACK ||
       located_track != DCSHARP_GDROM_DATA_FAD ||
       TOC_LBA(toc.leadout_sector) != DCSHARP_GDROM_LEADOUT) {
        printf("dcSharp GD-ROM multitrack TOC mismatch\n");
        return 2;
    }

    if(memcmp(sector_buffer, "MTK4", 4) != 0) {
        printf("dcSharp GD-ROM multitrack sentinel mismatch\n");
        return 3;
    }

    printf("dcSharp GD-ROM multitrack probe done\n");
    return 0;
}
