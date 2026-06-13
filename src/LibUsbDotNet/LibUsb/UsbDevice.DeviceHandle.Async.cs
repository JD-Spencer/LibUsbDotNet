using LibUsbDotNet.Main;

using System;
using System.Buffers;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace LibUsbDotNet.LibUsb;

#nullable enable

public partial class UsbDevice
{

    /// <summary>
    /// Gets a memory rental used for control transfers.
    /// The memory has a pre-allocated header for the <see cref="UsbSetupPacket"/> so the memory does not need to be copied for async control transfers.
    /// Because of the pre-allocated header, offsetting the memory will remove the benefit.
    /// </summary>
    /// <param name="length">The length of the memory requested.</param> 
    /// <param name="zeroMemory">If the memory should be zeroed before given. Only set to false if the full memory will be written to.</param> 
    public static IMemoryOwner<byte> RentControlTransferMemory(int length, bool zeroMemory = true)
    {
        var m = new ControlTransferMemoryManager(length, true);
        if (zeroMemory) m.Memory.Span.Clear();
        return m;
    }

    /// <summary>
    /// Gets memory used for control transfers.
    /// The memory has a pre-allocated header for the <see cref="UsbSetupPacket"/> so the memory does not need to be copied for async control transfers.
    /// Because of the pre-allocated header, it is not advised to offset the memory.
    /// </summary>
    /// <param name="length">The length of the memory requested.</param> 
    public static Memory<byte> AllocControlTransferMemory(int length)
    {
        return new ControlTransferMemoryManager(length, false).Memory;
    }

    public async Task<int> ControlTransferAsync(UsbSetupPacket setupPacket, Memory<byte> buffer)
    {
        this.EnsureNotDisposed();
        this.EnsureOpen();

        Memory<byte> memory;
        IMemoryOwner<byte>? rental = null;

        try
        {
            bool isCTM = MemoryMarshal.TryGetMemoryManager<byte, ControlTransferMemoryManager>(buffer, out var manager, out var offset, out var length) && offset == 0;

            if (isCTM && manager is not null)
            {
                memory = manager.GetMemoryWithHeader(length);
                manager.WriteSetupPacket(setupPacket);
            }
            else
            {
                rental = MemoryPool<byte>.Shared.Rent(8 + buffer.Length);
                memory = rental.Memory[..(8 + buffer.Length)];
                if (!buffer.IsEmpty)
                {
                    buffer.CopyTo(memory[8..]);
                }
                setupPacket.WriteToSpan(memory.Span[0..8]);
            }
             
            var (error, transferLength) = await AsyncTransfer.TransferAsyncValueTask(
                deviceHandle,
                0,
                EndpointType.Control,
                memory,
                UsbConstants.DefaultTimeout).ConfigureAwait(false);

            if (rental is not null && !buffer.IsEmpty)
            {
                memory[8..].CopyTo(buffer);
            }

            error.ThrowOnError();
            return transferLength;
        }
        finally
        {
            rental?.Dispose(); // Return rented memory.
        }

    }

    /// <inheritdoc/>
    public Task<int> ControlTransferAsync(UsbSetupPacket setupPacket, byte[] buffer, int offset, int length) =>
        ControlTransferAsync(setupPacket, new Memory<byte>(buffer, offset, length));

    /// <inheritdoc/>
    public Task<int> ControlTransferAsync(UsbSetupPacket setupPacket) =>
        ControlTransferAsync(setupPacket, Memory<byte>.Empty);

}