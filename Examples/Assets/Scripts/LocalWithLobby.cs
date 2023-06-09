using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Tashi.ConsensusEngine;
using Tashi.NetworkTransport;
using TMPro;
using Unity.Netcode;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using UnityEngine;

public class LocalWithLobby : MonoBehaviour
{
    public TMP_InputField profileName;
    public Canvas profileMenu;
    public Canvas lobbyMenu;
    public TMP_Text statusText;

    // We only support a single lobby in this demo.
    private const string LobbyName = "example lobby";
    private const int MaxConnections = 10;

    private int _clientCount;
    private TashiNetworkTransport NetworkTransport => NetworkManager.Singleton.NetworkConfig.NetworkTransport as TashiNetworkTransport;
    private string PlayerId => AuthenticationService.Instance.PlayerId;
    private string _lobbyId;
    private bool _isLobbyHost;
    private float _nextLobbyRefresh;

    public async void SignInButtonClicked()
    {
        if (string.IsNullOrEmpty(profileName.text))
        {
            Debug.Log($"Signing in with the default profile");
            await UnityServices.InitializeAsync();
        }
        else
        {
            Debug.Log($"Signing in with profile '{profileName.text}'");
            var options = new InitializationOptions();
            options.SetProfile(profileName.text);
            await UnityServices.InitializeAsync(options);
        }

        try
        {
            AuthenticationService.Instance.SignedIn += delegate
            {
                UpdateStatusText();
                profileMenu.enabled = false;
                lobbyMenu.enabled = true;
            };

            await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            throw;
        }
    }

    public async void CreateLobbyButtonClicked()
    {
        lobbyMenu.enabled = false;

        Debug.Log("Create lobby");

        NetworkManager.Singleton.StartServer();

        var lobbyOptions = new CreateLobbyOptions
        {
            IsPrivate = false,
        };

        var lobby = await LobbyService.Instance.CreateLobbyAsync(LobbyName, MaxConnections, lobbyOptions);
        _lobbyId = lobby.Id;
        _isLobbyHost = true;
    }

    public async void JoinLobbyButtonClicked()
    {
        lobbyMenu.enabled = false;

        Debug.Log("Join lobby");

        NetworkManager.Singleton.StartClient();

        var lobby = await LobbyService.Instance.QuickJoinLobbyAsync();
        _lobbyId = lobby.Id;
    }

    private Dictionary<string, PlayerDataObject> GetPlayerData()
    {
        if (NetworkTransport.AddressBookEntry is null)
        {
            return new();
        }

        return new()
        {
            {
                "AddressBookEntry",
                new PlayerDataObject(PlayerDataObject.VisibilityOptions.Member, NetworkTransport.AddressBookEntry.Serialize())
            }
        };
    }

    private void Start()
    {
        Debug.Log("Start");

        lobbyMenu.enabled = false;

        NetworkManager.Singleton.OnClientConnectedCallback += delegate(ulong clientId)
        {
            _clientCount += 1;
            UpdateStatusText();
            Debug.Log($"Client {clientId} connected");
        };
    }

    private void OnApplicationQuit()
    {
        if (_isLobbyHost)
        {
            LobbyService.Instance.DeleteLobbyAsync(_lobbyId);
        }
    }

    private async Task SendPlayerDataToLobby()
    {
        var options = new UpdatePlayerOptions
        {
            Data = GetPlayerData(),
        };

        Debug.Log($"Sending AddressBookEntry = {NetworkTransport.AddressBookEntry?.Serialize()}");

        await LobbyService.Instance.UpdatePlayerAsync(_lobbyId, PlayerId, options);
    }

    void UpdateStatusText()
    {
        if (!string.IsNullOrEmpty(PlayerId))
        {
            statusText.text = $"Signed in as {PlayerId}";
        }
        else
        {
            statusText.text = "";
        }

        statusText.text += $"\n{_clientCount} / {NetworkTransport.Config.TotalNodes - 1} peer connections";
    }

    private async Task ApplyPlayerDataFromLobby()
    {
        var lobby = await LobbyService.Instance.GetLobbyAsync(_lobbyId);

        foreach (var player in lobby.Players)
        {
            if (player.Id == PlayerId || player.Data == null)
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
            if (entry == null)
            {
                continue;
            }

            NetworkTransport.AddAddressBookEntry(entry, player.Id == lobby.HostId);
        }
    }

    private async void Update()
    {
        if (!string.IsNullOrEmpty(_lobbyId) && !NetworkTransport.SessionHasStarted && Time.realtimeSinceStartup >= _nextLobbyRefresh)
        {
            _nextLobbyRefresh = Time.realtimeSinceStartup + 10;

            Debug.Log("Refreshing lobby data");

            if (_isLobbyHost)
            {
                await LobbyService.Instance.SendHeartbeatPingAsync(_lobbyId);
            }

            await SendPlayerDataToLobby();
            await ApplyPlayerDataFromLobby();
        }
    }
}