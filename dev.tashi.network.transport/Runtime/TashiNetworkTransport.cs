#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Unity.Netcode;
using UnityEngine;
using Tashi.ConsensusEngine;
using UnityEngine.Assertions;

namespace Tashi.NetworkTransport
{
    [AddComponentMenu("Netcode/Tashi Network Transport")]
    public class TashiNetworkTransport : Unity.Netcode.NetworkTransport
    {
        public override ulong ServerClientId { get; }

        /// <summary>
        /// The <see cref="TashiNetworkTransportEditorConfig"/> used to configure the network transport. This is set
        /// automatically.
        /// </summary>
        public TashiNetworkTransportEditorConfig Config = new();

        /// <summary>
        /// Determines whether the session state is <c>Running</c>. This is intended to help determine when the lobby
        /// should be left or closed for games that don't allow new players to join after it has started.
        /// </summary>
        public bool SessionHasStarted => _state == State.Running;

        /// <summary>
        /// Session details that should be shared with other players.
        /// </summary>
        public OutgoingSessionDetails OutgoingSessionDetails { get; private set; }

        public delegate void OnPlatformInitHandler(object sender);

        /// <summary>
        /// A delegate that will be called once the Tashi Consensus Engine has successfully initialized.
        /// </summary>
        public OnPlatformInitHandler? OnPlatformInit;

        private const ushort MaximumSessionSize = 8;
        private Platform? _platform;
        private SecretKey _secretKey;
        private List<AddressBookEntry> _addressBook = new();
        private bool _isServer;
        private PublicKey? _hostPublicKey;

        // For now we just use 8 bytes of the public key.
        // LB FIXME: Handle collisions, or make use of a sequence somehow.
        private ulong? _clientId;

        private List<PublicKey> _connectedPeers = new();

        private ExternalConnectionManager? _externalConnectionManager;

        TashiNetworkTransport()
        {
            OutgoingSessionDetails = new OutgoingSessionDetails();
            _secretKey = SecretKey.Generate();
            PublicKey publicKey = _secretKey.PublicKey;
            _clientId = publicKey.ClientId;
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
            if (_platform == null)
            {
                throw new InvalidOperationException("_platform is null");
            }

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

        // Returns true if an event was received.
        private bool ProcessEvent()
        {
            if (_platform == null)
            {
                throw new InvalidOperationException("_platform is null");
            }

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

            var creatorId =dataEvent.CreatorPublicKey.ClientId;
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
                else if (dataEvent.CreatorPublicKey.Equals(_hostPublicKey))
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

            if (!SessionHasStarted)
            {
                return NetworkEvent.Nothing;
            }
            
            while (ProcessEvent())
            {
            }
            
            return NetworkEvent.Nothing;
        }

        private enum State
        {
            WaitingForSessionDetails,
            WaitingForTashiRelay,
            Running,
        }

        private State _state = State.WaitingForSessionDetails;

        private void Update()
        {
            _externalConnectionManager?.Update();
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
            _isServer = true;
            InitializePlatform();
            return true;
        }

        private void InitializePlatform()
        {
            if (_platform != null)
            {
                return;
            }

            var bindEndPoint = Config.NetworkMode == TashiNetworkMode.UnityRelay
                ? _secretKey.PublicKey.SyntheticEndpoint
                : new IPEndPoint(IPAddress.Any, Config.BindPort);

            _platform = new Platform(
                Config.NetworkMode.ToTceNetworkMode(),
                bindEndPoint,
                TimeSpan.FromMilliseconds(Config.SyncInterval),
                _secretKey
            );

            if (Config.NetworkMode == TashiNetworkMode.UnityRelay)
            {
                Debug.Log("binding in external mode");
                
                var externalManager = _externalConnectionManager = new ExternalConnectionManager(_platform, MaximumSessionSize, _secretKey.PublicKey.ClientId);
                
                // C#'s task system is markedly different from Rust's: in Rust, the consumer of a `Future`
                // is responsible for driving it forward unless explicitly spawned into a runtime,
                // whereas C# is closer to Javascript's Promise system where tasks dependent on the completion of a
                // Promise just need to be attached to it to be automatically started when it completes.
                //
                // This makes sense if you consider that in C#, like Javascript, there is no overarching concept
                // of ownership and multiple dependent tasks can consume the result of a single antecedent task.
                //
                // And in fact, like Javascript, the task returned by this method is automatically started ("hot"):
                // https://stackoverflow.com/a/43089445
                //
                // Only explicitly created tasks are not started (or "cold").
                var bindTask = externalManager.BindAsync();

                bindTask.ContinueWith(_ =>
                    {
                        if (bindTask.IsCompletedSuccessfully)
                        {
                            Debug.Log($"Unity Relay allocation completed, the join code is {bindTask.Result}");
                            InitFinished(new ExternalAddressBookEntry(bindTask.Result, _secretKey.PublicKey));
                        }
                        else
                        {
                            Debug.LogWarning("exception from ExternalConnectionManager.BindAsync()");
                            Debug.LogException(bindTask.Exception);
                        }
                    },
                    // Ensure we use the Unity task scheduler and not the default, global one.
                    TaskScheduler.FromCurrentSynchronizationContext());
            }
            else
            {
                InitFinished(new DirectAddressBookEntry(_platform.GetBoundAddress(), _secretKey.PublicKey));
            }
        }

        private void InitFinished(AddressBookEntry addressBookEntry)
        {
            OutgoingSessionDetails.AddressBookEntry = addressBookEntry;
            AddAddressBookEntry(addressBookEntry, _isServer);
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
            _platform?.Dispose();
            _state = State.WaitingForSessionDetails;
            _externalConnectionManager?.Dispose();
        }

        public override void Initialize(NetworkManager? networkManager = null)
        {
            Debug.Log("TNT Initialize");
        }

        public void UpdateSessionDetails(IncomingSessionDetails sessionDetails)
        {
            if (sessionDetails.AddressBook.Count > MaximumSessionSize)
            {
                throw new Exception($"The maximum supported session size is {MaximumSessionSize}");
            }

            Debug.Log($"Applying incoming session data with {sessionDetails.AddressBook.Count} entries");

            foreach (var entry in sessionDetails.AddressBook)
            {
                AddAddressBookEntry(entry, entry == sessionDetails.Host);
            }

            if (_isServer)
            {
                BeginHostSessionSetup();
            }
            else
            {
                BeginClientSessionSetup(sessionDetails.TashiRelay);
            }
        }

        private void AddAddressBookEntry(AddressBookEntry entry, bool treatAsHost)
        {
            if (_addressBook.Contains(entry))
            {
                return;
            }

            if (entry is DirectAddressBookEntry direct)
            {
                if (direct.Port == 0)
                {
                    Debug.LogError("Can't add a DirectAddressBookEntry without an address or a valid port");
                    return;
                }

                Debug.Log($"Added node {direct.Address}");
            }
            else if (entry is ExternalAddressBookEntry external)
            {
                if (_externalConnectionManager == null)
                {
                    Debug.LogError($"Received external address book entry in non-external mode: {external}");
                    return;
                }

                Debug.Log($"Added node {external.PublicKey.SyntheticSockAddr} with join code {external.RelayJoinCode}");

#pragma warning disable CS4014
                if (!entry.PublicKey.Equals(_secretKey.PublicKey))
                {
                    // Only attempt to connect to another peer.
                    _externalConnectionManager.ConnectAsync(external.PublicKey.ClientId, external.RelayJoinCode)
                        .ContinueWith(task =>
                            {
                                if (task.IsFaulted)
                                {
                                    Debug.LogWarning($"failed to connect to {external.PublicKey.ClientId} with join code {external.RelayJoinCode}");
                                    Debug.LogException(task.Exception);
                                }
                            },
                            TaskScheduler.FromCurrentSynchronizationContext());
                }
#pragma warning restore CS4014
            }
            else
            {
                throw new ArgumentException("Invalid AddressBookEntry type");
            }

            if (treatAsHost)
            {
                if (_hostPublicKey != null)
                {
                    throw new ArgumentException("Another address book entry has already been set as the host");
                }

                _hostPublicKey = entry.PublicKey;
            }

            _addressBook.Add(entry);
        }

        private void BeginHostSessionSetup()
        {
            Assert.IsNotNull(_platform);

            if (!string.IsNullOrWhiteSpace(Config.TashiRelayApiKey))
            {
                if (_state == State.WaitingForTashiRelay)
                {
                    Debug.Log("Still waiting for a Tashi Relay allocation");
                    return;
                }

                // Important: populate TCE's address book before we try to create the Relay session.
                _platform?.SetAddressBook(_addressBook);
                
                Debug.Log($"Requesting a Tashi Relay allocation");
                _state = State.WaitingForTashiRelay;

                _platform?.CreateRelaySession(
                    Config.TashiRelayBaseUrl,
                    Config.TashiRelayApiKey,
                    entry =>
                    {
                        Debug.Log(
                            $"The Tashi Relay has been allocated: {entry.Address}:{entry.Port}");
                        OutgoingSessionDetails.TashiRelay = entry;

                        // Setting the relay as the host might be a good idea
                        // until we try using a virtual host.
                        AddAddressBookEntry(entry, false);

                        CompleteSessionSetup();
                    },
                    e =>
                    {
                        Debug.LogException(e);
                        _state = State.WaitingForSessionDetails;
                    }
                );

                return;
            }

            Debug.Log("Tashi Relay isn't being used");
            CompleteSessionSetup();
        }

        private void BeginClientSessionSetup(DirectAddressBookEntry? tashiRelay)
        {
            Assert.IsNotNull(_platform);

            if (!string.IsNullOrWhiteSpace(Config.TashiRelayApiKey))
            {
                if (tashiRelay is not null)
                {
                    Debug.Log($"Tashi Relay is now available: {tashiRelay}");
                    AddAddressBookEntry(tashiRelay, false);
                    CompleteSessionSetup();
                }
                else
                {
                    Debug.Log("Waiting for the host to share the Tashi Relay allocation");
                }

                return;
            }

            CompleteSessionSetup();
        }

        private void CompleteSessionSetup()
        {
            _platform?.SetAddressBook(_addressBook);

            try
            {
                // FIXME: Handle re-initializing _platform if we need to call tce_start again.

                _platform?.Start(_addressBook);
                _state = State.Running;

                // TAS-76
                _platform?.Send(Encoding.ASCII.GetBytes("Hi"));
            }
            catch (Exception e)
            {
                _state = State.WaitingForSessionDetails;
                Debug.LogException(e);
            }
        }
    }
}