using UnityEngine;
using System;

namespace Tashi.NetworkTransport
{
    [Serializable]
    public class TashiNetworkTransportEditorConfig
    {
        [Tooltip("The local port to listen on. Use 0 to have one assigned for you.")] [SerializeField]
        public ushort BindPort = 0;

        [Tooltip("How often syncs should be sent to other nodes.")]
        public uint SyncInterval = 33;

        [Tooltip("If using Tashi Relay, supply the API key you were given here.")]
        public string RelayApiKey = "";
    }
}