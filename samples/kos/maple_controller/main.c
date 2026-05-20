#include <dc/maple.h>
#include <dc/maple/controller.h>
#include <kos.h>
#include <stdio.h>

KOS_INIT_FLAGS(INIT_DEFAULT);

int main(int argc, char **argv) {
    (void)argc;
    (void)argv;

    maple_device_t *cont = maple_enum_type(0, MAPLE_FUNC_CONTROLLER);
    if(!cont) {
        printf("dcSharp Maple controller probe: no controller\n");
        return 0;
    }

    cont_state_t *state = (cont_state_t *)maple_dev_status(cont);
    if(!state) {
        printf("dcSharp Maple controller probe: no state\n");
        return 0;
    }

    printf("dcSharp Maple controller probe: buttons=0x%08lx joy=(%d,%d) triggers=(%d,%d)\n",
           (unsigned long)state->buttons,
           state->joyx,
           state->joyy,
           state->ltrig,
           state->rtrig);

    return 0;
}
