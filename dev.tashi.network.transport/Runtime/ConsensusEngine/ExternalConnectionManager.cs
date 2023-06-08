#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using Unity.Jobs;
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

        private readonly Dictionary<SockAddr, ExternalConnection> _externalConnections = new();

        public ExternalConnectionManager(Platform platform, ushort totalNodes, ulong localClientId)
        {
            Debug.Assert(totalNodes > 0);
            
            _platform = platform;
            _peerNodesCount = (ushort) (totalNodes - 1);
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
            
            if (_externalConnections.ContainsKey(sockAddr)) return;

            var connection = await ExternalConnection.ConnectAsync(_localClientId, remoteClientId, joinCode);
            
            _externalConnections.Add(sockAddr, connection);
        }

        public void Update()
        {
            foreach (var conn in _externalConnections)
            {
                conn.Value.Update(_platform);
            }
            
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

            _externalListener?.Update(_platform);
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

        private bool _connected;
        private readonly ulong _localClientId;
        private readonly ulong _remoteClientId;

        private ExternalConnection(ulong localClientId, ulong remoteClientId, NetworkDriver networkDriver, NetworkConnection networkConnection, bool connected)
        {
            _localClientId = localClientId;
            _remoteClientId = remoteClientId;
            _networkDriver = networkDriver;
            _networkConnection = networkConnection;
            _connected = connected;
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

            return new ExternalConnection(localClientId, remoteClientId, networkDriver: networkDriver,
                networkConnection: networkConnection, connected: false);
        }

        internal void Send(byte[] packet)
        {
            // Even though this is UDP we still have to get a `BIND` message through
            // to the Relay before this is considered "connected" and we can send data.
            //
            // We get a `NullReferenceException` inside UTP if we try to send data before the connection
            // attempt completes.
            //
            // This does mean we drop packets initially but TCE should be able to just figure that out and go into
            // a backoff loop until they start going through.
            if (!_connected) return;
            
            _networkDriver.ScheduleUpdate().Complete();
            
            _networkDriver.BeginSend(_networkConnection, out var writer, packet.Length + 8);

            try
            {
                writer.WriteULong((ulong)IPAddress.HostToNetworkOrder((long)_localClientId));

                // writer.AsNativeArray() uses the writer's entire buffer,
                // without the internal offset.
                // TODO: We should avoid the additional copies on the C# side eventually.
                unsafe
                {
                    fixed (byte* data = packet)
                    {
                        writer.WriteBytes(data, packet.Length);
                    }
                }

                _networkDriver.EndSend(writer);
            }
            catch (Exception e)
            {
                Debug.Log("exception in ExternalConnection.Send()");
                Debug.LogException(e);
            }
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
                        _connected = true;
                        break;
                    case NetworkEvent.Type.Disconnect:
                        // TODO: handle disconnection
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
            
            networkDriver.ScheduleUpdate().Complete();

            return new ExternalListener(networkDriver: networkDriver, allocation: allocation);
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
                            break;
                        case NetworkEvent.Type.Disconnect:
                            // TODO: handle disconnection
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
}