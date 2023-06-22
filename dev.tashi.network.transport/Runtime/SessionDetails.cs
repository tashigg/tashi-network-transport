#nullable enable

using System.Collections.Generic;
using Unity.Services.Lobbies.Models;
using Tashi.ConsensusEngine;
using Unity.Services.Lobbies;
using UnityEngine;

namespace Tashi.NetworkTransport
{
    public class IncomingSessionDetails
    {
        public DirectAddressBookEntry? TashiRelay;
        public List<AddressBookEntry> AddressBook = new();
        public AddressBookEntry? Host;

        public static IncomingSessionDetails FromUnityLobby(Lobby lobby)
        {
            var sessionDetails = new IncomingSessionDetails();

            if (lobby.Data is not null && lobby.Data.TryGetValue("TashiRelay", out var tashiRelayData))
            {
                sessionDetails.TashiRelay = DirectAddressBookEntry.Deserialize(tashiRelayData.Value);
            }

            foreach (var player in lobby.Players)
            {
                if (player.Data == null)
                {
                    continue;
                }

                if (!player.Data.TryGetValue("AddressBookEntry", out var addressBookEntryData))
                {
                    Debug.LogError($"Player {player.Id} didn't provide an AddressBookEntry");
                    continue;
                }

                Debug.Log($"Received AddressBookEntry = {addressBookEntryData.Value}");

                var entry = AddressBookEntry.Deserialize(addressBookEntryData.Value);
                if (entry != null)
                {
                    if (player.Id == lobby.HostId)
                    {
                        sessionDetails.Host = entry;
                    }

                    sessionDetails.AddressBook.Add(entry);
                }
            }

            return sessionDetails;
        }
    }

    public class OutgoingSessionDetails
    {
        public DirectAddressBookEntry? Relay;
        public AddressBookEntry? AddressBookEntry;

        public void AddTo(UpdatePlayerOptions playerOptions)
        {
            if (AddressBookEntry is null)
            {
                return;
            }

            playerOptions.Data ??= new();

            playerOptions.Data.Add("AddressBookEntry",
                new PlayerDataObject(
                    PlayerDataObject.VisibilityOptions.Member,
                    AddressBookEntry.Serialize()
                )
            );
        }

        public void AddTo(UpdateLobbyOptions lobbyOptions)
        {
            if (Relay is null)
            {
                return;
            }

            lobbyOptions.Data ??= new();

            lobbyOptions.Data.Add(
                "TashiRelay",
                new DataObject(DataObject.VisibilityOptions.Member, Relay.Serialize())
            );
        }
    }
}