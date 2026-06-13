#if !NET9_0_OR_GREATER
#pragma warning disable IDE0130 // Namespace does not match folder structure

namespace System.Runtime.CompilerServices 
{

    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Constructor | AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
    internal sealed class OverloadResolutionPriorityAttribute(int priority) : Attribute
    {
        public int Priority => priority;
    }
}

#endif