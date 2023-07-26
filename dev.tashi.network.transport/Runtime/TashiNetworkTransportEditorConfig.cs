using UnityEngine;
using System;
using Tashi.ConsensusEngine;

namespace Tashi.NetworkTransport
{
    [Serializable]
    public class TashiNetworkTransportEditorConfig
    {
        [Tooltip("The local port to listen on. Use 0 to have one assigned for you.")] [SerializeField]
        public ushort BindPort;

        [Tooltip("How often syncs should be sent to other nodes.")]
        public uint SyncInterval = 33;

        [Tooltip("Which network mode to run TNT in")]
        public TashiNetworkMode NetworkMode = TashiNetworkMode.TashiRelay;

        [Tooltip("If using Tashi Relay, supply the base URL you were given here.")]
        public string TashiRelayBaseUrl = "https://eastus.relay.infra.tashi.dev";

        [Tooltip("If using Tashi Relay, supply the API key you were given here.")]
        public string TashiRelayApiKey;
    }
}