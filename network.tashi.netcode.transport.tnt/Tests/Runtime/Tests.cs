using System;
using System.Net;
using NUnit.Framework;
using Tashi.ConsensusEngine;
using UnityEngine;

namespace Tashi.NetworkTransport
{
    public class PlatformTests
    {
        [Test]
        public void PlatformCanBeStarted()
        {
            var secretKey = ConsensusEngine.SecretKey.Generate();
            var platform = new ConsensusEngine.Platform(NetworkMode.Loopback, 0, TimeSpan.FromMilliseconds(10), secretKey);
            platform.Start(Array.Empty<AddressBookEntry>());

            var addr = platform.GetBoundAddress();
            Debug.Log($"Bound to {addr}");
        }
    }
}