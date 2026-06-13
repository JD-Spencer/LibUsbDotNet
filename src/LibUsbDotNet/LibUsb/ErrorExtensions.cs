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

using MethodImplAttribute = System.Runtime.CompilerServices.MethodImplAttribute;
using MethodImplOptions = System.Runtime.CompilerServices.MethodImplOptions;

namespace LibUsbDotNet.LibUsb;

/// <summary>
/// Provides extension methods for the <see cref="Error"/> enumeration.
/// </summary>
public static class ErrorExtensions
{
    /// <summary>
    /// Throws a <see cref="UsbException"/> if the value of <paramref name="error"/> is not <see cref="Error.Success"/>.
    /// </summary>
    /// <param name="error">
    /// The error code based on which to throw an exception.
    /// </param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ThrowOnError(this Error error)
    {
        if (error is not Error.Success)
        {
            throw new UsbException(error);
        }
    }

    /// <summary>
    /// Gets the function's return value (if ret &gt;= 0), or throws an error if the return value was negative
    /// and indicated an error.
    /// </summary>
    /// <param name="error">
    /// The return value to inspect.
    /// </param>
    /// <returns>
    /// The function's return value (if ret &gt;= 0);.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetValueOrThrow(this Error error) => (int)error switch
    {
        < 0 => throw new UsbException(error),
        int e => e
    };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Error ToError(TransferStatus transferStatus) => transferStatus switch
    {
        TransferStatus.Completed => Error.Success,
        TransferStatus.Error => Error.Pipe,
        TransferStatus.TimedOut => Error.Timeout,
        TransferStatus.Cancelled => Error.Io,
        TransferStatus.Stall => Error.Pipe,
        TransferStatus.NoDevice => Error.NoDevice,
        TransferStatus.Overflow => Error.Overflow,
        _ => Error.Other,
    };
}