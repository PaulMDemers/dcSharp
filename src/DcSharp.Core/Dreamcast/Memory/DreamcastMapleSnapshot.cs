using DcSharp.Core.Dreamcast.Input;

namespace DcSharp.Core.Dreamcast.Memory;

public sealed record DreamcastMapleSnapshot(
    IReadOnlyList<DreamcastMapleDmaTransfer> Transfers,
    IReadOnlyList<DreamcastMapleDmaBatch> DmaBatches)
{
    public DreamcastMapleSnapshot(IReadOnlyList<DreamcastMapleDmaTransfer> transfers)
        : this(transfers, [])
    {
    }
}

public sealed record DreamcastMapleDmaBatch(
    uint DescriptorAddress,
    string DescriptorAddressHex,
    int DescriptorsScanned,
    int TransferCount,
    bool Completed,
    bool HitDescriptorLimit,
    uint LastDescriptorAddress,
    string LastDescriptorAddressHex);

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
    string DestinationName,
    byte Response,
    string ResponseName,
    int ResponseBytes,
    DreamcastControllerState? ControllerState);
