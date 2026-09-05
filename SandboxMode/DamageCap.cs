using System;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.InteropServices;

namespace SandboxMode;

internal static class DamageCap
{
    private static IntPtr branch;
    private static IntPtr execute;
    private static IntPtr methodInfo;
    internal static bool Available { get; private set; }
    internal static bool Removed => branch != IntPtr.Zero && Marshal.ReadByte(branch) == 0xEB;

    internal static void Initialize()
    {
        var field = typeof(EnemyDecreaseHealthJob).GetField(
            "NativeMethodInfoPtr_Execute_Public_Virtual_Final_New_Void_0",
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new MissingFieldException("Enemy damage job method was not found.");
        methodInfo = (IntPtr)field.GetValue(null)!;
        execute = Marshal.ReadIntPtr(methodInfo);
        var code = new byte[1024];
        Marshal.Copy(execute, code, 0, code.Length);
        // ponytail: supports this x64 clamp layout; revalidate the signature if the game changes it.
        var prefix = new byte[] { 0xF3, 0x0F, 0x10, 0x43, 0x20, 0x0F, 0x2F, 0x05 };
        var suffix = new byte[] { 0x76, 0x07, 0xC7, 0x43, 0x20, 0x28, 0x6B, 0x6E, 0x4E,
            0x48, 0x8B, 0x8B, 0x80, 0, 0, 0, 0x8B, 0x43, 0x20, 0x89, 0x01 };
        var offset = -1;
        for (var i = 0; i <= code.Length - 12 - suffix.Length; i++)
        {
            if (!code.AsSpan(i, prefix.Length).SequenceEqual(prefix)
                || !code.AsSpan(i + 12, suffix.Length).SequenceEqual(suffix)) continue;
            var limit = IntPtr.Add(execute, i + 12 + BitConverter.ToInt32(code, i + 8));
            if (Marshal.ReadInt32(limit) != BitConverter.SingleToInt32Bits(1_000_000_000f)) continue;
            if (offset >= 0) throw new InvalidOperationException("Enemy damage cap signature is ambiguous.");
            offset = i + 12;
        }
        if (offset < 0) throw new InvalidOperationException("Enemy damage cap signature does not match this game build.");
        branch = IntPtr.Add(execute, offset);
        SelfCheck();
        Available = true;
    }

    internal static void SetRemoved(bool removed)
    {
        if (branch == IntPtr.Zero)
        {
            if (removed) throw new InvalidOperationException("Damage cap removal is unavailable for this game build.");
            return;
        }
        var current = Marshal.ReadByte(branch);
        if (current != 0x76 && current != 0xEB)
            throw new InvalidOperationException("Another patch changed the enemy damage cap.");
        if (Removed == removed) return;
        if (!VirtualProtect(branch, (UIntPtr)1, 0x40, out var previous)) throw new Win32Exception();
        try
        {
            // Change only the one-byte branch opcode: conditional skip becomes unconditional skip.
            Marshal.WriteByte(branch, removed ? (byte)0xEB : (byte)0x76);
            if (!FlushInstructionCache(new IntPtr(-1), branch, (UIntPtr)1)) throw new Win32Exception();
        }
        finally
        {
            if (!VirtualProtect(branch, (UIntPtr)1, previous, out _)) throw new Win32Exception();
        }
    }

    // Exercises the game's real native calculation, including multipliers and restoring the cap.
    private static void SelfCheck()
    {
        var job = Marshal.AllocHGlobal(0x90);
        var result = Marshal.AllocHGlobal(sizeof(float));
        try
        {
            var run = Marshal.GetDelegateForFunctionPointer<ExecuteJob>(execute);
            foreach (var removed in new[] { false, true, false })
            {
                SetRemoved(removed);
                foreach (var input in new[] { 10f, 1_000_000_000f, 2_000_000_000f, 750_000_000f })
                {
                    Marshal.Copy(new byte[0x90], 0, job, 0x90);
                    var multiplier = input == 750_000_000f ? 2f : 1f;
                    Marshal.WriteInt32(job, BitConverter.SingleToInt32Bits(multiplier));
                    Marshal.WriteInt32(job, 4, BitConverter.SingleToInt32Bits(1f));
                    Marshal.WriteInt32(job, 0x20, BitConverter.SingleToInt32Bits(input));
                    Marshal.WriteIntPtr(job, 0x80, result);
                    run(job, methodInfo);
                    var actual = BitConverter.Int32BitsToSingle(Marshal.ReadInt32(result));
                    var expected = removed ? input * multiplier : Math.Min(input * multiplier, 1_000_000_000f);
                    if (actual != expected)
                        throw new InvalidOperationException($"Damage cap self-check failed: removed={removed}, input={input}, actual={actual}, expected={expected}.");
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(result);
            Marshal.FreeHGlobal(job);
            SetRemoved(false);
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void ExecuteJob(IntPtr job, IntPtr method);

    [DllImport("kernel32", SetLastError = true)]
    private static extern bool VirtualProtect(IntPtr address, UIntPtr size, uint protection, out uint previous);

    [DllImport("kernel32", SetLastError = true)]
    private static extern bool FlushInstructionCache(IntPtr process, IntPtr address, UIntPtr size);
}
