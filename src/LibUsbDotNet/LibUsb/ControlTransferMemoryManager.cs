using System;
using System.Buffers;
using System.Runtime.InteropServices;
using System.Threading;

using LibUsbDotNet.Main;

namespace LibUsbDotNet.LibUsb;

#nullable enable

/// <summary>
/// Provides memory for the use of Control Transfers. An additional 8 bytes is provided before the memory to be used for the setup packet.
/// </summary>
/// <param name="length">The length of the memory.</param>
/// <param name="isRental">If the memory should be rented from an array pool or allocated.</param>
internal unsafe sealed class ControlTransferMemoryManager(int length, bool isRental) : MemoryManager<byte>
{
    private const int headerLength = 8;

    private byte[]? buffer = isRental ? ArrayPool<byte>.Shared.Rent(headerLength + length) : new byte[headerLength + length];
    private int pinCount = 0;
    private bool disposeOnUnpin = false;

    public override Span<byte> GetSpan()
    {
        return buffer.AsSpan(headerLength, length);
    }

    public Memory<byte> GetMemoryWithHeader(int length)
    {
        var b = this.buffer ?? throw new ObjectDisposedException(nameof(ControlTransferMemoryManager));
        return new Memory<byte>(b, 0, headerLength + length);
    }

    public override MemoryHandle Pin(int elementIndex = 0)
    {
        var b = this.buffer ?? throw new ObjectDisposedException(nameof(ControlTransferMemoryManager));
        Interlocked.Increment(ref pinCount);
        var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        return new MemoryHandle((void*)(handle.AddrOfPinnedObject() + headerLength + elementIndex), handle, this);
    }

    public MemoryHandle PinWithHeader()
    {
        return Pin(-headerLength);
    }

    public void WriteSetupPacket(UsbSetupPacket packet)
    { 
        packet.WriteToSpan(buffer.AsSpan(0, 8));
    }

    public override void Unpin()
    {
        if (Interlocked.Decrement(ref pinCount) == 0 && disposeOnUnpin)
        {
            Dispose(true);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (pinCount > 0)
        {
            disposeOnUnpin = true;
        }
        else
        {
            var b = this.buffer;
            if (b is null) return;
            if (isRental) ArrayPool<byte>.Shared.Return(b);
            buffer = null;
        }
    }

}