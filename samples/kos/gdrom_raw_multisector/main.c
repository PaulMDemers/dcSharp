#include <kos.h>
#include <dc/cdrom.h>
#include <stdint.h>
#include <stdio.h>
#include <string.h>

#define BIG_FILE_LBA 45010
#define SECTOR_SIZE 2048
#define SECTOR_COUNT 3
#define EXPECTED_SIZE 5000

KOS_INIT_FLAGS(INIT_DEFAULT);

static uint8_t sector_buffer[SECTOR_SIZE * SECTOR_COUNT] __attribute__((aligned(32)));

static uint8_t expected_byte(unsigned index) {
    return (uint8_t)((index * 31 + 7) & 0xff);
}

int main(int argc, char **argv) {
    int status;
    unsigned checksum = 0;
    unsigned mismatches = 0;
    unsigned padding_nonzero = 0;
    unsigned index;

    (void)argc;
    (void)argv;

    memset(sector_buffer, 0xa5, sizeof(sector_buffer));
    status = cdrom_read_sectors(sector_buffer, BIG_FILE_LBA, SECTOR_COUNT);

    for(index = 0; index < EXPECTED_SIZE; index++) {
        checksum = (checksum + sector_buffer[index]) & 0xffff;
        if(sector_buffer[index] != expected_byte(index)) {
            mismatches++;
        }
    }

    for(index = EXPECTED_SIZE; index < sizeof(sector_buffer); index++) {
        if(sector_buffer[index] != 0) {
            padding_nonzero++;
        }
    }

    printf("dcSharp GD-ROM raw multi read status=%d bytes=%u checksum=0x%04x mismatches=%u padding=%u\n",
           status,
           (unsigned)sizeof(sector_buffer),
           checksum,
           mismatches,
           padding_nonzero);
    printf("dcSharp GD-ROM raw multi edges s0=0x%02x s1=0x%02x s2=0x%02x last=0x%02x pad0=0x%02x padLast=0x%02x\n",
           sector_buffer[0],
           sector_buffer[SECTOR_SIZE],
           sector_buffer[SECTOR_SIZE * 2],
           sector_buffer[EXPECTED_SIZE - 1],
           sector_buffer[EXPECTED_SIZE],
           sector_buffer[(SECTOR_SIZE * SECTOR_COUNT) - 1]);

    if(status != ERR_OK) {
        printf("dcSharp GD-ROM raw multi read failed\n");
        return 1;
    }

    if(mismatches != 0 || padding_nonzero != 0) {
        printf("dcSharp GD-ROM raw multi data mismatch\n");
        return 2;
    }

    printf("dcSharp GD-ROM raw multi probe done\n");
    return 0;
}
