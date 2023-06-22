#nullable enable

using System;
using System.Collections.Generic;
using Unity.Services.Lobbies.Models;
using Tashi.ConsensusEngine;
using Unity.Services.Authentication;
using UnityEngine;

namespace Tashi.NetworkTransport
{
    public class SessionSetupDetails
    {
        public DirectAddressBookEntry? TashiRelay;
        public List<AddressBookEntry> AddressBook;
        public string? HostId;

        private AddressBookEntry _entry;
        private bool _isHost;

        public SessionSetupDetails(bool isHost, AddressBookEntry entry)
        {
            _isHost = isHost;
            _entry = entry;
        }

        public void Update(Lobby lobby)
        {
            HostId = lobby.HostId;

            if (lobby.Data.TryGetValue("TashiRelay", out var tashiRelayData))
            {
                TashiRelay = DirectAddressBookEntry.Deserialize(tashiRelayData.Value);
                if (TashiRelay == null)
                {
                    throw new Exception($"Couldn't deserialize Tashi Relay from {tashiRelayData}");
                }
            }

            foreach (var player in lobby.Players)
            {
                if (player.Id == AuthenticationService.Instance.PlayerId || player.Data == null)
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
                    AddressBook.Add(entry);
                }
            }
        }

        public void ModifyPlayerDataForUpdate(Dictionary<string, PlayerDataObject> playerData)
        {
            playerData.Add("AddressBookEntry",
                new PlayerDataObject(
                    PlayerDataObject.VisibilityOptions.Member,
                    _entry.Serialize()
                )
            );

            Debug.Log($"Sending AddressBookEntry = {_entry.Serialize()}");
        }

        public void ModifyLobbyDataForUpdate(Dictionary<string, LobbyDataObject> lobbyData)
        {

        }
    }
}