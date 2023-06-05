using System;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

namespace Tashi.ConsensusEngine
{
    public class NativeLogger
    {
        public static void Init(string envFilter)
        {
            Result result;

            unsafe
            {
                result = tce_set_log_functions(
                    envFilter,
                    // SAFETY: we must ensure `pointer` doesn't escape the function call, which it won't here.
                    // We must also be sure to *only* use static delegates here because delegates with captures
                    // will throw `NullReferenceException`s.
                    static (pointer, len) => Debug.Log(DecodeUtf8String(pointer, len)),
                    static(pointer, len) => Debug.LogWarning(DecodeUtf8String(pointer, len)),
                    static(pointer, len) => Debug.LogError(DecodeUtf8String(pointer, len))
                );
            }

            if (result != Result.Success)
            {
                throw new InvalidOperationException($"error initializing native logger: {result}");
            }
        }

        private static unsafe string DecodeUtf8String(byte* pointer, UIntPtr len)
        {
            try
            {
                checked
                {
                    // SAFETY: this interface requires us to cast `len` to `int`, but apparently casting
                    // `UIntPtr` to `int` has no interaction with the `checked {}` block.
                    //
                    // However, converting to `UInt64` first does, likely because it returns a primitive `ulong`.
                    return Encoding.UTF8.GetString(pointer, (int)len.ToUInt64());
                }
            }
            catch (OverflowException)
            {
                return "(oversized message)";
            }
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private unsafe delegate void LogDelegate(byte* utf8String, UIntPtr len);

        [DllImport("tashi_consensus_engine", EntryPoint = "tce_set_log_functions", CallingConvention = CallingConvention.Cdecl)]
        static extern Result tce_set_log_functions
        (
            [MarshalAs(UnmanagedType.LPUTF8Str)] string envFilter,
            LogDelegate log,
            LogDelegate logWarning,
            LogDelegate logError
        );
    }
}