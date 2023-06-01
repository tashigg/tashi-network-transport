#nullable enable

using System;
using System.Net;
using Newtonsoft.Json;

namespace Tashi.ConsensusEngine
{
    class IPAddressConverter : JsonConverter<IPAddress>
    {
        public override void WriteJson(JsonWriter writer, IPAddress? value, JsonSerializer serializer)
        {
            writer.WriteValue(value?.ToString());
        }

        public override IPAddress? ReadJson(JsonReader reader, Type objectType, IPAddress? existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            if (reader.Value is string s)
            {
                if (IPAddress.TryParse(s, out var address))
                {
                    return address;
                }
            }

            return null;
        }
    }

    public abstract class AddressBookEntry : IEquatable<AddressBookEntry>
    {
        public PublicKey PublicKey;

        private static JsonSerializerSettings _jsonSerializerSettings = new()
        {
            TypeNameHandling = TypeNameHandling.All,
            Converters = new JsonConverter[] { new IPAddressConverter() }
        };


        protected AddressBookEntry(PublicKey publicKey)
        {
            PublicKey = publicKey;
        }

        public string Serialize()
        {
            return JsonConvert.SerializeObject(this, _jsonSerializerSettings);
        }

        public static AddressBookEntry? Deserialize(string? data)
        {
            return data is null ? null : JsonConvert.DeserializeObject<AddressBookEntry>(data, _jsonSerializerSettings);
        }

        public abstract bool Equals(AddressBookEntry other);
    }

    public class DirectAddressBookEntry : AddressBookEntry
    {
        public readonly IPAddress Address;
        public readonly int Port;

        [JsonIgnore]
        public IPEndPoint EndPoint => new(Address, Port);

        [JsonConstructor]
        public DirectAddressBookEntry(IPAddress address, int port, PublicKey publicKey) : base(publicKey)
        {
            Address = address;
            Port = port;
        }

        public DirectAddressBookEntry(IPEndPoint endPoint, PublicKey publicKey) : this(endPoint.Address, endPoint.Port, publicKey)
        {
        }

        public override bool Equals(AddressBookEntry other)
        {
            if (other is not DirectAddressBookEntry direct)
            {
                return false;
            }

            return Address.Equals(direct.Address) && PublicKey.Equals(direct.PublicKey);
        }
    }

    public class ExternalAddressBookEntry : AddressBookEntry
    {
        public readonly string RelayJoinCode;
        
        public ExternalAddressBookEntry(string relayJoinCode, PublicKey publicKey) : base(publicKey)
        {
            RelayJoinCode = relayJoinCode;
        }

        public override bool Equals(AddressBookEntry other)
        {
            if (other is not ExternalAddressBookEntry external)
            {
                return false;
            }

            return RelayJoinCode.Equals(external.RelayJoinCode) && PublicKey.Equals(external.PublicKey);
        }
    }
}
