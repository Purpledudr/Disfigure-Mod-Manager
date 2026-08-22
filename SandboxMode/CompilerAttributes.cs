using System;

namespace System.Runtime.CompilerServices;

// Unity interop exposes stripped nullable attributes with no constructors.
[AttributeUsage(AttributeTargets.All, Inherited = false)]
internal sealed class NullableAttribute : Attribute
{
    public NullableAttribute(byte flag) => NullableFlags = new[] { flag };
    public NullableAttribute(byte[] flags) => NullableFlags = flags;
    public byte[] NullableFlags { get; }
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Method |
    AttributeTargets.Interface | AttributeTargets.Delegate, Inherited = false)]
internal sealed class NullableContextAttribute : Attribute
{
    public NullableContextAttribute(byte flag) => Flag = flag;
    public byte Flag { get; }
}

