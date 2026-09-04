using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace NeonSumo
{
    /// <summary>
    /// UI Toolkit view for the main menu panel. Exposes events and a small API;
    /// NeonSumoUIManager orchestrates and subscribes. No GameManager dependency here.
    /// </summary>
    public class MainMenuPanelView : MonoBehaviour
    {
        private const string MainMenuRootName = "mainMenuRoot";
        private const string ConnectionStatusTextName = "connectionStatusText";
        private const string WinsCounterTextName = "winsCounterText";
        private const string PlayerListContainerName = "playerListContainer";
        private const string ReadyButtonName = "readyButton";
        private const string RestartButtonName = "restartButton";
        private const string QuitButtonName = "quitButton";
        private const string LobbyActionsRowName = "lobbyActionsRow";
        private const string PlayerNameLabelName = "playerNameLabel";
        private const string EliminatedClassName = "eliminated";
        private const string MenuButtonFocusedClass = "menu-button--focused";

        [Header("Optional row template (clone per player)")]
        [SerializeField] private VisualTreeAsset playerRowTemplate;

        // Events for the orchestrator to subscribe to (no FindObjectOfType in view)
        public event Action ReadyClicked;
        public event Action RestartClicked;
        public event Action QuitClicked;

        private UIDocument _document;
        private VisualElement _root;
        private VisualElement _mainMenuRoot;
        private Label _connectionStatusText;
        private Label _winsCounterText;
        private VisualElement _playerListContainer;
        private VisualElement _lobbyActionsRow;
        private Button _readyButton;
        private Button _restartButton;
        private Button _quitButton;

        private Coroutine _focusDefaultLobbyActionCoroutine;

        private sealed class PlayerRowRefs
        {
            public VisualElement Root;
            public Label NameLabel;
        }

        private readonly Dictionary<string, PlayerRowRefs> _playerRows = new();

        private void Awake()
        {
            _document = GetComponent<UIDocument>();
            RebindVisualTreeReferences();
        }

        private void OnEnable()
        {
            // Re-query references when panel is shown; UIDocument may have recreated the visual tree when disabled.
            UnsubscribeButtons();
            RebindVisualTreeReferences();
            SubscribeButtons();
        }

        private void OnDestroy()
        {
            if (_focusDefaultLobbyActionCoroutine != null)
            {
                StopCoroutine(_focusDefaultLobbyActionCoroutine);
                _focusDefaultLobbyActionCoroutine = null;
            }
            UnsubscribeButtons();
        }

        private void RebindVisualTreeReferences()
        {
            if (_document == null)
            {
                Debug.LogError("[MainMenuPanelView] UIDocument not found on same GameObject.");
                return;
            }

            var previousContainer = _playerListContainer;

            _root = _document.rootVisualElement;
            if (_root == null) return;

            _mainMenuRoot = _root.Q<VisualElement>(MainMenuRootName);
            _connectionStatusText = _root.Q<Label>(ConnectionStatusTextName);
            _winsCounterText = _root.Q<Label>(WinsCounterTextName);
            _playerListContainer = _root.Q<VisualElement>(PlayerListContainerName);
            _lobbyActionsRow = _root.Q<VisualElement>(LobbyActionsRowName);
            _readyButton = _root.Q<Button>(ReadyButtonName);
            _restartButton = _root.Q<Button>(RestartButtonName);
            _quitButton = _root.Q<Button>(QuitButtonName);

            if (!ReferenceEquals(previousContainer, _playerListContainer))
                ClearCachedPlayerRowReferencesOnly();
        }

        private void ClearCachedPlayerRowReferencesOnly()
        {
            _playerRows.Clear();
        }

        private static void Subscribe(Button button, Action handler)
        {
            if (button != null)
                button.clicked += handler;
        }

        private static void Unsubscribe(Button button, Action handler)
        {
            if (button != null)
                button.clicked -= handler;
        }

        private void SubscribeButtons()
        {
            Subscribe(_readyButton, OnReadyClicked);
            Subscribe(_restartButton, OnRestartClicked);
            Subscribe(_quitButton, OnQuitClicked);
            RegisterMenuButtonFocusRing(_readyButton);
            RegisterMenuButtonFocusRing(_restartButton);
            RegisterMenuButtonFocusRing(_quitButton);
        }

        private void UnsubscribeButtons()
        {
            UnregisterMenuButtonFocusRing(_readyButton);
            UnregisterMenuButtonFocusRing(_restartButton);
            UnregisterMenuButtonFocusRing(_quitButton);
            Unsubscribe(_readyButton, OnReadyClicked);
            Unsubscribe(_restartButton, OnRestartClicked);
            Unsubscribe(_quitButton, OnQuitClicked);
        }

        private static void RegisterMenuButtonFocusRing(Button button)
        {
            if (button == null) return;
            button.RegisterCallback<FocusInEvent>(OnMenuButtonFocusIn);
            button.RegisterCallback<FocusOutEvent>(OnMenuButtonFocusOut);
        }

        private static void UnregisterMenuButtonFocusRing(Button button)
        {
            if (button == null) return;
            button.UnregisterCallback<FocusInEvent>(OnMenuButtonFocusIn);
            button.UnregisterCallback<FocusOutEvent>(OnMenuButtonFocusOut);
        }

        private static void OnMenuButtonFocusIn(FocusInEvent evt)
        {
            if (evt.target is Button btn)
                btn.AddToClassList(MenuButtonFocusedClass);
        }

        private static void OnMenuButtonFocusOut(FocusOutEvent evt)
        {
            if (evt.target is Button btn)
                btn.RemoveFromClassList(MenuButtonFocusedClass);
        }

        private void OnReadyClicked() => ReadyClicked?.Invoke();
        private void OnRestartClicked() => RestartClicked?.Invoke();
        private void OnQuitClicked() => QuitClicked?.Invoke();

        // ---------- View API ----------

        public void SetVisible(bool visible)
        {
            gameObject.SetActive(visible);
        }

        /// <summary>
        /// After layout, focus Ready (if enabled), else Restart, else Quit — same pattern as <see cref="NeonSumoLobbyPanelView"/>.
        /// Skips when the lobby action row is hidden (e.g. winner modal owns focus).
        /// </summary>
        public void FocusDefaultLobbyActionNextFrame()
        {
            if (!isActiveAndEnabled) return;
            if (_focusDefaultLobbyActionCoroutine != null)
                StopCoroutine(_focusDefaultLobbyActionCoroutine);
            _focusDefaultLobbyActionCoroutine = StartCoroutine(FocusDefaultLobbyActionAfterLayout());
        }

        private IEnumerator FocusDefaultLobbyActionAfterLayout()
        {
            yield return null;
            ApplyDefaultLobbyControllerFocus();
            _focusDefaultLobbyActionCoroutine = null;
        }

        private void ApplyDefaultLobbyControllerFocus()
        {
            if (!isActiveAndEnabled || _document == null) return;
            if (_lobbyActionsRow != null && _lobbyActionsRow.resolvedStyle.display == DisplayStyle.None)
                return;

            if (_readyButton != null && _readyButton.panel != null && _readyButton.enabledSelf)
            {
                _readyButton.Focus();
                return;
            }

            if (_restartButton != null && _restartButton.panel != null && _restartButton.enabledSelf)
            {
                _restartButton.Focus();
                return;
            }

            if (_quitButton != null && _quitButton.panel != null && _quitButton.enabledSelf)
                _quitButton.Focus();
        }

        public void SetReadyButtonState(bool interactable, string text)
        {
            if (_readyButton == null) return;
            _readyButton.SetEnabled(interactable);
            _readyButton.text = text ?? string.Empty;
        }

        public void SetRestartInteractable(bool interactable)
        {
            if (_restartButton == null) return;
            _restartButton.SetEnabled(interactable);
        }

        public void ShowConnectionStatus(string text)
        {
            if (_connectionStatusText == null) return;
            _connectionStatusText.text = text ?? string.Empty;
            _connectionStatusText.style.display = DisplayStyle.Flex;
        }

        public void HideConnectionStatus()
        {
            if (_connectionStatusText == null) return;
            _connectionStatusText.style.display = DisplayStyle.None;
        }

        /// <summary>Hide Ready/Restart/Quit row while winner overlay owns those actions (Option A).</summary>
        public void SetLobbyActionsRowVisible(bool visible)
        {
            if (_lobbyActionsRow == null) return;
            _lobbyActionsRow.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>
        /// When true, the full-screen menu root ignores pointer input so a UIDocument sorted above (winner modal) receives clicks.
        /// Restore false whenever the winner overlay is dismissed.
        /// </summary>
        public void SetRootPointerPassthrough(bool passthrough)
        {
            if (_mainMenuRoot == null) return;
            _mainMenuRoot.pickingMode = passthrough ? PickingMode.Ignore : PickingMode.Position;
        }

        public void SetWinsCounter(int wins)
        {
            if (_winsCounterText == null) return;
            _winsCounterText.text = "Wins: " + wins.ToString();
        }

        /// <summary>Add or update a player row keyed by userId. Source of truth: incremental updates from UIManager.</summary>
        public void AddOrUpdatePlayerRow(string userId, string displayName, Color color, bool eliminated)
        {
            if (string.IsNullOrEmpty(userId) || _playerListContainer == null) return;

            if (!_playerRows.TryGetValue(userId, out var rowRefs))
            {
                rowRefs = CreatePlayerRowRefs();
                if (rowRefs == null) return;

                _playerListContainer.Add(rowRefs.Root);
                _playerRows[userId] = rowRefs;
            }

            rowRefs.Root.style.backgroundColor = new Color(color.r, color.g, color.b, 0.9f);

            if (rowRefs.NameLabel != null)
            {
                rowRefs.NameLabel.text = string.IsNullOrEmpty(displayName) ? userId : displayName;
                rowRefs.NameLabel.style.color = Color.white;
            }

            SetEliminatedClass(rowRefs.Root, eliminated);
        }

        public void RemovePlayerRow(string userId)
        {
            if (string.IsNullOrEmpty(userId)) return;

            if (_playerRows.TryGetValue(userId, out var rowRefs))
            {
                rowRefs.Root?.RemoveFromHierarchy();
                _playerRows.Remove(userId);
            }
        }

        public void ClearPlayerRows()
        {
            foreach (var rowRefs in _playerRows.Values)
                rowRefs.Root?.RemoveFromHierarchy();
            _playerRows.Clear();
        }

        private PlayerRowRefs CreatePlayerRowRefs()
        {
            var root = CreatePlayerRowVisual();
            if (root == null) return null;

            return new PlayerRowRefs
            {
                Root = root,
                NameLabel = root.Q<Label>(PlayerNameLabelName)
            };
        }

        private VisualElement CreatePlayerRowVisual()
        {
            if (playerRowTemplate != null)
                return playerRowTemplate.CloneTree();

            var container = new VisualElement();
            container.name = "playerRowRoot";
            container.AddToClassList("player-row");
            var nameLabel = new Label { name = PlayerNameLabelName, text = "Player" };
            nameLabel.AddToClassList("player-name");
            container.Add(nameLabel);
            return container;
        }

        private static void SetEliminatedClass(VisualElement row, bool eliminated)
        {
            if (row == null) return;

            if (eliminated)
                row.AddToClassList(EliminatedClassName);
            else
                row.RemoveFromClassList(EliminatedClassName);
        }
    }
}
