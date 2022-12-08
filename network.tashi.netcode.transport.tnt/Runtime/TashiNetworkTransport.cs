#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using Unity.Netcode;
using UnityEngine;
using Tashi.ConsensusEngine;
using UnityEngine.Serialization;

namespace Tashi.NetworkTransport
{
    [AddComponentMenu("Netcode/Tashi Network Transport")]
    public class TashiNetworkTransport : Unity.Netcode.NetworkTransport
    {
        public TashiNetworkTransportConfig Config;
        public AddressBookEntry AddressBookEntry;
        public PublicKey? HostPublicKey;

        public delegate void OnPlatformInitHandler(object sender);

        public event OnPlatformInitHandler OnPlatformInit;

        private Platform? _platform;
        private SecretKey _secretKey;
        private List<AddressBookEntry> _addressBook = new();
        private bool _platformStarted;
        private bool _isServer;

        // For now we just use 8 bytes of the public key.
        // LB FIXME: Handle collisions, or make use of a sequence somehow.
        private ulong? _clientId;

        private List<PublicKey> _connectedPeers = new();

        TashiNetworkTransport()
        {
            _secretKey = SecretKey.Generate();
            PublicKey publicKey = _secretKey.GetPublicKey();

            AddressBookEntry = new AddressBookEntry
            {
                Address = null,
                PublicKey = publicKey,
            };

            _clientId = GetClientIdFromPublicKey(publicKey);
        }

        // Network transport events must be in terms of seconds since the game
        // started, but the Tashi Platform reports times as unix timestamp in
        // nanoseconds.
        private float TashiPlatformTimestampToTransportTimestamp(UInt64 nanos)
        {
            var unixSecondsBeforeGameStartup =
                (ulong)DateTimeOffset.Now.ToUnixTimeSeconds() - (ulong)Time.realtimeSinceStartup;
            return nanos / 1_000_000_000.0f - unixSecondsBeforeGameStartup;
        }

        public override void Send(ulong clientId, ArraySegment<byte> data, NetworkDelivery delivery)
        {
            if (data.Array is null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            var copied = new byte[data.Count + sizeof(UInt64)];
            var clientIdBytes = BitConverter.GetBytes(IPAddress.HostToNetworkOrder((Int64)clientId));
            Array.Copy(clientIdBytes, 0, copied, 0, sizeof(UInt64));
            Array.Copy(data.Array, data.Offset, copied, sizeof(UInt64), data.Count);
            _platform.Send(copied);
            Debug.Log($"Sending a {copied.Length} byte message to {clientId}");
        }

        private ulong GetClientIdFromPublicKey(PublicKey pk)
        {
            return BitConverter.ToUInt64(pk.Der, pk.Der.Length - 8);
        }

        // Returns true if an event was received.
        private bool ProcessEvent()
        {
            ConsensusEvent? dataEvent;

            try
            {
                dataEvent = _platform.GetEvent();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                // FIXME: Invoke TransportFailure
                return false;
            }

            if (dataEvent == null)
            {
                return false;
            }

            var creatorId = GetClientIdFromPublicKey(dataEvent.CreatorPublicKey);
            if (creatorId == _clientId)
            {
                Debug.Log("Ignoring a message that was created by me");
                return false;
            }

            var receiveTime = TashiPlatformTimestampToTransportTimestamp(dataEvent.TimestampReceived);

            if (!_connectedPeers.Contains(dataEvent.CreatorPublicKey))
            {
                _connectedPeers.Add(dataEvent.CreatorPublicKey);

                if (_isServer)
                {
                    Debug.Log($"First sighting of client {creatorId}");
                    InvokeOnTransportEvent(NetworkEvent.Connect, creatorId,
                        default, receiveTime);
                }
                else if (dataEvent.CreatorPublicKey.Equals(HostPublicKey))
                {
                    Debug.Log($"First sighting of host {creatorId}");
                    InvokeOnTransportEvent(NetworkEvent.Connect, 0, default,
                        receiveTime);
                }
                else
                {
                    Debug.Log($"First sighting of peer {creatorId}");
                }
            }

            foreach (var data in dataEvent.Transactions)
            {
                // TAS-76
                // For now we force a data event to be sent when a peer is added so
                // we can detect it has joined the session. When the Tashi Platform
                // supports sending session events we'll remove this.
                if (data.SequenceEqual(Encoding.ASCII.GetBytes("Hi")))
                {
                    Debug.Log($"Ignoring the {data.Count} byte greeting");
                    continue;
                }

                if (data.Array is null)
                {
                    continue;
                }

                ulong clientId;
                UInt64 recipientId =
                    (UInt64)IPAddress.NetworkToHostOrder((Int64)BitConverter.ToUInt64(data.Array, data.Offset));

                if (_isServer)
                {
                    if (recipientId != 0)
                    {
                        Debug.Log($"Ignoring a {data.Count} byte message from peer {creatorId} to {recipientId}");
                        continue;
                    }

                    Debug.Log($"I received a {data.Count} byte message from client {creatorId}");
                    clientId = creatorId;
                }
                else
                {
                    if (recipientId != _clientId)
                    {
                        Debug.Log($"Ignoring a {data.Count} byte message from the host to peer {recipientId}");
                        continue;
                    }

                    Debug.Log($"I received a {data.Count} byte message from the host {creatorId}");
                    clientId = 0;
                }

                var payload = new ArraySegment<byte>(data.Array, data.Offset + sizeof(UInt64),
                    data.Count - sizeof(UInt64));

                InvokeOnTransportEvent(NetworkEvent.Data, clientId, payload, receiveTime);
            }

            return true;
        }

        public override NetworkEvent PollEvent(out ulong clientId, out ArraySegment<byte> payload,
            out float receiveTime)
        {
            clientId = default;
            payload = default;
            receiveTime = Time.realtimeSinceStartup;

            if (!_platformStarted)
            {
                return NetworkEvent.Nothing;
            }

            while (ProcessEvent())
            {
            }

            return NetworkEvent.Nothing;
        }

        public override bool StartClient()
        {
            Debug.Log($"TNT StartClient for client {_clientId}");
            InitializePlatform();
            return true;
        }

        public override bool StartServer()
        {
            Debug.Log($"TNT StartServer for client {_clientId}");
            InitializePlatform();
            _isServer = true;
            return true;
        }

        private void InitializePlatform()
        {
            if (_platform != null)
            {
                return;
            }

            _platform = new Platform(
                NetworkMode.Local,
                Config.BindPort,
                TimeSpan.FromMilliseconds(Config.SyncInterval),
                _secretKey
            );

            AddressBookEntry.Address = _platform.GetBoundAddress();

            Debug.Log($"Listening on {AddressBookEntry.Address}");

            AddAddressBookEntry(AddressBookEntry);

            OnPlatformInit?.Invoke(this);
        }

        public override void DisconnectRemoteClient(ulong clientId)
        {
            // TODO: Expose vote kicking.
            Debug.Log("DisconnectRemoteClient");
        }

        public override void DisconnectLocalClient()
        {
            Debug.Log("DisconnectLocalClient");
        }

        public override ulong GetCurrentRtt(ulong clientId)
        {
            // TODO: We need to determine how to define a round trip time - do
            // we want to include consensus, or simply the network round trip?
            return 0;
        }

        public override void Shutdown()
        {
            Debug.Log("TNT Shutdown");
            _platform = null;
        }

        public override void Initialize(NetworkManager? networkManager = null)
        {
            Debug.Log("TNT Initialize");
        }

        public override ulong ServerClientId { get; }

        public void AddAddressBookEntry(AddressBookEntry entry)
        {
            if (entry.PublicKey is null || entry.Address is null)
            {
                return;
            }

            if (entry.Address.Port == 0)
            {
                Debug.LogError($"{entry.Address.Port} has a bound port of 0");
                return;
            }

            if (_addressBook.Contains(entry))
            {
                return;
            }

            _addressBook.Add(entry);
            Debug.Log($"Added node {entry.Address} with pk {Convert.ToBase64String(entry.PublicKey.AsDer())}. Discovered {_addressBook.Count} of {Config.TotalNodes}");

            if (_addressBook.Count == Config.TotalNodes && !_platformStarted)
            {
                StartSyncing();
            }
        }

        private void StartSyncing()
        {
            Debug.Log($"StartSyncing for client ID {_clientId} on {AddressBookEntry.Address}");

            try
            {
                _platform.Start(_addressBook);
                _platformStarted = true;

                // TAS-76
                _platform.Send(Encoding.ASCII.GetBytes("Hi"));
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }
    }
}
