# Neon Sumo sample

![Neon Sumo gameplay](Neon_Sumo_Screenshot.png)

A Unity multiplayer demo showcasing the **VIVERSE Unity SDK**. This project walks through the full lifecycle of a multiplayer game—from lobby matchmaking to gameplay sync—and how each SDK module is used to solve real problems.

**Target audience:** Game developers building multiplayer games in Unity who are new to the VIVERSE Unity SDK or need guidance on how to apply it.

## Play the hosted demo

A public build is available on VIVERSE: [Neon Sumo (4 players)](https://www.viverse.com/HwW3QU2).

## App ID

To run this sample yourself, create a game in [VIVERSE Studio](https://studio.viverse.com/) and copy the App ID it generates. In the Unity Editor, select `Assets/NeonSumo/Config/NeonSumoConfig.asset` and paste that value into the **App Id** field. The committed placeholder (`example-app-id`) will not connect.

The hosted world uses its own Studio App ID. The git sample stays on the placeholder.

## Open this sample

1. Clone [viverse-unity](https://github.com/VIVERSE-DOCS/viverse-unity).
2. In Unity Hub, open **`samples/neon-sumo`** as the project folder (Unity 6000.0.49f1).
3. Let Unity import and resolve packages.
4. Set your App ID on `NeonSumoConfig` as described above.

The VIVERSE Unity SDK is already installed under `Assets/viverse-unity-sdk/`. You do not need to import the repo-root `unity-sdk` folder.

---

## Neon Sumo - Viverse Multiplayer SDK Demo

**Neon Sumo** is a party game inspired by Fusion Frenzy's Sumo mode:
- **2–4 players** battle on a circular platform
- Push other players off to eliminate them
- Last player standing wins
- 60 second rounds with restart capability

---

## What This Demo Demonstrates

This demo implements a complete multiplayer game using the Viverse Multiplayer SDK:

- **Matchmaking** — `MatchmakingClient` for room creation, joining, and player management
- **Presence** — `MultiplayerClient` for real-time connection and player discovery
- **Game Lifecycle** — `GameModule` for ready states, countdowns, timers, and restart
- **Position Sync** — `NetworkSyncModule` + `GeneralModule` for host-authoritative state broadcast
- **Gameplay Events** — `ActionSyncModule` for eliminations, boosts, spawns, and round wins
- **Scoring** — `LeaderboardModule` for win tracking

---

## PlaySDK Unity clients

`MatchmakingClient` and `MultiplayerClient` are implemented in **`Assets/Scripts/PlaySDK/`** as `MonoBehaviour` types (each exposes a static **`Instance`** when used as a scene / root object). Transport is **platform-specific**: **WebGL** builds call into JavaScript via **`DllImport`**; **Editor and standalone** use managed networking (e.g. **`NativeWebSocket`** on matchmaking, **`MediasoupProxyClient`** / websocket path on multiplayer). In Neon Sumo, prefer **`PlaySdkRuntimeRoot`** to **`CreateChildClient<T>`** / destroy lifecycle so lobby and gameplay scenes share a consistent client graph.

---

## How the Viverse SDK Solves Problems: Game Flow Documentation

### Phase 1: Lobby (Matchmaking)

**Problem:** Players need to find or create rooms before entering the game.

**Lobby UI (UI Toolkit):** The lobby scene uses **`NeonSumoLobbyPanelView`** (`Assets/UI/NeonSumoLobbyPanelView.cs`) on a **`UIDocument`**, with layout in **`NeonSumoLobbyPanel.uxml`** and styles in **`NeonSumoLobbyPanel.uss`**. The view is **passive** (click / navigation events and property accessors only); **`NeonSumoLobbyManager`** assigns **`lobbyPanelView`** and connects it to **`NeonSumoLobbyService`**.

**SDK solution:** Use `MatchmakingClient` to handle all lobby operations. Obtain the client:

```csharp
public MatchmakingClient GetOrCreateMatchmakingClient()
{
    return _matchmakingClient ??= CreateChildClient<MatchmakingClient>("NeonSumoMatchmakingClient");
}
```
*— `PlaySdkRuntimeRoot.GetOrCreateMatchmakingClient()`*

Initialize with your AppId:

```csharp
_matchmakingClient.Initialize(appId, debugMode: true);
```
*— `NeonSumoLobbyService` — `MatchmakingClient.Initialize`*

Set each player's identity (e.g. in `SetActorAsync`):

```csharp
await _matchmakingClient.SetActor(_currentActor);
```
*— `NeonSumoLobbyService` — `SetActor` / `SetActorAsync`*

Refresh the room list:

```csharp
var response = await _matchmakingClient.GetAvailableRooms();
if (response?.rooms != null)
    rooms.AddRange(response.rooms);
```
*— `NeonSumoLobbyService` — `GetAvailableRooms`*

Create a new room:

```csharp
await _matchmakingClient.CreateRoom(roomCode, _roomMode, maxPlayers, minPlayers);
```
*— `NeonSumoLobbyService.CreateRoom`*

Join an existing room:

```csharp
await _matchmakingClient.JoinRoom(roomId);
```
*— `NeonSumoLobbyService.JoinRoom`*

Leave the current room:

```csharp
await _matchmakingClient.LeaveRoom();
```
*— `NeonSumoLobbyService.LeaveRoom`*

**Handoff to gameplay:** Before loading the game scene, save the room and session data so `MultiplayerClient` can connect to the same game. In `HandoffToGameplayScene`:

```csharp
var handoff = _lobbyService.GetHandoffData();
if (!handoff.HasValue) return;

var (roomId, appId, userSessionId) = handoff.Value;
MatchmakingHandoffStore.SaveHandoff(roomId, appId, userSessionId);
SceneManager.LoadScene(gameplaySceneName);
```
*— `NeonSumoLobbyManager.HandoffToGameplayScene`*

`GetHandoffData` returns the necessary IDs:

```csharp
return (_currentRoom.game_session, appId, _currentActor.session_id);
```
*— `NeonSumoLobbyService.GetHandoffData`*

Use `MatchmakingHandoffStore` to save, load, and clear handoff state:

```csharp
// Save before loading gameplay scene
MatchmakingHandoffStore.SaveHandoff(roomId, appId, userSessionId);

// Load in gameplay scene bootstrap
MatchmakingHandoffStore.TryLoad(out handoff);

// Clear after successful connection
MatchmakingHandoffStore.Clear();
```
*— `MatchmakingHandoffStore` — `SaveHandoff`, `TryLoad`, `Clear`*

---

### Phase 2: Connecting to the Game Room

**Problem:** The gameplay scene must connect to the same room and enable all multiplayer features.

**SDK solution:** Initialize `MultiplayerClient` and call `Init` with module options. Get the client:

```csharp
PlaySdkRuntimeRoot.Instance.DestroyMultiplayerClient();
return PlaySdkRuntimeRoot.Instance.GetOrCreateMultiplayerClient();
```
*— `NeonSumoMatchmakingFlow` — `DestroyMultiplayerClient` / `GetOrCreateMultiplayerClient`*

Subscribe to `OnConnected` before calling `Initialize`, then connect:

```csharp
bool connected = false;
void OnConnected() => connected = true;

_multiplayerClient.OnConnected += OnConnected;
try
{
    _ = _multiplayerClient.Initialize(roomId, appId, userSessionId);
    yield return WaitUntil(() => connected, ConnectTimeoutSeconds);
}
finally
{
    _multiplayerClient.OnConnected -= OnConnected;
}
```
*— `NeonSumoMatchmakingFlow` — `OnConnected` + `MultiplayerClient.Initialize`*

If the connect wait times out, the flow **logs a warning and continues** (`ConnectClient`) so `Initialize` may still complete asynchronously.

Build module options and call `Init` — see **`NeonSumoMatchmakingFlow.BuildInitOptions`**: each module sets a `desc`; the **game** module includes `ready_time`, `start_delay_time`, `play_time`, **`change_second`** (`RingShrinkAccelerationSecond`), **`total_player`**, `min_total_player`, `max_total_player`, and `wait_player_timeout`. Player counts are validated in **`TryGetValidatedPlayerCounts`** (from `NeonSumoConfig`).

```csharp
var options = BuildInitOptions(minPlayers, maxPlayers);
var initTask = _multiplayerClient.Init(options);
// InitializeModules waits up to InitTimeoutSeconds; if the task is still running,
// flow may proceed when IsSemanticallyReady() (PeerId, RoomId, AppId are set).
```
*— `NeonSumoMatchmakingFlow.InitializeModules`, `BuildInitOptions`, `WaitForTask`, `IsSemanticallyReady`*

After a successful module init, **`InitializeGameManager`** calls `NeonSumoGameManager.Initialize(_multiplayerClient, _multiplayerClient.PeerId, maxPlayers)`, then **`MatchmakingHandoffStore.Clear()`** so the gameplay scene does not re-read stale handoff on reload.

---

### Phase 3: Player Discovery & Spawning

**Problem:** Players must see each other and spawn correctly even when joining at different times.

**SDK solution:** Use `Game.GetRoomInfo()` to backfill existing players and `OnClientConnected` for late joiners. **`NeonSumoGameManager.Initialize`** only spawns the **local** player up front (they never get `OnClientConnected` for themselves). **Room backfill is deferred**: `TryBackfillExistingPlayers` runs when readiness allows— notably from **`MasterSyncHandler.RunCommonSync`** (`TryBackfill` callback) and other game-flow hooks, not inline at the end of `Initialize`.

Backfill core (simplified; **`ExtractUserIds`** accepts several room payload shapes, e.g. `user_ids`, `user_list`, `users`, `members`, `connected_clients`):

```csharp
var roomInfo = await _multiplayerClient.Game.GetRoomInfo();
var userIds = ExtractUserIds(roomInfo);
foreach (var userId in userIds)
{
    if (!_players.ContainsKey(userId))
        SpawnPlayer(userId, userId == _localPlayerId);
}
```
*— `NeonSumoGameManager.BackfillExistingPlayersAsync` via `TryBackfillExistingPlayers`*

Local player spawn in `Initialize`:

```csharp
if (!string.IsNullOrEmpty(_localPlayerId) && !_players.ContainsKey(_localPlayerId))
{
    SpawnPlayer(_localPlayerId, true);
}
```
*— `NeonSumoGameManager.Initialize` — explicit local spawn*

Subscribe to player join/leave:

```csharp
_multiplayerClient.OnClientConnected += HandlePlayerJoined;
_multiplayerClient.OnClientDisconnected += HandlePlayerLeft;
```
*— `NeonSumoGameManager` — `OnClientConnected` / `OnClientDisconnected`*

**`SpawnCoordinator`** (used by `NeonSumoGameManager`) wraps more than raw ActionSync calls: **`SpawnAckGate`** (wait for all spawn acks, dedupe local ack), **`StorePendingRampSpawn` / `TryApplyPendingRampSpawn`** for members not spawned yet, **`BuildSpawnSyncMessage`** (pose + **`color_index`**), **`BroadcastRampSpawn` / `BroadcastRampSpawns`**, and stable **`AssignRampSpawnIndices`**.

For spawn coordination, use ActionSync. When a client finishes local spawn setup:

```csharp
var msg = new SpawnAckMessage { user_id = userId };
string actionMsg = JsonConvert.SerializeObject(msg);
_ctx.MultiplayerClient.ActionSync.Competition(NeonSumoNetEvents.SpawnAck, actionMsg, Guid.NewGuid().ToString());
```
*— `SpawnCoordinator` — `SpawnAck` via `ActionSync.Competition`*

When the host broadcasts spawn state:

```csharp
string actionMsg = JsonConvert.SerializeObject(message);
_ctx.MultiplayerClient.ActionSync.Competition(NeonSumoNetEvents.PlayerSpawnSync, actionMsg, Guid.NewGuid().ToString());
```
*— `SpawnCoordinator` — `PlayerSpawnSync` broadcast*

---

### Phase 4: Master Client & Authority

**Problem:** One source of truth for colors, spawns, ring drops, winner, and restart.

**SDK solution:** The `GameModule` fires `master_notify` when the master is resolved. **`NeonSumoGameManager`** subscribes and forwards the payload to **`MasterSyncHandler.Handle`**:

```csharp
EnsureMasterSyncHandler();
_onMasterNotifyHandler = data => _masterSyncHandler?.Handle(data);
_multiplayerClient.Game.OnMasterNotify += _onMasterNotifyHandler;
```
*— `NeonSumoGameManager.SubscribeSdkEvents` + `MasterSyncHandler.Handle`*

**`MasterSyncHandler.Handle`** pipeline:
1. **`TryGetMasterUser`** — reads `master_user` from `JObject`, `Dictionary<string, object>`, or `JObject.FromObject(data)` (exceptions yield `null`).
2. **`OnCanonicalRoomHostUserId`** — publishes the SDK host id for **host-first ordering** (UI, colors, ramps) when present.
3. **`ResolveIsMaster`** — compares `master_user` to `PeerId`, else falls back to `IsMasterUser()`.
4. **`RunCommonSync`** (all clients): **`TryBackfill`**, **`Arena.ConfigureNetwork`**, **`ApplyAuthorityToAll`** (physics authority on every `NeonSumoPlayer`).
5. **`RunMasterOnlySync`**: **`SyncColorsAndBroadcast`** (remap colors in host-first order, rebuild player bar) and **`BeginRampSpawnPhaseIfNeeded`** when ramp spawns are enabled and not already in `Playing`.

**Host-first user-id ordering** is centralized in **`NeonSumoPlayerOrder`** (`OrderedUserIds`, `SortUserIdsHostFirst`, `CompareHostFirst`, `IndexInHostFirstOrder`). **`NeonSumoGameManager`**, **`PlayerBarCoordinator`**, and **`PlayerListPresenter`** use it with the effective room host id so roster, UI slots, and spawn-related ordering stay consistent with **`OnCanonicalRoomHostUserId`**.

Further game-only authority (winner, start/restart, ring drop competitions, etc.) still lives in **`NeonSumoGameManager`** guarded by master checks.

---

### Phase 5: Ready Phase & Countdown

**Problem:** UI must align with server-defined min/max players and timers.

**SDK solution:** The `GameModule` drives the full lifecycle. When a player presses Ready:

```csharp
_multiplayerClient.Game.Ready();
```
*— `NeonSumoGameManager` — `Game.Ready`*

Subscribe to lifecycle events:

```csharp
_multiplayerClient.Game.OnCountdownToStart += HandleCountdownToStart;
_multiplayerClient.Game.OnCountdownToEnd += HandleCountdownToEnd;
_multiplayerClient.Game.OnGameEnd += HandleGameEnd;
_multiplayerClient.Game.OnGameRestart += HandleGameRestart;
_multiplayerClient.Game.OnGameTimeUp += HandleGameTimeUp;
_multiplayerClient.Game.OnPlayerAllReady += HandleAllPlayersReady;
_multiplayerClient.Game.OnWaitForPlayer += HandleWaitForPlayer;
```
*— `NeonSumoGameManager` — `Game` lifecycle subscriptions*

Only the master starts the game:

```csharp
_multiplayerClient.Game.TriggerGameStart();
```
*— `NeonSumoGameManager` — `Game.TriggerGameStart`*

When the round ends, the master can restart:

```csharp
_multiplayerClient.Game.TriggerGameRestart();
```
*— `NeonSumoGameManager` — `Game.TriggerGameRestart`*

---

### Phase 6: Position & Physics Sync (Host-Authoritative)

**Problem:** Naive position sync from every client conflicts with physics authority.

**SDK solution:** Use a host-authoritative loop with `GeneralModule.SendMessage`. Clients send input to the host; the host simulates and broadcasts state to all clients. Non-hosts drive `TickClient` from **`FixedUpdate`**; the host drives **`TickHost`** from **`FixedUpdate`** (physics timestep). When entering the playing phase, `NeonSumoGameManager.StartGame` calls `HostInputSync.ResetTimers()` so send/broadcast timers do not carry over from a prior phase.

**Clients send input** (every ~50ms in `TickClient`; same interval for input vs state via `InputSendInterval` / `StateBroadcastInterval`):

```csharp
(Vector2 move, bool boost) = player.GetInputForHost();
var payload = NeonSumoMessageFactory.CreatePlayerInput(
    player.UserId,
    move.x,
    move.y,
    boost,
    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
_ctx.MultiplayerClient.SendMessage(payload);
```
*— `HostInputSync.TickClient` / `SendPlayerInput` — `CreatePlayerInput` + `SendMessage`*

`CreatePlayerInput` payload schema (action from `NeonSumoMessageActions.PlayerInput`):

```csharp
return new Dictionary<string, object>
{
    { "action", NeonSumoMessageActions.PlayerInput },
    { "userId", userId },
    { "moveX", moveX },
    { "moveZ", moveZ },
    { "boost", boost },
    { "timestamp", timestamp }
};
```
*— `NeonSumoMessageFactory.CreatePlayerInput`*

**Host simulates:** `ReceivePlayerInput` stores the latest remote packet per user. On each host tick, `ApplyHostMovement` walks every non-eliminated player with **controls enabled**: the **local** host player uses live `GetInputForHost()`; **remote** players use the stored snapshot only if it is **fresh** (see `MaxRemoteInputAgeMs` / `IsFresh` in `HostInputSync`), then `ApplyInputFromHost`.

```csharp
public void ReceivePlayerInput(string userId, float moveX, float moveZ, bool boost, double timestamp)
{
    _latestInputByUserId[userId] = new PlayerInputSnapshot {
        MoveX = moveX, MoveZ = moveZ, Boost = boost,
        ClientTimestamp = timestamp, HostReceivedTime = Time.realtimeSinceStartup
    };
}

// In ApplyHostMovement — per player, after ResolveInputForPlayer(...)
player.ApplyInputFromHost(input.Move, input.Boost);
```
*— `HostInputSync.ReceivePlayerInput`, `ApplyHostMovement`, `ResolveInputForPlayer`*

`ApplyInputFromHost` on the player (movement uses **turn resistance**: horizontal velocity alignment with the intended move direction scales force between **`movementConfig.turnResistanceMin`** and **`1f`**):

```csharp
public void ApplyInputFromHost(Vector2 moveInput, bool boost)
{
    if (_rb == null || _isEliminated) return;

    if (moveInput.sqrMagnitude > 0.01f)
    {
        Vector3 moveDirection = new Vector3(moveInput.x, 0f, moveInput.y).normalized;
        float forceScale = 1f;
        Vector3 horizontalVelocity = Vector3.ProjectOnPlane(_rb.linearVelocity, Vector3.up);
        if (horizontalVelocity.sqrMagnitude > 0.01f)
        {
            float alignment = Vector3.Dot(horizontalVelocity.normalized, moveDirection);
            float alignment01 = Mathf.Clamp01((alignment + 1f) * 0.5f);
            forceScale = Mathf.Lerp(movementConfig.turnResistanceMin, 1f, alignment01);
        }
        _rb.AddForce(moveDirection * movementConfig.moveSpeed * forceScale, ForceMode.Acceleration);
    }

    if (boost)
        TryBoostFromHost();
}
```
*— `NeonSumoPlayer.ApplyInputFromHost`*

**Host broadcasts state** (every ~50ms from `TickHost` via `BroadcastPlayerStates`):

```csharp
var payload = NeonSumoMessageFactory.CreatePlayerStateUpdate(
    player.UserId,
    rb.position,
    rb.rotation,
    rb.linearVelocity);
_ctx.MultiplayerClient.SendMessage(payload);
```
*— `HostInputSync.BroadcastPlayerStates` — `CreatePlayerStateUpdate` + `SendMessage`*

`CreatePlayerStateUpdate` payload schema (position, **full quaternion**, yaw alias, velocity; action string from `NeonSumoMessageActions.PlayerStateUpdate`):

```csharp
return new Dictionary<string, object>
{
    { "action", NeonSumoMessageActions.PlayerStateUpdate },
    { "userId", userId },
    { "x", position.x }, { "y", position.y }, { "z", position.z },
    { "rotY", rotation.eulerAngles.y },
    { "qx", rotation.x }, { "qy", rotation.y }, { "qz", rotation.z }, { "qw", rotation.w },
    { "vx", velocity.x }, { "vy", velocity.y }, { "vz", velocity.z }
};
```
*— `NeonSumoMessageFactory.CreatePlayerStateUpdate`*

**`NeonSumoMessageHandler` (`OnMessage`):** `Handle` parses the payload to a dictionary (`Dictionary<string, object>`, JSON `string`, `JObject`, or serialize-then-deserialize), looks up `action`, dispatches to a handler, logs **unknown** actions **once**, and catches parse/handler exceptions. **`HandlePlayerInput`** runs only when **`IsMasterClient`** is true and forwards to **`HostInputSync.ReceivePlayerInput`**. **`HandlePlayerStateUpdate`** returns immediately on the host (same physics state is local).

**Clients apply state** — build a compact `posData` map (only keys present on the wire) and pass it to **`NeonSumoPlayer.UpdateRemotePosition`**:

```csharp
private void HandlePlayerStateUpdate(IReadOnlyDictionary<string, object> dict)
{
    if (_ctx.IsMasterClient?.Invoke() == true)
        return;

    if (!TryGetString(dict, "userId", out var userId))
        return;

    var player = _ctx.GetPlayer?.Invoke(userId);
    if (player == null)
        return;

    var posData = new Dictionary<string, object>();
    CopyIfPresent(dict, posData, "x");
    CopyIfPresent(dict, posData, "y");
    CopyIfPresent(dict, posData, "z");
    CopyIfPresent(dict, posData, "rotY");
    CopyIfPresent(dict, posData, "qx");
    CopyIfPresent(dict, posData, "qy");
    CopyIfPresent(dict, posData, "qz");
    CopyIfPresent(dict, posData, "qw");
    CopyIfPresent(dict, posData, "vx");
    CopyIfPresent(dict, posData, "vy");
    CopyIfPresent(dict, posData, "vz");

    player.UpdateRemotePosition(posData);
}
```
*— `NeonSumoMessageHandler.HandlePlayerStateUpdate`*

Register message handlers (action keys match **`NeonSumoMessageActions`**):

```csharp
_handlers = new Dictionary<string, Action<IReadOnlyDictionary<string, object>>>
{
    [NeonSumoMessageActions.PlayerInput] = HandlePlayerInput,
    [NeonSumoMessageActions.PlayerStateUpdate] = HandlePlayerStateUpdate,
};
```
*— `NeonSumoMessageHandler` constructor*

**Physics authority:** When **`NeonSumoGameManager`** reports the host, that machine runs **simulated** rigidbodies for everyone; other clients keep remote avatars **kinematic** and **lerp** toward received state in **`FixedUpdate`** (`TickRemoteInterpolation`). On the **host**, **`NeonSumoPlayer.FixedUpdate`** still applies speed caps, edge handling, fall elimination, and hit-pause / knockback gates—not only forces from **`ApplyInputFromHost`**.

```csharp
public void ApplyAuthority(bool isHost)
{
    if (_rb == null) return;

    if (_gameManager != null && _gameManager.CurrentState != NeonSumoGameState.Playing)
    {
        _isInterpolating = false;
        return;
    }

    _isInterpolating = !isHost;
    if (isHost)
    {
        _rb.isKinematic = false;
        _rb.WakeUp();
    }
    else
    {
        _rb.isKinematic = true;
    }
}
```
*— `NeonSumoPlayer.ApplyAuthority` — kinematic/interpolation toggling applies in **`Playing`** only*

**Remote snapshots:** `UpdateRemotePosition` parses **`Dictionary`** or **`JObject`** payloads via **`ParsePositionDataToSnapshot`** (rotation prefers valid **`qx`–`qw`**; otherwise **`rotY`**), then **`ApplyRemoteState`** sets interpolation targets. Typed snapshots can call **`ApplyRemoteState`** directly.

```csharp
public void UpdateRemotePosition(object positionData)
{
    try
    {
        var snapshot = ParsePositionDataToSnapshot(positionData);
        if (snapshot.HasValue)
            ApplyRemoteState(snapshot.Value);
    }
    catch (System.Exception ex)
    {
        DebugLogger.LogError($"[NeonSumoPlayer] Error updating remote position: {ex.Message}");
    }
}
```
*— `NeonSumoPlayer.UpdateRemotePosition` → `ApplyRemoteState`*

---

### Phase 7: Discrete Gameplay Events (ActionSync)

**Problem:** Position streams are wrong for eliminations, boosts, spawns, and UI phases.

**SDK solution:** Use `ActionSync.Competition(actionName, actionMsg, actionId)` for discrete events. Action name constants:

```csharp
public const string Eliminate = "eliminate";
public const string RingDrop = "ring_drop";
public const string RampsRetract = "ramps_retract";
public const string UnlockControls = "unlock_controls";
public const string RoundWin = "round_win";
public const string PlayerSpawnSync = "player_spawn_sync";
public const string SpawnAck = "spawn_ack";
public const string StartIntroPhase = "start_intro_phase";
```
*— `NeonSumoNetEvents` — action name constants*

**Elimination** (master-only):

```csharp
string eliminationId = Guid.NewGuid().ToString();
_multiplayerClient.ActionSync.Competition(NeonSumoNetEvents.Eliminate, userId, eliminationId);
```
*— `NeonSumoGameManager` — `NeonSumoNetEvents.Eliminate`*

**Round win** (master-only):

```csharp
_multiplayerClient.ActionSync.Competition(NeonSumoNetEvents.RoundWin, actionMsg, Guid.NewGuid().ToString());
```
*— `NeonSumoGameManager` — `NeonSumoNetEvents.RoundWin`*

**Ring drop** (arena shrink, master-only):

```csharp
_multiplayerClient.ActionSync.Competition(NeonSumoNetEvents.RingDrop, ringIndex.ToString(), Guid.NewGuid().ToString());
```
*— `NeonSumoArena` — `NeonSumoNetEvents.RingDrop`*

**Unlock controls:**

```csharp
_multiplayerClient.ActionSync.Competition(NeonSumoNetEvents.UnlockControls, actionMsg, Guid.NewGuid().ToString());
```
*— `NeonSumoGameManager` — `NeonSumoNetEvents.UnlockControls`*

**Subscribing to ActionSync:** Use a router that parses payloads and forwards to handlers:

```csharp
public void Setup(MultiplayerClient client, string localPlayerId, Action<ParsedActionSyncEvent> handler)
{
    _client = client;
    _localPlayerId = localPlayerId;
    _handler = handler;
    if (_client?.ActionSync != null)
    {
        _client.ActionSync.OnCompetition += HandleRawEvent;
    }
}
```
*— `NeonSumoNetworkEventRouter.Setup`*

**Self-filtering:** For actions where the sender already applied the effect locally (e.g. `eliminate`, `round_win`), skip the event when `userId == _localPlayerId`:

```csharp
private static readonly HashSet<string> IgnoreSelfActions = new HashSet<string>
{
    NeonSumoNetEvents.Eliminate,
    NeonSumoNetEvents.RoundWin,
    NeonSumoNetEvents.PlayerColor,
    NeonSumoNetEvents.SpawnAck,
    NeonSumoNetEvents.StartIntroPhase,
    // ...
};
```
*— `NeonSumoNetworkEventRouter.IgnoreSelfActions`*

---

### Phase 8: Round End & Leaderboard

**Problem:** Track wins and persist scores through the SDK.

**SDK solution:** The master broadcasts `round_win` via ActionSync (see Phase 7). When the local player wins, update the leaderboard:

```csharp
_localWins++;
_multiplayerClient.Leaderboard.LeaderboardUpdate(_localWins);
```
*— `NeonSumoGameManager` — `LeaderboardModule.LeaderboardUpdate`*

---

### Phase 9: Teardown & Cleanup

When leaving the gameplay scene, disconnect in `OnDestroy`:

```csharp
private void OnDestroy()
{
    _multiplayerClient?.Disconnect();
}
```
*— `NeonSumoMatchmakingFlow.OnDestroy` — `Disconnect`*

To fully destroy SDK clients:

```csharp
PlaySdkRuntimeRoot.Instance.DestroyMultiplayerClient();
PlaySdkRuntimeRoot.Instance.DestroyMatchmakingClient();
```
*— `PlaySdkRuntimeRoot.DestroyMultiplayerClient`, `DestroyMatchmakingClient`*

---

## Project Structure

```
Assets/Scripts/NeonSumo/
├── NeonSumoGameManager.cs      # Main game controller & SDK integration
├── NeonSumoPlayer.cs           # Player physics, Input System, host sim vs remote interpolation
├── NeonSumoPlayerOrder.cs      # Host-first user id ordering (roster, UI, spawn-related logic)
├── NeonSumoArena.cs            # Arena setup & spawn logic
├── NeonSumoRamps.cs            # Ramp retract (procedural lerp or Animator); reset; OnRetracted
├── NeonSumoRampUnlockTrigger.cs # Optional trigger → IRampUnlockHandler
├── NeonSumoUIManager.cs        # Facade: GameManager → presenters; binds views (no logic in views)
├── NeonSumoMatchmakingFlow.cs  # Matchmaking → Multiplayer transition
├── NeonSumoLobbyService.cs     # Lobby SDK calls (CreateRoom, JoinRoom, etc.)
├── NeonSumoLobbyManager.cs     # Wires NeonSumoLobbyPanelView to NeonSumoLobbyService; handoff to gameplay
├── MatchmakingHandoffStore.cs  # Persists room/session IDs across scenes
├── ActionSyncEventHandler.cs  # Handles ActionSync events (elimination, spawn, etc.)
├── HostInputSync.cs           # Host-authoritative input sync & state broadcast
├── NeonSumoNetworkEventRouter.cs # ActionSync subscription & payload parsing
├── MasterSyncHandler.cs       # master_notify: host id, backfill, arena, authority; master-only colors & ramp phase
├── SpawnCoordinator.cs        # SpawnAck gate, pending ramp spawns, PlayerSpawnSync build/broadcast, indices
├── RingShrinkHandler.cs       # Master-only ring drops
├── NeonSumoMessageHandler.cs  # OnMessage: parse payloads; input (host-only); state (non-host)
├── NeonSumoMessageFactory.cs  # Payload builders (player_input, player_state_update)
├── NeonSumoNetEvents.cs       # ActionSync action name constants
├── NeonSumoMessageActions.cs  # General message action constants
├── PlayerColorManager.cs      # 4-color palette, assignments, ApplyAssignedColor, ActionSync PlayerColor
├── UIFlowController.cs        # Panel visibility flow
├── HudPresenter.cs            # Timer, error display
├── PlayerListPresenter.cs     # Lobby list on MainMenuPanelView; host-first Player N labels; ready / wins
├── PlayerBarCoordinator.cs    # In-game fusion player bar (max 4 slots), placements, host-first roster
├── CountdownController.cs     # 3-2-1-Go animations
├── WinnerPresenter.cs         # Winner panel animations
└── PlaySdkRuntimeRoot.cs      # Singleton for MatchmakingClient / MultiplayerClient
```

### `Assets/UI/` (UI Toolkit)

```
Assets/UI/
├── NeonSumoLobbyPanel.uxml       # Lobby layout; references NeonSumoLobbyPanel.uss
├── NeonSumoLobbyPanel.uss        # Lobby visual styling
├── NeonSumoLobbyPanelView.cs    # Passive lobby view; binds UXML; raises Refresh/Create/Join/Enter/Leave
├── MainMenuPanelView.cs         # In-game-ready flow: player rows, ready UI (NeonSumoUIManager / PlayerListPresenter)
├── PlayerBarFusionModernView.cs # HUD fusion bar: slots p1–p4, PlayerBarData (driven by PlayerBarCoordinator)
```

### `Assets/Scripts/PlaySDK/` (excerpt)

```
Assets/Scripts/PlaySDK/
├── MatchmakingClient.cs   # Lobby: rooms, actor, events; WebGL JS vs websocket stack
├── MultiplayerClient.cs   # Game room, modules, SendMessage; WebGL JS vs proxy client
│   …                      # GeneralModule, GameModule, Config, Services, etc.
```

---

## SDK Modules Quick Reference

| Module | Purpose | When to Use |
|--------|---------|-------------|
| **MatchmakingClient** | Lobby, rooms, create/join | Before entering game room |
| **MultiplayerClient** | Connection, presence, message routing | Core client in gameplay |
| **GameModule** | Lifecycle, ready, countdown, timer, restart | Server-driven game flow |
| **NetworkSyncModule** | Position/entity sync | Alternative to General messages for positions |
| **ActionSyncModule** | Discrete events (eliminate, boost, win) | One-off gameplay events |
| **LeaderboardModule** | Score persistence | Win tracking, rankings |
| **GeneralModule** (`SendMessage` / `OnMessage`) | Custom P2P-style messages | Host input sync, state snapshots |

---

## Configuration

Create a `NeonSumoConfig` asset and assign it to lobby and gameplay components:

- **AppId** — Application ID for matchmaking/multiplayer
- **MaxPlayers** — 4 (for 2–4 player games)
- **MinPlayers** — 2

Game module parameters — including `ready_time`, `start_delay_time`, `play_time`, **`change_second`**, `total_player`, `min_total_player`, `max_total_player`, and `wait_player_timeout` — are set in **`NeonSumoMatchmakingFlow.BuildInitOptions`** (`NeonSumoConfig` values).

---

## Setup Instructions

1. **Set App ID** — On `Assets/NeonSumo/Config/NeonSumoConfig.asset`, replace the placeholder App ID with your VIVERSE Studio App ID.
2. **Lobby scene** — Use a scene such as **`NeonSumoLobbyScene.unity`**: **`UIDocument`** source asset = **`NeonSumoLobbyPanel.uxml`**, **`NeonSumoLobbyPanelView`** on the same GameObject (or wired), and **`NeonSumoLobbyManager`** pointing at that view plus config / **`NeonSumoLobbyService`**.
3. **Gameplay scene** — Build **`NeonSumoScene.unity`** with **`NeonSumoGameManager`**, **`NeonSumoArena`**, **`NeonSumoUIManager`**, **`NeonSumoMatchmakingFlow`**. Assign **`PlayerBarFusionModernView`** on a **`UIDocument`** when using the fusion HUD bar.
4. **Create player prefab** — Object with `Rigidbody` and `NeonSumoPlayer` (sphere or mesh, e.g. ball FBX). Assign **`bodyRenderer`** for tinting, or rely on **`EnsureBodyRenderer`** (auto-picks PlasmaBars material, skinned mesh, or mesh child).
5. **Assign references** — Link all references in the GameManager (and lobby manager) inspectors.

---

## Architecture Patterns

- **ActionSync** for discrete events (elimination, boost, round win)
- **GeneralModule** `SendMessage` for host-authoritative input and state
- **GameModule** as authoritative source for timing and lifecycle
- **Master client** handles game state decisions
- **`NeonSumoUIManager`** — game code calls the manager only; it composes **presenters** (`HudPresenter`, `PlayerListPresenter`, `CountdownController`, `WinnerPresenter`, …) and **UI Toolkit** view assets under **`Assets/UI/`** (`MainMenuPanelView`, panel UXML/USS as assigned). Logs a warning if multiple `BaseInputModule` instances exist (WebGL expects one).
- **Lobby** — separate **`NeonSumoLobbyPanel*`** UITK assets + **`NeonSumoLobbyPanelView`**; gameplay HUD bar = **`PlayerBarFusionModernView`** (four slots **`p1`–`p4`**).
- **`PlayerColorManager`** — palette + slot assignments, body/UI tint, **`PlayerColor`** broadcasts over ActionSync
- **`NeonSumoPlayerOrder`** for consistent host-first ordering across UI and gameplay
- **Local player** — full physics; **remote players** — kinematic with position interpolation

---

## Debugging

Connection details such as PeerId, RoomId, and master status are logged on initialization. App IDs and auth tokens are not. Check the Unity Console for `[NeonSumoFlow]` and `[NeonSumo]` messages.

---

## Performance Notes

- State broadcast runs at ~20 Hz (`HostInputSync` — `StateBroadcastInterval`).
- Recommended max players for WebGL: 2–4 (as configured).

---

**Built with Viverse Multiplayer SDK**
