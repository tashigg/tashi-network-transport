#nullable enable

using System;
using System.Collections.Generic;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Unity.Networking.Transport;
using Unity.Networking.Transport.Relay;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;

namespace Tashi.ConsensusEngine
{
    public class ExternalConnectionManager : IDisposable
    {
        private readonly Platform _platform;
        private readonly ushort _peerNodesCount;
        private readonly ulong _localClientId;

        private bool _bindStarted;
        private ExternalListener? _externalListener;

        private readonly HashSet<SockAddr> _pendingConnections = new();

        private readonly Dictionary<SockAddr, ExternalConnection> _externalConnections = new();

        public ExternalConnectionManager(Platform platform, ushort totalNodes, ulong localClientId)
        {
            Debug.Assert(totalNodes > 1);

            _platform = platform;
            _peerNodesCount = (ushort)(totalNodes - 1);
            _localClientId = localClientId;
        }

        /**
         * Call this after Unity services have been initialized.
         *
         * Returns the join code to send to other peers.
         */
        public async Task<string> BindAsync()
        {
            if (_bindStarted) throw new Exception("already called");

            _bindStarted = true;

            _externalListener = await ExternalListener.BindAsync(_peerNodesCount);

            return await _externalListener.GetJoinCode();
        }

        /**
         * Call this when we get a new join code for a client.
         */
        public async Task ConnectAsync(ulong remoteClientId, string joinCode)
        {
            var sockAddr = SockAddr.FromClientId(remoteClientId);

            if (_externalConnections.ContainsKey(sockAddr) || _pendingConnections.Contains(sockAddr))
            {
                return;
            }

            _pendingConnections.Add(sockAddr);
            var connection = await ExternalConnection.ConnectAsync(_localClientId, remoteClientId, joinCode);
            _externalConnections.Add(sockAddr, connection);
            _pendingConnections.Remove(sockAddr);

            Debug.Log($"opening connection to {remoteClientId}");
        }

        public void Update()
        {
            _externalListener?.Update(_platform);

            foreach (var conn in _externalConnections)
            {
                conn.Value.Update(_platform);
            }

            while (true)
            {
                // This will automatically call `.Dispose()` when leaving the scope.
                using var transmit = _platform.GetExternalTransmit();

                if (transmit == null)
                {
                    break;
                }

                ExternalConnection conn;

                try
                {
                    conn = _externalConnections[transmit.Addr];
                }
                catch (KeyNotFoundException)
                {
                    if (!_pendingConnections.Contains(transmit.Addr))
                    {
                        Debug.Log($"Attempting to transmit on unknown connection {transmit.Addr}");
                    }

                    continue;
                }
                
                conn.Send(transmit);
            }
        }

        public void Dispose()
        {
            _externalListener?.Dispose();

            foreach (var connection in _externalConnections.Values)
            {
                connection.Dispose();
            }

            _externalConnections.Clear();
        }
    }

    internal class ExternalConnection : IDisposable
    {
        // Because the `NetworkSettings` we pass to the `NetworkDriver` has the host allocation ID,
        // we actually need a separate instance of `NetworkDriver` *per* peer.
        private NetworkDriver _networkDriver;
        private NetworkConnection _networkConnection;

        private readonly ulong _localClientId;
        private readonly ulong _remoteClientId;

        private NetworkConnection.State State => _networkConnection.GetState(_networkDriver);

        private ExternalConnection(ulong localClientId, ulong remoteClientId, NetworkDriver networkDriver, NetworkConnection networkConnection)
        {
            _localClientId = localClientId;
            _remoteClientId = remoteClientId;
            _networkDriver = networkDriver;
            _networkConnection = networkConnection;
        }

        internal static async Task<ExternalConnection> ConnectAsync(ulong localClientId, ulong remoteClientId, string joinCode)
        {
            var allocation = await RelayService.Instance.JoinAllocationAsync(joinCode);

            var serverData = new RelayServerData(allocation, "udp");

            var networkSettings = new NetworkSettings();
            networkSettings.WithRelayParameters(ref serverData);

            var networkDriver = NetworkDriver.Create(networkSettings);

            if (networkDriver.Bind(NetworkEndPoint.AnyIpv4) != 0)
            {
                throw new Exception("failed to bind to 0.0.0.0:0");
            }

            var networkConnection = networkDriver.Connect();

            return new ExternalConnection(localClientId, remoteClientId, networkDriver, networkConnection);
        }

        internal void Send(ExternalTransmit transmit)
        {
            // Even though this is UDP we still have to get a `BIND` message through
            // to the Relay before this is considered "connected" and we can send data.
            //
            // We get a `NullReferenceException` inside UTP if we try to send data before the connection
            // attempt completes.
            //
            // This does mean we drop packets initially but TCE should be able to just figure that out and go into
            // a backoff loop until they start going through.
            if (State != NetworkConnection.State.Connected) return;

            _networkDriver.BeginSend(_networkConnection, out var writer, transmit.PacketLen + 8);

            try
            {
                writer.WriteULong((ulong)IPAddress.HostToNetworkOrder((long)_localClientId));
                
                unsafe
                {
                    writer.WriteBytes((byte*)transmit.Packet, transmit.PacketLen);
                }

                _networkDriver.EndSend(writer);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return;
            }

            Debug.Log($"sent {transmit.PacketLen} bytes to {_remoteClientId}");
        }

        internal void Update(Platform platform)
        {
            _networkDriver.ScheduleUpdate().Complete();

            NetworkEvent.Type eventType;

            while ((eventType = _networkDriver.PopEventForConnection(_networkConnection, out DataStreamReader stream)) !=
                   NetworkEvent.Type.Empty)
            {
                switch (eventType)
                {
                    case NetworkEvent.Type.Data:
                        // There's no method for reading a `ulong` or even `long` in network byte order.
                        var clientId = stream.ReadULong();

                        // And this method isn't overloaded for unsigned types, sigh.
                        clientId = (ulong)IPAddress.NetworkToHostOrder((long)clientId);

                        var sockAddr = SockAddr.FromClientId(clientId);

                        platform.ExternalReceive(sockAddr, stream);
                        break;
                    case NetworkEvent.Type.Connect:
                        Debug.Log($"Connected to {_remoteClientId}");
                        break;
                    case NetworkEvent.Type.Disconnect:
                        Debug.Log($"Disconnected from {_remoteClientId}");
                        break;
                }
            }
        }

        public void Dispose()
        {
            _networkDriver.Dispose();
        }
    }

    internal class ExternalListener : IDisposable
    {
        private NetworkDriver _networkDriver;

        private Allocation _allocation;

        private readonly List<NetworkConnection> _connections = new();

        private ExternalListener(NetworkDriver networkDriver, Allocation allocation)
        {
            _networkDriver = networkDriver;
            _allocation = allocation;
        }

        internal static async Task<ExternalListener> BindAsync(int peerCount)
        {
            var allocation = await RelayService.Instance.CreateAllocationAsync(peerCount + 1);
            Debug.Log($"Created a Unity Relay allocation with {peerCount + 1} maximum connections");


            var serverData = new RelayServerData(allocation, "udp");

            var networkSettings = new NetworkSettings();
            networkSettings.WithRelayParameters(ref serverData);

            var networkDriver = NetworkDriver.Create(networkSettings);

            if (networkDriver.Bind(NetworkEndPoint.AnyIpv4) != 0)
            {
                throw new Exception("failed to bind to 0.0.0.0:0");
            }

            if (networkDriver.Listen() != 0)
            {
                throw new Exception("Host client failed to listen");
            }

            return new ExternalListener(networkDriver, allocation);
        }

        internal async Task<String> GetJoinCode()
        {
            return await RelayService.Instance.GetJoinCodeAsync(_allocation.AllocationId);
        }

        internal void Update(Platform platform)
        {
            _networkDriver.ScheduleUpdate().Complete();

            NetworkConnection incomingConnection;

            while ((incomingConnection = _networkDriver.Accept()) != default)
            {
                _connections.Add(incomingConnection);
            }

            foreach (var conn in _connections)
            {
                NetworkEvent.Type eventType;

                while ((eventType = _networkDriver.PopEventForConnection(conn, out DataStreamReader stream)) !=
                       NetworkEvent.Type.Empty)
                {
                    switch (eventType)
                    {
                        case NetworkEvent.Type.Data:
                            // There's no method for reading a `ulong` or even `long` in network byte order.
                            var clientId = stream.ReadULong();

                            // And this method isn't overloaded for unsigned types, sigh.
                            clientId = (ulong)IPAddress.NetworkToHostOrder((long)clientId);

                            var sockAddr = SockAddr.FromClientId(clientId);

                            platform.ExternalReceive(sockAddr, stream);
                            break;
                        case NetworkEvent.Type.Connect:
                            Debug.Log($"A client connected.");
                            break;
                        case NetworkEvent.Type.Disconnect:
                            Debug.Log($"A client disconnected.");
                            break;
                    }
                }
            }
        }

        public void Dispose()
        {
            _networkDriver.Dispose();
        }
    }

    internal class ExternalTransmit: IDisposable
    {
        private IntPtr _ptr;

        internal readonly SockAddr Addr;

        internal readonly IntPtr Packet;
        internal readonly int PacketLen;

        internal ExternalTransmit(IntPtr ptr)
        {
            _ptr = ptr;

            try
            {
                var result = tce_external_transmit_get_addr(ptr, out var addr);

                if (result != Result.Success)
                {
                    throw new Exception($"error from tce_external_transmit_get_addr: {result}");
                }

                Addr = addr;

                result = tce_external_transmit_get_packet(ptr, out var packet, out var packetLen);
                
                if (result != Result.Success)
                {
                    throw new Exception($"error from tce_external_transmit_get_packet: {result}");
                }

                Packet = packet;

                checked
                {
                    PacketLen = (int) packetLen;
                }
            }
            catch (Exception)
            {
                tce_external_transmit_destroy(ptr);
                throw;
            }
        }
        
        

        public void Dispose()
        {
            if (_ptr != IntPtr.Zero)
            {

                var result = tce_external_transmit_destroy(_ptr);
                if (result != Result.Success)
                {
                    throw new Exception($"error from tce_external_transmit_destroy: {result}");
                }
                
                _ptr = IntPtr.Zero;
            }
        }

        [DllImport("tashi_consensus_engine", EntryPoint = "tce_external_transmit_get_addr", CallingConvention = CallingConvention.Cdecl)]
        static extern Result tce_external_transmit_get_addr(
            IntPtr transmit,
            out SockAddr addr
        );

        [DllImport("tashi_consensus_engine", EntryPoint = "tce_external_transmit_get_packet", CallingConvention = CallingConvention.Cdecl)]
        static extern Result tce_external_transmit_get_packet(
            IntPtr transmit,
            out IntPtr packetOut,
            out UInt64 packetLenOut
        );

        [DllImport("tashi_consensus_engine", EntryPoint = "tce_external_transmit_destroy", CallingConvention = CallingConvention.Cdecl)]
        static extern Result tce_external_transmit_destroy(IntPtr transmit);
    }
}
