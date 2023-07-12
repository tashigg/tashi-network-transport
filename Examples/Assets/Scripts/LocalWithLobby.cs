using System;
using Tashi.NetworkTransport;
using TMPro;
using Unity.Netcode;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Lobbies;
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

        statusText.text += $"\n{_clientCount} peer connections";
    }

    private float _nextHeartbeat;
    private float _nextLobbyRefresh;

    private async void Update()
    {
        if (string.IsNullOrEmpty(_lobbyId))
        {
            return;
        }

        if (Time.realtimeSinceStartup >= _nextHeartbeat && _isLobbyHost)
        {
            _nextHeartbeat = Time.realtimeSinceStartup + 15;
            await LobbyService.Instance.SendHeartbeatPingAsync(_lobbyId);
        }

        if (Time.realtimeSinceStartup >= _nextLobbyRefresh)
        {
            _nextLobbyRefresh = Time.realtimeSinceStartup + 2;

            var outgoingSessionDetails = NetworkTransport.OutgoingSessionDetails;

            var updatePlayerOptions = new UpdatePlayerOptions();
            if (outgoingSessionDetails.AddTo(updatePlayerOptions))
            {
                await LobbyService.Instance.UpdatePlayerAsync(_lobbyId, PlayerId, updatePlayerOptions);
            }

            if (_isLobbyHost)
            {
                var updateLobbyOptions = new UpdateLobbyOptions();
                if (outgoingSessionDetails.AddTo(updateLobbyOptions))
                {
                    await LobbyService.Instance.UpdateLobbyAsync(_lobbyId, updateLobbyOptions);
                }
            }

            if (!NetworkTransport.SessionHasStarted)
            {
                var lobby = await LobbyService.Instance.GetLobbyAsync(_lobbyId);
                var incomingSessionDetails = IncomingSessionDetails.FromUnityLobby(lobby);

                // This should be replaced with whatever logic you use to determine when a lobby is locked in.
                if (incomingSessionDetails.AddressBook.Count == 2)
                {
                    NetworkTransport.UpdateSessionDetails(incomingSessionDetails);
                }
            }
        }
    }
}