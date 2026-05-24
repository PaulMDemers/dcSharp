namespace DcSharp.Core.Dreamcast.Audio;

internal sealed class AicaAdpcmDecoder
{
    private const int MinStep = 127;
    private const int MaxStep = 24_576;
    private static readonly int[] StepScale = [230, 230, 230, 230, 307, 409, 512, 614];

    private int sample;
    private int step = MinStep;

    public short DecodeNibble(int nibble)
    {
        var code = nibble & 0x0F;
        var magnitude = code & 0x07;
        var delta = (((magnitude << 1) + 1) * step) >> 3;
        sample = (code & 0x08) == 0
            ? Math.Min(short.MaxValue, sample + delta)
            : Math.Max(short.MinValue, sample - delta);
        step = Math.Clamp((step * StepScale[magnitude]) >> 8, MinStep, MaxStep);
        return (short)sample;
    }

    public static int ReadNibble(byte[] audioRam, uint sampleAddress, uint sampleIndex)
    {
        var byteOffset = sampleAddress + (sampleIndex / 2);
        if (byteOffset >= audioRam.Length)
        {
            return 0;
        }

        var packed = audioRam[byteOffset];
        return (sampleIndex & 1) == 0
            ? packed & 0x0F
            : packed >> 4;
    }
}
