using UnityEngine;
using System;

namespace Tashi.NetworkTransport
{
    [Serializable]
    public class TashiNetworkTransportEditorConfig
    {
        [Tooltip(
            "The total number of nodes participating in the network. If you're using a relay or monitor, include them in the count.")]
        [SerializeField]
        public ushort TotalNodes = 2;

        [Tooltip("The local port to listen on. Use 0 to have one assigned for you.")] [SerializeField]
        public ushort BindPort = 0;

        [Tooltip("How often syncs should be sent to other nodes.")]
        public uint SyncInterval = 33;
    }
}