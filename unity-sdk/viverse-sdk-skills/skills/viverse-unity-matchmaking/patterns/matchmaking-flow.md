# Matchmaking Flow Pattern

## Connection → Actor → Room Lifecycle

```
Initialize(appId, debug)
        │
        ▼  (async — WebGL waits for play-sdk UMD to load)
   OnConnect fires
        │
        ▼
  SetActor(properties)   ← REQUIRED before any room operations
        │
        ▼
  ┌─────────────────────────────┐
  │        LOBBY STATE          │
  │  GetAvailableRooms → list   │
  │  CreateRoom / JoinRoom      │
  │  StartMatch (auto-match)    │
  └─────────────┬───────────────┘
                │
                ▼
  ┌─────────────────────────────┐
  │        IN ROOM              │
  │  OnRoomActorChange events   │
  │  OnGameStartNotify          │
  │  LeaveRoom → OnJoinedLobby  │
  └─────────────────────────────┘
```

## Complete Integration Example

```csharp
using ViverseSDK;
using UnityEngine;

public class MatchmakingManager : MonoBehaviour
{
    [SerializeField] private string appId = "your-app-id";
    [SerializeField] private bool debugMode = true;

    private MatchmakingClient _client;

    private void Start()
    {
        // Get or create singleton
        _client = MatchmakingClient.Instance;
        if (_client == null)
        {
            var go = new GameObject("[MatchmakingClient]");
            _client = go.AddComponent<MatchmakingClient>();
        }

        // Subscribe BEFORE Initialize
        _client.OnConnect += HandleConnect;
        _client.OnDisconnect += HandleDisconnect;
        _client.OnJoinRoom += HandleJoinRoom;
        _client.OnRoomListUpdate += HandleRoomList;
        _client.OnRoomActorChange += HandleActorChange;
        _client.OnJoinedLobby += HandleJoinedLobby;

        // Connect (session_id generated internally)
        _client.Initialize(appId, debugMode);
    }

    private void HandleConnect()
    {
        // MUST SetActor immediately — properties wrapper required by server
        var payload = "{\"properties\":{\"name\":\"Player1\"}}";
        _client.SendRequest("SetActor", payload, (res) =>
        {
            Debug.Log($"SetActor response: {res}");
            // Now safe to do room operations
            _client.SendRequest("GetAvailableRooms", "{}", null);
        });
    }

    private void HandleRoomList(string json)
    {
        // Parse and display rooms in UI
        Debug.Log($"Room list: {json}");
    }

    private void HandleJoinRoom(string json)
    {
        Debug.Log($"Joined room: {json}");
    }

    private void HandleActorChange(string json)
    {
        Debug.Log($"Actors changed: {json}");
    }

    private void HandleJoinedLobby()
    {
        Debug.Log("Back in lobby");
    }

    private void HandleDisconnect()
    {
        Debug.Log("Disconnected");
    }

    // --- Room Operations (call after SetActor) ---

    public void CreateRoom(string roomName, int maxPlayers = 4)
    {
        // NOTE: field is "room_name" NOT "name" — server rejects "name"
        var payload = $"{{\"room_name\":\"{roomName}\",\"mode\":\"default\",\"max_players\":{maxPlayers},\"min_players\":2}}";
        _client.SendRequest("CreateRoom", payload, (res) =>
        {
            Debug.Log($"CreateRoom: {res}");
        });
    }

    public void JoinRoom(string roomId)
    {
        var payload = $"{{\"room_id\":\"{roomId}\"}}";
        _client.SendRequest("JoinRoom", payload, null);
    }

    public void LeaveRoom()
    {
        _client.SendRequest("LeaveRoom", "{}", null);
    }

    public void RefreshRooms()
    {
        _client.SendRequest("GetAvailableRooms", "{}", null);
    }

    // Scene teardown: ONLY unsubscribe scene-local handlers.
    // Do NOT call _client.Disconnect() here — MatchmakingClient uses
    // DontDestroyOnLoad and is designed to survive scene transitions
    // (e.g. Lobby → Gameplay) while the room stays open and discoverable
    // to other players. Disconnecting the room owner here removes the
    // room from GetAvailableRooms / OnRoomListUpdate for everyone else.
    //
    // Call Disconnect() ONLY when matchmaking is truly ending:
    //   - Application quit           → OnApplicationQuit
    //   - User logout / return to menu
    //   - Explicit "Leave matchmaking" UI action
    // To leave a room while staying connected, use LeaveRoom instead.
    private void OnDestroy()
    {
        if (_client != null)
        {
            _client.OnConnect -= HandleConnect;
            _client.OnDisconnect -= HandleDisconnect;
            _client.OnJoinRoom -= HandleJoinRoom;
            _client.OnRoomListUpdate -= HandleRoomList;
            _client.OnRoomActorChange -= HandleActorChange;
            _client.OnJoinedLobby -= HandleJoinedLobby;
            // NOTE: intentionally NOT calling _client.Disconnect() — see comment above.
        }
    }
}
```

## Lifecycle: Disconnect vs LeaveRoom vs CloseRoom vs StartGame

`MatchmakingClient` is a singleton with `DontDestroyOnLoad` — it is meant to
outlive individual scenes. Room discoverability is tied to the owner's
matchmaking socket, so choosing the wrong teardown call is the most common
source of "the room disappeared from the list" bugs.

| Call | Effect on owner's connection | Effect on room discoverability |
|------|------------------------------|-------------------------------|
| `LeaveRoom` | Stays connected, returns to lobby | Room persists if other actors remain; auto-closes if empty |
| `CloseRoom` | Stays connected | Room removed from `GetAvailableRooms` immediately; existing actors stay in the room |
| `StartGame` (matchmaking) | Stays connected | Room is no longer joinable via matchmaking (game in progress) |
| `Disconnect` | Socket closed | Room is torn down for everyone; **not discoverable** to any client |

### Scene-transition rule

When the room owner moves Lobby → Gameplay **while still waiting for players**:

- ✅ Keep `MatchmakingClient` alive (`DontDestroyOnLoad` already does this).
- ✅ Unsubscribe the *lobby scene's* handlers in `OnDestroy`.
- ✅ Re-subscribe the *gameplay scene's* handlers in the new scene's `Start`.
- ❌ Do **not** call `Disconnect()` in the lobby scene's `OnDestroy`.
- ❌ Do **not** call `LeaveRoom`/`CloseRoom` unless you genuinely want to close the waiting room.

### When to actually call Disconnect()

```csharp
// Application quit — clean socket shutdown
private void OnApplicationQuit()
{
    MatchmakingClient.Instance?.Disconnect();
}

// User logs out or returns to main menu
public void OnReturnToMainMenu()
{
    MatchmakingClient.Instance?.Disconnect();
}
```

## Request Payload Format

`SendRequest()` auto-injects protocol fields. You only provide the inner payload:

```csharp
// What you write:
SendRequest("CreateRoom", "{\"room_name\":\"Test\",\"max_players\":4,\"min_players\":2}", callback);

// What goes on the wire (auto-injected: request_id, request_type, app_id, session_id):
// {"request_id":1,"request_type":"CreateRoom","app_id":"your-app","session_id":"auto-uuid","room_name":"Test","max_players":4,"min_players":2}
```

**Do NOT include `session_id` in your payloads** — it's auto-injected from the UUID generated at `Initialize()`.

## Payload Gotchas

| Request | Correct Payload | Common Mistake |
|---------|----------------|----------------|
| SetActor | `{"properties":{"name":"..."}}` | `{"name":"..."}` (server ignores) |
| CreateRoom | `{"room_name":"...","max_players":N,"min_players":N}` | `{"name":"..."}` (wrong field) |
| JoinRoom | `{"room_id":"uuid"}` | Missing room_id |
| LeaveRoom | `{}` | Including session_id manually |
| GetAvailableRooms | `{}` | N/A |

## GetAvailableRooms Response

The response is an envelope with a `data` array:

```json
{"request_id":3,"success":true,"request_type":"GetAvailableRooms","data":[{"id":"uuid","name":"MyRoom","actors":[...],"max_players":4}]}
```

Parse the `data` field to get the room list array.

## Editor Path: Heartbeat

The Editor path sends a `0x00` byte every 30 seconds to keep the connection alive. This is handled automatically by `MatchmakingClient.cs` via a coroutine — no action needed from the integration layer.

If you build a custom client, implement:
```csharp
private IEnumerator HeartbeatLoop()
{
    while (true)
    {
        yield return new WaitForSeconds(30f);
        if (_ws != null && _ws.State == WebSocketState.Open)
            _ws.Send(new byte[] { 0x00 });
    }
}
```

## Editor Path: XOR Encryption

When `debugMode = false`, messages are XOR-encrypted with key `{0x12, 0x34, 0x56, 0x78}`:

```csharp
private static byte[] XorEncrypt(byte[] data)
{
    byte[] result = new byte[data.Length];
    for (int i = 0; i < data.Length; i++)
        result[i] = (byte)(data[i] ^ _xorKey[i % _xorKey.Length]);
    return result;
}
```

In debug mode (`debug=true` in URL), messages are plaintext JSON.

## Event Sources

| Path | Format | Delivery |
|------|--------|----------|
| WebGL (jslib) | `{"event":"onJoinRoom","data":{...}}` | `SendMessage(go, "OnJSMessage", json)` |
| Editor (server) | `{"event":"onJoinRoom","data":{...}}` | WebSocket OnMessage → ProcessIncomingMessage |
| Request response | `{"request_id":N,"success":true,"request_type":"...","data":...}` | Resolved via _pendingRequests callback |

Both event paths converge in `DispatchEvent()` which fires the appropriate C# Action events.

## WebGL Initialization is Async

In WebGL, `Initialize()` triggers an async flow:
1. C# calls `ViversePlay_Matchmaking_Initialize()` in jslib
2. jslib creates `new (globalThis.viverse.Play)()` 
3. Awaits `playClient.newMatchmakingClient(appId, debug)` — this loads play-sdk UMD from CDN
4. Once loaded, subscribes to events and `OnConnect` fires back to C#

The client is NOT ready until `OnConnect` fires. All room operations before that will fail.
