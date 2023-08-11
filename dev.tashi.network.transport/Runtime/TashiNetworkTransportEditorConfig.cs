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

        // TODO: Include a link to good documentation
        [Tooltip("How often events should be created. This is multiplied by the session size and dynamically adjusts depending on network conditions.")]
        public UInt64 MinBaseEventIntervalMicros = 1500;

        [Tooltip("Which network mode to run TNT in")]
        public TashiNetworkMode NetworkMode = TashiNetworkMode.TashiRelay;

        [Tooltip("If using Tashi Relay, supply the base URL you were given here.")]
        public string TashiRelayBaseUrl = "https://eastus.relay.infra.tashi.dev";

        [Tooltip("If using Tashi Relay, supply the API key you were given here.")]
        public string TashiRelayApiKey;
    }
}