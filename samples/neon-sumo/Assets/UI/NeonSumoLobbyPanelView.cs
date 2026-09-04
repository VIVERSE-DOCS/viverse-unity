using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace NeonSumo
{
    /// <summary>
    /// UI Toolkit view for the NeonSumo lobby.
    /// Passive view only: exposes events and simple UI state accessors.
    /// No business logic.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class NeonSumoLobbyPanelView : MonoBehaviour
    {
        public event Action RefreshClicked;
        public event Action CreateRoomClicked;
        public event Action<string> JoinRoomClicked;
        public event Action EnterGameClicked;
        public event Action LeaveRoomClicked;
        public event Action BackClicked;

        private UIDocument _document;
        private VisualElement _root;

        private TextField _roomCodeInput;
        private TextField _actorNameInput;
        private Button _refreshButton;
        private Button _createRoomButton;
        private Button _joinSelectedButton;
        private Button _enterGameButton;
        private Button _leaveRoomButton;
        private VisualElement _roomListContainer;
        private ScrollView _roomListScroll;
        private VisualElement _roomListFocusAnchor;
        private Label _statusText;

        private bool _isBound;
        private bool _roomListJoinEnabled = true;

        /// <summary>Controller navigation order: name, each room row, room code, buttons (rebuilt when the list changes).</summary>
        private readonly List<VisualElement> _lobbyFocusChain = new List<VisualElement>(24);

        public string RoomCode
        {
            get => _roomCodeInput?.value ?? string.Empty;
            set
            {
                if (_roomCodeInput != null)
                    _roomCodeInput.value = value ?? string.Empty;
            }
        }

        public string ActorName
        {
            get => _actorNameInput?.value ?? string.Empty;
            set
            {
                if (_actorNameInput != null)
                    _actorNameInput.value = value ?? string.Empty;
            }
        }

        public string Status
        {
            set
            {
                if (_statusText != null)
                    _statusText.text = value ?? string.Empty;
            }
        }

        private void Awake()
        {
            _document = GetComponent<UIDocument>();
            _isBound = BindElements();

            if (!_isBound)
                Debug.LogError("[NeonSumoLobbyPanelView] Failed to bind one or more required UI elements.", this);
        }

        private void Start()
        {
            if (!_isBound) return;
            StartCoroutine(FocusInitialControlNextFrame());
        }

        private void Update()
        {
            if (!_isBound || !WasGamepadBackPressedThisFrame())
                return;

            BackClicked?.Invoke();
        }

        private static bool WasGamepadBackPressedThisFrame()
        {
            var gamepad = Gamepad.current;
            return gamepad != null && gamepad.buttonEast.wasPressedThisFrame;
        }

        private IEnumerator FocusInitialControlNextFrame()
        {
            yield return null;
            ApplyInitialControllerFocus();
        }

        /// <summary>Prefer Create Room for gamepad-first flow; fall back if disabled (e.g. in room / busy).</summary>
        private void ApplyInitialControllerFocus()
        {
            if (_createRoomButton != null && _createRoomButton.panel != null && _createRoomButton.enabledSelf)
            {
                _createRoomButton.Focus();
                return;
            }

            if (_refreshButton != null && _refreshButton.panel != null && _refreshButton.enabledSelf)
            {
                _refreshButton.Focus();
                return;
            }

            if (_actorNameInput != null && _actorNameInput.panel != null)
                _actorNameInput.Focus();
        }

        /// <summary>Deferred focus so Enter Game is selected after enable/list rebuild (e.g. after create room).</summary>
        public void FocusEnterGameNextFrame()
        {
            if (!_isBound) return;
            StartCoroutine(FocusEnterGameAfterLayout());
        }

        private IEnumerator FocusEnterGameAfterLayout()
        {
            yield return null;
            if (_enterGameButton != null && _enterGameButton.panel != null && _enterGameButton.enabledSelf)
                _enterGameButton.Focus();
        }

        private void OnEnable()
        {
            if (!_isBound) return;

            _refreshHandler = () => RefreshClicked?.Invoke();
            _createRoomHandler = () => CreateRoomClicked?.Invoke();
            _joinSelectedHandler = HandleJoinSelectedClicked;
            _enterGameHandler = () => EnterGameClicked?.Invoke();
            _leaveRoomHandler = () => LeaveRoomClicked?.Invoke();

            _refreshButton.clicked += _refreshHandler;
            _createRoomButton.clicked += _createRoomHandler;
            _joinSelectedButton.clicked += _joinSelectedHandler;
            _enterGameButton.clicked += _enterGameHandler;
            _leaveRoomButton.clicked += _leaveRoomHandler;

            if (_root != null)
            {
                _root.RegisterCallback<NavigationMoveEvent>(OnLobbyRootNavigationMove, TrickleDown.TrickleDown);
                _root.RegisterCallback<NavigationSubmitEvent>(OnLobbyRootNavigationSubmit, TrickleDown.TrickleDown);
            }

            RegisterLobbyButtonFocusRing(_refreshButton);
            RegisterLobbyButtonFocusRing(_createRoomButton);
            RegisterLobbyButtonFocusRing(_joinSelectedButton);
            RegisterLobbyButtonFocusRing(_enterGameButton);
            RegisterLobbyButtonFocusRing(_leaveRoomButton);
        }

        private void OnDisable()
        {
            if (!_isBound) return;

            if (_root != null)
            {
                _root.UnregisterCallback<NavigationMoveEvent>(OnLobbyRootNavigationMove, TrickleDown.TrickleDown);
                _root.UnregisterCallback<NavigationSubmitEvent>(OnLobbyRootNavigationSubmit, TrickleDown.TrickleDown);
            }

            UnregisterLobbyButtonFocusRing(_refreshButton);
            UnregisterLobbyButtonFocusRing(_createRoomButton);
            UnregisterLobbyButtonFocusRing(_joinSelectedButton);
            UnregisterLobbyButtonFocusRing(_enterGameButton);
            UnregisterLobbyButtonFocusRing(_leaveRoomButton);

            _refreshButton.clicked -= _refreshHandler;
            _createRoomButton.clicked -= _createRoomHandler;
            _joinSelectedButton.clicked -= _joinSelectedHandler;
            _enterGameButton.clicked -= _enterGameHandler;
            _leaveRoomButton.clicked -= _leaveRoomHandler;
        }

        private string _selectedRoomId;
        private string _ownedRoomHighlightId;
        private bool _joinSelectedAllowedByManager = true;
        private Action _refreshHandler;
        private Action _createRoomHandler;
        private Action _joinSelectedHandler;
        private Action _enterGameHandler;
        private Action _leaveRoomHandler;

        private void HandleJoinSelectedClicked()
        {
            if (!string.IsNullOrEmpty(_selectedRoomId))
                JoinRoomClicked?.Invoke(_selectedRoomId);
        }

        private const string LobbyButtonFocusedClass = "lobby-button--focused";

        private static void RegisterLobbyButtonFocusRing(Button button)
        {
            if (button == null) return;
            button.RegisterCallback<FocusInEvent>(OnLobbyButtonFocusIn);
            button.RegisterCallback<FocusOutEvent>(OnLobbyButtonFocusOut);
        }

        private static void UnregisterLobbyButtonFocusRing(Button button)
        {
            if (button == null) return;
            button.UnregisterCallback<FocusInEvent>(OnLobbyButtonFocusIn);
            button.UnregisterCallback<FocusOutEvent>(OnLobbyButtonFocusOut);
        }

        private static void OnLobbyButtonFocusIn(FocusInEvent evt)
        {
            var btn = FindAncestorButton(evt.target as VisualElement);
            btn?.AddToClassList(LobbyButtonFocusedClass);
        }

        private static void OnLobbyButtonFocusOut(FocusOutEvent evt)
        {
            var btn = FindAncestorButton(evt.target as VisualElement);
            btn?.RemoveFromClassList(LobbyButtonFocusedClass);
        }

        private static Button FindAncestorButton(VisualElement ve)
        {
            while (ve != null)
            {
                if (ve is Button b)
                    return b;
                ve = ve.parent;
            }

            return null;
        }

        private static bool IsDescendantOfOrSelf(VisualElement ve, VisualElement ancestor)
        {
            while (ve != null)
            {
                if (ve == ancestor)
                    return true;
                ve = ve.parent;
            }

            return false;
        }

        private int FindLobbyFocusChainIndex(VisualElement focused)
        {
            if (focused == null)
                return -1;

            for (int i = 0; i < _lobbyFocusChain.Count; i++)
            {
                var chainEl = _lobbyFocusChain[i];
                if (chainEl != null && IsDescendantOfOrSelf(focused, chainEl))
                    return i;
            }

            return -1;
        }

        private static bool CanReceiveControllerFocus(VisualElement el)
        {
            return el != null && el.enabledInHierarchy && el.focusable;
        }

        private static bool IsKeyboardDrivingLobbyNavigation(NavigationMoveEvent evt)
        {
            var k = Keyboard.current;
            if (k == null)
                return false;

            return evt.direction switch
            {
                NavigationMoveEvent.Direction.Up => k.upArrowKey.isPressed || k.wKey.isPressed,
                NavigationMoveEvent.Direction.Down => k.downArrowKey.isPressed || k.sKey.isPressed,
                NavigationMoveEvent.Direction.Left => k.leftArrowKey.isPressed || k.aKey.isPressed,
                NavigationMoveEvent.Direction.Right => k.rightArrowKey.isPressed || k.dKey.isPressed,
                _ => false
            };
        }

        private static bool IsGamepadDrivingLobbyNavigation(NavigationMoveEvent evt)
        {
            var g = Gamepad.current;
            if (g == null)
                return false;

            const float stickDeadzone = 0.35f;
            var stick = g.leftStick.ReadValue();

            return evt.direction switch
            {
                NavigationMoveEvent.Direction.Up =>
                    g.dpad.up.isPressed || stick.y > stickDeadzone,
                NavigationMoveEvent.Direction.Down =>
                    g.dpad.down.isPressed || stick.y < -stickDeadzone,
                NavigationMoveEvent.Direction.Left =>
                    g.dpad.left.isPressed || stick.x < -stickDeadzone,
                NavigationMoveEvent.Direction.Right =>
                    g.dpad.right.isPressed || stick.x > stickDeadzone,
                _ => false
            };
        }

        /// <summary>
        /// Keyboard sends NavigationMove for WASD/arrow UI navigation; that must not move focus here (breaks typing).
        /// Custom Up/Down chain is for gamepad only; keyboard users use Tab.
        /// (<see cref="NavigationMoveEvent"/> does not expose device publicly, so we infer from Input System.)
        /// </summary>
        private void OnLobbyRootNavigationMove(NavigationMoveEvent evt)
        {
            var focused = evt.target as VisualElement;
            if (focused == null || _root == null || !IsDescendantOfOrSelf(focused, _root))
                return;

            if (IsKeyboardDrivingLobbyNavigation(evt))
            {
                evt.PreventDefault();
                evt.StopImmediatePropagation();
                return;
            }

            if (!IsGamepadDrivingLobbyNavigation(evt))
                return;

            int delta;
            if (evt.direction == NavigationMoveEvent.Direction.Up)
                delta = -1;
            else if (evt.direction == NavigationMoveEvent.Direction.Down)
                delta = 1;
            else
                return;

            int idx = FindLobbyFocusChainIndex(focused);
            if (idx < 0)
                return;

            int next = idx + delta;
            if (next < 0 || next >= _lobbyFocusChain.Count)
                return;

            while (next >= 0 && next < _lobbyFocusChain.Count)
            {
                var candidate = _lobbyFocusChain[next];
                if (CanReceiveControllerFocus(candidate))
                {
                    evt.PreventDefault();
                    evt.StopImmediatePropagation();
                    candidate.Focus();
                    return;
                }

                next += delta;
            }
        }

        private void OnRoomRowFocusIn(FocusInEvent evt)
        {
            var row = FindRoomListRowAncestor(evt.target as VisualElement);
            if (row == null || !(row.userData is string id) || string.IsNullOrEmpty(id))
                return;

            _selectedRoomId = id;
            UpdateSelectionStyle();
            UpdateJoinSelectedEnabled();
            ScrollSelectedRowIntoView();
        }

        private VisualElement FindRoomListRowAncestor(VisualElement ve)
        {
            while (ve != null && ve != _roomListScroll)
            {
                if (ve.userData is string id && !string.IsNullOrEmpty(id))
                    return ve;
                ve = ve.parent;
            }

            return null;
        }

        private void OnLobbyRootNavigationSubmit(NavigationSubmitEvent evt)
        {
            var t = evt.target as VisualElement;
            var row = FindRoomListRowAncestor(t);
            if (row == null || !(row.userData is string roomId) || string.IsNullOrEmpty(roomId))
                return;
            if (!_roomListJoinEnabled || !_joinSelectedAllowedByManager)
                return;

            evt.PreventDefault();
            evt.StopImmediatePropagation();
            JoinRoomClicked?.Invoke(roomId);
        }

        private void ScrollSelectedRowIntoView()
        {
            if (_roomListScroll == null || _roomListContainer == null || string.IsNullOrEmpty(_selectedRoomId))
                return;

            foreach (var c in _roomListContainer.Children())
            {
                if (c.userData is string id && id == _selectedRoomId)
                {
                    _roomListScroll.ScrollTo(c);
                    return;
                }
            }
        }

        private void ApplyTabNavigationOrder()
        {
            int i = 0;
            foreach (var el in _lobbyFocusChain)
            {
                if (el != null)
                    el.tabIndex = i++;
            }
        }

        private void RebuildLobbyFocusChainAndTabOrder()
        {
            _lobbyFocusChain.Clear();
            if (_actorNameInput != null)
                _lobbyFocusChain.Add(_actorNameInput);
            if (_roomListContainer != null)
            {
                foreach (var c in _roomListContainer.Children())
                {
                    if (c.userData is string id && !string.IsNullOrEmpty(id))
                        _lobbyFocusChain.Add(c);
                }
            }

            if (_roomCodeInput != null)
                _lobbyFocusChain.Add(_roomCodeInput);
            if (_refreshButton != null)
                _lobbyFocusChain.Add(_refreshButton);
            if (_createRoomButton != null)
                _lobbyFocusChain.Add(_createRoomButton);
            if (_joinSelectedButton != null)
                _lobbyFocusChain.Add(_joinSelectedButton);
            if (_enterGameButton != null)
                _lobbyFocusChain.Add(_enterGameButton);
            if (_leaveRoomButton != null)
                _lobbyFocusChain.Add(_leaveRoomButton);

            ApplyTabNavigationOrder();
        }

        private bool BindElements()
        {
            if (_document == null || (_root = _document.rootVisualElement) == null)
            {
                Debug.LogError("[NeonSumoLobbyPanelView] UIDocument or rootVisualElement not found.", this);
                return false;
            }

            _roomCodeInput = _root.Q<TextField>("RoomCodeInput");
            _actorNameInput = _root.Q<TextField>("ActorNameInput");
            _refreshButton = _root.Q<Button>("RefreshButton");
            _createRoomButton = _root.Q<Button>("CreateRoomButton");
            _joinSelectedButton = _root.Q<Button>("JoinSelectedButton");
            _enterGameButton = _root.Q<Button>("EnterGameButton");
            _leaveRoomButton = _root.Q<Button>("LeaveRoomButton");
            _roomListContainer = _root.Q<VisualElement>("RoomListContainer");
            _roomListScroll = _root.Q<ScrollView>("RoomListScroll");
            _roomListFocusAnchor = _root.Q<VisualElement>("RoomListFocusAnchor");
            _statusText = _root.Q<Label>("StatusText");

            bool ok = true;
            ok &= Require(_roomCodeInput, nameof(_roomCodeInput), "RoomCodeInput");
            ok &= Require(_actorNameInput, nameof(_actorNameInput), "ActorNameInput");
            ok &= Require(_refreshButton, nameof(_refreshButton), "RefreshButton");
            ok &= Require(_createRoomButton, nameof(_createRoomButton), "CreateRoomButton");
            ok &= Require(_joinSelectedButton, nameof(_joinSelectedButton), "JoinSelectedButton");
            ok &= Require(_enterGameButton, nameof(_enterGameButton), "EnterGameButton");
            ok &= Require(_leaveRoomButton, nameof(_leaveRoomButton), "LeaveRoomButton");
            ok &= Require(_roomListContainer, nameof(_roomListContainer), "RoomListContainer");
            ok &= Require(_roomListScroll, nameof(_roomListScroll), "RoomListScroll");
            ok &= Require(_roomListFocusAnchor, nameof(_roomListFocusAnchor), "RoomListFocusAnchor");
            ok &= Require(_statusText, nameof(_statusText), "StatusText");

            if (ok)
            {
                _roomListFocusAnchor.focusable = false;
                _roomListScroll.focusable = false;
                RebuildLobbyFocusChainAndTabOrder();
            }

            return ok;
        }

        private static bool RowMatchesOwnedHighlight(RoomListItemData r, string highlightId)
        {
            if (r == null || string.IsNullOrEmpty(highlightId))
                return false;
            if (r.Id == highlightId)
                return true;
            return !string.IsNullOrEmpty(r.GameSession) && r.GameSession == highlightId;
        }

        private bool Require(object element, string fieldName, string uxmlName)
        {
            if (element != null) return true;
            Debug.LogError($"[NeonSumoLobbyPanelView] Required UI element missing: field={fieldName}, uxmlName={uxmlName}", this);
            return false;
        }

        public void SetRoomList(IReadOnlyList<RoomListItemData> rooms, bool joinEnabled = true, string ownedRoomHighlightId = null)
        {
            if (_roomListContainer == null) return;

            string previousSelectedId = _selectedRoomId;
            _roomListJoinEnabled = joinEnabled;
            _ownedRoomHighlightId = ownedRoomHighlightId;
            _roomListContainer.Clear();

            if (rooms == null)
            {
                _selectedRoomId = null;
                UpdateJoinSelectedEnabled();
                RebuildLobbyFocusChainAndTabOrder();
                return;
            }

            foreach (var r in rooms)
            {
                var row = new VisualElement();
                row.AddToClassList("room-list-row");
                if (RowMatchesOwnedHighlight(r, _ownedRoomHighlightId))
                    row.AddToClassList("room-list-row--owned");
                row.userData = r.Id;
                row.focusable = true;
                row.RegisterCallback<FocusInEvent>(OnRoomRowFocusIn);

                var nameLabel = new Label(r.DisplayName);
                nameLabel.AddToClassList("room-list-name");
                row.Add(nameLabel);

                var statusLabel = new Label(r.StatusText);
                statusLabel.AddToClassList("room-list-status");
                row.Add(statusLabel);

                var joinBtn = new Button(() => JoinRoomClicked?.Invoke(r.Id)) { text = "Join" };
                joinBtn.focusable = false;
                joinBtn.AddToClassList("room-list-join-btn");
                joinBtn.SetEnabled(joinEnabled);
                row.Add(joinBtn);

                row.RegisterCallback<ClickEvent>(evt =>
                {
                    if (evt.target == joinBtn) return;
                    _selectedRoomId = r.Id;
                    row.Focus();
                    UpdateSelectionStyle();
                    UpdateJoinSelectedEnabled();
                });

                _roomListContainer.Add(row);
            }

            _selectedRoomId = ResolveSelectionAfterRebuild(rooms, previousSelectedId);
            UpdateJoinSelectedEnabled();
            if (!string.IsNullOrEmpty(_selectedRoomId))
            {
                UpdateSelectionStyle();
                if (_selectedRoomId != previousSelectedId)
                    ScrollSelectedRowIntoView();
            }

            RebuildLobbyFocusChainAndTabOrder();
        }

        private static string ResolveSelectionAfterRebuild(IReadOnlyList<RoomListItemData> rooms, string previousSelectedId)
        {
            if (rooms == null || rooms.Count == 0)
                return null;

            if (!string.IsNullOrEmpty(previousSelectedId))
            {
                for (int i = 0; i < rooms.Count; i++)
                {
                    if (rooms[i] != null && rooms[i].Id == previousSelectedId)
                        return previousSelectedId;
                }
            }

            return rooms[0].Id;
        }

        private void UpdateSelectionStyle()
        {
            if (_roomListContainer == null) return;
            foreach (var c in _roomListContainer.Children())
            {
                bool selected = c.userData is string id && id == _selectedRoomId;
                c.EnableInClassList("room-list-row--selected", selected);
            }
        }

        private void UpdateJoinSelectedEnabled()
        {
            _joinSelectedButton?.SetEnabled(_joinSelectedAllowedByManager && !string.IsNullOrEmpty(_selectedRoomId));
        }

        public void SetRoomListLoading(bool loading)
        {
            Status = loading ? "Searching for rooms..." : "";
        }

        public void SetRefreshEnabled(bool enabled) => _refreshButton?.SetEnabled(enabled);
        public void SetCreateRoomEnabled(bool enabled) => _createRoomButton?.SetEnabled(enabled);
        public void SetJoinSelectedEnabled(bool enabled)
        {
            _joinSelectedAllowedByManager = enabled;
            UpdateJoinSelectedEnabled();
        }
        public void SetEnterGameEnabled(bool enabled) => _enterGameButton?.SetEnabled(enabled);
        public void SetLeaveRoomEnabled(bool enabled) => _leaveRoomButton?.SetEnabled(enabled);
        public void SetRoomCodeInputEnabled(bool enabled) => _roomCodeInput?.SetEnabled(enabled);
    }
}
