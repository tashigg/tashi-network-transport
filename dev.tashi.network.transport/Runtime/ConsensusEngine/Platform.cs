#nullable enable

using System;
using System.Collections.Generic;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using Unity.Collections;
using Unity.Networking.Transport;
using UnityEngine;

namespace Tashi.ConsensusEngine
{
    public enum NetworkMode
    {
        Loopback,
        Local,
        External
    }

    /// <summary>
    /// The Tashi Platform.
    /// </summary>
    public class Platform : IDisposable
    {
        private IntPtr _platform;
        private bool _started;
        private bool _disposed;

        private ulong _clientId;

        // Unity Relay enforces a limit of 1400 bytes per each datagram.
        private static UIntPtr MaxRelayDataLen = new(1400);

        /// <summary>
        /// Create a new platform. It won't begin communicating with other nodes
        /// until the Start method is called.
        /// </summary>
        ///
        public Platform(NetworkMode mode, IPEndPoint bindEndPoint, TimeSpan syncInterval, SecretKey secretKey)
        {
            _clientId = secretKey.PublicKey.ClientId;
            
            _platform = tce_init(
                mode,
                bindEndPoint.Address.ToString(),
                (ushort) bindEndPoint.Port,
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
                String address;

                if (entry is DirectAddressBookEntry direct)
                {
                    address = new IPEndPoint(direct.Address, direct.Port).ToString();
                }
                else if (entry is ExternalAddressBookEntry external)
                {
                    address = external.PublicKey.SyntheticEndpoint.ToString();
                }
                else
                {
                    throw new Exception($"unsupported AddressBookEntry type: {entry}");
                }

                var result = tce_add_node(
                    _platform,
                    address,
                    entry.PublicKey.Der,
                    (uint)entry.PublicKey.Der.Length
                );

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

        internal (SockAddr addr, byte[] packet)? GetExternalTransmit()
        {
            var result = tce_external_transmit_get(_platform, out var transmit);

            if (result != Result.Success)
            {
                if (result != Result.TransmitQueueEmpty)
                {
                    Debug.Log($"error from tce_external_transmit_get: {result}");
                }
                return null;
            }

            result = tce_external_transmit_get_addr(transmit, out var sockAddr, out var sockAddrLen);

            if (result != Result.Success)
            {
                Debug.Log($"error from tce_external_transmit_get_addr: {result}");
                return null;
            }

            result = tce_external_transmit_get_packet(transmit, out var packetPtr, out var packetLen);

            if (result != Result.Success)
            {
                Debug.Log($"error from tce_external_transmit_get_addr: {result}");
                return null;
            }

            var packet = new byte[(int)packetLen];
                
            Marshal.Copy(packetPtr, packet, 0, (int) packetLen);

            tce_external_transmit_destroy(transmit);

            return (sockAddr, packet);
        }

        internal void ExternalReceive(SockAddr addr, DataStreamReader stream)
        {
            if (!_started)
            {
                throw new InvalidOperationException("The platform hasn't been started");
            }

            var result = tce_external_recv_prepare(_platform, MaxRelayDataLen, out IntPtr buf, out UIntPtr bufLen);

            if (result != Result.Success)
            {
                return;
            }

            // Kind of baffling that this is actually necessary.
            // It should just take a capacity and return the number of bytes written.
            var bytesAvailable = stream.Length - stream.GetBytesRead();

            if (bytesAvailable < 8)
            {
                Debug.Log("received message shorter than 8 bytes");
            }

            var clientId = stream.ReadULong();

            var readLen = Math.Min(bytesAvailable - 8, (int) bufLen);
            
            unsafe
            {
                // SAFETY: `buf` is valid for up to `MaxRelayDataLen`
                stream.ReadBytes((byte*) buf, readLen);
            }

            var sockAddr = SockAddr.FromClientId(clientId);

            result = tce_external_recv_commit(_platform, new((uint) readLen), ref sockAddr, sockAddr.Len);

            if (result != Result.Success)
            {
                Debug.Log($"error from tce_external_recv_commit: {result}");
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
            [MarshalAs(UnmanagedType.LPUTF8Str)] string? address,
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

        [DllImport("tashi_consensus_engine", EntryPoint = "tce_external_transmit_get")]
        static extern Result tce_external_transmit_get(
            IntPtr platform,
            out IntPtr transmitOut
        );
        
        [DllImport("tashi_consensus_engine", EntryPoint = "tce_external_transmit_get_addr")]
        static extern Result tce_external_transmit_get_addr(
            IntPtr transmit,
            out SockAddr addr,
            out UIntPtr sockAddrLen
        );

        [DllImport("tashi_consensus_engine", EntryPoint = "tce_external_transmit_get_packet")]
        static extern Result tce_external_transmit_get_packet(
            IntPtr transmit,
            out IntPtr packetOut,
            out UIntPtr packetLenOut
        );

        [DllImport("tashi_consensus_engine", EntryPoint = "tce_external_transmit_get_packet")]
        static extern Result tce_external_transmit_destroy(IntPtr transmit);

        [DllImport("tashi_consensus_engine", EntryPoint = "tce_external_recv_prepare")]
        static extern Result tce_external_recv_prepare(
            IntPtr platform,
            UIntPtr bufCapacity,
            out IntPtr buf,
            out UIntPtr len
        );
        
        [DllImport("tashi_consensus_engine", EntryPoint = "tce_external_recv_commit")]
        static extern Result tce_external_recv_commit(
            IntPtr platform,
            UIntPtr writtenLen,
            ref SockAddr sockAddr,
            UIntPtr sockAddrLen
        );

        [DllImport("tashi_consensus_engine", EntryPoint = "tce_event_free")]
        static extern void tce_event_free(IntPtr consensusEvent);

        [DllImport("tashi_consensus_engine", EntryPoint = "tce_free")]
        static extern void tce_free(IntPtr platform);
    }
}