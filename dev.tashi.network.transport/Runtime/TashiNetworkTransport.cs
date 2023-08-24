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
    public enum SessionState
    {
        /// <summary>
        /// `StartSession` has not been called, or the existing session
        /// failed or concluded.
        /// </summary>
        NotStarted,

        /// <summary>
        /// `StartSession` has been called and the session is being set up.
        /// This can take some time if a relay needs to be allocated.
        /// </summary>
        Starting,

        /// <summary>
        /// The session was completely started and is in progress.
        /// </summary>
        InProgress,
    }

    [AddComponentMenu("Netcode/Tashi Network Transport")]
    public sealed class TashiNetworkTransport : Unity.Netcode.NetworkTransport
    {
        public override ulong ServerClientId { get; }

        /// <summary>
        /// The <see cref="TashiNetworkTransportEditorConfig"/> used to configure the network transport. This is set
        /// automatically.
        /// </summary>
        public TashiNetworkTransportEditorConfig Config = new();

        public SessionState SessionState { get; private set;  }

        /// <summary>
        /// Session details that should be shared with other players.
        /// </summary>
        public OutgoingSessionDetails OutgoingSessionDetails { get; private set; }

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

        private void Awake()
        {
            NativeAddressFamily.InitializeStatics(SystemInfo.operatingSystemFamily);
        }

        private void Update()
        {
            if (_beginSessionSucceeded != null)
            {
                if (_beginSessionSucceeded == true)
                {
                    _beginSessionSucceededHandler?.Invoke();
                }
                else
                {
                    _beginSessionFailedHandler?.Invoke();
                }

                _beginSessionSucceededHandler = null;
                _beginSessionFailedHandler = null;
                _beginSessionSucceeded = null;
            }

            _externalConnectionManager?.Update();
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

            var creatorId = dataEvent.CreatorPublicKey.ClientId;
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

            if (SessionState != SessionState.InProgress)
            {
                return NetworkEvent.Nothing;
            }

            while (ProcessEvent())
            {
            }

            return NetworkEvent.Nothing;
        }

        private Action? _beginSessionSucceededHandler;
        private Action? _beginSessionFailedHandler;
        private bool? _beginSessionSucceeded;

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
                Config.MinBaseEventIntervalMicros,
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
            SessionState = SessionState.NotStarted;
            _externalConnectionManager?.Dispose();
        }

        public override void Initialize(NetworkManager? networkManager = null)
        {
            Debug.Log("TNT Initialize");
        }

        /// <summary>
        /// <para>Attempts to start the session.</para>
        ///
        /// <para>Hosts</para>
        ///
        /// <para>
        /// This should only be called once by hosts. If a Tashi Relay is going
        /// to be used then this will create a thread to handle the allocation.
        /// Once the relay is allocated its details will be available in
        /// <see cref="OutgoingSessionDetails"/>, which should be shared with the rest of
        /// the lobby.
        /// </para>
        ///
        /// <para>Clients</para>
        ///
        /// <para>
        /// This should only be called once by clients. If a Tashi Relay is
        /// going to be used then you should check that
        /// <see cref="IncomingSessionDetails.TashiRelay"/> is set so that you
        /// know the host has successfully allocated the relay.
        /// </para>
        /// </summary>
        /// <param name="sessionDetails"></param>
        /// <param name="onSuccess"></param>
        /// <param name="onError"></param>
        /// <exception cref="InvalidOperationException">If the session detail's
        /// address book contains too many entries.</exception>
        public void StartSession(
            IncomingSessionDetails sessionDetails,
            Action? onSuccess,
            Action? onError
        )
        {
            Assert.AreEqual(SessionState.NotStarted, SessionState);

            if (sessionDetails.AddressBook.Count > MaximumSessionSize)
            {
                throw new InvalidOperationException($"The maximum supported session size is {MaximumSessionSize}");
            }

            _beginSessionSucceededHandler = onSuccess;
            _beginSessionFailedHandler = onError;
            _beginSessionSucceeded = null;

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

            SessionState = SessionState.Starting;

            if (Config.NetworkMode == TashiNetworkMode.TashiRelay)
            {
                // Important: populate TCE's address book before we try to create the Relay session.
                _platform?.SetAddressBook(_addressBook);

                Debug.Log($"Requesting a Tashi Relay allocation");

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
                        SessionState = SessionState.NotStarted;
                        _beginSessionSucceeded = false;
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

            if (Config.NetworkMode == TashiNetworkMode.TashiRelay)
            {
                if (tashiRelay is not null)
                {
                    // This is set so late so that clients can continually call
                    // `StartSession` until the host shared the Tashi Relay
                    // address book entry.
                    SessionState = SessionState.Starting;

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

            SessionState = SessionState.Starting;
            CompleteSessionSetup();
        }

        private void CompleteSessionSetup()
        {
            _platform?.SetAddressBook(_addressBook);

            try
            {
                // FIXME: Handle re-initializing _platform if we need to call tce_start again.

                SessionState = SessionState.InProgress;
                _platform?.Start(_addressBook);

                // TAS-76
                _platform?.Send(Encoding.ASCII.GetBytes("Hi"));

                _beginSessionSucceeded = true;
            }
            catch (Exception e)
            {
                SessionState = SessionState.NotStarted;
                _beginSessionSucceeded = false;
                Debug.LogException(e);
            }
        }
    }
}