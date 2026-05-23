namespace DcSharp.Core.Dreamcast.Memory;

public sealed record DreamcastGdromSnapshot(
    bool HasMedia,
    int? SectorSize,
    ulong? SectorCount,
    IReadOnlyList<DreamcastGdromReadCommand> ReadCommands,
    IReadOnlyList<DreamcastGdromTocCommand> TocCommands)
{
    public static DreamcastGdromSnapshot Empty { get; } = new(false, null, null, [], []);
}

public sealed record DreamcastGdromReadCommand(
    uint ParameterAddress,
    string ParameterAddressHex,
    uint? Sector,
    string? SectorHex,
    uint? Destination,
    string? DestinationHex,
    uint? SectorCount,
    int? SectorSize,
    int BytesRequested,
    int BytesRead,
    bool Success,
    string Status);

public sealed record DreamcastGdromTocCommand(
    uint ParameterAddress,
    string ParameterAddressHex,
    uint? BufferAddress,
    string? BufferAddressHex,
    int? FirstTrack,
    int? LastTrack,
    uint? DataTrackStartFad,
    string? DataTrackStartFadHex,
    uint? LeadoutFad,
    string? LeadoutFadHex,
    bool Success,
    string Status);
