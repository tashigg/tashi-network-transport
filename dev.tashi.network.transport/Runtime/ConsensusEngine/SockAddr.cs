using System;
using System.Buffers.Binary;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace Tashi.ConsensusEngine
{
    [StructLayout(LayoutKind.Sequential, Pack=8, Size=128)]
    public struct SockAddr
    {
        public SockAddr(IPEndPoint ipEndPoint)
        {
            _addressFamily = ipEndPoint.AddressFamily;
            _data = new byte[DataLen];
            
            BinaryPrimitives.WriteUInt16BigEndian(_dataSpan, (ushort) ipEndPoint.Port);
            
            ipEndPoint.Address.TryWriteBytes(_dataSpan, out int bytesWritten);
        }

        // Total struct size is 128 bytes, minus 2 bytes for AddressFamily
        private const int DataLen = 126;
        
        [MarshalAs(UnmanagedType.U2)] private AddressFamily _addressFamily;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst=DataLen)] private byte[] _data;
        
        public AddressFamily AddressFamily => _addressFamily;

        private Span<byte> _dataSpan => new(_data);

        internal UIntPtr Len
        {
            get
            {
                switch (_addressFamily)
                {
                    case AddressFamily.InterNetwork:
                        // 2 bytes for `AddressFamily`, 2 bytes for the port, 4 bytes for the address.
                        return new(2 + 2 + 4);
                    case AddressFamily.InterNetworkV6:
                        // 2 bytes for `AddressFamily`, 2 bytes for the port,
                        // 4 bytes for `flowinfo`, 16 bytes for the address, 4 bytes for the scope ID.
                        return new(2 + 2 + 4 + 16 + 4);
                    default:
                        throw new InvalidOperationException($"SockAddr.AddressFamily is not an IP subtype: {AddressFamily}");
                }
            }
        }

        public IPEndPoint IPEndPoint
        {
            get
            {
                // First two bytes are the port
                UInt16 port = BinaryPrimitives.ReadUInt16BigEndian(_dataSpan);

                IPAddress address;

                switch (_addressFamily)
                {
                    case AddressFamily.InterNetwork:
                        address = new IPAddress(_dataSpan[2..6]);
                        break;
                    case AddressFamily.InterNetworkV6:
                        // sockaddr_in6 actually has the `flowinfo` field before the address, which is 4 bytes.
                        // Then the scope identifier follows the address, which for most intents and purposes is zero.
                        address = new IPAddress(_dataSpan[6..22], BinaryPrimitives.ReadUInt16BigEndian(_dataSpan[22..]));
                        break;
                    default:
                        throw new InvalidOperationException($"SockAddr.AddressFamily is not an IP subtype: {AddressFamily}");
                }

                return new IPEndPoint(address, port);
            }
        }

        public bool HasClientId
        {
            get
            {
                if (_addressFamily != AddressFamily.InterNetworkV6) return false;

                return
                    BinaryPrimitives.ReadUInt16BigEndian(_dataSpan) == ClientIdPort &&
                    _dataSpan[6..].StartsWith(new ReadOnlySpan<byte>(ClientIdAddressPrefix));
            }
        }

        public ulong? ClientId
        {
            get
            {
                if (!HasClientId) return null;
                return BinaryPrimitives.ReadUInt64BigEndian(_dataSpan[10..]);
            }
        }

        public static SockAddr FromClientId(ulong clientId)
        {
            SockAddr addrOut = new SockAddr
            {
                _addressFamily = AddressFamily.InterNetworkV6,
                _data = new byte[DataLen]
            };
            
            var dataSpan = addrOut._dataSpan;

            BinaryPrimitives.WriteUInt16BigEndian(dataSpan, ClientIdPort);
            
            // sockaddr_in6 actually has the `flowinfo` field before the address, which is 4 bytes. 
            // By default we just initialize that to zero.
            new ReadOnlySpan<byte>(ClientIdAddressPrefix).TryCopyTo(dataSpan[6..]);
            
            BinaryPrimitives.WriteUInt64BigEndian(dataSpan[14..], clientId);

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