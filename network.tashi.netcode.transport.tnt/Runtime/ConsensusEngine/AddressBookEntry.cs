using System;
using System.Linq;
using System.Net;

namespace Tashi.ConsensusEngine
{
    public struct AddressBookEntry : IEquatable<AddressBookEntry>
    {
        public IPEndPoint Address;
        public PublicKey PublicKey;

        public AddressBookEntry(IPEndPoint address, PublicKey publicKey)
        {
            Address = address;
            PublicKey = publicKey;
        }

        public bool Equals(AddressBookEntry other)
        {
            return Address.Equals(other.Address) && PublicKey.Equals(other.PublicKey);
        }
    }
}