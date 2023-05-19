#nullable enable

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

        public const int RawBytesLength = 64; 

        public byte[] Der { get; }

        /// <summary>
        ///  Returns the uncompressed raw bytes of the public key (X and Y coordinates concatenated together).
        /// </summary>
        public ReadOnlySpan<Byte> RawBytes => new(Der, Der.Length - RawBytesLength, RawBytesLength);

        public PublicKey(byte[] der)
        {
            if (der.Length != DerLength)
            {
                throw new ArgumentException($"The DER encoding must have {DerLength} bytes");
            }

            Der = der;
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
