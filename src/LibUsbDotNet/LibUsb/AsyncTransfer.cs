// Copyright © 2006-2010 Travis Robinson. All rights reserved.
// Copyright © 2011-2023 LibUsbDotNet contributors. All rights reserved.
// 
// website: http://github.com/libusbdotnet/libusbdotnet
// 
// This program is free software; you can redistribute it and/or modify it
// under the terms of the GNU General Public License as published by the
// Free Software Foundation; either version 2 of the License, or 
// (at your option) any later version.
// 
// This program is distributed in the hope that it will be useful, but 
// WITHOUT ANY WARRANTY; without even the implied warranty of MERCHANTABILITY
// or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU General Public License
// for more details.
// 
// You should have received a copy of the GNU General Public License along
// with this program; if not, write to the Free Software Foundation, Inc.,
// 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301, USA. or 
// visit www.gnu.org.
// 
//

using LibUsbDotNet.Main;

using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;

namespace LibUsbDotNet.LibUsb;

/// <summary>
/// Handles submission and awaiting of asynchronous transfers.
/// </summary>
internal static class AsyncTransfer
{
    private class TransferCallbackCompletion : IDisposable
    {
        private MemoryHandle _memoryHandle;
        public TaskCompletionSource<(Error error, int transferLength)> TaskCompletionSource { get; }

        public TransferCallbackCompletion(
            TaskCompletionSource<(Error error, int transferLength)> taskCompletionSource, MemoryHandle memoryHandle)
        {
            TaskCompletionSource = taskCompletionSource;
            _memoryHandle = memoryHandle;
        }

        public void Dispose() => _memoryHandle.Dispose();
    }

    private static readonly object TransferLock = new object();
    private static int _transferIndex;

    private static readonly unsafe TransferDelegate TransferCallback = new TransferDelegate(Callback);
    private static readonly IntPtr TransferDelegatePtr =
        Marshal.GetFunctionPointerForDelegate(TransferCallback);
    private static readonly ConcurrentDictionary<int, TransferCallbackCompletion>
        TransferDictionary = new();

    [Obsolete("Try to use the TransferAsyncValueTask variant to reduce allocations.")]
    public static unsafe Task<(Error error, int transferLength)> TransferAsync(
        DeviceHandle device,
        byte endPoint,
        EndpointType endPointType,
        Memory<byte> buffer,
        int timeout,
        int isoPacketSize = 0)
    {
        ArgumentNullException.ThrowIfNull(device);

        int transferId;

        lock (TransferLock)
        {
            if (_transferIndex == int.MaxValue) // Potential edge case for long-running application?
                _transferIndex = 0;
            transferId = _transferIndex++;
        }

        var memoryHandle = buffer.Pin();
        var transferCompletion =
            new TaskCompletionSource<(Error error, int transferLength)>(TaskCreationOptions.RunContinuationsAsynchronously);
        var transferCallbackCompletion = new TransferCallbackCompletion(transferCompletion, memoryHandle);

        if (!TransferDictionary.TryAdd(transferId, transferCallbackCompletion))
            throw new InvalidOperationException(
                $"{transferId} already exists in {nameof(TransferDictionary)}");

        // Determine the amount of iso-synchronous packets
        int numIsoPackets = 0;

        if (isoPacketSize > 0)
            numIsoPackets = buffer.Length / isoPacketSize;

        var transfer = NativeMethods.AllocTransfer(numIsoPackets); // TODO: Check if transfer is null.

        // Fill common properties
        transfer->DevHandle = device.DangerousGetHandle();
        transfer->Endpoint = endPoint;
        transfer->Timeout = (uint)timeout;
        transfer->Type = (byte)endPointType;
        transfer->Buffer = (byte*)memoryHandle.Pointer;
        transfer->Length = buffer.Length;
        transfer->NumIsoPackets = numIsoPackets;
        transfer->Flags = (byte)TransferFlags.None;
        transfer->Callback = TransferDelegatePtr;
        transfer->UserData = new IntPtr(transferId);

        var error = NativeMethods.SubmitTransfer(transfer);

        if (error != Error.Success)
        {
            transferCallbackCompletion.Dispose();
            error.ThrowOnError();
        }

        return transferCompletion.Task;
    }

    private static unsafe void Callback(Transfer* transfer)
    {
        int transferId = transfer->UserData.ToInt32();
        if (TransferDictionary.TryRemove(transferId, out var transferCompletion))
        {
            transferCompletion.TaskCompletionSource.TrySetResult((GetErrorFromTransferStatus(transfer->Status), transfer->ActualLength));
            transferCompletion.Dispose();
        }
        else
        {
            throw new InvalidOperationException(
                $"Can't find transfer id # {transferId} in {nameof(TransferDictionary)}");
        }
        NativeMethods.FreeTransfer(transfer);
    }

#nullable enable

    private sealed class TransferValueTaskSource : IValueTaskSource<(LibUsbDotNet.Error error, int transferLength)>
    {

        static TransferValueTaskSource()
        {
            _sourcePool = [new()];
        }

        public static TransferValueTaskSource Take(MemoryHandle handle)
        {
            var source = _sourcePool.TryTake(out var s) ? s : new TransferValueTaskSource();
            source.SetHandle(handle);
            return source;
        }

        private TransferValueTaskSource()
        {
            this.sourceHandler = new();
            this.memoryHandle = null;
        }

        private static readonly ConcurrentBag<TransferValueTaskSource> _sourcePool;
        private ManualResetValueTaskSourceCore<(Error error, int transferLength)> sourceHandler;
        private MemoryHandle? memoryHandle;

        private void SetHandle(MemoryHandle handle)
        {
            this.memoryHandle = handle;
        }

        public void ReleaseHandle()
        {
            var m = this.memoryHandle;
            this.memoryHandle = null;
            m?.Dispose();
        }

        public void Return()
        {
            this.ReleaseHandle();
            this.sourceHandler.Reset();
            _sourcePool.Add(this);
        }

        public ValueTaskSourceStatus GetStatus(short token)
        {
            return this.sourceHandler.GetStatus(token);
        }

        public void OnCompleted(Action<object> continuation, object state, short token, ValueTaskSourceOnCompletedFlags flags)
        {
            this.sourceHandler.OnCompleted(continuation, state, token, flags);
        }

        public (Error error, int transferLength) GetResult(short token)
        {
            var result = sourceHandler.GetResult(token);
            this.Return();
            return result;
        }

        public void SetResult(Error error, int transferLength)
        {
            this.sourceHandler.SetResult((error, transferLength));
        }

        public ValueTask<(Error error, int transferLength)> Task => new(this, this.sourceHandler.Version);
    }

    public static unsafe ValueTask<(Error error, int transferLength)> TransferAsyncValueTask(
      DeviceHandle device,
      byte endPoint,
      EndpointType endPointType,
      Memory<byte> buffer,
      int timeout,
      int isoPacketSize = 0)
    {
        ArgumentNullException.ThrowIfNull(device);

        int numIsoPackets = isoPacketSize switch
        {
            > 0 => buffer.Length / isoPacketSize,
            _ => 0
        };

        ref var transfer = ref Unsafe.AsRef<Transfer>(NativeMethods.AllocTransfer(numIsoPackets));

        if (Unsafe.IsNullRef(ref transfer))
        {
            throw new UsbException("Could not allocate the async transfer.");
        }

        var memoryHandle = buffer.Pin();
        var taskSource = TransferValueTaskSource.Take(memoryHandle);
        var gchandle = GCHandle.Alloc(taskSource);

        transfer = transfer with
        {
            DevHandle = device.DangerousGetHandle(),
            Endpoint = endPoint,
            Timeout = (uint)timeout,
            Type = (byte)endPointType,
            Buffer = (byte*)memoryHandle.Pointer,
            Length = buffer.Length,
            NumIsoPackets = numIsoPackets,
            Flags = (byte)TransferFlags.None,
            UserData = (nint)gchandle,
            Callback = (nint)(delegate*<ref Transfer, void>)&Callback
        };

        var error = NativeMethods.SubmitTransfer((Transfer*)Unsafe.AsPointer(ref transfer));

        if (error != Error.Success)
        {
            taskSource.Return();
            error.ThrowOnError();
        }

        return taskSource.Task;

        static void Callback(ref Transfer transfer)
        {
            var gch = GCHandle.FromIntPtr(transfer.UserData);
            TransferValueTaskSource? source = null;
            try
            {
                source = gch.Target as TransferValueTaskSource ?? throw new InvalidOperationException("Cannot complete async transfer.");
                source.SetResult(GetErrorFromTransferStatus(transfer.Status), transfer.ActualLength);
            }
            finally
            {
                source?.ReleaseHandle();
                gch.Free();
                NativeMethods.FreeTransfer((Transfer*)Unsafe.AsPointer(ref transfer));
            }
        }
    }

    private static Error GetErrorFromTransferStatus(TransferStatus status) => status switch
    {
        TransferStatus.Completed => Error.Success,
        TransferStatus.TimedOut => Error.Timeout,
        TransferStatus.Stall => Error.Pipe,
        TransferStatus.Overflow => Error.Overflow,
        TransferStatus.NoDevice => Error.NoDevice,
        TransferStatus.Error or TransferStatus.Cancelled => Error.Io,
        _ => Error.Other,
    };


}