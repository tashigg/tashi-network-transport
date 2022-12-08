using System;
using System.Net;
using Tashi.ConsensusEngine;
using Xunit;

namespace TashiConsensusEngineTests;

public class AddressBookEntryTests
{
    [Fact]
    public void Equals_Works()
    {
        var ep = new IPEndPoint(123, 123);
        var sk = SecretKey.Generate();
        var entry1 = new AddressBookEntry(ep, sk.GetPublicKey());
        var entry2 = new AddressBookEntry(ep, sk.GetPublicKey());
        Assert.Equal(entry1, entry2);
    }
}
