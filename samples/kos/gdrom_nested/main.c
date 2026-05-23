#include <kos.h>
#include <dirent.h>
#include <errno.h>
#include <stdio.h>
#include <string.h>

KOS_INIT_FLAGS(INIT_DEFAULT);

int main(int argc, char **argv) {
    DIR *dir;
    struct dirent *entry;
    FILE *file;
    char buffer[128];
    size_t bytes;
    int entries = 0;
    int found_second = 0;
    int found_big = 0;

    (void)argc;
    (void)argv;

    errno = 0;
    dir = opendir("/cd/DATA");
    if(dir == NULL) {
        printf("dcSharp GD-ROM nested dir open failed errno=%d\n", errno);
        return 1;
    }

    while((entry = readdir(dir)) != NULL) {
        entries++;
        printf("dcSharp GD-ROM nested entry %d name=%s type=%d\n",
               entries,
               entry->d_name,
               entry->d_type);

        if(strcmp(entry->d_name, "second.txt") == 0) {
            found_second = 1;
        }

        if(strcmp(entry->d_name, "big.bin") == 0) {
            found_big = 1;
        }
    }

    closedir(dir);

    printf("dcSharp GD-ROM nested dir entries=%d second=%d big=%d\n",
           entries,
           found_second,
           found_big);
    if(!found_second || !found_big) {
        printf("dcSharp GD-ROM nested probe missing expected entries\n");
        return 2;
    }

    memset(buffer, 0, sizeof(buffer));
    file = fopen("/cd/DATA/SECOND.TXT", "rb");
    if(file == NULL) {
        printf("dcSharp GD-ROM nested file open failed errno=%d\n", errno);
        return 3;
    }

    bytes = fread(buffer, 1, sizeof(buffer) - 1, file);
    fclose(file);

    printf("dcSharp GD-ROM nested file bytes=%u\n", (unsigned)bytes);
    printf("dcSharp GD-ROM nested file text=%s", buffer);

    if(strstr(buffer, "dcSharp ISO9660 nested fixture") == NULL) {
        printf("dcSharp GD-ROM nested file mismatch\n");
        return 4;
    }

    printf("dcSharp GD-ROM nested probe done\n");
    return 0;
}
