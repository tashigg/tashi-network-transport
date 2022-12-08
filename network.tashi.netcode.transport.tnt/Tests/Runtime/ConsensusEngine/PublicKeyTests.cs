using System;
using Tashi.ConsensusEngine;
using Xunit;

namespace TashiConsensusEngineTests;

public class PublicKeyTests
{
    [Fact]
    public void Equals_WhenDistinct_ReturnsTrue()
    {
        var der = new byte[PublicKey.DerLength];
        Assert.Equal(
            PublicKey.FromDer(der),
            PublicKey.FromDer(der)
        );
    }

    [Fact]
    public void AsDer_ReturnsConstructorValue()
    {
        var der = new byte[PublicKey.DerLength];
        Assert.Equal(
            PublicKey.FromDer(der).AsDer(),
            der
        );
    }

    [Fact]
    public void FromDer_ForInvalidValue_ThrowsException()
    {
        var small = new byte[PublicKey.DerLength - 1];
        Assert.Throws<ArgumentException>(() => PublicKey.FromDer(small));

        var large = new byte[PublicKey.DerLength + 1];
        Assert.Throws<ArgumentException>(() => PublicKey.FromDer(large));
    }
}
