using System;

namespace Tashi.NetworkTransport
{
    public enum TashiNetworkMode
    {
        Loopback,
        Local,
        UnityRelay,
        TashiRelay
    }

    internal static class TashiNetworkExtensions
    {
        internal static ConsensusEngine.NetworkMode ToTceNetworkMode(this TashiNetworkMode mode)
        {
            return mode switch
            {
                TashiNetworkMode.Loopback => ConsensusEngine.NetworkMode.Loopback,
                TashiNetworkMode.Local => ConsensusEngine.NetworkMode.Local,
                TashiNetworkMode.UnityRelay => ConsensusEngine.NetworkMode.External,
                // From TCE's perspective, operating with Relay is identical to normal networked operation.
                TashiNetworkMode.TashiRelay => ConsensusEngine.NetworkMode.Local,
                _ => throw new ArgumentException($"BUG: uncovered TashiNetworkMode ${mode}"),
            };
        }
    }
}