#nullable enable

using System.Net;
using Tashi.ConsensusEngine;
using NUnit.Framework;
using UnityEngine;

namespace TashiConsensusEngineTests
{
    public class AddressBookEntryTests
    {
        [Test]
        public void Equals_Works()
        {
            var sk = SecretKey.Generate();
            var entry1 = new DirectAddressBookEntry(IPAddress.Loopback,  0, sk.PublicKey);
            var entry2 = new DirectAddressBookEntry(IPAddress.Loopback,  0, sk.PublicKey);
            var entry3 = new DirectAddressBookEntry(IPAddress.Broadcast, 0, sk.PublicKey);
            Assert.AreEqual(entry1, entry2);
            Assert.AreNotEqual(entry1, entry3);

            var external1 = new ExternalAddressBookEntry("123", sk.PublicKey);
            var external2 = new ExternalAddressBookEntry("123", sk.PublicKey);
            var external3 = new ExternalAddressBookEntry("456", sk.PublicKey);
            Assert.AreEqual(external1, external2);
            Assert.AreNotEqual(external1, external3);
        }

        [Test]
        public void DirectSerialization_RoundTrip_Works()
        {
            var sk = SecretKey.Generate();
            var expected = new DirectAddressBookEntry(IPAddress.Loopback, 0, sk.PublicKey);

            var serialized = expected.Serialize();
            Debug.Log(serialized);

            var addressBookEntry = AddressBookEntry.Deserialize(serialized);
            Assert.AreEqual(expected, addressBookEntry);

            if (addressBookEntry is DirectAddressBookEntry actual)
            {
                Assert.AreEqual(expected, actual);
            }
            else
            {
                Assert.Fail("object isn't a DirectAddressBookEntry");
            }
        }

        [Test]
        public void ExternalSerialization_RoundTrip_Works()
        {
            var sk = SecretKey.Generate();
            var expected = new ExternalAddressBookEntry("123", sk.PublicKey);

            var serialized = expected.Serialize();
            Debug.Log(serialized);

            var addressBookEntry = AddressBookEntry.Deserialize(serialized);
            Assert.AreEqual(expected, addressBookEntry);

            if (addressBookEntry is ExternalAddressBookEntry actual)
            {
                Assert.AreEqual(expected, actual);
            }
            else
            {
                Assert.Fail("object isn't an ExternalAddressBookEntry");
            }
        }
    }
}