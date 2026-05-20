namespace DcSharp.Core.Dreamcast;

public static class HardwareProfile
{
    public const int CpuClockHz = 200_000_000;
    public const int SystemRamBytes = 16 * 1024 * 1024;
    public const int VideoRamBytes = 8 * 1024 * 1024;
    public const int AudioRamBytes = 2 * 1024 * 1024;

    public const string Cpu = "Hitachi SH7750/SH-4";
    public const string Gpu = "NEC PowerVR2 CLX2";
    public const string Audio = "Yamaha AICA";
}
