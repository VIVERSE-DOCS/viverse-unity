# Neon Sumo - Viverse Multiplayer SDK Demo

A polished, shareable multiplayer mini-game demo showcasing the Viverse Multiplayer SDK capabilities.

## 🎮 What This Demo Shows

This demo demonstrates a complete multiplayer game implementation using the Viverse Multiplayer SDK, including:

- **Matchmaking System**: Room creation, joining, player management
- **Multiplayer Presence**: Real-time player synchronization
- **Game Module**: Ready states, countdowns, game lifecycle management
- **Network Sync**: Position synchronization across players
- **Action Sync**: Boost/dash and elimination events
- **Leaderboard**: Win tracking and scoring

## 🎯 Game Concept

**Neon Sumo** is a party game inspired by Fusion Frenzy's Sumo mode:
- **2-8 players** battle on a circular platform
- Push other players off to eliminate them
- Last player standing wins
- ~45 second rounds with restart capability

## 📁 Project Structure

```
Assets/Scripts/NeonSumo/
├── NeonSumoGameManager.cs      # Main game controller & SDK integration
├── NeonSumoPlayer.cs           # Player controller (physics + network sync)
├── NeonSumoArena.cs            # Arena setup & spawn logic
├── NeonSumoUIManager.cs        # UI controller (delegates to UIFlowController, HudPresenter, etc.)
├── NeonSumoMatchmakingFlow.cs  # Matchmaking → Multiplayer transition
├── ActionSyncEventHandler.cs  # Handles ActionSync events (elimination, spawn, ramps, etc.)
├── HostInputSync.cs            # Host-authoritative input sync & state broadcast
├── NeonSumoNetworkEventRouter.cs # ActionSync subscription & payload parsing
├── UIFlowController.cs        # Panel visibility flow (main menu, game, countdown, winner)
├── HudPresenter.cs            # Timer, error display
├── PlayerListPresenter.cs     # Player list UI state
└── CountdownController.cs     # 3-2-1-Go and winner panel animations
```

## 🏗️ Architecture Highlights

### Correct SDK Usage ✅

- **ActionSync** for discrete events (elimination, boost)
- **NetworkSync** only for position data
- **GameModule** as authoritative source for timing
- **Master client** handles game state decisions

### Multiplayer Authority Model ✅

- **Local player**: Full physics simulation
- **Remote players**: Kinematic + position interpolation
- **Elimination**: Master-client authoritative
- **Winner determination**: Master-only to avoid race conditions

### Scalability ✅

- Player-count aware spawn logic (supports 2-8+ players)
- Configurable via `NeonSumoConfig` asset
- Network sync throttled to 10 Hz
- No polling - explicit dependency injection

## 🚀 Setup Instructions

1. **Configure SDK**: Ensure `.env` file is set up with service URLs
2. **Set App ID**: Create a `NeonSumoConfig` asset and set `AppId` in its inspector (or use the default)
3. **Create Scene**: 
   - Create `NeonSumoScene.unity`
   - Add `NeonSumoGameManager` to an empty GameObject
   - Add `NeonSumoArena` to an arena GameObject
   - Add `NeonSumoUIManager` to UI Canvas
   - Add `NeonSumoMatchmakingFlow` for SDK connection handling
4. **Create Player Prefab**:
   - Sphere with `Rigidbody` component
   - Add `NeonSumoPlayer` component
   - Assign renderer for visual color
5. **Assign References**: Link all references in GameManager inspector

## Main menu and launch modes (single build)

The first scene in **File → Build Settings** should be **`NeonSumoMainMenuScene`** (or your own scene with `NeonSumoMainMenuController`). That menu sets a runtime **`NeonSumoSessionStore.Mode`** and loads the next scene:

| Button | Effect |
|--------|--------|
| **Single Player** | `Mode = OfflineSinglePlayer`, clears `MatchmakingHandoffStore`, loads **NeonSumoScene** (offline / bots). |
| **Multiplayer** | Clears session mode, loads **NeonSumoLobbyScene** only. After lobby handoff, `NeonSumoLobbyManager` sets `OnlineMultiplayer` and saves handoff, then loads gameplay. |

In **NeonSumoScene**, **`NeonSumoSceneBootstrap`** picks offline vs online in this order:

1. **`forceOffline`** (inspector) — safety / kiosk: always offline, clears handoff.
2. **`OfflineSinglePlayer`** session mode — offline, clears handoff, then consumes mode (one-shot).
3. **`OnlineMultiplayer`** session mode — online **only if** handoff is valid; otherwise logs error and falls back to offline.
4. **Valid handoff** (legacy lobby → gameplay without session mode) — online.
5. **Default** — offline (no handoff).

Logs: `[NeonSumoBootstrap] path=... modeAtEntry=... handoffPending=...` on every gameplay load.

For **VIVERSE / direct-launch** without a menu, enable **`forceOffline`** on the gameplay scene’s `NeonSumoSceneBootstrap` so stale PlayerPrefs handoff does not force online.

**Offline / single-player intro:** `StartLocalGame()` runs the same **`StartIntroPhaseIfNeeded()` → `RunStartCountdown()`** path as online (Get Ready, announcer audio, 3-2-1-Go), then **`StartGame()`** for ramps, roll, retract, and HUD—no duplicate intro logic.

**Offline match timer / ring events:** `NeonSumoOfflineBootstrap` starts its countdown and ring-timing coroutines only after the manager reaches **`Playing`** with **`IsGameActive`** true, so the match clock does not tick during the intro (HUD stays at 1:00 until GO).

## 🎮 Game Flow

1. **Matchmaking**: Players join/create rooms via MatchmakingClient
2. **Ready Phase**: All players press "Ready"
3. **Countdown**: 3...2...1...GO (from GameModule)
4. **Gameplay**: Players push each other off the platform
5. **Elimination**: Falling off = eliminated (synced via ActionSync)
6. **Winner**: Last player standing wins
7. **Restart**: Master client can restart for another round

## 🔧 Configuration

### NeonSumoConfig

Create a `NeonSumoConfig` asset and assign it to the lobby and gameplay scene components:
- **AppId**: Application ID for matchmaking/multiplayer. Replace the default with your actual App ID.
- **MaxPlayers** / **MinPlayers**: Lobby and match limits.

### Game Module Settings

Configured in `NeonSumoMatchmakingFlow.cs`:
- `ready_time`: 10 seconds
- `play_time`: 45 seconds
- `min_total_player`: 2
- `max_total_player`: from `NeonSumoConfig.MaxPlayers`

### Arena camera

`NeonSumoArenaCamera` supports **FixedArena** (default, Fusion-style: stays at the scene / reset pose with impact shake) and **DynamicSpectator** (orbit framing on the living-player cluster).

## 📝 Key Implementation Details

### Elimination Sync
Uses **ActionSync** for discrete elimination events, not NetworkSync:
```csharp
_multiplayerClient.ActionSync.Competition("eliminate", userId, eliminationId);
```

### Remote Player Physics
Remote players are **kinematic** - only local player simulates physics:
```csharp
if (!isLocal)
{
    _rb.isKinematic = true;
}
```

### Winner Check
Only **master client** determines winner to avoid double end-game triggers:
```csharp
if (!_multiplayerClient.IsMasterUser()) return;
CheckForWinner();
```

## 🐛 Debugging

Connection details are logged on initialization:
- PeerId
- RoomId
- AppId
- IsMasterUser status
- MaxPlayers configuration

Check Unity Console for `[NeonSumoFlow]` and `[NeonSumo]` log messages.

## 🎨 Optional Enhancements

For production polish (not required):
- Fade countdown animations
- Winner panel animations
- Visual feedback for boosts
- Arena shrink visual effects
- Camera zoom near arena collapse

## 📚 SDK Modules Used

- ✅ **MatchmakingClient**: Room management
- ✅ **MultiplayerClient**: Connection & presence
- ✅ **GameModule**: Game lifecycle
- ✅ **NetworkSyncModule**: Position sync
- ✅ **ActionSyncModule**: Event sync
- ✅ **LeaderboardModule**: Score tracking

## ⚠️ Performance Notes

- Network sync rate: 10 Hz (configurable in `NeonSumoPlayer.cs`)
- Recommended max players for WebGL: 6-8
- Consider reducing spawn radius with more players

## 🎯 This Demo Is Production-Ready

This implementation:
- ✅ Uses correct SDK patterns
- ✅ Handles edge cases (disconnects, master authority)
- ✅ Scales to multiple players
- ✅ Has clean separation of concerns
- ✅ Is safe to share as a reference implementation

---

**Built with Viverse Multiplayer SDK** 🚀

