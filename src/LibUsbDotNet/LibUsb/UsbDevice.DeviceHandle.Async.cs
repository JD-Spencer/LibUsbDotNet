using LibUsbDotNet.Main;

using System;
using System.Buffers;
using System.Threading.Tasks;

namespace LibUsbDotNet.LibUsb;

public partial class UsbDevice
{
    /// <inheritdoc/>
    public Task<int> ControlTransferAsync(UsbSetupPacket setupPacket, byte[] buffer, int offset, int length)
    {
        return ControlTransferAsync(setupPacket, new Memory<byte>(buffer, offset, length));
    }

    /// <inheritdoc/>
    public async Task<int> ControlTransferAsync(UsbSetupPacket setupPacket, Memory<byte> buffer)
    {
        this.EnsureNotDisposed();
        this.EnsureOpen();

        using IMemoryOwner<byte> rental = MemoryPool<byte>.Shared.Rent(buffer.Length + 8);
        Memory<byte> data = rental.Memory[0..(buffer.Length + 8)];

        setupPacket.WriteToSpan(data.Span[0..8]);
        if (buffer.Length > 0 && (setupPacket.RequestType & (byte)UsbCtrlFlags.Direction_In) == 0)
        {
            buffer.CopyTo(data[8..]);
        }

        (Error error, int dataTransferred) = await AsyncTransfer.TransferAsyncValueTask(deviceHandle, 0, EndpointType.Control, data, ControlTransferTimeout).ConfigureAwait(false);
        error.ThrowOnError();

        if (buffer is { Length: > 0 } && (setupPacket.RequestType & (byte)UsbCtrlFlags.Direction_In) != 0)
        {
            data.Slice(8, dataTransferred).CopyTo(buffer);
        }

        return dataTransferred;
    }

    /// <inheritdoc/>
    public Task<int> ControlTransferAsync(UsbSetupPacket setupPacket) =>
        ControlTransferAsync(setupPacket, Memory<byte>.Empty);
}