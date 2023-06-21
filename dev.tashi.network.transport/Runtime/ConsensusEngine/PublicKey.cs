#nullable enable

using System;
using System.Buffers.Binary;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using Newtonsoft.Json;
using UnityEngine;

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
        [JsonIgnore]
        public ReadOnlySpan<Byte> RawBytes => new(Der, Der.Length - RawBytesLength, RawBytesLength);

        public PublicKey(byte[] der)
        {
            if (der.Length != DerLength)
            {
                throw new ArgumentException($"The DER encoding must have {DerLength} bytes");
            }

            Der = der;
        }

        internal static PublicKey FromPtr(IntPtr der, UIntPtr derLen)
        {
            if (derLen != new UIntPtr(DerLength))
            {
                throw new ArgumentException($"DER encoded public key must be {DerLength} bytes");
            }

            byte[] derOut = new byte[DerLength];
            Marshal.Copy(der, derOut, 0, (int) DerLength);

            return new PublicKey(derOut);
        }

        public bool Equals(PublicKey? other)
        {
            if (ReferenceEquals(null, other))
            {
                return false;
            }

            return ReferenceEquals(this, other) || Der.SequenceEqual(other.Der);
        }

        // Use the first 8 bytes of the public key, which should be the high bytes of the X coordinate.
        // The last 32 bytes of the public key is the Y coordinate, which is reduced to a single bit
        // in the compressed form, as there's only two possible Y coordinates for a given X coordinate.

        // Using this method ensures the generated value is the same regardless of the platform endianness.
        public ulong ClientId => BinaryPrimitives.ReadUInt64BigEndian(RawBytes);

        [JsonIgnore]
        public SockAddr SyntheticSockAddr => SockAddr.FromClientId(ClientId);

        [JsonIgnore]
        public IPEndPoint SyntheticEndpoint => SyntheticSockAddr.IPEndPoint;
    }
}
