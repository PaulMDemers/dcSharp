using System.Buffers.Binary;

namespace DcSharp.Core.Dreamcast.Audio;

public static class DreamcastAudioWavWriter
{
    public const int SampleRate = 44_100;
    private const short MaxSample = short.MaxValue;
    private const short MinSample = short.MinValue;

    public static void WritePcm16Stereo(Stream output, DreamcastAudioSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(snapshot);

        var sampleCount = snapshot.Channels
            .Where(IsModeledPcmChannel)
            .Select(channel => channel.PlaybackSamplesAdvanced)
            .DefaultIfEmpty(0UL)
            .Max();
        if (sampleCount > int.MaxValue)
        {
            throw new InvalidDataException($"Audio WAV dump is too large: {sampleCount} samples.");
        }

        var frames = (int)sampleCount;
        var pcm = new byte[frames * 4];
        foreach (var channel in snapshot.Channels.Where(IsModeledPcmChannel))
        {
            MixChannel(snapshot.AudioRam, channel, pcm, frames);
        }

        WriteWav(output, pcm, channels: 2, sampleRate: SampleRate, bitsPerSample: 16);
    }

    private static bool IsModeledPcmChannel(DreamcastAicaChannelSnapshot channel) =>
        !channel.Compressed
        && channel.SampleStrideBytes is 1 or 2
        && channel.PlaybackSamplesAdvanced > 0;

    private static void MixChannel(byte[] audioRam, DreamcastAicaChannelSnapshot channel, byte[] pcm, int frames)
    {
        var channelFrames = (int)Math.Min((ulong)frames, channel.PlaybackSamplesAdvanced);
        for (var frame = 0; frame < channelFrames; frame++)
        {
            var sourceIndex = ResolveSampleIndex(channel, (ulong)frame);
            var sourceOffset = channel.SampleAddress + (sourceIndex * (uint)channel.SampleStrideBytes);
            if (sourceOffset >= audioRam.Length || sourceOffset + (uint)channel.SampleStrideBytes > audioRam.Length)
            {
                break;
            }

            var sample = ReadSample(audioRam, (int)sourceOffset, channel.SampleStrideBytes);
            var scaled = (sample * channel.Volume) / 255;
            var left = (scaled * channel.LeftBalance) / 15;
            var right = (scaled * channel.RightBalance) / 15;
            var outputOffset = frame * 4;
            WriteMixedSample(pcm, outputOffset, left);
            WriteMixedSample(pcm, outputOffset + 2, right);
        }
    }

    private static uint ResolveSampleIndex(DreamcastAicaChannelSnapshot channel, ulong frame)
    {
        if (!channel.LoopEnabled || channel.LoopEnd <= channel.LoopStart || frame < channel.LoopEnd)
        {
            return frame > uint.MaxValue ? uint.MaxValue : (uint)frame;
        }

        var loopLength = channel.LoopEnd - channel.LoopStart;
        return channel.LoopStart + (uint)((frame - channel.LoopEnd) % loopLength);
    }

    private static int ReadSample(byte[] audioRam, int offset, int stride) =>
        stride switch
        {
            1 => (audioRam[offset] - 128) << 8,
            2 => BinaryPrimitives.ReadInt16LittleEndian(audioRam.AsSpan(offset, 2)),
            _ => 0
        };

    private static void WriteMixedSample(byte[] pcm, int offset, int value)
    {
        var existing = BinaryPrimitives.ReadInt16LittleEndian(pcm.AsSpan(offset, 2));
        var mixed = Math.Clamp(existing + value, MinSample, MaxSample);
        BinaryPrimitives.WriteInt16LittleEndian(pcm.AsSpan(offset, 2), (short)mixed);
    }

    private static void WriteWav(Stream output, byte[] pcm, short channels, int sampleRate, short bitsPerSample)
    {
        var byteRate = sampleRate * channels * bitsPerSample / 8;
        var blockAlign = (short)(channels * bitsPerSample / 8);
        var riffSize = 36 + pcm.Length;

        WriteAscii(output, "RIFF");
        WriteInt32(output, riffSize);
        WriteAscii(output, "WAVE");
        WriteAscii(output, "fmt ");
        WriteInt32(output, 16);
        WriteInt16(output, 1);
        WriteInt16(output, channels);
        WriteInt32(output, sampleRate);
        WriteInt32(output, byteRate);
        WriteInt16(output, blockAlign);
        WriteInt16(output, bitsPerSample);
        WriteAscii(output, "data");
        WriteInt32(output, pcm.Length);
        output.Write(pcm);
    }

    private static void WriteAscii(Stream output, string text)
    {
        foreach (var value in text)
        {
            output.WriteByte((byte)value);
        }
    }

    private static void WriteInt16(Stream output, short value)
    {
        Span<byte> bytes = stackalloc byte[2];
        BinaryPrimitives.WriteInt16LittleEndian(bytes, value);
        output.Write(bytes);
    }

    private static void WriteInt32(Stream output, int value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        output.Write(bytes);
    }
}
