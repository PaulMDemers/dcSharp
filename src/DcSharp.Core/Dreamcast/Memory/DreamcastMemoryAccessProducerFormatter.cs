namespace DcSharp.Core.Dreamcast.Memory;

public static class DreamcastMemoryAccessProducerFormatter
{
    public static string Format(MemoryAccess access)
    {
        if (access.Opcode is not { } opcode)
        {
            return access.Kind == MemoryAccessKind.Read
                ? "op=- target=- trace=-"
                : "op=- source=- trace=-";
        }

        var decoded = Decode(access.Kind, opcode);
        return decoded is null
            ? $"op=0x{opcode:X4} {RoleLabel(access.Kind)}=- trace=-"
            : $"op=0x{opcode:X4} {decoded.Role}={decoded.Register} trace=\"{decoded.Trace}\"";
    }

    private static string RoleLabel(MemoryAccessKind kind) =>
        kind == MemoryAccessKind.Read ? "target" : "source";

    private static DecodedAccess? Decode(MemoryAccessKind kind, ushort opcode) =>
        kind == MemoryAccessKind.Read ? DecodeRead(opcode) : DecodeWrite(opcode);

    private static DecodedAccess? DecodeRead(ushort opcode)
    {
        var n = (opcode >> 8) & 0xF;
        var m = (opcode >> 4) & 0xF;
        var low = opcode & 0xF;

        if ((opcode & 0xF000) == 0xD000)
        {
            var displacement = (opcode & 0xFF) * 4;
            return Target($"r{n}", $"mov.l @({displacement},pc),r{n}");
        }

        if ((opcode & 0xF000) == 0x9000)
        {
            var displacement = (opcode & 0xFF) * 2;
            return Target($"r{n}", $"mov.w @({displacement},pc),r{n}");
        }

        if ((opcode & 0xFF00) == 0xC400)
        {
            var displacement = opcode & 0xFF;
            return Target("r0", $"mov.b @({displacement},gbr),r0");
        }

        if ((opcode & 0xFF00) == 0xC500)
        {
            var displacement = (opcode & 0xFF) * 2;
            return Target("r0", $"mov.w @({displacement},gbr),r0");
        }

        if ((opcode & 0xFF00) == 0xC600)
        {
            var displacement = (opcode & 0xFF) * 4;
            return Target("r0", $"mov.l @({displacement},gbr),r0");
        }

        if ((opcode & 0xF00F) == 0x000C)
        {
            return Target($"r{n}", $"mov.b @(r0,r{m}),r{n}");
        }

        if ((opcode & 0xF00F) == 0x000D)
        {
            return Target($"r{n}", $"mov.w @(r0,r{m}),r{n}");
        }

        if ((opcode & 0xF00F) == 0x000E)
        {
            return Target($"r{n}", $"mov.l @(r0,r{m}),r{n}");
        }

        if ((opcode & 0xF000) == 0x5000)
        {
            var displacement = low * 4;
            return Target($"r{n}", $"mov.l @({displacement},r{m}),r{n}");
        }

        if ((opcode & 0xFF00) == 0x8400)
        {
            return Target("r0", $"mov.b @({low},r{m}),r0");
        }

        if ((opcode & 0xFF00) == 0x8500)
        {
            return Target("r0", $"mov.w @({low * 2},r{m}),r0");
        }

        if ((opcode & 0xF00F) == 0x6000)
        {
            return Target($"r{n}", $"mov.b @r{m},r{n}");
        }

        if ((opcode & 0xF00F) == 0x6001)
        {
            return Target($"r{n}", $"mov.w @r{m},r{n}");
        }

        if ((opcode & 0xF00F) == 0x6002)
        {
            return Target($"r{n}", $"mov.l @r{m},r{n}");
        }

        if ((opcode & 0xF00F) == 0x6004)
        {
            return Target($"r{n}", $"mov.b @r{m}+,r{n}");
        }

        if ((opcode & 0xF00F) == 0x6005)
        {
            return Target($"r{n}", $"mov.w @r{m}+,r{n}");
        }

        if ((opcode & 0xF00F) == 0x6006)
        {
            return Target($"r{n}", $"mov.l @r{m}+,r{n}");
        }

        return null;
    }

    private static DecodedAccess? DecodeWrite(ushort opcode)
    {
        var n = (opcode >> 8) & 0xF;
        var m = (opcode >> 4) & 0xF;

        if ((opcode & 0xF00F) == 0x2000)
        {
            return Source($"r{m}", $"mov.b r{m},@r{n}");
        }

        if ((opcode & 0xF00F) == 0x2001)
        {
            return Source($"r{m}", $"mov.w r{m},@r{n}");
        }

        if ((opcode & 0xF00F) == 0x2002)
        {
            return Source($"r{m}", $"mov.l r{m},@r{n}");
        }

        if ((opcode & 0xF00F) == 0x0004)
        {
            return Source($"r{m}", $"mov.b r{m},@(r0,r{n})");
        }

        if ((opcode & 0xF00F) == 0x0005)
        {
            return Source($"r{m}", $"mov.w r{m},@(r0,r{n})");
        }

        if ((opcode & 0xF00F) == 0x0006)
        {
            return Source($"r{m}", $"mov.l r{m},@(r0,r{n})");
        }

        if ((opcode & 0xF000) == 0x1000)
        {
            var displacement = (opcode & 0xF) * 4;
            return Source($"r{m}", $"mov.l r{m},@({displacement},r{n})");
        }

        if ((opcode & 0xFF00) == 0xC000)
        {
            var displacement = opcode & 0xFF;
            return Source("r0", $"mov.b r0,@({displacement},gbr)");
        }

        if ((opcode & 0xFF00) == 0xC100)
        {
            var displacement = (opcode & 0xFF) * 2;
            return Source("r0", $"mov.w r0,@({displacement},gbr)");
        }

        if ((opcode & 0xFF00) == 0xC200)
        {
            var displacement = (opcode & 0xFF) * 4;
            return Source("r0", $"mov.l r0,@({displacement},gbr)");
        }

        if ((opcode & 0xF00F) == 0xF007)
        {
            return Source($"fr{m}", $"fmov.s fr{m},@(r0,r{n})");
        }

        if ((opcode & 0xF00F) == 0xF00A)
        {
            return Source($"fr{m}", $"fmov.s fr{m},@r{n}");
        }

        if ((opcode & 0xF00F) == 0xF00B)
        {
            return Source($"fr{m}", $"fmov.s fr{m},@-r{n}");
        }

        return null;
    }

    private static DecodedAccess Source(string register, string trace) =>
        new("source", register, trace);

    private static DecodedAccess Target(string register, string trace) =>
        new("target", register, trace);

    private sealed record DecodedAccess(string Role, string Register, string Trace);
}
