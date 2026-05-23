#include <kos.h>
#include <errno.h>
#include <stdio.h>
#include <string.h>

#define EXPECTED_SIZE 5000

KOS_INIT_FLAGS(INIT_DEFAULT);

static unsigned char buffer[64];

static unsigned char expected_byte(unsigned index) {
    return (unsigned char)((index * 31 + 7) & 0xff);
}

static unsigned checksum_range(const unsigned char *data, size_t bytes) {
    unsigned checksum = 0;
    size_t index;

    for(index = 0; index < bytes; index++) {
        checksum = (checksum + data[index]) & 0xffff;
    }

    return checksum;
}

static unsigned mismatch_range(const unsigned char *data, size_t bytes, unsigned start) {
    unsigned mismatches = 0;
    size_t index;

    for(index = 0; index < bytes; index++) {
        if(data[index] != expected_byte(start + (unsigned)index)) {
            mismatches++;
        }
    }

    return mismatches;
}

int main(int argc, char **argv) {
    FILE *file;
    long size;
    long position;
    long tail_start;
    size_t bytes;
    int eof_after_tail;
    int eof_after_empty;
    unsigned mismatches;

    (void)argc;
    (void)argv;

    errno = 0;
    file = fopen("/cd/DATA/BIG.BIN", "rb");
    if(file == NULL) {
        printf("dcSharp GD-ROM seek open failed errno=%d\n", errno);
        return 1;
    }

    if(fseek(file, 0, SEEK_END) != 0) {
        printf("dcSharp GD-ROM seek end failed errno=%d\n", errno);
        fclose(file);
        return 2;
    }

    size = ftell(file);
    printf("dcSharp GD-ROM seek size=%ld\n", size);
    if(size != EXPECTED_SIZE) {
        printf("dcSharp GD-ROM seek size mismatch\n");
        fclose(file);
        return 3;
    }

    memset(buffer, 0, sizeof(buffer));
    if(fseek(file, 2040, SEEK_SET) != 0) {
        printf("dcSharp GD-ROM seek cross seek failed errno=%d\n", errno);
        fclose(file);
        return 4;
    }

    bytes = fread(buffer, 1, 32, file);
    position = ftell(file);
    mismatches = mismatch_range(buffer, bytes, 2040);
    printf("dcSharp GD-ROM seek cross bytes=%u pos=%ld checksum=0x%04x mismatches=%u first=0x%02x last=0x%02x\n",
           (unsigned)bytes,
           position,
           checksum_range(buffer, bytes),
           mismatches,
           buffer[0],
           buffer[31]);

    if(bytes != 32 || position != 2072 || mismatches != 0) {
        printf("dcSharp GD-ROM seek cross mismatch\n");
        fclose(file);
        return 5;
    }

    memset(buffer, 0, sizeof(buffer));
    if(fseek(file, -17, SEEK_END) != 0) {
        printf("dcSharp GD-ROM seek tail seek failed errno=%d\n", errno);
        fclose(file);
        return 6;
    }

    tail_start = ftell(file);
    bytes = fread(buffer, 1, 32, file);
    position = ftell(file);
    eof_after_tail = feof(file) ? 1 : 0;
    mismatches = mismatch_range(buffer, bytes, (unsigned)tail_start);
    printf("dcSharp GD-ROM seek tail start=%ld bytes=%u pos=%ld checksum=0x%04x mismatches=%u eof=%d\n",
           tail_start,
           (unsigned)bytes,
           position,
           checksum_range(buffer, bytes),
           mismatches,
           eof_after_tail);

    if(tail_start != 4983 || bytes != 17 || position != EXPECTED_SIZE || !eof_after_tail || mismatches != 0) {
        printf("dcSharp GD-ROM seek tail mismatch\n");
        fclose(file);
        return 7;
    }

    clearerr(file);
    memset(buffer, 0, sizeof(buffer));
    if(fseek(file, 4090, SEEK_SET) != 0) {
        printf("dcSharp GD-ROM seek reread seek failed errno=%d\n", errno);
        fclose(file);
        return 8;
    }

    bytes = fread(buffer, 1, 32, file);
    position = ftell(file);
    mismatches = mismatch_range(buffer, bytes, 4090);
    printf("dcSharp GD-ROM seek reread bytes=%u pos=%ld checksum=0x%04x mismatches=%u first=0x%02x last=0x%02x\n",
           (unsigned)bytes,
           position,
           checksum_range(buffer, bytes),
           mismatches,
           buffer[0],
           buffer[31]);

    if(bytes != 32 || position != 4122 || mismatches != 0) {
        printf("dcSharp GD-ROM seek reread mismatch\n");
        fclose(file);
        return 9;
    }

    clearerr(file);
    if(fseek(file, EXPECTED_SIZE, SEEK_SET) != 0) {
        printf("dcSharp GD-ROM seek eof seek failed errno=%d\n", errno);
        fclose(file);
        return 10;
    }

    bytes = fread(buffer, 1, 1, file);
    eof_after_empty = feof(file) ? 1 : 0;
    printf("dcSharp GD-ROM seek eof bytes=%u eof=%d\n",
           (unsigned)bytes,
           eof_after_empty);

    fclose(file);

    if(bytes != 0 || !eof_after_empty) {
        printf("dcSharp GD-ROM seek eof mismatch\n");
        return 11;
    }

    printf("dcSharp GD-ROM seek probe done\n");
    return 0;
}
