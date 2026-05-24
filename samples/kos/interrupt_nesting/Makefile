TARGET = dcsharp_interrupt_nesting.elf
OBJS = start.o

all: rm-elf $(TARGET)

clean: rm-elf
	-rm -f $(OBJS)

rm-elf:
	-rm -f $(TARGET)

$(TARGET): $(OBJS) link.ld
	sh-elf-gcc -nostdlib -Wl,-T,link.ld -Wl,-e,_start -o $(TARGET) $(OBJS)

%.o: %.S
	sh-elf-gcc -c $< -o $@
