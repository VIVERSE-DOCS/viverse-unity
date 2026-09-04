using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace NeonSumo
{
    public class PlayerIndicatorManager : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private UIDocument uiDocument;
        [SerializeField] private Camera targetCamera;

        [Header("Layout")]
        [SerializeField, Tooltip("World-space meters above the player (e.g. above head).")]
        private float worldSpaceOffsetMeters = 2f;

        [Header("Local Player Pulse")]
        [SerializeField] private float localPulseSpeed = 14f;
        [SerializeField] private float localPulseMinOpacity = 0.2f;
        [SerializeField] private float localPulseMaxOpacity = 1f;

        private VisualElement _root;
        private VisualElement _container;
        private readonly Dictionary<Transform, IndicatorEntry> _indicators = new();

        private sealed class IndicatorEntry
        {
            public Transform Target;
            public VisualElement Root;
            public Label Label;
            public VisualElement Triangle;
            public bool IsLocal;
        }

        private void Awake()
        {
            if (!ResolveReferences())
            {
                enabled = false;
                return;
            }

            EnsureContainer();
            ResolveCamera();
        }

        private bool ResolveReferences()
        {
            if (uiDocument == null)
                uiDocument = GetComponent<UIDocument>();

            if (uiDocument == null)
            {
                Debug.LogError("[NeonSumo] PlayerIndicatorManager: UIDocument is not assigned and not on this GameObject. Assign in Inspector or add UIDocument to this GameObject.");
                return false;
            }

            _root = uiDocument.rootVisualElement;
            if (_root == null)
            {
                Debug.LogError("[NeonSumo] PlayerIndicatorManager: UIDocument rootVisualElement is null.");
                return false;
            }

            return true;
        }

        private void EnsureContainer()
        {
            _container = _root.Q<VisualElement>("indicatorsRoot");
            if (_container != null)
                return;

            _container = new VisualElement { name = "indicatorsRoot" };
            _container.AddToClassList("pi-root");
            _root.Add(_container);
        }

        private void ResolveCamera()
        {
            if (targetCamera == null)
                targetCamera = Camera.main;
        }

        private void LateUpdate()
        {
            if (_container == null || targetCamera == null)
                return;

            foreach (var entry in _indicators.Values)
            {
                UpdateIndicator(entry);
            }
        }

        private void UpdateIndicator(IndicatorEntry indicator)
        {
            if (indicator?.Target == null || indicator.Root == null || targetCamera == null || _root == null)
            {
                if (indicator?.Root != null)
                    indicator.Root.style.display = DisplayStyle.None;
                return;
            }

            var panel = _root.panel;
            if (panel == null)
            {
                indicator.Root.style.display = DisplayStyle.None;
                return;
            }

            Vector3 worldPos = indicator.Target.position + Vector3.up * worldSpaceOffsetMeters;
            Vector3 viewportPos = targetCamera.WorldToViewportPoint(worldPos);

            if (viewportPos.z <= 0f || viewportPos.x < 0f || viewportPos.x > 1f || viewportPos.y < 0f || viewportPos.y > 1f)
            {
                indicator.Root.style.display = DisplayStyle.None;
                return;
            }

            Vector2 panelPos = RuntimePanelUtils.CameraTransformWorldToPanel(panel, worldPos, targetCamera);

            float width = indicator.Root.resolvedStyle.width > 0f ? indicator.Root.resolvedStyle.width : 60f;
            float height = indicator.Root.resolvedStyle.height > 0f ? indicator.Root.resolvedStyle.height : 24f;

            indicator.Root.style.display = DisplayStyle.Flex;
            indicator.Root.style.left = panelPos.x - width * 0.5f;
            indicator.Root.style.top = panelPos.y - height;
            indicator.Root.style.opacity = indicator.IsLocal ? EvaluateLocalOpacity() : 1f;
        }

        private float EvaluateLocalOpacity()
        {
            float t = (Mathf.Sin(Time.realtimeSinceStartup * localPulseSpeed) + 1f) * 0.5f;
            return Mathf.Lerp(localPulseMinOpacity, localPulseMaxOpacity, t);
        }

        public void RegisterPlayer(Transform target, int playerIndex, Color color, bool isLocal = false)
        {
            if (target == null || _container == null)
                return;

            if (_indicators.ContainsKey(target))
                return;

            var entry = CreateIndicator(target, playerIndex, color, isLocal);
            _indicators.Add(target, entry);
        }

        public void UnregisterPlayer(Transform target)
        {
            if (target == null)
                return;

            if (!_indicators.TryGetValue(target, out var entry))
                return;

            entry.Root?.RemoveFromHierarchy();
            _indicators.Remove(target);
        }

        public void ClearAll()
        {
            if (_container != null)
                _container.Clear();

            _indicators.Clear();
        }

        private IndicatorEntry CreateIndicator(Transform target, int playerIndex, Color color, bool isLocal)
        {
            var indicatorRoot = new VisualElement();
            indicatorRoot.AddToClassList("pi-indicator");

            var label = new Label($"P{playerIndex}");
            label.AddToClassList("pi-label");
            label.style.color = Color.Lerp(color, Color.white, 0.1f);
            label.style.unityTextOutlineColor = Color.Lerp(color, Color.black, 0.6f);

            var triangle = new VisualElement();
            triangle.AddToClassList("pi-triangle");
            triangle.style.borderTopColor = color;

            indicatorRoot.Add(label);
            indicatorRoot.Add(triangle);
            _container.Add(indicatorRoot);

            return new IndicatorEntry
            {
                Target = target,
                Root = indicatorRoot,
                Label = label,
                Triangle = triangle,
                IsLocal = isLocal
            };
        }
    }
}
