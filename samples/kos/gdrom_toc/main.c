#include <kos.h>
#include <dc/cdrom.h>
#include <stdint.h>
#include <stdio.h>
#include <string.h>

KOS_INIT_FLAGS(INIT_DEFAULT);

#define DCSHARP_GDROM_TRACK 3
#define DCSHARP_GDROM_FAD 45000
#define DCSHARP_GDROM_LEADOUT 45020
#define DCSHARP_GDROM_TRACK_WORD 0x4000afc8
#define DCSHARP_GDROM_INDEX 2

static CDROM_TOC toc __attribute__((aligned(32)));

int main(int argc, char **argv) {
    int status;
    uint32_t data_track;
    uint32_t located_track;

    (void)argc;
    (void)argv;

    memset(&toc, 0xa5, sizeof(toc));
    status = cdrom_read_toc(&toc, false);
    data_track = toc.entry[DCSHARP_GDROM_INDEX];
    located_track = cdrom_locate_data_track(&toc);

    printf("dcSharp GD-ROM TOC status=%d first=%lu last=%lu data=0x%08lx ctrl=%lu leadout=%lu located=%lu\n",
           status,
           (unsigned long)TOC_TRACK(toc.first),
           (unsigned long)TOC_TRACK(toc.last),
           (unsigned long)data_track,
           (unsigned long)TOC_CTRL(data_track),
           (unsigned long)TOC_LBA(toc.leadout_sector),
           (unsigned long)located_track);

    if(status != ERR_OK) {
        printf("dcSharp GD-ROM TOC read failed\n");
        return 1;
    }

    if(TOC_TRACK(toc.first) != DCSHARP_GDROM_TRACK ||
       TOC_TRACK(toc.last) != DCSHARP_GDROM_TRACK) {
        printf("dcSharp GD-ROM TOC track range mismatch\n");
        return 2;
    }

    if(data_track != DCSHARP_GDROM_TRACK_WORD ||
       TOC_CTRL(data_track) != 4 ||
       TOC_LBA(data_track) != DCSHARP_GDROM_FAD) {
        printf("dcSharp GD-ROM TOC data track mismatch\n");
        return 3;
    }

    if(TOC_LBA(toc.leadout_sector) != DCSHARP_GDROM_LEADOUT ||
       located_track != DCSHARP_GDROM_FAD) {
        printf("dcSharp GD-ROM TOC leadout/data locate mismatch\n");
        return 4;
    }

    printf("dcSharp GD-ROM TOC probe done\n");
    return 0;
}
