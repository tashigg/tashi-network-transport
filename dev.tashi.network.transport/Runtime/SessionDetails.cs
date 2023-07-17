#nullable enable

using System.Collections.Generic;
using Unity.Services.Lobbies.Models;
using Tashi.ConsensusEngine;
using Unity.Services.Lobbies;
using UnityEngine;

namespace Tashi.NetworkTransport
{
    /// <summary>
    /// Session details that have been received from other players.
    /// </summary>
    public class IncomingSessionDetails
    {
        /// <summary>
        /// An optional <see cref="DirectAddressBookEntry"/> that specifies the address of an allocated Tashi Relay
        /// server. This is provided by the initial session host. A Tashi Relay isn't always necessary, but it can aid
        /// in bypassing NAT and firewalls for many players.
        /// </summary>
        public DirectAddressBookEntry? TashiRelay;

        /// <summary>
        /// A list of <see cref="AddressBookEntry"/> that is used to define the initial session address book.
        /// </summary>
        public List<AddressBookEntry> AddressBook = new();

        /// <summary>
        /// The session host's <see cref="AddressBookEntry"/>.
        /// </summary>
        public AddressBookEntry? Host;

        /// <summary>
        /// Creates a <see cref="IncomingSessionDetails"/> from a <see cref="Lobby"/>.
        /// </summary>
        /// <param name="lobby">The Unity lobby to load player and host data from.</param>
        /// <returns>An <see cref="IncomingSessionDetails"/> with as many details filled in as possible.</returns>
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

    /// <summary>
    /// Session details that should be shared with other players.
    /// </summary>
    public class OutgoingSessionDetails
    {
        /// <summary>
        /// An optional <see cref="DirectAddressBookEntry"/> that specifies the address of an allocated Tashi Relay
        /// server. This is provided by the initial session host. A Tashi Relay isn't always necessary, but it can aid
        /// in bypassing NAT and firewalls for many players.
        /// </summary>
        public DirectAddressBookEntry? TashiRelay;

        /// <summary>
        /// The local <see cref="AddressBookEntry"/> that will enable other players to attempt a connection.
        /// </summary>
        public AddressBookEntry? AddressBookEntry;

        private int _lastUpdatePlayerOptionsHash;
        private int _lastUpdateLobbyOptionsHash;

        /// <summary>
        /// Adds the required data to <see cref="UpdatePlayerOptions"/>, which must be sent to the lobby.
        /// </summary>
        /// <param name="playerOptions"></param>
        /// <returns><c>true</c> if the data added has been updated since the last call.</returns>
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
        /// <returns><c>true</c> if the data added has been updated since the last call.</returns>
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