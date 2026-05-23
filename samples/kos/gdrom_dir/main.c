#include <kos.h>
#include <dirent.h>
#include <errno.h>
#include <stdio.h>
#include <string.h>

KOS_INIT_FLAGS(INIT_DEFAULT);

int main(int argc, char **argv) {
    DIR *dir;
    struct dirent *entry;
    int entries = 0;
    int found_readme = 0;
    int found_data = 0;

    (void)argc;
    (void)argv;

    errno = 0;
    dir = opendir("/cd");
    if(dir == NULL) {
        printf("dcSharp GD-ROM dir probe open failed errno=%d\n", errno);
        return 1;
    }

    while((entry = readdir(dir)) != NULL) {
        entries++;
        printf("dcSharp GD-ROM dir entry %d name=%s type=%d\n",
               entries,
               entry->d_name,
               entry->d_type);

        if(strcmp(entry->d_name, "readme.txt") == 0) {
            found_readme = 1;
        }

        if(strcmp(entry->d_name, "data") == 0) {
            found_data = 1;
        }
    }

    closedir(dir);

    printf("dcSharp GD-ROM dir probe entries=%d readme=%d data=%d\n",
           entries,
           found_readme,
           found_data);
    if(!found_readme || !found_data) {
        printf("dcSharp GD-ROM dir probe missing expected entries\n");
        return 2;
    }

    printf("dcSharp GD-ROM dir probe done\n");
    return 0;
}
