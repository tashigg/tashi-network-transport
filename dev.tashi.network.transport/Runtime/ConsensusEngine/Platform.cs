#nullable enable

using System;
using System.Collections.Generic;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;

namespace Tashi.ConsensusEngine
{
    public enum NetworkMode
    {
        Loopback,
        Local,
    }

    /// <summary>
    /// The Tashi Platform.
    /// </summary>
    public class Platform : IDisposable
    {
        private IntPtr _platform;
        private bool _started;
        private bool _disposed;

        /// <summary>
        /// Create a new platform. It won't begin communicating with other nodes
        /// until the Start method is called.
        /// </summary>
        ///
        public Platform(NetworkMode mode, UInt16 port, TimeSpan syncInterval, SecretKey secretKey)
        {
            _platform = tce_init(
                mode,
                port,
                // FIXME: Ensure the conversion can succeed
                (UInt32)syncInterval.TotalMilliseconds,
                secretKey.Der,
                (uint)secretKey.Der.Length,
                out var result
            );

            if (result != Result.Success)
            {
                throw new ArgumentException($"Failed to initialize the platform: {result}");
            }
        }

        /// <summary>
        /// If a port wasn't specified, or if port 0 was specified then this
        /// function will help in determining which port was actually used.
        /// </summary>
        public IPEndPoint GetBoundAddress()
        {
            var buffer = new StringBuilder();
            var requiredSize = tce_bound_address_get(_platform, buffer, buffer.Capacity, out var port);
            if (requiredSize < 0)
            {
                throw new SystemException($"Failed to get the bound address: {requiredSize}");
            }
            else if (requiredSize > buffer.Capacity)
            {
                buffer.Capacity = requiredSize;
                if (requiredSize != tce_bound_address_get(_platform, buffer, buffer.Capacity, out port))
                {
                    throw new Exception($"Failed to get the bound address: {requiredSize}");
                }
            }

            buffer.Length = requiredSize - 1;

            return new IPEndPoint(
                IPAddress.Parse(buffer.ToString()),
                port
            );
        }

        private void SetAddressBook(IList<AddressBookEntry> entries)
        {
            foreach (var entry in entries)
            {
                var result = Result.EmptyAddressBook;

                if (entry is DirectAddressBookEntry direct)
                {
                    result = tce_add_node(
                        _platform,
                        $"{direct.Address}:{direct.Port}",
                        entry.PublicKey.Der,
                        (uint)entry.PublicKey.Der.Length
                    );
                }
                else
                {
                    // TODO: handle external address book entries, set result
                }

                if (result != Result.Success)
                {
                    throw new ArgumentException($"Failed to add address book entry for {entry}: {result}");
                }
            }
        }

        public void Start(IList<AddressBookEntry> entries)
        {
            if (_started)
            {
                throw new InvalidOperationException("The platform has already been started");
            }

            SetAddressBook(entries);

            var result = tce_start(_platform);
            switch (result)
            {
                case Result.Success:
                    _started = true;
                    break;
                case Result.EmptyAddressBook:
                    throw new InvalidOperationException("The address book hasn't been set");
                default:
                    throw new InvalidOperationException($"Failed to start the platform: {result}");
            }
        }

        /// <summary>
        /// Returns a <see>ConsensusEvent</see> if one is available, or <c>null</c>
        /// otherwise.
        /// </summary>
        public ConsensusEvent? GetEvent()
        {
            if (!_started)
            {
                throw new InvalidOperationException("The platform hasn't been started");
            }

            var ptr = tce_event_get(_platform, out var result);
            if (result != Result.Success)
            {
                throw new SystemException($"Failed to get a consensus event: {result}");
            }

            if (ptr == IntPtr.Zero)
            {
                return null;
            }

            var consensusEvent = new ConsensusEvent(Marshal.PtrToStructure<NativeConsensusEvent>(ptr));
            tce_event_free(ptr);
            return consensusEvent;
        }

        public void Send(byte[] data)
        {
            var result = tce_send(_platform, data, (UInt32)data.Length);
            if (result != Result.Success)
            {
                throw new SystemException($"Failed to send the data: {result}");
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    if (_platform != IntPtr.Zero)
                    {
                        tce_free(_platform);
                        _platform = IntPtr.Zero;
                    }
                }

                _disposed = true;
            }
        }

        ~Platform()
        {
            Dispose(false);
        }

        [DllImport("tashi_consensus_engine", EntryPoint = "tce_init")]
        static extern IntPtr tce_init(
            NetworkMode mode,
            UInt16 port,
            UInt32 syncIntervalMilliseconds,
            byte[] publicKeyDer,
            UInt32 publicKeyDerLen,
            out Result result
        );

        [DllImport("tashi_consensus_engine", EntryPoint = "tce_bound_address_get")]
        static extern Int32 tce_bound_address_get(
            IntPtr platform,
            [MarshalAs(UnmanagedType.LPUTF8Str)] StringBuilder buffer,
            Int32 bufferLen,
            out UInt16 port
        );

        [DllImport("tashi_consensus_engine", EntryPoint = "tce_add_node")]
        static extern Result tce_add_node(
            IntPtr platform,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string address,
            byte[] publicKeyDer,
            UInt32 publicKeyDerLen
        );

        [DllImport("tashi_consensus_engine", EntryPoint = "tce_start")]
        static extern Result tce_start(IntPtr platform);

        [DllImport("tashi_consensus_engine", EntryPoint = "tce_event_get")]
        static extern IntPtr tce_event_get(
            IntPtr platform,
            out Result result
        );

        [DllImport("tashi_consensus_engine", EntryPoint = "tce_send")]
        static extern Result tce_send(
            IntPtr platform,
            byte[] data,
            UInt32 len
        );

        [DllImport("tashi_consensus_engine", EntryPoint = "tce_event_free")]
        static extern void tce_event_free(IntPtr consensusEvent);

        [DllImport("tashi_consensus_engine", EntryPoint = "tce_free")]
        static extern void tce_free(IntPtr platform);
    }
}