#include <kos.h>
#include <errno.h>
#include <stdio.h>
#include <string.h>

#define EXPECTED_SIZE 5000
#define READ_CHUNK 777

KOS_INIT_FLAGS(INIT_DEFAULT);

static unsigned char buffer[EXPECTED_SIZE + READ_CHUNK];

static unsigned char expected_byte(unsigned index) {
    return (unsigned char)((index * 31 + 7) & 0xff);
}

int main(int argc, char **argv) {
    FILE *file;
    size_t total = 0;
    size_t bytes;
    unsigned chunks = 0;
    unsigned mismatches = 0;
    unsigned checksum = 0;
    unsigned index;

    (void)argc;
    (void)argv;

    memset(buffer, 0, sizeof(buffer));
    errno = 0;
    file = fopen("/cd/DATA/BIG.BIN", "rb");
    if(file == NULL) {
        printf("dcSharp GD-ROM multisector open failed errno=%d\n", errno);
        return 1;
    }

    while(total < sizeof(buffer)) {
        size_t remaining = sizeof(buffer) - total;
        size_t request = remaining < READ_CHUNK ? remaining : READ_CHUNK;

        bytes = fread(buffer + total, 1, request, file);
        if(bytes == 0) {
            break;
        }

        total += bytes;
        chunks++;
    }

    fclose(file);

    for(index = 0; index < total; index++) {
        unsigned char actual = buffer[index];
        checksum = (checksum + actual) & 0xffff;
        if(index < EXPECTED_SIZE && actual != expected_byte(index)) {
            mismatches++;
        }
    }

    printf("dcSharp GD-ROM multisector bytes=%u chunks=%u checksum=0x%04x mismatches=%u\n",
           (unsigned)total,
           chunks,
           checksum,
           mismatches);
    printf("dcSharp GD-ROM multisector edges first=0x%02x boundary=0x%02x last=0x%02x\n",
           buffer[0],
           buffer[2048],
           buffer[EXPECTED_SIZE - 1]);

    if(total != EXPECTED_SIZE) {
        printf("dcSharp GD-ROM multisector size mismatch\n");
        return 2;
    }

    if(chunks != 7) {
        printf("dcSharp GD-ROM multisector chunk mismatch\n");
        return 3;
    }

    if(mismatches != 0) {
        printf("dcSharp GD-ROM multisector data mismatch\n");
        return 4;
    }

    if(buffer[EXPECTED_SIZE] != 0) {
        printf("dcSharp GD-ROM multisector overread mismatch\n");
        return 5;
    }

    printf("dcSharp GD-ROM multisector probe done\n");
    return 0;
}
