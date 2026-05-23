#include <kos.h>
#include <errno.h>
#include <stdio.h>
#include <string.h>

KOS_INIT_FLAGS(INIT_DEFAULT);

int main(int argc, char **argv) {
    FILE *file;
    char buffer[96];
    size_t bytes;

    (void)argc;
    (void)argv;

    memset(buffer, 0, sizeof(buffer));
    file = fopen("/cd/README.TXT", "rb");

    if(file == NULL) {
        printf("dcSharp GD-ROM file probe open failed errno=%d\n", errno);
        return 1;
    }

    bytes = fread(buffer, 1, sizeof(buffer) - 1, file);
    fclose(file);

    printf("dcSharp GD-ROM file probe bytes=%u\n", (unsigned)bytes);
    printf("dcSharp GD-ROM file text=%s", buffer);

    if(strstr(buffer, "dcSharp ISO9660 fixture") == NULL) {
        printf("dcSharp GD-ROM file probe mismatch\n");
        return 2;
    }

    printf("dcSharp GD-ROM file probe done\n");
    return 0;
}
