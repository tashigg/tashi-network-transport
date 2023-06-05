using System;
using System.Buffers.Binary;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Tashi.ConsensusEngine
{
    [StructLayout(LayoutKind.Sequential, Pack=8, Size=128)]
    public struct SockAddr
    {
        private const UInt16 NativeInetNetwork = 10;
        private const UInt16 NativeInetNetwork6 = 17;

        // Total struct size is 128 bytes, minus 2 bytes for AddressFamily
        private const int DataLen = 126;
        
        [MarshalAs(UnmanagedType.U2)] private UInt16 _addressFamily;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst=DataLen)] private byte[] _data;

        // C#'s AddressFamily doesn't match up to the native `AF_*` values and
        // it's 4 bytes instead of 2.
        public AddressFamily AddressFamily
        {
            get
            {
                switch (_addressFamily)
                {
                    case NativeInetNetwork:
                        return AddressFamily.InterNetwork;
                    case NativeInetNetwork6:
                        return AddressFamily.InterNetworkV6;
                    default:
                        throw new InvalidOperationException($"SockAddr.AddressFamily is not an IP subtype: {_addressFamily}");
                }
            }
        }

        internal UIntPtr Len
        {
            get
            {
                switch (AddressFamily)
                {
                    case AddressFamily.InterNetwork:
                        // 2 bytes for `AddressFamily`, 2 bytes for the port, 4 bytes for the address.
                        return new(2 + 2 + 4);
                    case AddressFamily.InterNetworkV6:
                        // 2 bytes for `AddressFamily`, 2 bytes for the port,
                        // 4 bytes for `flowinfo`, 16 bytes for the address, 4 bytes for the scope ID.
                        return new(2 + 2 + 4 + 16 + 4);
                    default:
                        throw new InvalidOperationException($"SockAddr.AddressFamily is not an IP subtype: {_addressFamily}");
                }
            }
        }

        public IPEndPoint IPEndPoint
        {
            get
            {
                // First two bytes are the port
                UInt16 port = BinaryPrimitives.ReadUInt16BigEndian(_data);

                IPAddress address;

                switch (_addressFamily)
                {
                    case NativeInetNetwork:
                        address = new IPAddress(_data[2..6]);
                        break;
                    case NativeInetNetwork6:
                        // sockaddr_in6 actually has the `flowinfo` field before the address, which is 4 bytes.
                        // Then the scope identifier follows the address, which for most intents and purposes is zero.
                        address = new IPAddress(_data[6..22], BinaryPrimitives.ReadUInt16BigEndian(_data[22..]));
                        break;
                    default:
                        throw new InvalidOperationException($"SockAddr.AddressFamily is not an IP subtype: {AddressFamily}");
                }

                return new IPEndPoint(address, port);
            }

            set
            {
                // Unintuitively, the port is at the beginning of the data.
                BinaryPrimitives.WriteUInt16BigEndian(_data, (ushort) value.Port);

                switch (value.AddressFamily)
                {
                    case AddressFamily.InterNetwork:
                        _addressFamily = NativeInetNetwork;
                        value.Address.TryWriteBytes(_data[2..], out _);
                        break;
                    case AddressFamily.InterNetworkV6:
                        _addressFamily = NativeInetNetwork6;
                        // See above for why this starts at 6 instead of 2.
                        value.Address.TryWriteBytes(_data[6..], out _);
                        break;
                    default:
                        throw new InvalidOperationException($"SockAddr.AddressFamily is not an IP subtype: {value.AddressFamily}");
                }
            }
        }

        public bool HasClientId
        {
            get
            {
                if (_addressFamily != NativeInetNetwork6) return false;

                return
                    BinaryPrimitives.ReadUInt16BigEndian(_data) == ClientIdPort &&
                    new Span<byte>(_data[6..]).StartsWith(new ReadOnlySpan<byte>(ClientIdAddressPrefix));
            }
        }

        public ulong? ClientId
        {
            get
            {
                // This checks that the address is IPv6 and that it has our designated prefix,
                // then reads the 64-bit address portion as the client ID.
                if (!HasClientId) return null;
                return BinaryPrimitives.ReadUInt64BigEndian(_data[10..]);
            }
        }

        public static SockAddr FromClientId(ulong clientId)
        {
            SockAddr addrOut = new SockAddr
            {
                _addressFamily = NativeInetNetwork6,
                _data = new byte[DataLen]
            };
            
            // IMPORTANT: slicing a `Span` creates views into the underlying data,
            // but slicing a `byte[]` creates copies.
            var dataSpan = new Span<byte>(addrOut._data);

            var portSpan = dataSpan[..2];

            BinaryPrimitives.WriteUInt16BigEndian(portSpan, ClientIdPort);

            var addrPrefixSpan = new ReadOnlySpan<byte>(ClientIdAddressPrefix);
                
            // sockaddr_in6 actually has the `flowinfo` field before the address, which is 4 bytes. 
            // By default we just initialize that to zero.
            var addrSpan = dataSpan[6..];

            var successful = addrPrefixSpan.TryCopyTo(addrSpan);
            
            Debug.Assert(successful);
            
            var clientIdSpan = addrSpan[ClientIdAddressPrefix.Length ..];
            
            BinaryPrimitives.WriteUInt64BigEndian(clientIdSpan, clientId);

            return addrOut;
        }
        
        private static readonly byte[] ClientIdAddressPrefix = {
            // `fd00:/8` designates this as a locally assigned ULA (unique local address).
            // `fc00::/8` is reserved for future use.
            0xfd,
            // The next 5 bytes are the global ID.
            // Meant to be random, but this lets us unambiguously identify generated addresses
            // when we support mixed networking topology.
            //
            // It's unlikely for another organization to randomly choose this global ID 
            // *and* want to use TNT in their network. 
            (byte) 'T',
            (byte) 'a',
            (byte) 's',
            (byte) 'h',
            (byte) 'i',
            // Subnet ID (2 bytes), just choosing zero for this one.
            0,
            0,
        };

        private const ushort ClientIdPort = 0x6767; // 'gg'
    }
}