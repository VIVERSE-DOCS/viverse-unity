using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using System.Collections;
using System.IO;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

namespace NeonSumo
{
    /// <summary>
    /// Cold-start menu: Single Player (offline gameplay) vs Multiplayer (lobby first). Assign to a GameObject in the first scene of the build.
    /// </summary>
    public class NeonSumoMainMenuController : MonoBehaviour
    {
        [Tooltip("Scene name as in Build Settings (default gameplay arena).")]
        [SerializeField] private string gameplaySceneName = "NeonSumoScene";

        [Tooltip("Lobby / matchmaking scene name.")]
        [SerializeField] private string lobbySceneName = "NeonSumoLobbyScene";

        [Tooltip("Optional. Used to Initialize AuthManager on the main menu (WebGL). Lobby also initializes if this is unset.")]
        [SerializeField] private NeonSumoConfig config;

        [Tooltip("When true, creates Canvas + two buttons if none exist (quick setup).")]
        [SerializeField] private bool buildUiAtRuntime = true;
        [Tooltip("Optional background texture for the runtime main menu panel.")]
        [SerializeField] private Texture2D menuBackgroundTexture;
        [Tooltip("Optional fallback image file path used when Menu Background Texture is not assigned.")]
        [SerializeField] private string menuBackgroundImagePath = @"C:\Users\jamez\.cursor\projects\w-HTC-NeonSumo-UnitySDK-Multiplayer\assets\c__Users_jamez_AppData_Roaming_Cursor_User_workspaceStorage_3e669a4955aa920d592621a0341fd583_images_image-1c6ee5f3-17f6-4bf0-a49e-47423072991e.png";

        private Button _singlePlayerButton;
        private Button _multiplayerButton;
        private MenuButtonVisual _singlePlayerVisual;
        private MenuButtonVisual _multiplayerVisual;
        private bool _pendingInitialSelection;
        private GameObject _cachedMenuSelection;

        private void Awake()
        {
            if (buildUiAtRuntime)
            {
                TryBuildRuntimeUi();
            }
        }

        private void Start()
        {
            NeonSumoLoadingCoordinator.Instance?.NotifyMainMenuSceneLoaded();
            NeonSumoAuthSession.EnsureInitialized(config, null, this);

            if (_pendingInitialSelection)
            {
                StartCoroutine(EnsureInitialSelectionNextFrame());
            }
        }

        private void Update()
        {
            RefreshSelectionVisuals();
        }

        /// <summary>Wire to UI button: offline session, clear handoff, async load gameplay with loading overlay.</summary>
        public void StartSinglePlayer()
        {
            StartCoroutine(StartSinglePlayerFlow());
        }

        IEnumerator StartSinglePlayerFlow()
        {
            using (NeonSumoMenuProfilerMarkers.SinglePlayerClicked.Auto()) { }

            NeonSumoSessionStore.Mode = NeonSumoGameMode.OfflineSinglePlayer;
            MatchmakingHandoffStore.Clear();

            var coord = NeonSumoLoadingCoordinator.EnsureExists();
            using (NeonSumoMenuProfilerMarkers.ShowLoadingOverlay.Auto())
            {
                coord.BeginSinglePlayerTransitionToGameplay();
            }

            yield return null;

            AsyncOperation asyncOp;
            using (NeonSumoMenuProfilerMarkers.LoadGameplaySceneAsync.Auto())
            {
                asyncOp = SceneManager.LoadSceneAsync(gameplaySceneName);
            }

            while (asyncOp != null && !asyncOp.isDone)
                yield return null;
        }

        /// <summary>Wire to UI button: async load lobby (online mode set when lobby hands off).</summary>
        public void StartMultiplayer()
        {
            StartCoroutine(StartMultiplayerFlow());
        }

        IEnumerator StartMultiplayerFlow()
        {
            using (NeonSumoMenuProfilerMarkers.MultiplayerClicked.Auto()) { }

            NeonSumoAuthSession.EnsureInitialized(config, null, this);
            NeonSumoAuthSession.RequestLoginForMultiplayer();

            NeonSumoSessionStore.Clear();

            var coord = NeonSumoLoadingCoordinator.EnsureExists();
            using (NeonSumoMenuProfilerMarkers.ShowLoadingOverlay.Auto())
            {
                coord.BeginLobbySceneLoadFromMainMenu();
            }

            yield return null;

            AsyncOperation op;
            using (NeonSumoMenuProfilerMarkers.LoadGameplaySceneAsync.Auto())
            {
                op = SceneManager.LoadSceneAsync(lobbySceneName);
            }

            while (op != null && !op.isDone)
                yield return null;
        }

        void TryBuildRuntimeUi()
        {
            if (FindMainMenuCanvas() != null)
                return;

            EnsureEventSystem();

            var canvasGo = new GameObject("MainMenuCanvas");
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            canvasGo.AddComponent<GraphicRaycaster>();

            var panel = new GameObject("Panel");
            panel.transform.SetParent(canvasGo.transform, false);
            var panelRect = panel.AddComponent<RectTransform>();
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;
            var image = panel.AddComponent<Image>();
            image.color = new Color(0.08f, 0.08f, 0.12f, 1f);
            ApplyPanelBackgroundImage(image);

            (_singlePlayerButton, _singlePlayerVisual) = CreateMenuButton(
                panel.transform,
                "SinglePlayerButton",
                "Single Player",
                new Vector2(0, 80),
                StartSinglePlayer,
                new Color(0f, 240f / 255f, 255f / 255f, 0.95f));
            (_multiplayerButton, _multiplayerVisual) = CreateMenuButton(
                panel.transform,
                "MultiplayerButton",
                "Multiplayer",
                new Vector2(0, -40),
                StartMultiplayer,
                new Color(1f, 0f, 122f / 255f, 0.95f));

            ConfigureExplicitNavigation();
            SelectSinglePlayer();
            RefreshSelectionVisuals();
            _pendingInitialSelection = true;
        }

        static Canvas FindMainMenuCanvas()
        {
            var canvasGo = GameObject.Find("MainMenuCanvas");
            return canvasGo != null ? canvasGo.GetComponent<Canvas>() : null;
        }

        static void EnsureEventSystem()
        {
            if (FindFirstObjectByType<EventSystem>() != null)
            {
                return;
            }

            var esGo = new GameObject("EventSystem");
            esGo.AddComponent<EventSystem>();
#if ENABLE_INPUT_SYSTEM
            esGo.AddComponent<InputSystemUIInputModule>();
#else
            esGo.AddComponent<StandaloneInputModule>();
#endif
        }

        private IEnumerator EnsureInitialSelectionNextFrame()
        {
            yield return null;
            _pendingInitialSelection = false;

            var eventSystem = EventSystem.current;
            if (eventSystem == null || _singlePlayerButton == null || !_singlePlayerButton.gameObject.activeInHierarchy)
            {
                yield break;
            }

            // Runtime UI creation can temporarily clear selection on first frame.
            if (eventSystem.currentSelectedGameObject == null)
            {
                SelectSinglePlayer();
            }

            RefreshSelectionVisuals();
        }

        private void ApplyPanelBackgroundImage(Image panelImage)
        {
            if (panelImage == null)
            {
                return;
            }

            Texture2D texture = menuBackgroundTexture;
            if (texture == null && !string.IsNullOrEmpty(menuBackgroundImagePath))
            {
                texture = TryLoadTextureFromFile(menuBackgroundImagePath);
            }

            if (texture == null)
            {
                return;
            }

            var sprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(0.5f, 0.5f));

            panelImage.sprite = sprite;
            panelImage.type = Image.Type.Simple;
            panelImage.preserveAspect = false; // stretch to fill full screen panel area
            panelImage.color = Color.white;
        }

        private static Texture2D TryLoadTextureFromFile(string path)
        {
            try
            {
                if (!File.Exists(path))
                {
                    return null;
                }

                byte[] data = File.ReadAllBytes(path);
                if (data == null || data.Length == 0)
                {
                    return null;
                }

                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                return texture.LoadImage(data) ? texture : null;
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[NeonSumoMainMenuController] Failed to load background image from '{path}': {ex.Message}");
                return null;
            }
        }

        private void ConfigureExplicitNavigation()
        {
            if (_singlePlayerButton == null || _multiplayerButton == null)
            {
                return;
            }

            Navigation singleNav = _singlePlayerButton.navigation;
            singleNav.mode = Navigation.Mode.Explicit;
            singleNav.selectOnDown = _multiplayerButton;
            singleNav.selectOnUp = _multiplayerButton;
            _singlePlayerButton.navigation = singleNav;

            Navigation multiNav = _multiplayerButton.navigation;
            multiNav.mode = Navigation.Mode.Explicit;
            multiNav.selectOnDown = _singlePlayerButton;
            multiNav.selectOnUp = _singlePlayerButton;
            _multiplayerButton.navigation = multiNav;
        }

        private void SelectSinglePlayer()
        {
            if (_singlePlayerButton == null)
            {
                return;
            }

            var eventSystem = EventSystem.current;
            if (eventSystem != null)
            {
                eventSystem.SetSelectedGameObject(_singlePlayerButton.gameObject);
            }

            _singlePlayerButton.Select();
            RefreshSelectionVisuals();
        }

        static (Button button, MenuButtonVisual visual) CreateMenuButton(
            Transform parent,
            string name,
            string label,
            Vector2 anchoredPos,
            UnityEngine.Events.UnityAction onClick,
            Color highlightRailColor)
        {
            var btnGo = new GameObject(name);
            btnGo.transform.SetParent(parent, false);
            var rect = btnGo.AddComponent<RectTransform>();
            rect.sizeDelta = new Vector2(420, 72);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = anchoredPos;

            var img = btnGo.AddComponent<Image>();
            img.color = new Color(120f / 255f, 120f / 255f, 120f / 255f, 0.88f);

            var button = btnGo.AddComponent<Button>();
            button.targetGraphic = img;
            button.onClick.AddListener(onClick);
            button.transition = Selectable.Transition.None;

            var textGo = new GameObject("Text");
            textGo.transform.SetParent(btnGo.transform, false);
            var textRect = textGo.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
            var text = textGo.AddComponent<Text>();
            // Unity 6+ no longer provides Arial.ttf as a built-in resource (throws); use LegacyRuntime.ttf.
            Font uiFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            text.font = uiFont;
            text.text = label;
            text.fontSize = 28;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;

            var railGo = new GameObject("LeftRail");
            railGo.transform.SetParent(btnGo.transform, false);
            var railRect = railGo.AddComponent<RectTransform>();
            railRect.anchorMin = new Vector2(0f, 0f);
            railRect.anchorMax = new Vector2(0f, 1f);
            railRect.pivot = new Vector2(0f, 0.5f);
            railRect.anchoredPosition = Vector2.zero;
            railRect.sizeDelta = new Vector2(5f, 0f);

            var railImage = railGo.AddComponent<Image>();
            var baseRailColor = new Color(120f / 255f, 120f / 255f, 120f / 255f, 0.88f);
            railImage.color = baseRailColor;

            var visual = btnGo.AddComponent<MenuButtonVisual>();
            visual.Initialize(railImage, baseRailColor, highlightRailColor);

            return (button, visual);
        }

        private void RefreshSelectionVisuals()
        {
            var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            if (_singlePlayerButton != null && selected == _singlePlayerButton.gameObject)
                _cachedMenuSelection = selected;
            else if (_multiplayerButton != null && selected == _multiplayerButton.gameObject)
                _cachedMenuSelection = selected;

            _singlePlayerVisual?.SetSelected(selected == (_singlePlayerButton != null ? _singlePlayerButton.gameObject : null));
            _multiplayerVisual?.SetSelected(selected == (_multiplayerButton != null ? _multiplayerButton.gameObject : null));
        }

        private sealed class MenuButtonVisual : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
        {
            private Image _leftRail;
            private Color _baseRailColor;
            private Color _highlightRailColor;
            private bool _isHovered;
            private bool _isSelected;

            public void Initialize(Image leftRail, Color baseRailColor, Color highlightRailColor)
            {
                _leftRail = leftRail;
                _baseRailColor = baseRailColor;
                _highlightRailColor = highlightRailColor;
                Refresh();
            }

            public void SetSelected(bool selected)
            {
                if (_isSelected == selected) return;
                _isSelected = selected;
                Refresh();
            }

            public void OnPointerEnter(PointerEventData eventData)
            {
                _isHovered = true;
                Refresh();
            }

            public void OnPointerExit(PointerEventData eventData)
            {
                _isHovered = false;
                Refresh();
            }

            private void Refresh()
            {
                if (_leftRail == null) return;
                _leftRail.color = (_isHovered || _isSelected) ? _highlightRailColor : _baseRailColor;
            }
        }
    }
}
