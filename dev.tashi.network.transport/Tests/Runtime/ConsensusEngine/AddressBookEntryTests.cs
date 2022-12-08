using System.Net;
using Tashi.ConsensusEngine;
using NUnit.Framework;

namespace TashiConsensusEngineTests
{
    public class AddressBookEntryTests
    {
        [Test]
        public void Equals_Works()
        {
            var ep = new IPEndPoint(123, 123);
            var sk = SecretKey.Generate();
            var entry1 = new AddressBookEntry(ep, sk.GetPublicKey());
            var entry2 = new AddressBookEntry(ep, sk.GetPublicKey());
            Assert.AreEqual(entry1, entry2);
        }
    }
}