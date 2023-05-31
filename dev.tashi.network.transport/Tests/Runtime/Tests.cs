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

            var addressBook = new AddressBookEntry[]
            {
                // This IPEndPoint value is nonsense, but an address book
                // entry must exist to be able to start the platform.
                new DirectAddressBookEntry(new IPEndPoint(IPAddress.Any, 0), secretKey.GetPublicKey())
            };

            platform.Start(addressBook);

            var addr = platform.GetBoundAddress();
            Debug.Log($"Bound to {addr}");
        }
    }
}