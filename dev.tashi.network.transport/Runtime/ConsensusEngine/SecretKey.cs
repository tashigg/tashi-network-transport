#nullable enable

using System;
using System.Runtime.InteropServices;

namespace Tashi.ConsensusEngine
{

    /// <summary>
    /// A secret key in DER format.
    /// </summary>
    public class SecretKey
    {
        public const uint DerLength = 121;

        public byte[] Der { get; }

        private PublicKey? _publicKey;

        private SecretKey(byte[] der)
        {
            if (der.Length != DerLength)
            {
                throw new ArgumentException($"The DER encoding must have {DerLength} bytes");
            }

            Der = der;
        }

        public static SecretKey FromDer(byte[] der)
        {
            return new SecretKey(der);
        }

        public static SecretKey Generate()
        {
            var der = new byte[DerLength];
            uint actualLen = 0;
            var result = tce_secret_key_generate(der, (UInt32)der.Length, ref actualLen);
            if (result != Result.Success || actualLen != der.Length)
            {
                throw new Exception($"Failed to generate a secret key: {result}");
            }

            return new SecretKey(der);
        }

        public byte[] AsDer()
        {
            return Der;
        }

        public PublicKey PublicKey
        {
            get
            {
                if (_publicKey == null)
                {
                    var der = new byte[PublicKey.DerLength];
                    UInt32 actualLen = 0;

                    var result = tce_public_key_get(
                        Der,
                        (uint)Der.Length,
                        der,
                        (UInt32)der.Length,
                        ref actualLen
                    );

                    if (result != Result.Success || actualLen != der.Length)
                    {
                        throw new Exception($"Failed to get the public key from the secret key: {result}");
                    }

                    _publicKey = new PublicKey(der);
                }

                return _publicKey;
            }
        }

        [DllImport("tashi_consensus_engine", EntryPoint = "tce_secret_key_generate", CallingConvention = CallingConvention.Cdecl)]
        static extern Result tce_secret_key_generate(
            [MarshalAs(UnmanagedType.LPArray)] byte[] secretKeyDer,
            UInt32 secretKeyDerCapacity,
            ref UInt32 secretKeyDerLen
        );

        [DllImport("tashi_consensus_engine", EntryPoint = "tce_public_key_get", CallingConvention = CallingConvention.Cdecl)]
        static extern Result tce_public_key_get(
            byte[] secretKeyDer,
            UInt32 secretKeyDerLen,
            [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 4)]
            byte[] publicKeyDer,
            UInt32 publicKeyDerCapacity,
            ref UInt32 publicKeyDerLen
        );
    }
}
