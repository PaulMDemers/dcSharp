#include <kos.h>
#include <errno.h>
#include <stdio.h>

KOS_INIT_FLAGS(INIT_DEFAULT);

int main(int argc, char **argv) {
    FILE *file;
    int observed_errno;

    (void)argc;
    (void)argv;

    errno = 0;
    file = fopen("/cd/MISSING.TXT", "rb");
    observed_errno = errno;

    if(file != NULL) {
        fclose(file);
        printf("dcSharp GD-ROM missing file probe unexpectedly opened\n");
        return 1;
    }

    printf("dcSharp GD-ROM missing file probe errno=%d\n", observed_errno);
    printf("dcSharp GD-ROM missing file probe done\n");
    return 0;
}
