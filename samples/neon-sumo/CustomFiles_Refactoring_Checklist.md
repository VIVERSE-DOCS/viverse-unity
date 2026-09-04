# Custom Files Refactoring Checklist

### Phase 1: Config & Data

- [x] `Assets/Scripts/NeonSumo/NeonSumoConfig.cs`
- [ ] `Assets/NeonSumo/Config/NeonSumoConfig.asset` (if applicable)

---

### Phase 2: Core Flow & Bootstrap

- [x] `Assets/Scripts/NeonSumo/NeonSumoSceneBootstrap.cs`
- [x] `Assets/Scripts/NeonSumo/NeonSumoMatchmakingFlow.cs`
- [x] `Assets/Scripts/NeonSumo/NeonSumoLobbyManager.cs`
- [x] `Assets/Scripts/NeonSumo/NeonSumoOfflineBootstrap.cs`
- [ ] `Assets/Scripts/NeonSumo/PlaySdkRuntimeRoot.cs`

---

### Phase 3: Interfaces & Shared Types

- [x] `Assets/Scripts/NeonSumo/IRampUnlockHandler.cs`
- [ ] `Assets/Scripts/NeonSumo/ICoroutineRunner.cs`

---

### Phase 4: Game Logic

- [ ] `Assets/Scripts/NeonSumo/NeonSumoGameManager.cs`
- [x] `Assets/Scripts/NeonSumo/NeonSumoArena.cs`
- [x] `Assets/Scripts/NeonSumo/NeonSumoPlayer.cs`
- [x] `Assets/Scripts/NeonSumo/NeonSumoRamps.cs`
- [ ] `Assets/Scripts/NeonSumo/NeonSumoRampUnlockTrigger.cs`
- [ ] `Assets/Scripts/NeonSumo/SpawnCoordinator.cs`
- [ ] `Assets/Scripts/NeonSumo/RingShrinkHandler.cs`

---

### Phase 5: Network & Sync

- [ ] `Assets/Scripts/NeonSumo/NeonSumoNetworkEventRouter.cs`
- [ ] `Assets/Scripts/NeonSumo/NeonSumoNetEvents.cs`
- [ ] `Assets/Scripts/NeonSumo/NeonSumoMessageHandler.cs`
- [ ] `Assets/Scripts/NeonSumo/ActionSyncEventHandler.cs`
- [ ] `Assets/Scripts/NeonSumo/HostInputSync.cs`
- [ ] `Assets/Scripts/NeonSumo/MasterSyncHandler.cs`
- [ ] `Assets/Scripts/NeonSumo/MatchmakingHandoffStore.cs`

---

### Phase 6: UI Logic & Presenters

- [ ] `Assets/Scripts/NeonSumo/NeonSumoUIManager.cs`
- [ ] `Assets/Scripts/NeonSumo/UIFlowController.cs`
- [ ] `Assets/Scripts/NeonSumo/HudPresenter.cs`
- [ ] `Assets/Scripts/NeonSumo/PlayerListPresenter.cs`
- [ ] `Assets/Scripts/NeonSumo/PlayerBarCoordinator.cs`
- [ ] `Assets/Scripts/NeonSumo/PlayerColorManager.cs`
- [ ] `Assets/Scripts/NeonSumo/CountdownController.cs`
- [ ] `Assets/Scripts/NeonSumo/ReadyCountdownCoordinator.cs`

---

### Phase 7: UI Views

- [ ] `Assets/UI/MainMenuPanelView.cs`
- [ ] `Assets/UI/NeonSumoLobbyPanelView.cs`
- [ ] `Assets/UI/GameUIPanelView.cs`
- [ ] `Assets/UI/CountdownPanelView.cs`
- [ ] `Assets/UI/WinnerPanelView.cs`
- [ ] `Assets/UI/PlayerBarFusionModernView.cs`
- [ ] `Assets/UI/PlayerIndicatorManager.cs`

---

### Phase 8: Camera & Editor

- [ ] `Assets/Scripts/Camera/NeonSumoArenaCamera.cs`
- [ ] `Assets/Editor/NeonSumoArenaEditor.cs`

---

### Phase 9: Utilities

- [ ] `Assets/Scripts/DebugLogger.cs`

---

**Total: 38 custom files**
