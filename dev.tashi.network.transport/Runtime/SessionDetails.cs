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
        public DirectAddressBookEntry? TashiRelay;
        public AddressBookEntry? AddressBookEntry;

        private int _lastUpdatePlayerOptionsHash;
        private int _lastUpdateLobbyOptionsHash;

        /// <summary>
        /// Adds the required data to <see cref="UpdatePlayerOptions"/>, which must be sent to the lobby.
        /// </summary>
        /// <param name="playerOptions"></param>
        /// <returns>True if the data added has been updated since the last call.</returns>
        public bool AddTo(UpdatePlayerOptions playerOptions)
        {
            if (AddressBookEntry is null)
            {
                return false;
            }

            playerOptions.Data ??= new();

            var playerDataObject = new PlayerDataObject(
                PlayerDataObject.VisibilityOptions.Member,
                AddressBookEntry.Serialize());

            var playerDataObjectHash = playerDataObject.GetHashCode();

            playerOptions.Data.Add("AddressBookEntry", playerDataObject);

            if (playerDataObjectHash != _lastUpdatePlayerOptionsHash)
            {
                _lastUpdatePlayerOptionsHash = playerDataObjectHash;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Adds the required data to <see cref="UpdateLobbyOptions"/>, which must be sent to the lobby.
        /// </summary>
        /// <param name="lobbyOptions"></param>
        /// <returns>True if the data added has been updated since the last call.</returns>
        public bool AddTo(UpdateLobbyOptions lobbyOptions)
        {
            if (TashiRelay is null)
            {
                return false;
            }

            lobbyOptions.Data ??= new();

            var dataObject = new DataObject(DataObject.VisibilityOptions.Member, TashiRelay.Serialize());
            var dataObjectHash = dataObject.GetHashCode();

            lobbyOptions.Data.Add("TashiRelay", dataObject);

            if (dataObjectHash != _lastUpdateLobbyOptionsHash)
            {
                _lastUpdateLobbyOptionsHash = dataObjectHash;
                return true;
            }

            return false;
        }
    }
}