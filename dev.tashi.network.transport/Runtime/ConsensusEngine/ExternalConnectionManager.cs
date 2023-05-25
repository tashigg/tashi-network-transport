#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
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

        private Allocation? _allocation;

        private NetworkDriver? _networkDriver;

        private bool _bound;

        private List<NetworkConnection> _connections = new();

        private Dictionary<SockAddr, NetworkConnection> _addressToConnection = new();

        public ExternalConnectionManager(Platform platform, ushort totalNodes)
        {
            Debug.Assert(totalNodes > 0);
            
            _platform = platform;
            _peerNodesCount = (ushort) (totalNodes - 1);
        }

        public async Task BindAsync()
        {
            if (_bound) return;
            
            if (_allocation == null)
            {
                _allocation = await RelayService.Instance.CreateAllocationAsync(_peerNodesCount);
            }
            
            var serverData = new RelayServerData(_allocation, "udp");

            var networkSettings = new NetworkSettings();
            networkSettings.WithRelayParameters(ref serverData);

            _networkDriver = NetworkDriver.Create(networkSettings);
            
            if (_networkDriver?.Bind(NetworkEndPoint.AnyIpv4) != 0)
            {
                Debug.LogError("Host client failed to bind");
            }
            else
            {
                if (_networkDriver?.Listen() != 0)
                {
                    Debug.LogError("Host client failed to listen");
                }
                else
                {
                    Debug.Log("Host client bound to Relay server");
                    _bound = true;
                }
            }
        }

        public void Connect(SockAddr addr, AllocationData allocationData)
        {
            
        }

        public void Update()
        {
            if (!_bound) return;

            NetworkDriver networkDriver;

            if (_networkDriver != null)
            {
                networkDriver = _networkDriver.Value;
            }
            else
            {
                return;
            }

            networkDriver.ScheduleUpdate().Complete();

            NetworkConnection incomingConnection;

            while ((incomingConnection = networkDriver.Accept()) != default)
            {
                _connections.Add(incomingConnection);
            }

            foreach (var conn in _connections)
            {
                NetworkEvent.Type eventType;

                while ((eventType = networkDriver.PopEventForConnection(conn, out DataStreamReader stream)) !=
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

                            _addressToConnection.TryAdd(sockAddr, conn);
                            
                            _platform.ExternalReceive(sockAddr, stream);
                            break;
                        case NetworkEvent.Type.Connect:
                            break;
                        case NetworkEvent.Type.Disconnect:
                            break;
                    }
                }
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

                NetworkConnection conn;

                try
                {
                    conn = _addressToConnection[transmit.addr];
                }
                catch (KeyNotFoundException e)
                {
                    Debug.Log($"attempting to transmit on unknown connection {transmit.addr}");
                    continue;
                }

                var clientId = transmit.addr.ClientId;

                networkDriver.BeginSend(conn, out var writer, transmit.packet.Length + 8);

                writer.WriteULong((ulong)IPAddress.HostToNetworkOrder((long)clientId!));
                writer.AsNativeArray().CopyFrom(transmit.packet);

                networkDriver.EndSend(writer);
            }
        }
    }
}