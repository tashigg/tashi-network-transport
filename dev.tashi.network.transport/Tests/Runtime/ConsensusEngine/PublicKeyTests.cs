using System;
using Tashi.ConsensusEngine;
using NUnit.Framework;

namespace TashiConsensusEngineTests
{

    public class PublicKeyTests
    {
        [Test]
        public void Equals_WhenDistinct_ReturnsTrue()
        {
            var der = new byte[PublicKey.DerLength];
            Assert.AreEqual(
                new PublicKey(der),
                new PublicKey(der)
            );
        }

        [Test]
        public void FromDer_ForInvalidValue_ThrowsException()
        {
            var small = new byte[PublicKey.DerLength - 1];
            Assert.Throws<ArgumentException>(() =>
            {
                var publicKey = new PublicKey(small);
            });

            var large = new byte[PublicKey.DerLength + 1];
            Assert.Throws<ArgumentException>(() =>
            {
                var publicKey = new PublicKey(large);
            });
        }
    }
}