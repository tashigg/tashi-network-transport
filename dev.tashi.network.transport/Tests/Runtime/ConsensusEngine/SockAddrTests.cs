using System;
using System.Net;
using Tashi.ConsensusEngine;
using NUnit.Framework;

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
    }
}