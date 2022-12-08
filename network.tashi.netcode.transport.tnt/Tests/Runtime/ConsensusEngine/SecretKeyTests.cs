using System;
using Tashi.ConsensusEngine;
using Xunit;

namespace TashiConsensusEngineTests;

public class SecretKeyTests
{
    [Fact]
    public void Generate_CreatesUniqueValues()
    {
        Assert.NotEqual(
            SecretKey.Generate().AsDer(),
            SecretKey.Generate().AsDer()
        );
    }

    [Fact]
    public void Generate_AsDer_Is118Bytes()
    {
        Assert.Equal(
            (UInt32)SecretKey.Generate().AsDer().Length,
            SecretKey.DerLength
        );
    }

    [Fact]
    public void GetPublicKey_ReturnsExpectedValue()
    {
        var secretKeyDer = Convert.FromBase64String("MHcCAQEEIMjzyS35PFRvO/MUVrrPEVq9yRZd/zO5lrzFGX79NXcIoAoGCCqGSM49AwEHoUQDQgAEnuFeVYEXlsIVekaTuzkCdswic/bbNz1lO/x1OTn8AQZ4fT8W3YXjiuGyydzPniKsU6yw2r1wdiL0crhu52HFHQ==");
        var publicKeyDer = Convert.FromBase64String("MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEnuFeVYEXlsIVekaTuzkCdswic/bbNz1lO/x1OTn8AQZ4fT8W3YXjiuGyydzPniKsU6yw2r1wdiL0crhu52HFHQ==");

        Assert.Equal(
            SecretKey.FromDer(secretKeyDer).GetPublicKey().AsDer(),
            publicKeyDer
        );
    }

    [Fact]
    public void FromDer_ForInvalidValue_ThrowsException()
    {
        var small = new byte[SecretKey.DerLength - 1];
        Assert.Throws<ArgumentException>(() => SecretKey.FromDer(small));

        var large = new byte[SecretKey.DerLength + 1];
        Assert.Throws<ArgumentException>(() => SecretKey.FromDer(large));
    }
}
