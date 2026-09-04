using UnityEngine;
using ViverseSDK;

public class MatchmakingDemo : MonoBehaviour
{
    [SerializeField] private string appId = "YOUR_APP_ID";
    [SerializeField] private string playerName = "Player1";
    [SerializeField] private bool debugMode = true;

    private MatchmakingClient _client;
    private string _sessionId;

    private void Start()
    {
        Connect();
    }

    private void Connect()
    {
        if (_client != null) return;

        var go = new GameObject("MatchmakingClient");
        _client = go.AddComponent<MatchmakingClient>();

        _client.OnConnect += () => Debug.Log("[Demo] Connected!");
        _client.OnDisconnect += () => Debug.Log("[Demo] Disconnected");
        _client.OnError += (err) => Debug.LogError($"[Demo] Error: {err}");
        _client.OnStateChange += (state) => Debug.Log($"[Demo] State: {state}");
        _client.OnRoomListUpdate += (json) => Debug.Log($"[Demo] Rooms: {json}");
        _client.OnJoinRoom += (json) => Debug.Log($"[Demo] Joined room: {json}");
        _client.OnJoinedLobby += () => Debug.Log("[Demo] Joined lobby");
        _client.OnRoomActorChange += (json) => Debug.Log($"[Demo] Actors: {json}");
        _client.OnRoomClosed += () => Debug.Log("[Demo] Room closed");
        _client.OnGameStartNotify += () => Debug.Log("[Demo] Game starting!");
        _client.OnMatchingTimeout += () => Debug.Log("[Demo] Match timeout");
      
        _client.Initialize(appId, debugMode);
    }

    private void Reconnect()
    {
        if (_client != null)
        {
            _client.Disconnect();
            DestroyImmediate(_client.gameObject);
            _client = null;
        }
        Connect();
    }

    private void OnGUI()
    {
        if (_client == null) return;

        GUILayout.BeginArea(new Rect(10, 10, 350, 700));

        GUILayout.Label($"Connected: {_client.IsConnected()}");
        GUILayout.Label($"In Lobby: {_client.IsInLobby()}");
        GUILayout.Label($"In Room: {_client.IsJoinedToRoom()}");
        GUILayout.Label($"Session: {_sessionId ?? "none"}");

        GUILayout.Space(10);

        bool connected = _client.IsConnected();
        bool actorSet = _sessionId != null;

        if (connected && !actorSet)
        {
            if (GUILayout.Button("Set Actor"))
            {
                _sessionId = System.Guid.NewGuid().ToString().Substring(0, 8);
                var payload = $"{{\"session_id\":\"{_sessionId}\",\"properties\":{{\"name\":\"{playerName}\"}}}}";
                _client.SendRequest("SetActor", payload, (res) =>
                {
                    Debug.Log($"[Demo] SetActor response: {res}");
                });
            }
        }

        if (connected && actorSet)
        {
            if (GUILayout.Button("Create Room"))
            {
                var payload = $"{{\"session_id\":\"{_sessionId}\",\"room_name\":\"TestRoom\",\"mode\":\"default\",\"max_players\":4,\"min_players\":2,\"master_client_id\":\"{_sessionId}\"}}";
                _client.SendRequest("CreateRoom", payload, (res) =>
                {
                    Debug.Log($"[Demo] CreateRoom response: {res}");
                });
            }

            if (GUILayout.Button("Get Available Rooms"))
            {
                _client.SendRequest("GetAvailableRooms", "{}", (res) =>
                {
                    Debug.Log($"[Demo] Rooms response: {res}");
                });
            }

            if (GUILayout.Button("Auto-Join Room"))
            {
                _client.SendRequest("GetAvailableRooms", "{}", (res) =>
                {
                    var start = res.IndexOf("\"id\":\"");
                    if (start < 0)
                    {
                        Debug.Log("[Demo] No rooms available to join");
                        return;
                    }
                    start += 6;
                    var end = res.IndexOf("\"", start);
                    var roomId = res.Substring(start, end - start);
                    Debug.Log($"[Demo] Joining room: {roomId}");
                    _client.SendRequest("JoinRoom", $"{{\"room_id\":\"{roomId}\",\"session_id\":\"{_sessionId}\"}}", (joinRes) =>
                    {
                        Debug.Log($"[Demo] JoinRoom response: {joinRes}");
                    });
                });
            }

            if (GUILayout.Button("Leave Room"))
            {
                _client.SendRequest("LeaveRoom", $"{{\"session_id\":\"{_sessionId}\"}}", (res) =>
                {
                    Debug.Log($"[Demo] LeaveRoom response: {res}");
                });
            }

            if (GUILayout.Button("Disconnect"))
            {
                _client.Disconnect();
            }
        }

        if (!connected)
        {
            if (GUILayout.Button("Reconnect"))
            {
                Reconnect();
            }
        }

        GUILayout.EndArea();
    }
}
