namespace DcSharp.Core.Dreamcast.Memory;

public static class DreamcastMemoryAccessProducerFormatter
{
    public static string Format(MemoryAccess access)
    {
        if (access.Opcode is not { } opcode)
        {
            return "op=- source=- trace=-";
        }

        var decoded = Decode(opcode);
        return decoded is null
            ? $"op=0x{opcode:X4} source=- trace=-"
            : $"op=0x{opcode:X4} source={decoded.Source} trace=\"{decoded.Trace}\"";
    }

    private static DecodedProducer? Decode(ushort opcode)
    {
        var n = (opcode >> 8) & 0xF;
        var m = (opcode >> 4) & 0xF;
        var low = opcode & 0xF;

        if ((opcode & 0xF00F) == 0x2000)
        {
            return new DecodedProducer($"r{m}", $"mov.b r{m},@r{n}");
        }

        if ((opcode & 0xF00F) == 0x2001)
        {
            return new DecodedProducer($"r{m}", $"mov.w r{m},@r{n}");
        }

        if ((opcode & 0xF00F) == 0x2002)
        {
            return new DecodedProducer($"r{m}", $"mov.l r{m},@r{n}");
        }

        if ((opcode & 0xF00F) == 0x0004)
        {
            return new DecodedProducer($"r{m}", $"mov.b r{m},@(r0,r{n})");
        }

        if ((opcode & 0xF00F) == 0x0005)
        {
            return new DecodedProducer($"r{m}", $"mov.w r{m},@(r0,r{n})");
        }

        if ((opcode & 0xF00F) == 0x0006)
        {
            return new DecodedProducer($"r{m}", $"mov.l r{m},@(r0,r{n})");
        }

        if ((opcode & 0xF000) == 0x1000)
        {
            var displacement = (opcode & 0xF) * 4;
            return new DecodedProducer($"r{m}", $"mov.l r{m},@({displacement},r{n})");
        }

        if ((opcode & 0xFF00) == 0xC000)
        {
            var displacement = opcode & 0xFF;
            return new DecodedProducer("r0", $"mov.b r0,@({displacement},gbr)");
        }

        if ((opcode & 0xFF00) == 0xC100)
        {
            var displacement = (opcode & 0xFF) * 2;
            return new DecodedProducer("r0", $"mov.w r0,@({displacement},gbr)");
        }

        if ((opcode & 0xFF00) == 0xC200)
        {
            var displacement = (opcode & 0xFF) * 4;
            return new DecodedProducer("r0", $"mov.l r0,@({displacement},gbr)");
        }

        if ((opcode & 0xF00F) == 0xF007)
        {
            return new DecodedProducer($"fr{m}", $"fmov.s fr{m},@(r0,r{n})");
        }

        if ((opcode & 0xF00F) == 0xF00A)
        {
            return new DecodedProducer($"fr{m}", $"fmov.s fr{m},@r{n}");
        }

        if ((opcode & 0xF00F) == 0xF00B)
        {
            return new DecodedProducer($"fr{m}", $"fmov.s fr{m},@-r{n}");
        }

        return null;
    }

    private sealed record DecodedProducer(string Source, string Trace);
}
