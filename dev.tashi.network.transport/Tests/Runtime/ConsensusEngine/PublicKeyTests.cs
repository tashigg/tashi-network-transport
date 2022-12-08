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
                PublicKey.FromDer(der),
                PublicKey.FromDer(der)
            );
        }

        [Test]
        public void AsDer_ReturnsConstructorValue()
        {
            var der = new byte[PublicKey.DerLength];
            Assert.AreEqual(
                PublicKey.FromDer(der).AsDer(),
                der
            );
        }

        [Test]
        public void FromDer_ForInvalidValue_ThrowsException()
        {
            var small = new byte[PublicKey.DerLength - 1];
            Assert.Throws<ArgumentException>(() => PublicKey.FromDer(small));

            var large = new byte[PublicKey.DerLength + 1];
            Assert.Throws<ArgumentException>(() => PublicKey.FromDer(large));
        }
    }
}