using System;
using System.Buffers.Binary;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Assertions;

namespace Tashi.ConsensusEngine
{
    [StructLayout(LayoutKind.Sequential, Pack = 8, Size = 128)]
    public struct SockAddr : IEquatable<SockAddr>
    {
        // Total struct size is 128 bytes, minus 2 bytes for AddressFamily
        private const int DataLen = 126;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        private byte[] _addressFamily;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = DataLen)]
        private byte[] _data;

        // C#'s AddressFamily doesn't match up to the native `AF_*` values and
        // it's 4 bytes instead of 2.
        public AddressFamily AddressFamily => NativeAddressFamily.ToAddressFamily(_addressFamily);

        internal UInt64 Len => 128;

        public IPEndPoint IPEndPoint
        {
            get
            {
                UInt16 port = BinaryPrimitives.ReadUInt16BigEndian(PortSpan);

                IPAddress address;

                switch (AddressFamily)
                {
                    case AddressFamily.InterNetwork:
                        address = new IPAddress(_data[2..6]);
                        break;
                    case AddressFamily.InterNetworkV6:
                        // sockaddr_in6 actually has the `flowinfo` field before the address, which is 4 bytes.
                        // Then the scope identifier follows the address, which for most intents and purposes is zero.
                        address = new IPAddress(_data[6..22], BinaryPrimitives.ReadUInt16BigEndian(_data[22..]));
                        break;
                    default:
                        throw new InvalidOperationException(
                            $"SockAddr.AddressFamily is not an IP subtype: {AddressFamily}");
                }

                return new IPEndPoint(address, port);
            }

            set
            {
                _addressFamily = NativeAddressFamily.FromAddressFamily(value.AddressFamily);

                BinaryPrimitives.WriteUInt16BigEndian(PortSpan, (ushort)value.Port);

                value.Address.TryWriteBytes(AddrSpan, out _);
            }
        }

        public bool HasClientId
        {
            get
            {
                if (NativeAddressFamily.ToAddressFamily(_addressFamily) != AddressFamily.InterNetworkV6) return false;

                return
                    BinaryPrimitives.ReadUInt16BigEndian(PortSpan) == ClientIdPort &&
                    AddrSpan.StartsWith(new ReadOnlySpan<byte>(ClientIdAddressPrefix));
            }
        }

        public ulong? ClientId
        {
            get
            {
                // This checks that the address is IPv6 and that it has our designated prefix,
                // then reads the 64-bit address portion as the client ID.
                if (!HasClientId) return null;
                return BinaryPrimitives.ReadUInt64BigEndian(AddrSpan[ClientIdAddressPrefix.Length..]);
            }
        }


        private Span<byte> AddrSpan => AddressFamily switch
        {
            AddressFamily.InterNetwork => new Span<byte>(_data, 2, 4),
            // sockaddr_in6 actually has the `flowinfo` field before the address, which is 4 bytes.
            // For our intents and purposes that is always zero.
            AddressFamily.InterNetworkV6 => new Span<byte>(_data, 6, 16),
            _ => throw new InvalidOperationException($"SockAddr.AddressFamily is not an IP subtype: {AddressFamily}")
        };

        // Unintuitively, the port is at the beginning of the data.
        private Span<byte> PortSpan => new(_data, 0, 2);

        public static SockAddr FromClientId(ulong clientId)
        {
            SockAddr addrOut = new SockAddr
            {
                _addressFamily = NativeAddressFamily.FromAddressFamily(AddressFamily.InterNetworkV6),
                _data = new byte[DataLen]
            };

            // IMPORTANT: slicing a `Span` creates views into the underlying data,
            // but slicing a `byte[]` creates copies.
            var dataSpan = new Span<byte>(addrOut._data);

            var portSpan = dataSpan[..2];

            BinaryPrimitives.WriteUInt16BigEndian(portSpan, ClientIdPort);

            var addrPrefixSpan = new ReadOnlySpan<byte>(ClientIdAddressPrefix);

            // By default we just initialize that to zero.
            var addrSpan = addrOut.AddrSpan;

            var successful = addrPrefixSpan.TryCopyTo(addrSpan);

            Debug.Assert(successful);

            var clientIdSpan = addrSpan[ClientIdAddressPrefix.Length..];

            BinaryPrimitives.WriteUInt64BigEndian(clientIdSpan, clientId);

            return addrOut;
        }

        // `socklen_t` is defined to be 32 bits (signed on Windows, unsigned on *Nix but it doesn't really matter).
        internal static SockAddr FromPtr(IntPtr sockAddr, uint sockAddrLen)
        {
            // A `sockaddr` must always at least contain a 2-byte address family value,
            // and is defined to be 128 bytes or less.
            if (sockAddrLen is < 2 or > 128)
            {
                throw new ArgumentException($"sockAddrLen out of range: {sockAddrLen}");
            }

            var sockAddrOut = new SockAddr();
            sockAddrOut._data = new byte[126];
            sockAddrOut._addressFamily = new byte[2];

            // macOS and some other BSDs use the first byte to represent `ss_len`,
            // but others use the address family field as 2 bytes. Some BSDs
            // even got rid of `ss_len`.
            Marshal.Copy(sockAddr, sockAddrOut._addressFamily, 0, 2);
            
            // Offset to the actual data.
            var sockAddrData = sockAddr + 2;

            Marshal.Copy(sockAddrData, sockAddrOut._data, 0, (int) sockAddrLen - 2);

            return sockAddrOut;
        }

        public override string ToString()
        {
            return $"{IPEndPoint} (clientId = {ClientId})";
        }

        private static readonly byte[] ClientIdAddressPrefix =
        {
            // `fd00:/8` designates this as a locally assigned ULA (unique local address).
            // `fc00::/8` is reserved for future use.
            0xfd,
            // The next 5 bytes are the global ID.
            // Meant to be random, but this lets us unambiguously identify generated addresses
            // when we support mixed networking topology.
            //
            // It's unlikely for another organization to randomly choose this global ID
            // *and* want to use TNT in their network.
            (byte)'T',
            (byte)'a',
            (byte)'s',
            (byte)'h',
            (byte)'i',
            // Subnet ID (2 bytes), just choosing zero for this one.
            0,
            0,
        };

        private const ushort ClientIdPort = 0x6767; // 'gg'

        public bool Equals(SockAddr other)
        {
            return _addressFamily.SequenceEqual(other._addressFamily) && _data.SequenceEqual(other._data);
        }

        public override int GetHashCode()
        {
            var hasher = new HashCode();

            foreach (var b in _addressFamily)
            {
                hasher.Add(b);
            }

            // `hashCode.AddBytes` is unavailable
            foreach (var b in _data)
            {
                hasher.Add(b);
            }

            return hasher.ToHashCode();
        }
    }

    /// <summary>
    /// A supplement for the standard `AddressFamily` enum with correct constants for the current platform.
    /// </summary>
    internal class NativeAddressFamily
    {
        private static OperatingSystemFamily _operatingSystemFamily;
        
        /// <summary>
        /// Corresponds to AF_INET
        /// </summary>
        private const UInt16 InterNetwork = 2;

        /// <summary>
        /// Corresponds to AF_INET6
        /// </summary>
        private static UInt16 InterNetworkV6
        {
            get
            {
                // Annoyingly, `AF_INET6` has a different value on *every* platform.
                // While the major modern operating systems all started off with copies of the Berkeley Sockets API,
                // each of them implemented IPv6 support separately, and apparently never bothered
                // to agree on a single constant value.

                // For once, Unity's APIs actually save us here instead of making things harder than they should be.
                // There isn't anything close to this convenient in .NET Standard 2.1.
                // There's https://learn.microsoft.com/en-us/dotnet/api/system.platformid?view=netstandard-2.1
                // but the comment on the `MacOSX` constant says it's not returned on .NET Core and it instead returns
                // `Unix`, which is absolutely useless.
                //
                // There's more convenient getters in newer .NET versions but that of course doesn't help us here:
                // https://learn.microsoft.com/en-us/dotnet/api/system.operatingsystem.iswindows
                return _operatingSystemFamily switch
                {
                    OperatingSystemFamily.Windows => 23,
                    OperatingSystemFamily.Linux => 10,
                    OperatingSystemFamily.MacOSX => 30,
                    _ => throw new Exception("unsupported operating system")
                };
            }
        }

        /// <summary>
        /// This struct's methods support being called from any thread, but it
        /// must use functions that are only callable from the main thread and
        /// are lazily initialized. This must be called from the main thread
        /// to initialize those statics.
        /// </summary>
        public static void InitializeStatics(OperatingSystemFamily operatingSystemFamily)
        {
            _operatingSystemFamily = operatingSystemFamily;
        }

        public static AddressFamily ToAddressFamily(byte[] nativeAddressFamilyBytes)
        {
            Assert.AreEqual(nativeAddressFamilyBytes.Length, 2);

            UInt16 value = _operatingSystemFamily switch
            {
                OperatingSystemFamily.MacOSX => nativeAddressFamilyBytes[1],
                _ => BitConverter.ToUInt16(nativeAddressFamilyBytes),
            };
            
            // Can't use `switch()` here because `InterNetwork6` isn't a constant.
            if (value == InterNetwork)
            {
                return AddressFamily.InterNetwork;
            }

            if (value == InterNetworkV6)
            {
                return AddressFamily.InterNetworkV6;
            }

            throw new ArgumentException($"unknown AddressFamily value: {value}");
        }

        public static byte[] FromAddressFamily(AddressFamily addressFamily)
        {
            var nativeAddressFamily = addressFamily switch
            {
                AddressFamily.InterNetwork => InterNetwork,
                AddressFamily.InterNetworkV6 => InterNetworkV6,
                _ => throw new ArgumentException($"unsupported AddressFamily type: {addressFamily}")
            };

            if (_operatingSystemFamily == OperatingSystemFamily.MacOSX)
            {
                return new byte[] { 0, (byte)nativeAddressFamily };
            }
            else
            {
                return BitConverter.GetBytes(nativeAddressFamily);
            }
        }
    }
}