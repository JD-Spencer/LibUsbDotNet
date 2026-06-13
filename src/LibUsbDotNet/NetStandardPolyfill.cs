using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;
#nullable enable

#if NETSTANDARD2_0

#pragma warning disable IDE0130 // Namespace does not match folder structure
namespace System
{
    internal static class NetStandardPolyfill
    {
        extension(ArgumentNullException)
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static void ThrowIfNull(object? argument,
                [CallerArgumentExpression(nameof(argument))] string? paramName = default)
            {
                if (argument == null) throw new ArgumentNullException(paramName);
            } 
        }

        extension(ValueTask)
        {
            public static ValueTask CompletedTask => default;

        }

    }
}

namespace System.Runtime.CompilerServices
{
    [System.AttributeUsage(System.AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
    internal class CallerArgumentExpressionAttribute(string parameterName) : Attribute
    {
        public string ParameterName { get; } = parameterName;
    }
}

#endif