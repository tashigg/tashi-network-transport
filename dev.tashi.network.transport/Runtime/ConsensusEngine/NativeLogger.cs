#nullable enable
using System;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

namespace Tashi.ConsensusEngine
{
    public static class NativeLogger
    {
        private static LogDelegateHolder? _logDelegateHolder;
        
        public static void Init()
        {
            // Important: if there's an existing delegate holder, tell it not to clear the log functions
            // in its finalizer as we're going to be replacing them anyway.
            //
            // Otherwise, when the GC finalizes the object sometime after this method returns,
            // it will clear the log functions we just set.
            _logDelegateHolder?.Forget();

            unsafe
            {
                // SAFETY: we use a class that implements `IDisposable` to ensure we clear the log function pointers
                // before they are invalidated. This generally only matters when running the application in Unity,
                // as it may load and unload this class every time the application is run within the editor,
                // but the TCE dynamic library will remain loaded in the process and so may try to invoke a function
                // pointer from a previous run, which will immediately segfault or worse.
                _logDelegateHolder = new LogDelegateHolder(
                    // SAFETY: we must ensure `pointer` doesn't escape the function call, which it won't here.
                    log: static (pointer, len) => Debug.Log(DecodeUtf8String(pointer, len)),
                    logWarning: static(pointer, len) => Debug.LogWarning(DecodeUtf8String(pointer, len)),
                    logError: static(pointer, len) => Debug.LogError(DecodeUtf8String(pointer, len))
                ); 
            }
        }

        public static void SetFilter(string logFilter)
        {
            var result = tce_log_set_filter(logFilter);

            if (result != Result.Success)
            {
                throw new Exception($"error from tce_log_set_filter: {result}");
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

        [DllImport("tashi_consensus_engine", EntryPoint = "tce_log_set_functions", CallingConvention = CallingConvention.Cdecl)]
        static extern Result tce_log_set_functions
        (
            LogDelegate? log,
            LogDelegate? logWarning,
            LogDelegate? logError
        );
        
        [DllImport("tashi_consensus_engine", EntryPoint = "tce_log_set_filter", CallingConvention = CallingConvention.Cdecl)]
        static extern Result tce_log_set_filter
        (
            [MarshalAs(UnmanagedType.LPUTF8Str)] string logFilter
        );

        private class LogDelegateHolder: IDisposable
        {
            // ReSharper disable PrivateFieldCanBeConvertedToLocalVariable
            private readonly LogDelegate _log;
            private readonly LogDelegate _logWarning;
            private readonly LogDelegate _logError;
            // ReSharper restore PrivateFieldCanBeConvertedToLocalVariable

            private bool _clearOnDispose = true;

            public LogDelegateHolder(LogDelegate log, LogDelegate logWarning, LogDelegate logError)
            {
                _log = log;
                _logWarning = logWarning;
                _logError = logError;

                tce_log_set_functions(_log, _logWarning, _logError);
            }

            public void Forget()
            {
                _clearOnDispose = false;
            }

            public void Dispose()
            {
                // `.Dispose()` may be called multiple times but it must be idempotent.
                if (_clearOnDispose)
                {
                    // SAFETY: we must clear the function pointers in TCE before they are invalidated.
                    // This is thread-safe.
                    tce_log_set_functions(null, null, null);

                    _clearOnDispose = false;
                }
            }
        }
    }
}