using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using ViverseSDK;

#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

/// <summary>
/// Unified VIVERSE SDK test runner. Progressively unlocks feature sections as dependencies are met.
/// Auth → Lambda → (future: Cloud Save, Leaderboard, etc.)
/// </summary>
public class ViverseTestRunner : MonoBehaviour
{
#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")]
    private static extern void ViversePlay_DownloadLog(string filename, string content);
#endif

    [Header("Configuration")]
    [SerializeField] private string appId = "YOUR_APP_ID";

    [Header("Lambda Test")]
    [SerializeField] private string lambdaEventName = "your_event_name";
    [SerializeField] private string lambdaPayload = "{\"message\": \"hello from unity\"}";

    [Header("Achievements Test")]
    [SerializeField] private string achievementApiName = "your_achievement_api_name";

    // Auth state
    private AuthManager _auth;
    private string _authStatus = "Not initialized";
    private string _token = "";
    private string _accountId = "";

    // Lambda state
    private LambdaClient _lambda;
    private string _lambdaStatus = "Idle";
    private string _lambdaResult = "";
    private bool _lambdaRunning;
    private CancellationTokenSource _lambdaCts;

    // Cloud Save state
    private CloudSaveClient _cloudSave;
    private string _cloudSaveStatus = "Idle";
    private string _cloudSaveResult = "";
    private bool _cloudSaveRunning;
    private string _saveData = "{\"level\": 1, \"score\": 100}";
    private string _playerDataKey = "coins";
    private string _playerDataValue = "500";

    // Leaderboard state
    private LeaderboardClient _leaderboard;
    private string _leaderboardStatus = "Idle";
    private string _leaderboardResult = "";
    private bool _leaderboardRunning;
    private string _leaderboardMetaName = "your_leaderboard_meta_name";
    private string _leaderboardScore = "100";

    // Achievements state
    private AchievementsClient _achievements;
    private string _achievementsStatus = "Idle";
    private string _achievementsResult = "";
    private bool _achievementsRunning;

    // Avatar state
    private AvatarClient _avatar;
    private string _avatarStatus = "Idle";
    private string _avatarResult = "";
    private bool _avatarRunning;
    private string _publicAvatarId = "";

    // Matchmaking state
    private MatchmakingClient _matchmaking;
    private string _matchmakingStatus = "Not connected";
    private string _matchmakingResult = "";
    private string _matchmakingRoomName = "TestRoom";
    private int _matchmakingMaxPlayers = 4;
    private string _matchmakingJoinRoomId = "";
    private string _matchmakingActorName = "Player1";

    // Multiplayer state
    private MultiplayerClient _multiplayer;
    private string _multiplayerStatus = "Not connected";
    private string _multiplayerResult = "";
    private bool _multiplayerConnected;
    private bool _multiplayerModulesReady;
    private string _multiplayerRoomId = "";
    private string _generalMessage = "hello from unity";
    private string _networkSyncData = "{\"x\":1.5,\"y\":2.0,\"z\":-3.0}";
    private string _entityId = "npc_01";
    private string _actionName = "attack";
    private string _actionMsg = "sword_slash";
    private string _actionId = "";
    private int _moduleLeaderboardScore = 50;
    private int _moduleEventCount;

    // VRM controller (separate component, handles UniVRM10)
#if VIVERSE_VRM_INSTALLED
    private VrmAvatarController _vrmController;
#endif

    // Log buffer
    private List<string> _logBuffer = new List<string>();

    // UI
    private Vector2 _scrollPos;

    void Start()
    {
        Application.logMessageReceived += OnLogMessage;

        _auth = FindObjectOfType<AuthManager>();
        if (_auth == null)
        {
            var go = new GameObject("[AuthManager]");
            _auth = go.AddComponent<AuthManager>();
        }

        _auth.OnStatusChanged += msg => _authStatus = msg;
        _auth.OnLoginSuccess += result =>
        {
            _token = result.access_token ?? "";
            _accountId = result.account_id ?? "";
            _authStatus = "Logged in";
        };
        _auth.OnLogout += () =>
        {
            _token = "";
            _accountId = "";
            _authStatus = "Logged out";
        };
        _auth.OnError += err => _authStatus = $"Error: {err}";

        _auth.Initialize(appId);
        _lambda = new LambdaClient(appId);
        _cloudSave = new CloudSaveClient(appId);
        _leaderboard = new LeaderboardClient(appId);
        _achievements = new AchievementsClient(appId);
        _avatar = new AvatarClient();

        // Matchmaking client is a MonoBehaviour singleton
        _matchmaking = FindObjectOfType<MatchmakingClient>();
        if (_matchmaking == null)
        {
            var go = new GameObject("[MatchmakingClient]");
            _matchmaking = go.AddComponent<MatchmakingClient>();
        }
        _matchmaking.OnConnect += () =>
        {
            _matchmakingStatus = "Connected (lobby)";
            Debug.Log("[Matchmaking] Connected");
        };
        _matchmaking.OnDisconnect += () =>
        {
            _matchmakingStatus = "Disconnected";
            Debug.Log("[Matchmaking] Disconnected");
        };
        _matchmaking.OnError += err =>
        {
            _matchmakingStatus = $"Error: {err}";
            Debug.LogError($"[Matchmaking] Error: {err}");
        };
        _matchmaking.OnStateChange += state =>
        {
            _matchmakingStatus = $"State: {state}";
            Debug.Log($"[Matchmaking] State: {state}");
        };
        _matchmaking.OnJoinRoom += data =>
        {
            _matchmakingStatus = "Joined room";
            _matchmakingResult = data ?? "";
            Debug.Log($"[Matchmaking] Joined room: {data}");
        };
        _matchmaking.OnJoinedLobby += () =>
        {
            _matchmakingStatus = "In lobby";
            Debug.Log("[Matchmaking] Joined lobby");
        };
        _matchmaking.OnRoomListUpdate += data =>
        {
            _matchmakingResult = data ?? "";
            Debug.Log($"[Matchmaking] Room list update: {data}");
        };
        _matchmaking.OnRoomActorChange += data =>
        {
            Debug.Log($"[Matchmaking] Actor change: {data}");
        };
        _matchmaking.OnRoomClosed += () =>
        {
            _matchmakingStatus = "Room closed";
            Debug.Log("[Matchmaking] Room closed");
        };

        // VRM controller lives on this same GameObject
#if VIVERSE_VRM_INSTALLED
        _vrmController = GetComponent<VrmAvatarController>();
        if (_vrmController == null)
            _vrmController = gameObject.AddComponent<VrmAvatarController>();
#else
        Debug.LogWarning(
            "[ViverseSDK] UniVRM package not installed — VRM avatar rendering is disabled. " +
            "To enable, add the following to Packages/manifest.json:\n" +
            "  \"com.vrmc.gltf\": \"https://github.com/vrm-c/UniVRM.git?path=/Assets/UniGLTF#v0.130.1\",\n" +
            "  \"com.vrmc.vrm\": \"https://github.com/vrm-c/UniVRM.git?path=/Assets/VRM10#v0.130.1\"");
#endif
    }

    void OnDestroy()
    {
        Application.logMessageReceived -= OnLogMessage;
        _lambdaCts?.Cancel();
        _lambdaCts?.Dispose();

        if (_multiplayer != null)
            DisconnectMultiplayer();

        if (_matchmaking != null && _matchmaking.IsConnected())
            _matchmaking.Disconnect();
    }

    private void OnLogMessage(string message, string stackTrace, LogType type)
    {
        string prefix = type == LogType.Error || type == LogType.Exception ? "[ERR] " : "";
        _logBuffer.Add($"[{DateTime.Now:HH:mm:ss}] {prefix}{message}");
        if (_logBuffer.Count > 500) _logBuffer.RemoveAt(0);
    }

    private void DownloadLog()
    {
        string content = string.Join("\n", _logBuffer);
        string filename = $"viverse_log_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
#if UNITY_WEBGL && !UNITY_EDITOR
        ViversePlay_DownloadLog(filename, content);
#else
        string path = System.IO.Path.Combine(Application.persistentDataPath, filename);
        System.IO.File.WriteAllText(path, content);
        Debug.Log($"[ViverseTestRunner] Log saved: {path}");
#endif
    }

    void OnGUI()
    {
        float dpi = Screen.dpi > 0 ? Screen.dpi / 96f : 1f;
        float scale = Mathf.Max(1f, dpi);
        int fontSize = Mathf.RoundToInt(14 * scale);
        int btnHeight = Mathf.RoundToInt(40 * scale);
        int padding = Mathf.RoundToInt(16 * scale);
        int panelWidth = Mathf.RoundToInt(560 * scale);

        var boxStyle = new GUIStyle(GUI.skin.box) { fontSize = fontSize };
        var btnStyle = new GUIStyle(GUI.skin.button) { fontSize = fontSize };
        var labelStyle = new GUIStyle(GUI.skin.label) { fontSize = fontSize, wordWrap = true };
        var fieldStyle = new GUIStyle(GUI.skin.textField) { fontSize = fontSize };

        GUILayout.BeginArea(new Rect(padding, padding, panelWidth, Screen.height - padding * 2));
        _scrollPos = GUILayout.BeginScrollView(_scrollPos);

        // === AUTH SECTION ===
        DrawAuthSection(boxStyle, btnStyle, labelStyle, btnHeight);

        GUILayout.Space(16);

        // === MATCHMAKING SECTION (only when logged in) ===
        if (_auth != null && _auth.IsLoggedIn)
        {
            DrawMatchmakingSection(boxStyle, btnStyle, labelStyle, fieldStyle, btnHeight);
        }

        GUILayout.Space(16);

        // === MULTIPLAYER / DATA CHANNEL MODULES (when logged in) ===
        if (_auth != null && _auth.IsLoggedIn)
        {
            DrawMultiplayerSection(boxStyle, btnStyle, labelStyle, fieldStyle, btnHeight);
        }

        GUILayout.Space(16);

        // === LAMBDA SECTION (only when logged in) ===
        if (_auth != null && _auth.IsLoggedIn)
        {
            DrawLambdaSection(boxStyle, btnStyle, labelStyle, fieldStyle, btnHeight);
        }

        GUILayout.Space(16);

        // === CLOUD SAVE SECTION (only when logged in) ===
        if (_auth != null && _auth.IsLoggedIn)
        {
            DrawCloudSaveSection(boxStyle, btnStyle, labelStyle, fieldStyle, btnHeight);
        }

        GUILayout.Space(16);

        // === LEADERBOARD SECTION (only when logged in) ===
        if (_auth != null && _auth.IsLoggedIn)
        {
            DrawLeaderboardSection(boxStyle, btnStyle, labelStyle, fieldStyle, btnHeight);
        }

        GUILayout.Space(16);

        // === ACHIEVEMENTS SECTION (only when logged in) ===
        if (_auth != null && _auth.IsLoggedIn)
        {
            DrawAchievementsSection(boxStyle, btnStyle, labelStyle, fieldStyle, btnHeight);
        }

        GUILayout.Space(16);

        // === AVATAR SECTION ===
        DrawAvatarSection(boxStyle, btnStyle, labelStyle, fieldStyle, btnHeight);

        GUILayout.Space(16);

        // === DOWNLOAD LOG ===
        if (GUILayout.Button("Download Log", btnStyle, GUILayout.Height(btnHeight)))
            DownloadLog();

        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    private void DrawAuthSection(GUIStyle boxStyle, GUIStyle btnStyle, GUIStyle labelStyle, int btnHeight)
    {
        GUILayout.Box("Auth", boxStyle, GUILayout.ExpandWidth(true));
        GUILayout.Space(4);

        if (_auth != null && _auth.IsLoggedIn)
        {
            GUILayout.Label($"Account: {_accountId}", labelStyle);
            string tokenDisplay = _token.Length > 50 ? _token.Substring(0, 50) + "..." : _token;
            GUILayout.Label($"Token: {tokenDisplay}", labelStyle);
            GUILayout.Space(4);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Copy Token", btnStyle, GUILayout.Height(btnHeight * 0.75f)))
                GUIUtility.systemCopyBuffer = _token;
            if (GUILayout.Button("Logout", btnStyle, GUILayout.Height(btnHeight * 0.75f)))
                _auth.Logout();
            GUILayout.EndHorizontal();
        }
        else
        {
            GUILayout.Label($"Status: {_authStatus}", labelStyle);
            GUILayout.Space(4);
            if (GUILayout.Button("Login", btnStyle, GUILayout.Height(btnHeight)))
                _auth.Login();
        }
    }

    // === MATCHMAKING ===

    private void DrawMatchmakingSection(GUIStyle boxStyle, GUIStyle btnStyle, GUIStyle labelStyle, GUIStyle fieldStyle, int btnHeight)
    {
        GUILayout.Box("Matchmaking", boxStyle, GUILayout.ExpandWidth(true));
        GUILayout.Space(4);

        GUILayout.Label($"Status: {_matchmakingStatus}", labelStyle);
        GUILayout.Space(4);

        bool connected = _matchmaking != null && _matchmaking.IsConnected();
        bool inRoom = _matchmaking != null && _matchmaking.IsJoinedToRoom();

        // Connect / Disconnect
        if (!connected)
        {
            if (GUILayout.Button("Connect", btnStyle, GUILayout.Height(btnHeight)))
            {
                _matchmakingStatus = "Connecting...";
                _matchmaking.Initialize(appId, true);
            }
        }
        else
        {
            if (GUILayout.Button("Disconnect", btnStyle, GUILayout.Height(btnHeight)))
            {
                _matchmaking.Disconnect();
                _matchmakingStatus = "Disconnected";
            }
        }

        GUILayout.Space(8);

        // Set Actor
        GUI.enabled = connected && !inRoom;
        GUILayout.Label("Actor Name:", labelStyle);
        _matchmakingActorName = GUILayout.TextField(_matchmakingActorName, fieldStyle);
        if (GUILayout.Button("Set Actor", btnStyle, GUILayout.Height(btnHeight)))
        {
            string payload = $"{{\"properties\":{{\"name\":\"{_matchmakingActorName}\"}}}}";
            _matchmaking.SendRequest("SetActor", payload, resp =>
            {
                _matchmakingResult = resp ?? "";
                Debug.Log($"[Matchmaking] SetActor response: {resp}");
            });
        }
        GUI.enabled = true;

        GUILayout.Space(8);

        // Create Room
        GUI.enabled = connected && !inRoom;
        GUILayout.Label("Room Name:", labelStyle);
        _matchmakingRoomName = GUILayout.TextField(_matchmakingRoomName, fieldStyle);
        GUILayout.Label($"Max Players: {_matchmakingMaxPlayers}", labelStyle);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("-", btnStyle, GUILayout.Width(40), GUILayout.Height(btnHeight * 0.75f)))
            _matchmakingMaxPlayers = Mathf.Max(2, _matchmakingMaxPlayers - 1);
        if (GUILayout.Button("+", btnStyle, GUILayout.Width(40), GUILayout.Height(btnHeight * 0.75f)))
            _matchmakingMaxPlayers = Mathf.Min(16, _matchmakingMaxPlayers + 1);
        GUILayout.EndHorizontal();

        if (GUILayout.Button("Create Room", btnStyle, GUILayout.Height(btnHeight)))
        {
            string payload = $"{{\"room_name\":\"{_matchmakingRoomName}\",\"max_players\":{_matchmakingMaxPlayers},\"min_players\":2}}";
            _matchmaking.SendRequest("CreateRoom", payload, resp =>
            {
                _matchmakingResult = resp ?? "";
                Debug.Log($"[Matchmaking] CreateRoom response: {resp}");
            });
        }
        GUI.enabled = true;

        GUILayout.Space(4);

        // Get Available Rooms
        GUI.enabled = connected;
        if (GUILayout.Button("Get Available Rooms", btnStyle, GUILayout.Height(btnHeight)))
        {
            _matchmaking.SendRequest("GetAvailableRooms", "{}", resp =>
            {
                _matchmakingResult = resp ?? "";
                Debug.Log($"[Matchmaking] GetAvailableRooms response: {resp}");
            });
        }

        GUI.enabled = connected && !inRoom;
        if (GUILayout.Button("Join Any Room", btnStyle, GUILayout.Height(btnHeight)))
        {
            _matchmakingStatus = "Finding room...";
            _matchmaking.SendRequest("GetAvailableRooms", "{}", resp =>
            {
                if (string.IsNullOrEmpty(resp))
                {
                    _matchmakingStatus = "No rooms found";
                    _matchmakingResult = "GetAvailableRooms returned empty";
                    return;
                }
                // Response is envelope: {"request_id":..., "data":[...rooms...]}
                var envelope = MiniJson.Deserialize(resp) as Dictionary<string, object>;
                System.Collections.IList rooms = null;
                if (envelope != null && envelope.ContainsKey("data"))
                    rooms = envelope["data"] as System.Collections.IList;
                // Fallback: maybe response is a raw array
                if (rooms == null)
                    rooms = MiniJson.Deserialize(resp) as System.Collections.IList;
                if (rooms == null || rooms.Count == 0)
                {
                    _matchmakingStatus = "No rooms available";
                    _matchmakingResult = resp;
                    return;
                }
                var firstRoom = rooms[0] as Dictionary<string, object>;
                if (firstRoom == null || !firstRoom.ContainsKey("id"))
                {
                    _matchmakingStatus = "No valid room found";
                    _matchmakingResult = resp;
                    return;
                }
                string roomId = firstRoom["id"].ToString();
                _matchmakingStatus = $"Joining room {roomId}...";
                string joinPayload = $"{{\"room_id\":\"{roomId}\"}}";
                _matchmaking.SendRequest("JoinRoom", joinPayload, joinResp =>
                {
                    _matchmakingResult = joinResp ?? "";
                    Debug.Log($"[Matchmaking] JoinAnyRoom → JoinRoom response: {joinResp}");
                });
            });
        }
        GUI.enabled = true;

        GUILayout.Space(4);

        // Join Room
        GUI.enabled = connected && !inRoom;
        GUILayout.Label("Room ID:", labelStyle);
        _matchmakingJoinRoomId = GUILayout.TextField(_matchmakingJoinRoomId, fieldStyle);
        if (GUILayout.Button("Join Room", btnStyle, GUILayout.Height(btnHeight)))
        {
            string payload = $"{{\"room_id\":\"{_matchmakingJoinRoomId}\"}}";
            _matchmaking.SendRequest("JoinRoom", payload, resp =>
            {
                _matchmakingResult = resp ?? "";
                Debug.Log($"[Matchmaking] JoinRoom response: {resp}");
            });
        }
        GUI.enabled = true;

        GUILayout.Space(4);

        // Leave Room
        GUI.enabled = connected && inRoom;
        if (GUILayout.Button("Leave Room", btnStyle, GUILayout.Height(btnHeight)))
        {
            _matchmaking.SendRequest("LeaveRoom", "{}", resp =>
            {
                _matchmakingResult = resp ?? "";
                Debug.Log($"[Matchmaking] LeaveRoom response: {resp}");
            });
        }
        GUI.enabled = true;

        GUILayout.Space(8);

        // Current state display
        if (connected)
        {
            string actor = _matchmaking.GetCurrentActor();
            string room = _matchmaking.GetCurrentRoom();
            if (!string.IsNullOrEmpty(actor))
                GUILayout.Label($"Actor: {actor}", labelStyle);
            if (!string.IsNullOrEmpty(room))
                GUILayout.Label($"Room: {room}", labelStyle);
        }

        // Result display
        if (!string.IsNullOrEmpty(_matchmakingResult))
        {
            GUILayout.Space(4);
            GUILayout.Label($"Result: {_matchmakingResult}", labelStyle);
        }
    }

    // === MULTIPLAYER (Data Channel Modules) ===

    private void DrawMultiplayerSection(GUIStyle boxStyle, GUIStyle btnStyle, GUIStyle labelStyle, GUIStyle fieldStyle, int btnHeight)
    {
        GUILayout.Box("Multiplayer (Data Channel Modules)", boxStyle, GUILayout.ExpandWidth(true));
        GUILayout.Space(4);

        GUILayout.Label($"Status: {_multiplayerStatus}", labelStyle);
        if (_moduleEventCount > 0)
            GUILayout.Label($"Events received: {_moduleEventCount}", labelStyle);
        GUILayout.Space(4);

        // Phase 1: Connect
        if (_multiplayer == null)
        {
            // Auto-apply room ID from matchmaking
            if (_matchmaking != null && _matchmaking.IsJoinedToRoom())
            {
                string roomJson = _matchmaking.GetCurrentRoom();
                if (!string.IsNullOrEmpty(roomJson))
                {
                    var roomDict = MiniJson.Deserialize(roomJson) as Dictionary<string, object>;
                    if (roomDict != null)
                    {
                        if (roomDict.ContainsKey("id") && !string.IsNullOrEmpty(roomDict["id"]?.ToString()))
                            _multiplayerRoomId = roomDict["id"].ToString();
                        else if (roomDict.ContainsKey("room_id") && !string.IsNullOrEmpty(roomDict["room_id"]?.ToString()))
                            _multiplayerRoomId = roomDict["room_id"].ToString();
                        else if (roomDict.ContainsKey("game_session") && !string.IsNullOrEmpty(roomDict["game_session"]?.ToString()))
                            _multiplayerRoomId = roomDict["game_session"].ToString();
                    }
                }

                GUILayout.Label($"Room: {_multiplayerRoomId}", labelStyle);
                if (GUILayout.Button("Initialize Multiplayer", btnStyle, GUILayout.Height(btnHeight)))
                    InitMultiplayer();
            }
            else
            {
                GUILayout.Label("Join a room in Matchmaking first.", labelStyle);
            }
        }
        // Phase 2: Init modules
        else if (!_multiplayerModulesReady)
        {
            if (GUILayout.Button("Init Modules (All)", btnStyle, GUILayout.Height(btnHeight)))
                InitMultiplayerModules();

            GUILayout.Space(4);
            if (GUILayout.Button("Disconnect", btnStyle, GUILayout.Height(btnHeight)))
                DisconnectMultiplayer();
        }
        // Phase 3: Test modules
        else
        {
            // --- General ---
            GUILayout.Space(8);
            GUILayout.Label("=== General Module ===", labelStyle);
            GUILayout.Label("Message:", labelStyle);
            _generalMessage = GUILayout.TextField(_generalMessage, fieldStyle);
            if (GUILayout.Button("Send Message", btnStyle, GUILayout.Height(btnHeight)))
            {
                string data = $"\"{_generalMessage}\"";
                _multiplayer.General.SendMessage(data);
                _multiplayerResult = $"[General] Sent: {_generalMessage}";
                Debug.Log($"[Multiplayer] General.SendMessage: {data}");
            }

            // --- NetworkSync ---
            GUILayout.Space(8);
            GUILayout.Label("=== NetworkSync Module ===", labelStyle);
            GUILayout.Label("Position JSON:", labelStyle);
            _networkSyncData = GUILayout.TextField(_networkSyncData, fieldStyle);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Update My Position", btnStyle, GUILayout.Height(btnHeight)))
            {
                _multiplayer.NetworkSync.UpdateMyPosition(_networkSyncData);
                _multiplayerResult = $"[NetworkSync] Sent position: {_networkSyncData}";
                Debug.Log($"[Multiplayer] NetworkSync.UpdateMyPosition: {_networkSyncData}");
            }
            GUILayout.EndHorizontal();

            GUILayout.Label("Entity ID:", labelStyle);
            _entityId = GUILayout.TextField(_entityId, fieldStyle);
            if (GUILayout.Button("Update Entity Position", btnStyle, GUILayout.Height(btnHeight)))
            {
                _multiplayer.NetworkSync.UpdateEntityPosition(_entityId, _networkSyncData);
                _multiplayerResult = $"[NetworkSync] Entity '{_entityId}' position sent";
                Debug.Log($"[Multiplayer] NetworkSync.UpdateEntityPosition({_entityId}): {_networkSyncData}");
            }

            // --- ActionSync ---
            GUILayout.Space(8);
            GUILayout.Label("=== ActionSync Module ===", labelStyle);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Action:", labelStyle, GUILayout.Width(55));
            _actionName = GUILayout.TextField(_actionName, fieldStyle);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("Msg:", labelStyle, GUILayout.Width(55));
            _actionMsg = GUILayout.TextField(_actionMsg, fieldStyle);
            GUILayout.EndHorizontal();
            if (GUILayout.Button("Send Competition", btnStyle, GUILayout.Height(btnHeight)))
            {
                if (string.IsNullOrEmpty(_actionId))
                    _actionId = Guid.NewGuid().ToString().Substring(0, 8);
                _multiplayer.ActionSync.Competition(_actionName, _actionMsg, _actionId);
                _multiplayerResult = $"[ActionSync] Sent: {_actionName}/{_actionMsg} id={_actionId}";
                Debug.Log($"[Multiplayer] ActionSync.Competition: {_actionName}, {_actionMsg}, {_actionId}");
                _actionId = ""; // reset for next
            }

            // --- Leaderboard Module ---
            GUILayout.Space(8);
            GUILayout.Label("=== Leaderboard Module (in-room) ===", labelStyle);
            GUILayout.Label($"Score: {_moduleLeaderboardScore}", labelStyle);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("-10", btnStyle, GUILayout.Width(60), GUILayout.Height(btnHeight * 0.75f)))
                _moduleLeaderboardScore = Mathf.Max(0, _moduleLeaderboardScore - 10);
            if (GUILayout.Button("+10", btnStyle, GUILayout.Width(60), GUILayout.Height(btnHeight * 0.75f)))
                _moduleLeaderboardScore += 10;
            if (GUILayout.Button("+100", btnStyle, GUILayout.Width(60), GUILayout.Height(btnHeight * 0.75f)))
                _moduleLeaderboardScore += 100;
            GUILayout.EndHorizontal();
            if (GUILayout.Button("Update Score", btnStyle, GUILayout.Height(btnHeight)))
            {
                _multiplayer.Leaderboard.LeaderboardUpdate(_moduleLeaderboardScore);
                _multiplayerResult = $"[Leaderboard] Sent score: {_moduleLeaderboardScore}";
                Debug.Log($"[Multiplayer] Leaderboard.LeaderboardUpdate: {_moduleLeaderboardScore}");
            }

            // --- Game Module ---
            GUILayout.Space(8);
            GUILayout.Label("=== Game Module ===", labelStyle);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Ready", btnStyle, GUILayout.Height(btnHeight)))
            {
                _multiplayer.Game.Ready();
                _multiplayerResult = "[Game] Sent Ready";
                Debug.Log("[Multiplayer] Game.Ready");
            }
            if (GUILayout.Button("Start", btnStyle, GUILayout.Height(btnHeight)))
            {
                _multiplayer.Game.TriggerGameStart();
                _multiplayerResult = "[Game] Triggered Start";
                Debug.Log("[Multiplayer] Game.TriggerGameStart");
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("End", btnStyle, GUILayout.Height(btnHeight)))
            {
                _multiplayer.Game.TriggerGameEnd();
                _multiplayerResult = "[Game] Triggered End";
                Debug.Log("[Multiplayer] Game.TriggerGameEnd");
            }
            if (GUILayout.Button("Restart", btnStyle, GUILayout.Height(btnHeight)))
            {
                _multiplayer.Game.TriggerGameRestart();
                _multiplayerResult = "[Game] Triggered Restart";
                Debug.Log("[Multiplayer] Game.TriggerGameRestart");
            }
            GUILayout.EndHorizontal();

            if (GUILayout.Button("Get Room Info", btnStyle, GUILayout.Height(btnHeight)))
                GetMultiplayerRoomInfo();

            // --- Disconnect ---
            GUILayout.Space(12);
            if (GUILayout.Button("Disconnect Multiplayer", btnStyle, GUILayout.Height(btnHeight)))
                DisconnectMultiplayer();
        }

        // Result display
        if (!string.IsNullOrEmpty(_multiplayerResult))
        {
            GUILayout.Space(4);
            GUILayout.Label($"Result: {_multiplayerResult}", labelStyle);
        }
    }

    private async void InitMultiplayer()
    {
        _multiplayerStatus = "Connecting...";
        _multiplayerResult = "";

        try
        {
            var go = new GameObject("[MultiplayerClient]");
            _multiplayer = go.AddComponent<MultiplayerClient>();

            _multiplayer.OnConnected += () =>
            {
                _multiplayerConnected = true;
                _multiplayerStatus = "Connected";
                Debug.Log("[Multiplayer] Connected");
            };
            _multiplayer.OnDisconnected += () =>
            {
                _multiplayerConnected = false;
                _multiplayerModulesReady = false;
                _multiplayerStatus = "Disconnected";
                Debug.Log("[Multiplayer] Disconnected");
            };
            _multiplayer.OnClientConnected += (peer) =>
            {
                _moduleEventCount++;
                _multiplayerResult = $"[Event] Peer joined: {peer}";
                Debug.Log($"[Multiplayer] Peer connected: {peer}");
            };
            _multiplayer.OnClientDisconnected += (peer) =>
            {
                _moduleEventCount++;
                _multiplayerResult = $"[Event] Peer left: {peer}";
                Debug.Log($"[Multiplayer] Peer disconnected: {peer}");
            };
            _multiplayer.OnMessage += (msg) =>
            {
                _moduleEventCount++;
                Debug.Log($"[Multiplayer] Raw message: {msg}");
            };

            string sessionId = Guid.NewGuid().ToString();
            await _multiplayer.Initialize(_multiplayerRoomId, appId, sessionId);
            _multiplayerStatus = "Initialized (call Init Modules next)";

            // Subscribe to module events
            SubscribeModuleEvents();
        }
        catch (Exception ex)
        {
            _multiplayerStatus = $"Error: {ex.Message}";
            _multiplayerResult = ex.Message;
            Debug.LogError($"[Multiplayer] Init error: {ex.Message}");
        }
    }

    private void SubscribeModuleEvents()
    {
        if (_multiplayer == null) return;

        // General
        _multiplayer.General.OnMessage += (data) =>
        {
            _moduleEventCount++;
            _multiplayerResult = $"[General] Received: {Truncate(data, 100)}";
            Debug.Log($"[Multiplayer] General.OnMessage: {data}");
        };
        _multiplayer.General.OnClientConnected += (userId) =>
        {
            _moduleEventCount++;
            Debug.Log($"[Multiplayer] General.OnClientConnected: {userId}");
        };
        _multiplayer.General.OnClientDisconnected += (userId) =>
        {
            _moduleEventCount++;
            Debug.Log($"[Multiplayer] General.OnClientDisconnected: {userId}");
        };

        // NetworkSync
        _multiplayer.NetworkSync.OnNotifyPositionUpdate += (data) =>
        {
            _moduleEventCount++;
            _multiplayerResult = $"[NetworkSync] Position: {Truncate(data, 100)}";
            Debug.Log($"[Multiplayer] NetworkSync.OnNotifyPositionUpdate: {data}");
        };
        _multiplayer.NetworkSync.OnNotifyRemove += (data) =>
        {
            _moduleEventCount++;
            _multiplayerResult = $"[NetworkSync] Remove: {Truncate(data, 100)}";
            Debug.Log($"[Multiplayer] NetworkSync.OnNotifyRemove: {data}");
        };

        // ActionSync
        _multiplayer.ActionSync.OnCompetition += (data) =>
        {
            _moduleEventCount++;
            _multiplayerResult = $"[ActionSync] Competition: {Truncate(data, 100)}";
            Debug.Log($"[Multiplayer] ActionSync.OnCompetition: {data}");
        };

        // Leaderboard
        _multiplayer.Leaderboard.OnLeaderboardUpdate += (data) =>
        {
            _moduleEventCount++;
            _multiplayerResult = $"[Leaderboard] Update: {Truncate(data, 100)}";
            Debug.Log($"[Multiplayer] Leaderboard.OnLeaderboardUpdate: {data}");
        };

        // Game
        _multiplayer.Game.OnMasterNotify += (data) =>
        {
            _moduleEventCount++;
            _multiplayerResult = $"[Game] Master: {Truncate(data, 100)}";
            Debug.Log($"[Multiplayer] Game.OnMasterNotify: {data}");
        };
        _multiplayer.Game.OnWaitForPlayer += (data) =>
        {
            _moduleEventCount++;
            _multiplayerResult = $"[Game] WaitForPlayer: {Truncate(data, 100)}";
            Debug.Log($"[Multiplayer] Game.OnWaitForPlayer: {data}");
        };
        _multiplayer.Game.OnPlayerAllReady += (data) =>
        {
            _moduleEventCount++;
            _multiplayerResult = $"[Game] AllReady: {Truncate(data, 100)}";
            Debug.Log($"[Multiplayer] Game.OnPlayerAllReady: {data}");
        };
        _multiplayer.Game.OnCountdownToStart += (data) =>
        {
            _moduleEventCount++;
            _multiplayerResult = $"[Game] CountdownStart: {Truncate(data, 100)}";
            Debug.Log($"[Multiplayer] Game.OnCountdownToStart: {data}");
        };
        _multiplayer.Game.OnCountdownToEnd += (data) =>
        {
            _moduleEventCount++;
            _multiplayerResult = $"[Game] CountdownEnd: {Truncate(data, 100)}";
            Debug.Log($"[Multiplayer] Game.OnCountdownToEnd: {data}");
        };
        _multiplayer.Game.OnGameEnd += () =>
        {
            _moduleEventCount++;
            _multiplayerResult = "[Game] Game ended";
            Debug.Log("[Multiplayer] Game.OnGameEnd");
        };
        _multiplayer.Game.OnGameRestart += () =>
        {
            _moduleEventCount++;
            _multiplayerResult = "[Game] Game restarted";
            Debug.Log("[Multiplayer] Game.OnGameRestart");
        };
        _multiplayer.Game.OnGameTimeUp += () =>
        {
            _moduleEventCount++;
            _multiplayerResult = "[Game] Time up";
            Debug.Log("[Multiplayer] Game.OnGameTimeUp");
        };
        _multiplayer.Game.OnBotLeave += () =>
        {
            _moduleEventCount++;
            _multiplayerResult = "[Game] Bot left";
            Debug.Log("[Multiplayer] Game.OnBotLeave");
        };
        _multiplayer.Game.OnErrorNotify += (data) =>
        {
            _moduleEventCount++;
            _multiplayerResult = $"[Game] Error: {Truncate(data, 100)}";
            Debug.LogError($"[Multiplayer] Game.OnErrorNotify: {data}");
        };
    }

    private async void InitMultiplayerModules()
    {
        _multiplayerStatus = "Initializing modules...";
        _multiplayerResult = "";

        try
        {
            var options = new MultiplayerInitOptions
            {
                modules = new ModulesConfig
                {
                    game = new ModuleOption { enabled = true, play_time = 60, total_player = 4 },
                    networkSync = new ModuleOption { enabled = true },
                    actionSync = new ModuleOption { enabled = true },
                    leaderboard = new ModuleOption { enabled = true },
                    lambda = new ModuleOption { enabled = true },
                }
            };

            var result = await _multiplayer.Init(options);
            _multiplayerModulesReady = true;
            _multiplayerStatus = "Modules ready — test below";
            _multiplayerResult = $"Init result: {Truncate(result, 120)}";
            Debug.Log($"[Multiplayer] Modules initialized: {result}");
        }
        catch (Exception ex)
        {
            _multiplayerStatus = $"Module init error: {ex.Message}";
            _multiplayerResult = ex.Message;
            Debug.LogError($"[Multiplayer] Module init error: {ex.Message}");
        }
    }

    private async void GetMultiplayerRoomInfo()
    {
        try
        {
            _multiplayerResult = "[Game] Getting room info...";
            var info = await _multiplayer.GetRoomInfo();
            _multiplayerResult = $"[Game] Room info: {Truncate(info, 150)}";
            Debug.Log($"[Multiplayer] GetRoomInfo: {info}");
        }
        catch (Exception ex)
        {
            _multiplayerResult = $"[Game] GetRoomInfo error: {ex.Message}";
            Debug.LogError($"[Multiplayer] GetRoomInfo error: {ex.Message}");
        }
    }

    private void DisconnectMultiplayer()
    {
        if (_multiplayer != null)
        {
            _multiplayer.Disconnect();
            Destroy(_multiplayer.gameObject);
            _multiplayer = null;
        }
        _multiplayerConnected = false;
        _multiplayerModulesReady = false;
        _multiplayerStatus = "Disconnected";
        _multiplayerResult = "";
        _moduleEventCount = 0;
    }

    private static string Truncate(string s, int maxLen)
    {
        if (string.IsNullOrEmpty(s)) return "(null)";
        return s.Length <= maxLen ? s : s.Substring(0, maxLen) + "...";
    }

    private void DrawLambdaSection(GUIStyle boxStyle, GUIStyle btnStyle, GUIStyle labelStyle, GUIStyle fieldStyle, int btnHeight)
    {
        GUILayout.Box("Lambda", boxStyle, GUILayout.ExpandWidth(true));
        GUILayout.Space(4);

        GUILayout.Label("Event Name:", labelStyle);
        lambdaEventName = GUILayout.TextField(lambdaEventName, fieldStyle);
        GUILayout.Space(4);

        GUILayout.Label("Payload (JSON):", labelStyle);
        lambdaPayload = GUILayout.TextField(lambdaPayload, fieldStyle);
        GUILayout.Space(8);

        if (_lambdaRunning)
        {
            GUILayout.Label($"Status: {_lambdaStatus}", labelStyle);
            if (GUILayout.Button("Cancel", btnStyle, GUILayout.Height(btnHeight)))
            {
                _lambdaCts?.Cancel();
                _lambdaStatus = "Cancelled";
                _lambdaRunning = false;
            }
        }
        else
        {
            if (GUILayout.Button("Invoke Lambda", btnStyle, GUILayout.Height(btnHeight)))
                InvokeLambda();
        }

        if (!string.IsNullOrEmpty(_lambdaStatus) && _lambdaStatus != "Idle")
        {
            GUILayout.Space(4);
            GUILayout.Label($"Status: {_lambdaStatus}", labelStyle);
        }
        if (!string.IsNullOrEmpty(_lambdaResult))
        {
            GUILayout.Space(4);
            GUILayout.Label($"Result: {_lambdaResult}", labelStyle);
        }
    }

    private async void InvokeLambda()
    {
        _lambdaRunning = true;
        _lambdaStatus = "Creating job...";
        _lambdaResult = "";
        _lambdaCts?.Dispose();
        _lambdaCts = new CancellationTokenSource();

        try
        {
            var result = await _lambda.Invoke(lambdaEventName, lambdaPayload, _token, _lambdaCts.Token);
            _lambdaStatus = result.success ? "Succeeded" : $"Failed ({result.status})";
            _lambdaResult = result.success ? (result.result ?? "(empty)") : (result.error ?? "unknown error");
            Debug.Log($"[Lambda] Status: {_lambdaStatus}");
            Debug.Log($"[Lambda] Result: {_lambdaResult}");
        }
        catch (OperationCanceledException)
        {
            _lambdaStatus = "Cancelled";
            Debug.Log("[Lambda] Cancelled");
        }
        catch (Exception ex)
        {
            _lambdaStatus = "Error";
            _lambdaResult = ex.Message;
            Debug.LogError($"[Lambda] Error: {ex.Message}");
        }
        finally
        {
            _lambdaRunning = false;
        }
    }

    // === CLOUD SAVE ===

    private void DrawCloudSaveSection(GUIStyle boxStyle, GUIStyle btnStyle, GUIStyle labelStyle, GUIStyle fieldStyle, int btnHeight)
    {
        GUILayout.Box("Cloud Save", boxStyle, GUILayout.ExpandWidth(true));
        GUILayout.Space(4);

        // --- UserApp: Versioned Saves ---
        GUILayout.Label("=== Versioned Saves ===", labelStyle);
        GUILayout.Label("Save Data (JSON):", labelStyle);
        _saveData = GUILayout.TextField(_saveData, fieldStyle);
        GUILayout.Space(4);

        GUI.enabled = !_cloudSaveRunning;
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Save", btnStyle, GUILayout.Height(btnHeight)))
            CloudSaveOp("save");
        if (GUILayout.Button("Get Latest", btnStyle, GUILayout.Height(btnHeight)))
            CloudSaveOp("getLatest");
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Get All", btnStyle, GUILayout.Height(btnHeight)))
            CloudSaveOp("getAll");
        if (GUILayout.Button("Delete Latest", btnStyle, GUILayout.Height(btnHeight)))
            CloudSaveOp("deleteLatest");
        GUILayout.EndHorizontal();
        GUI.enabled = true;

        GUILayout.Space(8);

        // --- PlayerData: Key-Value ---
        GUILayout.Label("=== Player Data (Key-Value) ===", labelStyle);
        GUILayout.BeginHorizontal();
        GUILayout.Label("Key:", labelStyle, GUILayout.Width(40));
        _playerDataKey = GUILayout.TextField(_playerDataKey, fieldStyle);
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        GUILayout.Label("Value:", labelStyle, GUILayout.Width(40));
        _playerDataValue = GUILayout.TextField(_playerDataValue, fieldStyle);
        GUILayout.EndHorizontal();
        GUILayout.Space(4);

        GUI.enabled = !_cloudSaveRunning;
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Set", btnStyle, GUILayout.Height(btnHeight)))
            CloudSaveOp("setPlayerData");
        if (GUILayout.Button("Get", btnStyle, GUILayout.Height(btnHeight)))
            CloudSaveOp("getPlayerData");
        GUILayout.EndHorizontal();
        GUI.enabled = true;

        // Status/Result display
        if (!string.IsNullOrEmpty(_cloudSaveStatus) && _cloudSaveStatus != "Idle")
        {
            GUILayout.Space(4);
            GUILayout.Label($"Status: {_cloudSaveStatus}", labelStyle);
        }
        if (!string.IsNullOrEmpty(_cloudSaveResult))
        {
            GUILayout.Space(4);
            GUILayout.Label($"Result: {_cloudSaveResult}", labelStyle);
        }
    }

    private async void CloudSaveOp(string op)
    {
        _cloudSaveRunning = true;
        _cloudSaveStatus = $"Running {op}...";
        _cloudSaveResult = "";
        CloudSaveResult result = null;

        try
        {
            switch (op)
            {
                case "save":
                    result = await _cloudSave.Save(_saveData, _token);
                    break;
                case "getLatest":
                    result = await _cloudSave.GetLatest(_token);
                    break;
                case "getAll":
                    result = await _cloudSave.GetAll(_token);
                    break;
                case "deleteLatest":
                    // First get latest to find version, then delete
                    var latest = await _cloudSave.GetLatest(_token);
                    if (latest.success && !string.IsNullOrEmpty(latest.data))
                    {
                        var dict = MiniJson.Deserialize(latest.data) as Dictionary<string, object>;
                        if (dict != null && dict.ContainsKey("version"))
                        {
                            long version = Convert.ToInt64(dict["version"]);
                            result = await _cloudSave.Delete(version, _token);
                        }
                        else
                        {
                            result = new CloudSaveResult { success = false, error = "No version found in latest save" };
                        }
                    }
                    else
                    {
                        result = latest.success
                            ? new CloudSaveResult { success = false, error = "No saves to delete" }
                            : latest;
                    }
                    break;
                case "setPlayerData":
                    result = await _cloudSave.SetPlayerData(_playerDataKey, _playerDataValue, _token);
                    break;
                case "getPlayerData":
                    result = await _cloudSave.GetPlayerData(_playerDataKey, _token);
                    break;
            }

            _cloudSaveStatus = result != null && result.success ? "Success" : $"Failed";
            _cloudSaveResult = result != null
                ? (result.success ? (result.data ?? "(ok, no data)") : (result.error ?? "unknown error"))
                : "null result";
            Debug.Log($"[CloudSave] {op}: {_cloudSaveStatus} - {_cloudSaveResult}");
        }
        catch (Exception ex)
        {
            _cloudSaveStatus = "Error";
            _cloudSaveResult = ex.Message;
            Debug.LogError($"[CloudSave] {op} error: {ex.Message}");
        }
        finally
        {
            _cloudSaveRunning = false;
        }
    }

    // === LEADERBOARD ===

    private void DrawLeaderboardSection(GUIStyle boxStyle, GUIStyle btnStyle, GUIStyle labelStyle, GUIStyle fieldStyle, int btnHeight)
    {
        GUILayout.Box("Leaderboard", boxStyle, GUILayout.ExpandWidth(true));
        GUILayout.Space(4);

        GUILayout.Label("Meta Name (API Name):", labelStyle);
        _leaderboardMetaName = GUILayout.TextField(_leaderboardMetaName, fieldStyle);
        GUILayout.Space(4);

        GUILayout.Label("Score:", labelStyle);
        _leaderboardScore = GUILayout.TextField(_leaderboardScore, fieldStyle);
        GUILayout.Space(8);

        GUI.enabled = !_leaderboardRunning;
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Submit Score", btnStyle, GUILayout.Height(btnHeight)))
            LeaderboardOp("submitScore");
        if (GUILayout.Button("Get Ranking", btnStyle, GUILayout.Height(btnHeight)))
            LeaderboardOp("getRanking");
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Get Guest Ranking", btnStyle, GUILayout.Height(btnHeight)))
            LeaderboardOp("getGuestRanking");
        GUILayout.EndHorizontal();
        GUI.enabled = true;

        // Status/Result display
        if (!string.IsNullOrEmpty(_leaderboardStatus) && _leaderboardStatus != "Idle")
        {
            GUILayout.Space(4);
            GUILayout.Label($"Status: {_leaderboardStatus}", labelStyle);
        }
        if (!string.IsNullOrEmpty(_leaderboardResult))
        {
            GUILayout.Space(4);
            GUILayout.Label($"Result: {_leaderboardResult}", labelStyle);
        }
    }

    private async void LeaderboardOp(string op)
    {
        _leaderboardRunning = true;
        _leaderboardStatus = $"Running {op}...";
        _leaderboardResult = "";
        LeaderboardResult result = null;

        try
        {
            switch (op)
            {
                case "submitScore":
                    Debug.Log($"[Leaderboard] Submitting score: metaName={_leaderboardMetaName}, value={_leaderboardScore}");
                    result = await _leaderboard.SubmitScore(_leaderboardMetaName, _leaderboardScore, _token);
                    break;
                case "getRanking":
                    result = await _leaderboard.GetLeaderboard(_leaderboardMetaName, _token);
                    break;
                case "getGuestRanking":
                    result = await _leaderboard.GetGuestLeaderboard(_leaderboardMetaName);
                    break;
            }

            _leaderboardStatus = result != null && result.success ? "Success" : "Failed";
            _leaderboardResult = result != null
                ? (result.success ? (result.data ?? "(ok, no data)") : (result.error ?? "unknown error"))
                : "null result";
            Debug.Log($"[Leaderboard] {op}: {_leaderboardStatus} - {_leaderboardResult}");
        }
        catch (Exception ex)
        {
            _leaderboardStatus = "Error";
            _leaderboardResult = ex.Message;
            Debug.LogError($"[Leaderboard] {op} error: {ex.Message}");
        }
        finally
        {
            _leaderboardRunning = false;
        }
    }

    // === ACHIEVEMENTS ===

    private void DrawAchievementsSection(GUIStyle boxStyle, GUIStyle btnStyle, GUIStyle labelStyle, GUIStyle fieldStyle, int btnHeight)
    {
        GUILayout.Box("Achievements", boxStyle, GUILayout.ExpandWidth(true));
        GUILayout.Space(4);

        GUILayout.Label("Achievement API Name:", labelStyle);
        achievementApiName = GUILayout.TextField(achievementApiName, fieldStyle);
        GUILayout.Space(8);

        GUI.enabled = !_achievementsRunning;
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Get All", btnStyle, GUILayout.Height(btnHeight)))
            AchievementsOp("getAll");
        if (GUILayout.Button("Unlock", btnStyle, GUILayout.Height(btnHeight)))
            AchievementsOp("unlock");
        GUILayout.EndHorizontal();
        GUI.enabled = true;

        // Status/Result display
        if (!string.IsNullOrEmpty(_achievementsStatus) && _achievementsStatus != "Idle")
        {
            GUILayout.Space(4);
            GUILayout.Label($"Status: {_achievementsStatus}", labelStyle);
        }
        if (!string.IsNullOrEmpty(_achievementsResult))
        {
            GUILayout.Space(4);
            GUILayout.Label($"Result: {_achievementsResult}", labelStyle);
        }
    }

    private async void AchievementsOp(string op)
    {
        _achievementsRunning = true;
        _achievementsStatus = $"Running {op}...";
        _achievementsResult = "";
        AchievementResult result = null;

        try
        {
            switch (op)
            {
                case "getAll":
                    Debug.Log("[Achievements] Getting user achievements...");
                    result = await _achievements.GetUserAchievements(_token);
                    break;
                case "unlock":
                    string json = $"[{{\"api_name\":\"{achievementApiName}\",\"unlock\":true}}]";
                    Debug.Log($"[Achievements] Unlocking: {json}");
                    result = await _achievements.UnlockAchievements(json, _token);
                    break;
            }

            _achievementsStatus = result != null && result.success ? "Success" : "Failed";
            _achievementsResult = result != null
                ? (result.success ? (result.data ?? "(ok, no data)") : (result.error ?? "unknown error"))
                : "null result";
            Debug.Log($"[Achievements] {op}: {_achievementsStatus} - {_achievementsResult}");
        }
        catch (Exception ex)
        {
            _achievementsStatus = "Error";
            _achievementsResult = ex.Message;
            Debug.LogError($"[Achievements] {op} error: {ex.Message}");
        }
        finally
        {
            _achievementsRunning = false;
        }
    }

    // === AVATAR ===

    private void DrawAvatarSection(GUIStyle boxStyle, GUIStyle btnStyle, GUIStyle labelStyle, GUIStyle fieldStyle, int btnHeight)
    {
        GUILayout.Box("Avatar", boxStyle, GUILayout.ExpandWidth(true));
        GUILayout.Space(4);

        bool loggedIn = _auth != null && _auth.IsLoggedIn;

        // Auth-gated methods
        GUILayout.Label("Authenticated (requires login):", labelStyle);
        GUI.enabled = !_avatarRunning && loggedIn;
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Get Profile", btnStyle, GUILayout.Height(btnHeight)))
            AvatarOp("getProfile");
        if (GUILayout.Button("Get Avatar List", btnStyle, GUILayout.Height(btnHeight)))
            AvatarOp("getAvatarList");
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Get Active Avatar", btnStyle, GUILayout.Height(btnHeight)))
            AvatarOp("getActiveAvatar");
        if (GUILayout.Button("Download Active VRM", btnStyle, GUILayout.Height(btnHeight)))
            AvatarOp("downloadVrm");
        GUILayout.EndHorizontal();
        GUI.enabled = true;

        GUILayout.Space(8);

        // Public methods (no auth required)
        GUILayout.Label("Public (no login required):", labelStyle);
        GUI.enabled = !_avatarRunning;

        if (GUILayout.Button("Get Public Avatar List", btnStyle, GUILayout.Height(btnHeight)))
            AvatarOp("getPublicList");

        GUILayout.Space(4);
        GUILayout.Label("Public Avatar ID:", labelStyle);
        _publicAvatarId = GUILayout.TextField(_publicAvatarId, fieldStyle);
        if (GUILayout.Button("Get Public Avatar By ID", btnStyle, GUILayout.Height(btnHeight)))
            AvatarOp("getPublicById");

        GUI.enabled = true;

        // Status/Result display
        if (!string.IsNullOrEmpty(_avatarStatus) && _avatarStatus != "Idle")
        {
            GUILayout.Space(4);
            GUILayout.Label($"Status: {_avatarStatus}", labelStyle);
        }
        if (!string.IsNullOrEmpty(_avatarResult))
        {
            GUILayout.Space(4);
            GUILayout.Label($"Result: {_avatarResult}", labelStyle);
        }
    }

    private async void AvatarOp(string op)
    {
        _avatarRunning = true;
        _avatarStatus = $"Running {op}...";
        _avatarResult = "";

        try
        {
            AvatarResult result = null;

            switch (op)
            {
                case "getProfile":
                    Debug.Log("[Avatar] Getting profile...");
                    result = await _avatar.GetProfile(_token);
                    break;
                case "getAvatarList":
                    Debug.Log("[Avatar] Getting avatar list...");
                    result = await _avatar.GetAvatarList(_token);
                    break;
                case "getActiveAvatar":
                    Debug.Log("[Avatar] Getting active avatar...");
                    result = await _avatar.GetActiveAvatar(_token);
                    break;
                case "getPublicList":
                    Debug.Log("[Avatar] Getting public avatar list...");
                    result = await _avatar.GetPublicAvatarList();
                    break;
                case "getPublicById":
                    if (string.IsNullOrEmpty(_publicAvatarId))
                    {
                        _avatarStatus = "Failed";
                        _avatarResult = "Enter an Avatar ID first";
                        _avatarRunning = false;
                        return;
                    }
                    Debug.Log($"[Avatar] Getting public avatar: {_publicAvatarId}");
                    result = await _avatar.GetPublicAvatarByID(_publicAvatarId);
                    break;
                case "downloadVrm":
                    Debug.Log("[Avatar] Downloading active VRM...");
                    var activeResult = await _avatar.GetActiveAvatar(_token);
                    if (!activeResult.success)
                    {
                        _avatarStatus = "Failed";
                        _avatarResult = activeResult.error ?? "Could not get active avatar";
                        _avatarRunning = false;
                        return;
                    }
                    if (string.IsNullOrEmpty(activeResult.data))
                    {
                        _avatarStatus = "Failed";
                        _avatarResult = "No active avatar set";
                        _avatarRunning = false;
                        return;
                    }
                    // Parse VRM URL from active avatar data
                    string vrmUrl = ParseVrmUrl(activeResult.data);
                    if (string.IsNullOrEmpty(vrmUrl))
                    {
                        _avatarStatus = "Failed";
                        _avatarResult = "No VRM URL found in active avatar";
                        _avatarRunning = false;
                        return;
                    }
                    Debug.Log($"[Avatar] Downloading VRM from: {vrmUrl}");
                    var dlResult = await _avatar.DownloadAvatarFile(vrmUrl, _token);
                    _avatarStatus = dlResult.success ? "Success" : "Failed";
                    if (dlResult.success && dlResult.data != null && dlResult.data.Length > 0)
                    {
                        _avatarResult = $"Downloaded {dlResult.size} bytes, loading VRM...";
                        Debug.Log($"[Avatar] Loading VRM ({dlResult.size} bytes)...");
#if VIVERSE_VRM_INSTALLED
                        bool loaded = await _vrmController.LoadVrmFromBytes(dlResult.data);
                        if (loaded)
                        {
                            _avatarStatus = "Success";
                            _avatarResult = _vrmController.StatusText;
                        }
                        else
                        {
                            _avatarStatus = "Failed";
                            _avatarResult = _vrmController.StatusText;
                        }
#else
                        _avatarStatus = "Downloaded (no renderer)";
                        _avatarResult = $"Downloaded {dlResult.size} bytes. Install UniVRM to render.";
                        Debug.LogWarning(
                            "[ViverseSDK] VRM downloaded but UniVRM package not installed — " +
                            "cannot render. See README.md § Avatar for setup.");
#endif
                    }
                    else
                    {
                        _avatarResult = dlResult.success
                            ? $"Downloaded {dlResult.size} bytes but no data received"
                            : (dlResult.error ?? "download failed");
                    }
                    Debug.Log($"[Avatar] Download: {_avatarStatus} - {_avatarResult}");
                    _avatarRunning = false;
                    return;
            }

            _avatarStatus = result != null && result.success ? "Success" : "Failed";
            _avatarResult = result != null
                ? (result.success ? (result.data ?? "(ok, no data)") : (result.error ?? "unknown error"))
                : "null result";
            Debug.Log($"[Avatar] {op}: {_avatarStatus} - {_avatarResult}");
        }
        catch (Exception ex)
        {
            _avatarStatus = "Error";
            _avatarResult = ex.Message;
            Debug.LogError($"[Avatar] {op} error: {ex.Message}");
        }
        finally
        {
            _avatarRunning = false;
        }
    }

    private string ParseVrmUrl(string avatarJson)
    {
        try
        {
            var dict = MiniJson.Deserialize(avatarJson) as Dictionary<string, object>;
            if (dict == null) return null;

            if (dict.ContainsKey("VrmBinaryDataUrl") && dict["VrmBinaryDataUrl"] != null)
                return dict["VrmBinaryDataUrl"].ToString();
            if (dict.ContainsKey("vrmUrl") && dict["vrmUrl"] != null)
                return dict["vrmUrl"].ToString();
            if (dict.ContainsKey("file") && dict["file"] != null)
                return dict["file"].ToString();

            return null;
        }
        catch
        {
            return null;
        }
    }
}
