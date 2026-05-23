#include <kos.h>
#include <dc/cdrom.h>
#include <stdint.h>
#include <stdio.h>
#include <string.h>

KOS_INIT_FLAGS(INIT_DEFAULT);

static CDROM_TOC toc __attribute__((aligned(32)));

int main(int argc, char **argv) {
    int status;
    uint32_t *words = (uint32_t *)&toc;
    unsigned unchanged = 0;
    unsigned index;
    unsigned word_count = sizeof(toc) / sizeof(uint32_t);

    (void)argc;
    (void)argv;

    memset(&toc, 0xa5, sizeof(toc));
    status = cdrom_read_toc(&toc, false);

    for(index = 0; index < word_count; index++) {
        if(words[index] == 0xa5a5a5a5) {
            unchanged++;
        }
    }

    printf("dcSharp GD-ROM TOC no media status=%d unchanged=%u words=%u first=0x%08lx leadout=0x%08lx\n",
           status,
           unchanged,
           word_count,
           (unsigned long)toc.first,
           (unsigned long)toc.leadout_sector);

    if(status == ERR_OK) {
        printf("dcSharp GD-ROM TOC no media unexpectedly succeeded\n");
        return 1;
    }

    if(unchanged != word_count) {
        printf("dcSharp GD-ROM TOC no media buffer changed\n");
        return 2;
    }

    printf("dcSharp GD-ROM TOC no media probe done\n");
    return 0;
}
