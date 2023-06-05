#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using Unity.Networking.Transport;
using Unity.Networking.Transport.Relay;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;

namespace Tashi.ConsensusEngine
{
    public class ExternalConnectionManager
    {
        private Platform _platform;
        private ushort _peerNodesCount;

        private bool _bindStarted;
        private ExternalListener? _externalListener;

        private readonly Dictionary<SockAddr, ExternalConnection> _externalConnections = new();

        public ExternalConnectionManager(Platform platform, ushort totalNodes)
        {
            Debug.Assert(totalNodes > 0);
            
            _platform = platform;
            _peerNodesCount = (ushort) (totalNodes - 1);
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
            return _externalListener.JoinCode;
        }

        /**
         * Call this when we get a new join code for a client.
         */
        public async Task ConnectAsync(ulong clientId, string joinCode)
        {
            var sockAddr = SockAddr.FromClientId(clientId);
            
            if (_externalConnections.ContainsKey(sockAddr)) return;

            var connection = await ExternalConnection.ConnectAsync(clientId, joinCode);
            
            _externalConnections.Add(sockAddr, connection);
        }

        public void Update()
        {
            while (true)
            {
                var maybeTransmit = _platform.GetExternalTransmit();

                if (maybeTransmit == null)
                {
                    break;
                }

                // The docs on nullability suggest that this should happen automatically if we add an explicit check
                // against null, but it doesn't.
                var transmit = maybeTransmit.Value;

                ExternalConnection conn;

                try
                {
                    conn = _externalConnections[transmit.addr];
                }
                catch (KeyNotFoundException)
                {
                    Debug.Log($"attempting to transmit on unknown connection {transmit.addr}");
                    continue;
                }

                conn.Send(transmit.packet);
            }

            foreach (var conn in _externalConnections)
            {
                conn.Value.Update(_platform);
            }

            _externalListener?.Update(_platform);
        }
    }
    
    internal class ExternalConnection
    {
        // Because the `NetworkSettings` we pass to the `NetworkDriver` has the host allocation ID,
        // we actually need a separate instance of `NetworkDriver` *per* peer.
        private NetworkDriver _networkDriver;
        private NetworkConnection _networkConnection;

        private bool _connected;
        private ulong _clientId;

        private ExternalConnection(ulong clientId, NetworkDriver networkDriver, NetworkConnection networkConnection, bool connected)
        {
            _clientId = clientId;
            _networkDriver = networkDriver;
            _networkConnection = networkConnection;
            _connected = connected;
        }

        internal static async Task<ExternalConnection> ConnectAsync(ulong clientId, string joinCode)
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

            return new ExternalConnection(clientId: clientId, networkDriver: networkDriver,
                networkConnection: networkConnection, connected: false);
        }

        internal void Send(byte[] packet)
        {
            _networkDriver.BeginSend(_networkConnection, out var writer, packet.Length + 8);

            writer.WriteULong((ulong)IPAddress.HostToNetworkOrder((long)_clientId));
            writer.AsNativeArray().CopyFrom(packet);

            _networkDriver.EndSend(writer);
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
                        clientId = (ulong) IPAddress.NetworkToHostOrder((long)clientId);

                        var sockAddr = SockAddr.FromClientId(clientId);

                        platform.ExternalReceive(sockAddr, stream);
                        break;
                    case NetworkEvent.Type.Connect:
                        break;
                    case NetworkEvent.Type.Disconnect:
                        // TODO: handle disconnection
                        break;
                }
            }
        }
    }

    internal class ExternalListener
    {
        private NetworkDriver _networkDriver;

        private Allocation _allocation;
        internal string JoinCode;

        private readonly List<NetworkConnection> _connections = new();

        private ExternalListener(NetworkDriver networkDriver, Allocation allocation, string joinCode)
        {
            _networkDriver = networkDriver;
            _allocation = allocation;
            JoinCode = joinCode;
        }

        internal static async Task<ExternalListener> BindAsync(int peerCount)
        {
            var allocation = await RelayService.Instance.CreateAllocationAsync(peerCount);
            var joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);

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

            return new ExternalListener(networkDriver: networkDriver, allocation: allocation, joinCode: joinCode);
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
                            clientId = (ulong) IPAddress.NetworkToHostOrder((long)clientId);

                            var sockAddr = SockAddr.FromClientId(clientId);

                            platform.ExternalReceive(sockAddr, stream);
                            break;
                        case NetworkEvent.Type.Connect:
                            break;
                        case NetworkEvent.Type.Disconnect:
                            // TODO: handle disconnection
                            break;
                    }
                }
            }
        }
    }
}