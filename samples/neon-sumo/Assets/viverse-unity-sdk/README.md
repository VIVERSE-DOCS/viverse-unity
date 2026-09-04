# VIVERSE Unity SDK

Unity SDK for integrating VIVERSE platform services into WebGL and Editor builds.

## Features

| Feature | Status | Description |
|---------|--------|-------------|
| Authentication | Ready | OAuth2 login via VIVERSE account |
| Lambda | Ready | Invoke server-side functions |
| Matchmaking | Ready | Room creation, discovery, and joining |
| Multiplayer | Ready | WebRTC data channels + real-time modules (Game, NetworkSync, ActionSync, Leaderboard, General) |
| Cloud Save | Ready | Persistent player data storage |
| Leaderboard | Ready | Score submission and rankings (REST + encryption) |
| Achievements | Ready | Fetch and unlock achievements (REST + encryption) |
| Avatar | Ready | User profile, avatar list, VRM model loading |

## Quick Start

### 1. Setup

1. Import `viverse-unity-sdk` into your Unity project
2. Get an **App ID** from [VIVERSE Studio](https://studio.viverse.com/)
3. Add a GameObject with `AuthManager` component to your scene
4. Set the App ID in the AuthManager inspector (or pass to `Initialize()`)

### 2. Authentication

```csharp
using ViverseSDK;

public class MyGame : MonoBehaviour
{
    void Start()
    {
        var auth = AuthManager.Instance;
        auth.OnLoginSuccess += (result) => {
            Debug.Log($"Logged in: {result.account_id}");
        };
        auth.Initialize("YOUR_APP_ID");
    }

    public void OnLoginClicked()
    {
        AuthManager.Instance.Login();
    }

    public void OnLogoutClicked()
    {
        AuthManager.Instance.Logout();
    }
}
```

### 3. Lambda (Server-Side Functions)

```csharp
using ViverseSDK;

private LambdaClient _lambda;

void Start()
{
    _lambda = new LambdaClient("YOUR_APP_ID");
}

async void InvokeFunction()
{
    var result = await _lambda.Invoke(
        "your_event_name",
        "{\"key\": \"value\"}",
        AuthManager.Instance.AccessToken
    );

    if (result.success)
        Debug.Log($"Result: {result.result}");
    else
        Debug.LogError($"Error: {result.error}");
}
```

### 4. Matchmaking

```csharp
using ViverseSDK;

// MatchmakingClient is a MonoBehaviour singleton
var client = MatchmakingClient.Instance;
if (client == null)
{
    var go = new GameObject("[MatchmakingClient]");
    client = go.AddComponent<MatchmakingClient>();
}

// Subscribe to events BEFORE Initialize
client.OnConnect += () => {
    // Set actor after connection (required before room operations)
    var payload = "{\"properties\":{\"name\":\"Player1\"}}";
    client.SendRequest("SetActor", payload, null);
};
client.OnJoinRoom += (json) => Debug.Log($"Joined: {json}");
client.OnRoomListUpdate += (json) => Debug.Log($"Rooms: {json}");

// Connect (session_id auto-generated internally)
client.Initialize("YOUR_APP_ID", debugMode: true);

// After SetActor succeeds, do room operations:
client.SendRequest("GetAvailableRooms", "{}", (resp) => { /* parse rooms */ });
client.SendRequest("CreateRoom", "{\"room_name\":\"MyRoom\",\"max_players\":4,\"min_players\":2}", null);
client.SendRequest("JoinRoom", "{\"room_id\":\"uuid-here\"}", null);
client.SendRequest("LeaveRoom", "{}", null);
```

### 5. Multiplayer (WebRTC + Real-Time Modules)

After joining a room via matchmaking, hand off the room ID to `MultiplayerClient` for WebRTC data-channel sync. Five sub-modules are exposed as properties on the client:

```csharp
using ViverseSDK;

// Create MultiplayerClient (MonoBehaviour)
var go = new GameObject("[MultiplayerClient]");
var mp = go.AddComponent<MultiplayerClient>();

mp.OnConnected += () => Debug.Log("[MP] Connected");
mp.OnDisconnected += () => Debug.Log("[MP] Disconnected");

// Step 1: Initialize with room from matchmaking
string roomId = /* from MatchmakingClient JoinRoom response */;
string sessionId = System.Guid.NewGuid().ToString();
await mp.Initialize(roomId, "YOUR_APP_ID", sessionId);

// Step 2: Enable modules
var options = new MultiplayerInitOptions {
    modules = new ModulesConfig {
        game        = new ModuleOption { enabled = true, play_time = 60, total_player = 4 },
        networkSync = new ModuleOption { enabled = true },
        actionSync  = new ModuleOption { enabled = true },
        leaderboard = new ModuleOption { enabled = true },
    }
};
await mp.Init(options);

// Step 3: Subscribe to module events
mp.Game.OnPlayerAllReady   += (json) => Debug.Log($"All ready: {json}");
mp.Game.OnCountdownToStart += (json) => Debug.Log($"Start in: {json}");
mp.NetworkSync.OnNotifyPositionUpdate += (json) => { /* apply peer position */ };
mp.ActionSync.OnCompetition           += (json) => { /* handle peer action */ };
mp.Leaderboard.OnLeaderboardUpdate    += (json) => { /* update scoreboard UI */ };
mp.General.OnMessage                  += (json) => { /* handle chat / custom */ };

// Step 4: Send data
mp.Game.Ready();                                          // I'm ready
mp.Game.TriggerGameStart();                               // Start countdown
mp.NetworkSync.UpdateMyPosition("{\"x\":1,\"y\":0,\"z\":2}");   // Every frame
mp.ActionSync.Competition("attack", "sword_slash",              // On button press
    System.Guid.NewGuid().ToString().Substring(0, 8));
mp.Leaderboard.LeaderboardUpdate(150);                    // Score changed
mp.General.SendMessage("{\"chat\":\"gg\"}");              // Chat / custom
```

**Module reference (all send `void` synchronously; events are `Action<string>` JSON):**

| Module | Purpose | Key Methods | Key Events |
|--------|---------|-------------|------------|
| `Game` | Lifecycle & timing | `Ready()`, `TriggerGameStart()`, `TriggerGameEnd()`, `TriggerGameRestart()` | `OnMasterNotify`, `OnPlayerAllReady`, `OnCountdownToStart/End`, `OnGameEnd`, `OnGameRestart` |
| `NetworkSync` | Continuous transform sync | `UpdateMyPosition(json)`, `UpdateEntityPosition(entityId, json)` | `OnNotifyPositionUpdate`, `OnNotifyRemove` |
| `ActionSync` | Discrete action broadcasts | `Competition(name, msg, id)` | `OnCompetition` |
| `Leaderboard` | Real-time in-room scores | `LeaderboardUpdate(int score)` | `OnLeaderboardUpdate` |
| `General` | Freeform peer messages | `SendMessage(json)` | `OnMessage`, `OnClientConnected`, `OnClientDisconnected` |

> **NetworkSync vs ActionSync:** NetworkSync is for **continuous** per-frame updates (position, rotation). ActionSync is for **discrete** triggered events (attacks, skills, emotes) with `action_id` de-duplication.

> **Leaderboard Module vs `LeaderboardClient`:** The module broadcasts scores live within a multiplayer room (WebRTC, ephemeral). `LeaderboardClient` (see §7) persists scores to the VIVERSE backend (REST + encryption). Use both together for competitive games: module for live in-room ranking, REST for global historical rankings.

### 6. Cloud Save

```csharp
using ViverseSDK;

private CloudSaveClient _cloudSave;

void Start()
{
    _cloudSave = new CloudSaveClient("YOUR_APP_ID");
}

// Versioned saves
async void SaveGame()
{
    string token = AuthManager.Instance.AccessToken;
    var result = await _cloudSave.Save("{\"level\": 5, \"score\": 1200}", token);
    if (result.success) Debug.Log("Saved!");
}

async void LoadGame()
{
    string token = AuthManager.Instance.AccessToken;
    var result = await _cloudSave.GetLatest(token);
    if (result.success) Debug.Log($"Data: {result.data}");
}

// Key-value storage
async void SetCoins(int amount)
{
    string token = AuthManager.Instance.AccessToken;
    await _cloudSave.SetPlayerData("coins", amount.ToString(), token);
}

async void GetCoins()
{
    string token = AuthManager.Instance.AccessToken;
    var result = await _cloudSave.GetPlayerData("coins", token);
    if (result.success) Debug.Log($"Coins: {result.data}");
}
```

### 7. Leaderboard

```csharp
using ViverseSDK;

private LeaderboardClient _leaderboard;

void Start()
{
    _leaderboard = new LeaderboardClient("YOUR_APP_ID");
}

// Submit a score (encrypted via RSA + AES)
async void SubmitScore()
{
    string token = AuthManager.Instance.AccessToken;
    var result = await _leaderboard.SubmitScore("your_meta_name", "100", token);
    if (result.success) Debug.Log("Score submitted!");
}

// Get rankings (authenticated — shows user's rank)
async void GetRanking()
{
    string token = AuthManager.Instance.AccessToken;
    var result = await _leaderboard.GetLeaderboard("your_meta_name", token);
    if (result.success) Debug.Log($"Rankings: {result.data}");
}

// Get rankings (no auth required — guest view)
async void GetGuestRanking()
{
    var result = await _leaderboard.GetGuestLeaderboard("your_meta_name");
    if (result.success) Debug.Log($"Guest rankings: {result.data}");
}
```

> **Important:** You must create the leaderboard in [VIVERSE Studio](https://studio.viverse.com/) first.
> The `metaName` parameter must match exactly what you configured in Studio.
> Score behavior (accumulate vs best) and sort order are configured server-side.

### WebGL Leaderboard Notes

In WebGL, leaderboard API calls go through `viveport.com` which does **not** support CORS for localhost. The jslib uses environment-aware URLs:
- **Localhost**: relative URLs (`/`) + the included `serve_webgl.sh` proxies `/api/vrleaderboard/*`, `/api/ironhide/*`, `/api/optimusprime/*`
- **Deployed (VIVERSE hosting)**: absolute URL (`https://www.viveport.com/`) since the iframe origin (`*.world.viverse.app`) cannot proxy API traffic

Rankings (GetRanking/GetGuestRanking) use viverse-sdk `GameDashboard.getLeaderboard()` which automatically maps community display names — the raw REST API returns stale names from submission time.

### 8. Achievements

```csharp
using ViverseSDK;

private AchievementsClient _achievements;

void Start()
{
    _achievements = new AchievementsClient("YOUR_APP_ID");
}

// Get all achievements for the current user (locked + unlocked)
async void GetAchievements()
{
    string token = AuthManager.Instance.AccessToken;
    var result = await _achievements.GetUserAchievements(token);
    if (result.success) Debug.Log($"Achievements: {result.data}");
    else                Debug.LogError($"Error: {result.error}");
}

// Unlock one or more achievements (encrypted via RSA + AES)
async void UnlockAchievement()
{
    string token = AuthManager.Instance.AccessToken;
    // Pass a JSON array of {"api_name": "...", "unlock": true} objects.
    string payload = "[{\"api_name\":\"first_win\",\"unlock\":true}]";
    var result = await _achievements.UnlockAchievements(payload, token);
    if (result.success) Debug.Log("Achievement unlocked!");
    else                Debug.LogError($"Error: {result.error}");
}

// Batch unlock — send multiple achievements in one call
async void UnlockMany()
{
    string token = AuthManager.Instance.AccessToken;
    string payload =
        "[{\"api_name\":\"first_win\",\"unlock\":true}," +
        " {\"api_name\":\"score_1000\",\"unlock\":true}]";
    await _achievements.UnlockAchievements(payload, token);
}
```

> **Important:** You must register each achievement in [VIVERSE Studio](https://studio.viverse.com/) first.
> The `api_name` field must match exactly what you configured in Studio.
> Setting `unlock: false` is accepted by the API but has no effect — achievements are one-way.

### WebGL Achievements Notes

Same environment-aware URL strategy as Leaderboard:
- **Localhost**: relative URLs (`/`) proxied by `serve_webgl.sh` (`/api/optimusprime/*`, `/api/ironhide/*`)
- **Deployed (VIVERSE hosting)**: viverse-sdk's `GameDashboard.uploadUserAchievement()` which handles the RSA/AES encryption internally

The Editor path re-implements the same crypto flow (RSA key exchange via Ironhide → AES-encrypted POST) using `System.Security.Cryptography`, so both platforms have identical behavior.

### 9. Avatar

```csharp
using ViverseSDK;

private AvatarClient _avatar;

void Start()
{
    _avatar = new AvatarClient();
}

// Get user profile (requires auth)
async void GetProfile()
{
    string token = AuthManager.Instance.AccessToken;
    var result = await _avatar.GetProfile(token);
    if (result.success) Debug.Log($"Profile: {result.data}");
}

// Get user's avatar list (requires auth)
async void GetAvatarList()
{
    string token = AuthManager.Instance.AccessToken;
    var result = await _avatar.GetAvatarList(token);
    if (result.success) Debug.Log($"Avatars: {result.data}");
}

// Get active avatar and download VRM
async void DownloadActiveVrm()
{
    string token = AuthManager.Instance.AccessToken;
    var active = await _avatar.GetActiveAvatar(token);
    if (!active.success) return;

    // Parse VRM URL from avatar data
    string vrmUrl = /* extract VrmBinaryDataUrl from active.data */;
    var dlResult = await _avatar.DownloadAvatarFile(vrmUrl, token);
    if (dlResult.success && dlResult.data != null)
    {
        // Load VRM using VrmAvatarController (see below)
        var controller = GetComponent<VrmAvatarController>();
        await controller.LoadVrmFromBytes(dlResult.data);
    }
}

// Public avatars (no auth required)
async void GetPublicAvatars()
{
    var result = await _avatar.GetPublicAvatarList();
    if (result.success) Debug.Log($"Public avatars: {result.data}");
}
```

#### VRM Model Loading (Optional Dependency)

VRM loading is handled by `VrmAvatarController` (a separate MonoBehaviour that depends on UniVRM10):

```csharp
// Add VrmAvatarController to your GameObject
var controller = gameObject.AddComponent<VrmAvatarController>();
controller.OnVrmLoaded += () => Debug.Log("Avatar loaded!");
controller.OnVrmLoadFailed += (err) => Debug.LogError(err);

// Load VRM from downloaded bytes
await controller.LoadVrmFromBytes(vrmBytes);

// Built-in controls: WASD = move, 1-5 = expressions, 0 = reset
```

> **UniVRM is an optional dependency.** The SDK ships **without** UniVRM and compiles cleanly whether or not you install it.
>
> - **Without UniVRM**: SDK compiles with zero errors. `AvatarClient.DownloadAvatarFile()` still works and returns raw VRM bytes. `VrmAvatarController` is unavailable (the entire `Sample/VRM/` assembly is skipped) and the sample runner logs a runtime warning telling you how to enable rendering.
> - **With UniVRM installed**: Unity auto-detects the `com.vrmc.vrm` package (via `versionDefines` in `Sample/Viverse.SDK.Sample.asmdef`) and enables `VrmAvatarController` automatically — no manual configuration required.
>
> **To install UniVRM**, add both packages to `Packages/manifest.json`:
> ```json
> "com.vrmc.gltf": "https://github.com/vrm-c/UniVRM.git?path=/Assets/UniGLTF#v0.130.1",
> "com.vrmc.vrm": "https://github.com/vrm-c/UniVRM.git?path=/Assets/VRM10#v0.130.1"
> ```
> Unity will recompile and `VrmAvatarController` will activate on the next Editor refresh.

#### WebGL Avatar Notes

- VRM download uses a **3-tier strategy**: viverse-sdk `Avatar.getAvatarFileWithSDK()` → Avatar SDK `viaWorker({action:'downloadAndDecrypt'})` → plain fetch fallback
- Avatar SDK global is `globalThis.newViveAvatarSdk` (factory function, NOT a constructor on `window.AvatarSDK`)
- Binary bytes are transferred from JS to C# via memory copy (pinned GCHandle)
- For proper MToon materials, add VRM shaders to **Project Settings → Graphics → Always Included Shaders**
- Works on VIVERSE hosting (iframe), localhost proxy, and direct access without configuration changes

## Platform Support

| Platform | Transport | Notes |
|----------|-----------|-------|
| WebGL | viverse-sdk UMD (CDN) | Production target |
| Unity Editor | Native C# (REST + WebSocket) | Development/testing |

## Architecture

The SDK uses a **dual-path architecture**:
- **WebGL builds**: C# calls into `.jslib` plugins which use the viverse-sdk JavaScript library loaded from CDN
- **Editor**: C# makes direct HTTP/WebSocket calls for the same functionality

C# is a **thin interface** — all business logic lives in the JavaScript SDK (WebGL) or minimal REST calls (Editor).

## WebGL Build Setup

**No custom WebGL template required.** The SDK ships with an editor build hook
(`Editor/ViverseSDKBuildProcessor.cs`) that automatically injects the viverse-sdk
loader script into `index.html` after every WebGL build. It works with Unity's
**Default** template out of the box.

### How auto-injection works

1. You build for WebGL (Unity's default template is fine).
2. After the build completes, Unity invokes the SDK's `[PostProcessBuild]` callback.
3. The callback opens the generated `index.html` and inserts:
   ```html
   <!-- viverse-unity-sdk:auto-injected -->
   <script src="https://www.viverse.com/static-assets/viverse-sdk/index.umd.cjs"></script>
   ```
   immediately before Unity's `<script src="Build/...">` loader.
4. The injection is idempotent — rebuilds do not double-inject. If your custom
   template already contains a `viverse-sdk` script, the hook skips injection.

### Using a custom template

If you maintain your own WebGL template, you have two options:

- **Let auto-injection handle it** — write your template without a viverse-sdk
  `<script>`; the build hook injects it for you.
- **Pin your own SDK version** — put your own `<script src="..."> ` in your
  template. The hook detects it (any `<script>` with `viverse-sdk` in the URL)
  and skips auto-injection.

### Local WebGL Testing

The SDK ships with `serve_webgl.sh` (next to this README). It serves a Unity
WebGL build on **`http://localhost:40078`** (the OAuth redirect URI registered
with the SDK's shared dev OAuth client) and reverse-proxies VIVERSE backends so
authenticated requests work from `localhost` without CORS errors.

```bash
# Make sure it's executable (first time only):
chmod +x Assets/viverse-unity-sdk/serve_webgl.sh

# From your project root:
./Assets/viverse-unity-sdk/serve_webgl.sh                # serves ./Build
./Assets/viverse-unity-sdk/serve_webgl.sh ./MyBuild      # custom build folder
./Assets/viverse-unity-sdk/serve_webgl.sh -h             # show all options
```

| Local request                  | Proxied to                                |
|--------------------------------|-------------------------------------------|
| `/api/vrleaderboard/*`         | `https://www.viveport.com/api/vrleaderboard/*` |
| `/api/ironhide/*`              | `https://www.viveport.com/api/ironhide/*`      |
| `/api/optimusprime/*`          | `https://www.viveport.com/api/optimusprime/*`  |
| `/api/avatar/*`                | `https://sdk-api.viverse.com/*`                |
| `/api/avatar-files/*`          | `https://avatar.viverse.com/*`                 |

**Requirements:** `bash`, `python3` (≥ 3.7), `lsof`.

**Why port 40078?** The shared dev OAuth client used for local testing only
accepts `http://localhost:40078` as a redirect URI. If you want a different
port, register your own OAuth client in VIVERSE Studio and configure the SDK
to use it (see `Runtime/AuthHttpServer.cs` and `Plugins/JSLib/ViverseAuth.jslib`).

**Production (deployed on VIVERSE):** no proxy needed. The deployed build runs
in an iframe under `*.world.viverse.app` and uses absolute API URLs directly.

## API Reference

### AuthManager

| Member | Type | Description |
|--------|------|-------------|
| `Instance` | static property | Singleton access |
| `IsLoggedIn` | bool | Whether user has valid token |
| `AccessToken` | string | Current JWT access token |
| `AccountId` | string | User's account UUID |
| `OnLoginSuccess` | event | Fired with AuthResult on login |
| `OnLogout` | event | Fired on logout |
| `OnError` | event | Fired with error message |
| `Initialize(appId)` | method | Initialize SDK with App ID |
| `Login()` | method | Start login flow |
| `Logout()` | method | Clear session and tokens |

### LambdaClient

| Member | Type | Description |
|--------|------|-------------|
| `LambdaClient(appId)` | constructor | Create client with App ID |
| `Invoke(eventName, dataJson, token, ct)` | async method | Invoke Lambda function |

### LambdaResult

| Field | Type | Description |
|-------|------|-------------|
| `success` | bool | Whether invocation succeeded |
| `status` | string | `"succeeded"`, `"failed"`, or `"timeout"` |
| `result` | string | JSON result (Key/Value array format) |
| `error` | string | Error message if failed |

### CloudSaveClient

| Member | Type | Description |
|--------|------|-------------|
| `CloudSaveClient(appId)` | constructor | Create client with App ID |
| `Save(dataJson, token)` | async method | Save game data (creates new version) |
| `GetAll(token)` | async method | Get all saved versions |
| `GetLatest(token)` | async method | Get most recent save |
| `Delete(version, token)` | async method | Delete a specific version |
| `SetPlayerData(key, dataJson, token)` | async method | Set key-value pair |
| `GetPlayerData(key, token)` | async method | Get value by key |

### CloudSaveResult

| Field | Type | Description |
|-------|------|-------------|
| `success` | bool | Whether operation succeeded |
| `data` | string | JSON response data (null for write ops) |
| `error` | string | Error message if failed |

### LeaderboardClient

| Member | Type | Description |
|--------|------|-------------|
| `LeaderboardClient(appId)` | constructor | Create client with App ID |
| `GetLeaderboard(metaName, token, ...)` | async method | Get ranking (authenticated) |
| `GetGuestLeaderboard(metaName, ...)` | async method | Get ranking (no auth) |
| `SubmitScore(metaName, value, token)` | async method | Submit score (RSA/AES encrypted) |

### LeaderboardResult

| Field | Type | Description |
|-------|------|-------------|
| `success` | bool | Whether operation succeeded |
| `data` | string | JSON rankings (null for submit) |
| `error` | string | Error message if failed |

### AchievementsClient

| Member | Type | Description |
|--------|------|-------------|
| `AchievementsClient(appId)` | constructor | Create client with App ID |
| `GetUserAchievements(token, ct)` | async method | Get all achievements + unlock status |
| `UnlockAchievements(achievementsJson, token, ct)` | async method | Unlock achievements (RSA/AES encrypted). `achievementsJson` is a JSON array: `[{"api_name":"x","unlock":true}]` |

### AchievementResult

| Field | Type | Description |
|-------|------|-------------|
| `success` | bool | Whether operation succeeded |
| `data` | string | JSON response data (null for unlock) |
| `error` | string | Error message if failed |

### AvatarClient

| Member | Type | Description |
|--------|------|-------------|
| `AvatarClient()` | constructor | Create client (no App ID needed) |
| `GetProfile(token)` | async method | Get user profile |
| `GetAvatarList(token)` | async method | Get user's avatar list |
| `GetActiveAvatar(token)` | async method | Get currently active avatar |
| `GetPublicAvatarList()` | async method | Get public avatar catalog (no auth) |
| `GetPublicAvatarByID(avatarId)` | async method | Get public avatar by ID (no auth) |
| `DownloadAvatarFile(vrmUrl, token)` | async method | Download VRM binary |

### AvatarResult

| Field | Type | Description |
|-------|------|-------------|
| `success` | bool | Whether operation succeeded |
| `data` | string | JSON response data |
| `error` | string | Error message if failed |

### AvatarDownloadResult

| Field | Type | Description |
|-------|------|-------------|
| `success` | bool | Whether download succeeded |
| `data` | byte[] | Raw VRM bytes |
| `size` | int | Byte count |
| `error` | string | Error message if failed |

### VrmAvatarController

| Member | Type | Description |
|--------|------|-------------|
| `IsLoaded` | bool | Whether a VRM model is loaded |
| `StatusText` | string | Current status for UI display |
| `OnVrmLoaded` | event | Fired when VRM loads successfully |
| `OnVrmLoadFailed` | event | Fired with error string on failure |
| `LoadVrmFromBytes(vrmBytes)` | async method | Load and spawn VRM model |
| `DestroyVrm()` | method | Destroy current model |

## File Structure

```
Assets/viverse-unity-sdk/
├── README.md                       # This file
├── serve_webgl.sh                  # Local WebGL server + CORS proxy
├── Editor/
│   ├── Viverse.SDK.Editor.asmdef   # Editor-only assembly
│   └── ViverseSDKBuildProcessor.cs # Auto-injects viverse-sdk into built index.html
├── Runtime/
│   ├── Viverse.SDK.asmdef          # Main runtime assembly
│   ├── AuthManager.cs              # Authentication (singleton)
│   ├── AuthHttpServer.cs           # Editor OAuth server
│   ├── LambdaClient.cs             # Lambda invocation
│   ├── CloudSaveClient.cs          # Cloud Save (versioned + key-value)
│   ├── LeaderboardClient.cs        # Leaderboard (rankings + score submit)
│   ├── AchievementsClient.cs       # Achievements (fetch + unlock, RSA/AES)
│   ├── AvatarClient.cs             # Avatar (profile, list, VRM download)
│   ├── MatchmakingClient.cs        # Matchmaking client (singleton, dual-path)
│   ├── MultiplayerClient.cs        # Multiplayer (WebRTC via mediasoup)
│   ├── MultiplayerInitOptions.cs   # Config types (ModuleOption, ModulesConfig, etc.)
│   ├── MiniJson.cs                 # Internal — zero-dep JSON helper used by all clients
│   └── Modules/                    # Multiplayer sub-modules (all verified in WebGL)
│       ├── GameModule.cs           # Lifecycle: ready, start, end, restart + countdown events
│       ├── NetworkSyncModule.cs    # Position/transform sync (players + entities)
│       ├── ActionSyncModule.cs     # Discrete action broadcasts (attacks, skills)
│       ├── LeaderboardModule.cs    # Real-time in-room scores
│       ├── GeneralModule.cs        # Freeform peer messaging
│       └── LambdaModule.cs         # Lambda invocation via MultiplayerClient
├── Plugins/
│   ├── Viverse.NativeWebSocket/
│   │   ├── Viverse.NativeWebSocket.asmdef  # Vendored NativeWebSocket — renamed namespace + jslib exports to coexist with any user copy
│   │   └── WebSocket.cs
│   └── JSLib/
│       ├── ViverseAuth.jslib       # WebGL auth bridge
│       ├── ViverseLambda.jslib     # WebGL Lambda bridge
│       ├── ViverseCloudSave.jslib  # WebGL Cloud Save bridge
│       ├── ViverseLeaderboard.jslib # WebGL Leaderboard bridge
│       ├── ViverseAchievements.jslib # WebGL Achievements bridge
│       ├── ViverseAvatar.jslib     # WebGL Avatar bridge (download + decrypt)
│       └── ViversePlay.jslib       # WebGL matchmaking/multiplayer bridge
└── Sample/
    ├── Viverse.SDK.Sample.asmdef   # Sample assembly (auto-detects UniVRM)
    ├── ViverseTestRunner.cs        # Test UI for all features (VRM guarded by #if)
    ├── AuthTestRunner.cs           # Minimal auth-only demo
    ├── MatchmakingDemo.cs          # Matchmaking end-to-end demo
    ├── MultiplayerDemo.cs          # Multiplayer end-to-end demo
    └── VRM/                        # Optional — compiles only when UniVRM installed
        ├── Viverse.SDK.Sample.VRM.asmdef # defineConstraints:["VIVERSE_VRM_INSTALLED"]
        └── VrmAvatarController.cs  # VRM loading, movement, expressions (UniVRM10)
```

### Assembly Definitions & Optional UniVRM

The SDK uses Unity's Assembly Definition system to make UniVRM an **optional** dependency:

- `Sample/Viverse.SDK.Sample.asmdef` declares a `versionDefines` entry that automatically sets `VIVERSE_VRM_INSTALLED` when `com.vrmc.vrm` (v0.128.0+) is present in the project.
- `Sample/VRM/Viverse.SDK.Sample.VRM.asmdef` declares `defineConstraints:["VIVERSE_VRM_INSTALLED"]`, so the entire VRM assembly is skipped when UniVRM is absent.
- `ViverseTestRunner.cs` uses `#if VIVERSE_VRM_INSTALLED` guards to gracefully degrade — without UniVRM, it logs a warning at startup and reports "Downloaded (no renderer)" for avatar downloads.

Result: **Importing the SDK never produces compile errors** regardless of whether UniVRM is installed. If you later add UniVRM to `Packages/manifest.json`, Unity auto-recompiles and VRM features activate — no SDK reinstall needed.

## Requirements

- Unity 2021.2+ with .NET 4.x
- WebGL build support (for production)
- App ID from VIVERSE Studio
- [UniVRM v0.130.1+](https://github.com/vrm-c/UniVRM) — **optional**, only needed if you want to render downloaded VRM models. See [VRM Model Loading](#vrm-model-loading-optional-dependency) above.
