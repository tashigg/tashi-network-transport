using System;
using System.Net;
using System.Net.Sockets;
using Tashi.ConsensusEngine;
using NUnit.Framework;
using UnityEngine;

namespace TashiConsensusEngineTests
{
    public class SockAddrTests
    {
        const ulong TestClientId = 0x1122334455667788;
        const string TestAddrString = "fd54:6173:6869:0:1122:3344:5566:7788";
        const ushort TestAddrPort = 0x6767;

        [Test]
        public void TestFromClientId()
        {
            SockAddr addr = SockAddr.FromClientId(TestClientId);

            // Expected value comes first
            Assert.AreEqual(TestClientId, addr.ClientId);
        }

        [Test]
        public void TestHashCodeAndEquals()
        {
            NativeAddressFamily.InitializeStatics(OperatingSystemFamily.Linux);

            // These will be two different instances to ensure `Equals` and `GetHashCode` don't use referential equality
            SockAddr addrA = SockAddr.FromClientId(TestClientId);
            SockAddr addrB = SockAddr.FromClientId(TestClientId);

            // Expected value comes first
            Assert.AreEqual(addrA.GetHashCode(), addrB.GetHashCode());
            Assert.AreEqual(addrA, addrB);
        }

        [Test]
        public void TestToFromIPEndPoint()
        {
            SockAddr addr = SockAddr.FromClientId(TestClientId);

            Assert.IsTrue(IPAddress.TryParse(TestAddrString, out var ipAddr));

            IPEndPoint testEndPoint = new IPEndPoint(ipAddr, TestAddrPort);

            Assert.AreEqual(testEndPoint, addr.IPEndPoint);
        }

        [Test]
        public void TestAddressFamilyParsing_Windows()
        {
            NativeAddressFamily.InitializeStatics(OperatingSystemFamily.Windows);

            var actual = NativeAddressFamily.ToAddressFamily(BitConverter.GetBytes((UInt16)23));
            Assert.AreEqual(AddressFamily.InterNetworkV6, actual);
        }

        [Test]
        public void TestAddressFamilyParsing_Linux()
        {
            NativeAddressFamily.InitializeStatics(OperatingSystemFamily.Linux);

            var actual = NativeAddressFamily.ToAddressFamily(BitConverter.GetBytes((UInt16)10));
            Assert.AreEqual(AddressFamily.InterNetworkV6, actual);
        }

        [Test]
        public void TestAddressFamilyParsing_Mac()
        {
            NativeAddressFamily.InitializeStatics(OperatingSystemFamily.MacOSX);

            var actual = NativeAddressFamily.ToAddressFamily(new byte[] { 0, 30 });
            Assert.AreEqual( AddressFamily.InterNetworkV6, actual);
        }
    }
}