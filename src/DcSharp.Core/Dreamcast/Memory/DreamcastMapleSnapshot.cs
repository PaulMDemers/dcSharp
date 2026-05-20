using DcSharp.Core.Dreamcast.Input;

namespace DcSharp.Core.Dreamcast.Memory;

public sealed record DreamcastMapleSnapshot(
    IReadOnlyList<DreamcastMapleDmaTransfer> Transfers);

public sealed record DreamcastMapleDmaTransfer(
    uint DescriptorAddress,
    string DescriptorAddressHex,
    uint Header,
    string HeaderHex,
    uint ReceiveBufferAddress,
    string ReceiveBufferAddressHex,
    byte Command,
    string CommandName,
    byte Destination,
    string DestinationHex,
    byte Response,
    string ResponseName,
    int ResponseBytes,
    DreamcastControllerState? ControllerState);
