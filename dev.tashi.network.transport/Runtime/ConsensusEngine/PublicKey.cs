using System;
using System.Linq;

namespace Tashi.ConsensusEngine
{
    /// <summary>
    /// A public key in DER format. This is obtained from a SecretKey.
    /// </summary>
    public class PublicKey : IEquatable<PublicKey?>
    {
        public const uint DerLength = 91;

        public byte[] Der { get; }

        private PublicKey(byte[] der)
        {
            if (der.Length != DerLength)
            {
                throw new ArgumentException($"The DER encoding must have {DerLength} bytes");
            }

            Der = der;
        }

        public static PublicKey FromDer(byte[] der)
        {
            return new PublicKey(der);
        }

        public byte[] AsDer()
        {
            return Der;
        }

        public bool Equals(PublicKey? other)
        {
            if (ReferenceEquals(null, other))
            {
                return false;
            }

            return ReferenceEquals(this, other) || Der.SequenceEqual(other.Der);
        }
    }
}
