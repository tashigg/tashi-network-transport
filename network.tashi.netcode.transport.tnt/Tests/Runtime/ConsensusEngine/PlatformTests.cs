using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Tashi.ConsensusEngine;
using Xunit;

namespace TashiConsensusEngineTests;

class TestNode
{
    private Platform _platform;
    private PublicKey _publicKey;

    public TestNode(NetworkMode mode = NetworkMode.Loopback)
    {
        var secretKey = SecretKey.Generate();
        _publicKey = secretKey.GetPublicKey();
        _platform = new Platform(mode, 0, TimeSpan.FromMilliseconds(33), secretKey);
        Console.WriteLine($"Bound to {_platform.GetBoundAddress()}");
    }

    public void Start(IList<AddressBookEntry> addressBook)
    {
        _platform.Start(addressBook);
        _platform.Send(new byte[] { 1, 2, 3 });
        _platform.Send(new byte[] { 4, 5, 6 });
    }

    public AddressBookEntry GetAddressBookEntry()
    {
        return new AddressBookEntry { Address = _platform.GetBoundAddress(), PublicKey = _publicKey };
    }

    public ConsensusEvent? GetEvent()
    {
        return _platform?.GetEvent();
    }
}

public class PlatformTests
{
    [Theory]
    [InlineData(NetworkMode.Loopback)]
    [InlineData(NetworkMode.Local)]
    public void ThreeNodes_ForTwoSeconds_Succeeds(NetworkMode mode)
    {
        var nodes = new TestNode[] {
            new(mode),
            new(mode),
            new(mode)
        };

        var addressBook = new[] {
            nodes[0].GetAddressBookEntry(),
            nodes[1].GetAddressBookEntry(),
            nodes[2].GetAddressBookEntry(),
        };

        var tasks = new[] {
            Task.Run(() => nodes[0].Start(addressBook)),
            Task.Run(() => nodes[1].Start(addressBook)),
            Task.Run(() => nodes[2].Start(addressBook)),
            Task.Delay(TimeSpan.FromSeconds(2)),
        };

        Task.WaitAll(tasks);

        foreach (var node in nodes)
        {
            var ev = node.GetEvent();
            Assert.NotNull(ev);
            Assert.NotEqual(0ul, ev?.TimestampCreated);
            Assert.NotEqual(0ul, ev?.TimestampReceived);
            Assert.Equal(ev?.Transactions[0], new ArraySegment<byte>(new byte [] { 1, 2, 3}));
            Assert.Equal(ev?.Transactions[1], new ArraySegment<byte>(new byte [] { 4, 5, 6}));
        }
    }

    [Fact]
    public void Start_WithEmptyAddressBook_Throws()
    {
        var node = new TestNode();
        var exception = Assert.Throws<InvalidOperationException>(() => node.Start(Array.Empty<AddressBookEntry>()));
        Assert.Equal("The address book hasn't been set", exception.Message);
    }

    [Fact]
    public void Start_Twice_Throws()
    {
        var node = new TestNode();
        var addressBook = new[] { node.GetAddressBookEntry() };
        node.Start(addressBook);
        var exception = Assert.Throws<InvalidOperationException>(() => node.Start(addressBook));
        Assert.Equal("The platform has already been started", exception.Message);
    }

    [Fact]
    public void Start_AfterFailure_Succeeds()
    {
        var node = new TestNode();
        var exception = Assert.Throws<InvalidOperationException>(() => node.Start(Array.Empty<AddressBookEntry>()));
        Assert.Equal("The address book hasn't been set", exception.Message);
        var addressBook = new[] { node.GetAddressBookEntry() };
        node.Start(addressBook);
    }

    [Fact]
    public void GetEvent_BeforeStart_ThrowsInvalidOperationException()
    {
        var node = new TestNode();
        var exception = Assert.Throws<InvalidOperationException>(() => node.GetEvent());
        Assert.Equal("The platform hasn't been started", exception.Message);
    }

    [Fact]
    public void PlatformDisposal_DoesNotThrow()
    {
        var secretKey = SecretKey.Generate();
        using var platform = new Platform(NetworkMode.Loopback, 0, TimeSpan.FromMilliseconds(33), secretKey);
        var addressBook = new[] {
            new AddressBookEntry {
                Address = platform.GetBoundAddress(),
                PublicKey = secretKey.GetPublicKey(),
            },
        };
        platform.Start(addressBook);
        platform.Send(new byte[] { 1, 2, 3 });
        platform.Send(new byte[] { 1, 2, 3 });
    }
}
