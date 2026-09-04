using UnityEngine;
using ViverseSDK;

/// <summary>
/// Demo script for MultiplayerClient. Attach to a GameObject in your scene.
/// Requires MatchmakingClient to get a room first.
/// </summary>
public class MultiplayerDemo : MonoBehaviour
{
    [Header("Config")]
    [SerializeField] private string appId = "YOUR_APP_ID";
    [SerializeField] private string roomId = "";
    [SerializeField] private string sessionId = "";

    private MultiplayerClient _client;
    private bool _connected;
    private string _log = "";

    private void Start()
    {
        if (string.IsNullOrEmpty(sessionId))
            sessionId = System.Guid.NewGuid().ToString();
    }

    private void OnGUI()
    {
        GUILayout.BeginArea(new Rect(10, 10, 400, Screen.height - 20));

        GUILayout.Label("=== Multiplayer Demo ===");
        GUILayout.Label($"Status: {(_connected ? "Connected" : "Disconnected")}");

        if (_client == null)
        {
            GUILayout.Label("Room ID (from Matchmaking):");
            roomId = GUILayout.TextField(roomId);

            if (!string.IsNullOrEmpty(roomId) && GUILayout.Button("Initialize"))
            {
                InitializeMultiplayer();
            }
        }
        else if (_connected)
        {
            if (GUILayout.Button("Init Modules (Game + NetworkSync)"))
            {
                InitModules();
            }

            GUILayout.Space(10);
            GUILayout.Label("--- General ---");
            if (GUILayout.Button("Send Message"))
            {
                _client.General.SendMessage("\"hello from " + sessionId.Substring(0, 8) + "\"");
                Log("Sent general message");
            }

            GUILayout.Space(10);
            GUILayout.Label("--- NetworkSync ---");
            if (GUILayout.Button("Send Position"))
            {
                string posData = $"{{\"x\":{transform.position.x},\"y\":{transform.position.y},\"z\":{transform.position.z}}}";
                _client.NetworkSync.UpdateMyPosition(posData);
                Log("Sent position update");
            }

            GUILayout.Space(10);
            GUILayout.Label("--- Game ---");
            if (GUILayout.Button("Ready"))
            {
                _client.Game.Ready();
                Log("Sent Ready");
            }
            if (GUILayout.Button("Start Game"))
            {
                _client.Game.TriggerGameStart();
                Log("Triggered game start");
            }

            GUILayout.Space(10);
            GUILayout.Label("--- Leaderboard ---");
            if (GUILayout.Button("Update Score (100)"))
            {
                _client.Leaderboard.LeaderboardUpdate(100);
                Log("Sent score 100");
            }

            GUILayout.Space(10);
            if (GUILayout.Button("Disconnect"))
            {
                _client.Disconnect();
                Log("Disconnected");
            }
        }

        GUILayout.Space(20);
        GUILayout.Label("--- Log ---");
        GUILayout.TextArea(_log, GUILayout.Height(200));

        GUILayout.EndArea();
    }

    private async void InitializeMultiplayer()
    {
        var go = new GameObject("MultiplayerClient");
        _client = go.AddComponent<MultiplayerClient>();

        _client.OnConnected += () => { _connected = true; Log("Connected!"); };
        _client.OnDisconnected += () => { _connected = false; Log("Disconnected"); };
        _client.OnClientConnected += (peer) => Log($"Peer joined: {peer}");
        _client.OnClientDisconnected += (peer) => Log($"Peer left: {peer}");
        _client.OnMessage += (msg) => Log($"Message: {msg}");

        try
        {
            await _client.Initialize(roomId, appId, sessionId);
            Log("Initialize called");

            // Subscribe to module events AFTER Initialize (modules created there)
            _client.Game.OnMasterNotify += (data) => Log($"Master: {data}");
            _client.Game.OnCountdownToStart += (data) => Log($"Countdown start: {data}");
            _client.Game.OnGameEnd += () => Log("Game ended");
            _client.NetworkSync.OnNotifyPositionUpdate += (data) => Log($"Position: {data}");
            _client.Leaderboard.OnLeaderboardUpdate += (data) => Log($"Leaderboard: {data}");
        }
        catch (System.Exception ex)
        {
            Log($"Init error: {ex.Message}");
        }
    }

    private async void InitModules()
    {
        var options = new MultiplayerInitOptions
        {
            modules = new ModulesConfig
            {
                game = new ModuleOption { enabled = true, play_time = 60, total_player = 4 },
                networkSync = new ModuleOption { enabled = true },
                actionSync = new ModuleOption { enabled = true },
                leaderboard = new ModuleOption { enabled = true },
            }
        };

        try
        {
            var result = await _client.Init(options);
            Log($"Modules initialized: {result}");
        }
        catch (System.Exception ex)
        {
            Log($"Init modules error: {ex.Message}");
        }
    }

    private void Log(string msg)
    {
        _log = $"[{System.DateTime.Now:HH:mm:ss}] {msg}\n{_log}";
        if (_log.Length > 2000) _log = _log.Substring(0, 2000);
        Debug.Log($"[MultiplayerDemo] {msg}");
    }
}
