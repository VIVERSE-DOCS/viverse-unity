using UnityEngine;
using UnityEngine.UIElements;

namespace NeonSumo
{
    public class PlayerBarFusionModernView : MonoBehaviour
    {
        /// <summary>Used when roster color is unknown so USS slot themes (green/yellow per p1/p2) do not read as player colors.</summary>
        public static readonly Color UnassignedSlotColor = new Color(0.42f, 0.42f, 0.48f, 0.92f);

        private VisualElement _root;

        private PlayerSlot[] _slots = new PlayerSlot[4];

    public struct PlayerBarData
    {
        public string Name;
        public string Status;
        public int Placement;
        public bool HasPlacement;
        public bool IsEliminated;
        public Color Color;
        public bool HasColor;
    }

    private class PlayerSlot
    {
        public VisualElement Root;
        public Label Prefix;
        public Label Name;
        public Label Status;
        public VisualElement TopBar;
    }

    private void Awake()
    {
        CacheReferencesFromDocument();
    }

    private void OnEnable()
    {
        CacheReferencesFromDocument();
    }

    private void CacheReferencesFromDocument()
    {
        var doc = GetComponent<UIDocument>();
        if (doc == null) return;
        _root = doc.rootVisualElement;
        if (_root == null) return;
        CacheSlots();
    }

    private void CacheSlots()
    {
        for (int i = 1; i <= 4; i++)
        {
            var slotRoot = _root.Q<VisualElement>($"p{i}");

            _slots[i - 1] = new PlayerSlot
            {
                Root = slotRoot,
                Prefix = slotRoot.Q<Label>($"p{i}Prefix"),
                Name = slotRoot.Q<Label>($"p{i}Name"),
                Status = slotRoot.Q<Label>($"p{i}Status"),
                TopBar = slotRoot.Q<VisualElement>(className: "pm-topbar")
            };
        }
    }

    // ------------------------------------
    // PUBLIC API
    // ------------------------------------

    public void SetPlayer(int slotIndex, string playerName, string status)
    {
        if (!IsValid(slotIndex)) return;

        _slots[slotIndex].Name.text = playerName ?? "-";
        _slots[slotIndex].Status.text = status ?? "";
    }

    public void SetSlot(int slotIndex, PlayerBarData data)
    {
        if (!IsValid(slotIndex)) return;

        var slot = _slots[slotIndex];
        if (slot.Prefix != null)
            slot.Prefix.text = $"P{slotIndex + 1}:";
        slot.Name.text = data.Name ?? "-";
        slot.Status.text = data.Status ?? "";

        Color barColor = data.HasColor ? data.Color : UnassignedSlotColor;
        slot.TopBar.style.backgroundColor = barColor;
        if (slot.Prefix != null)
            slot.Prefix.style.color = barColor;

        slot.Root.style.opacity = data.IsEliminated ? 0.4f : 1f;
        if (data.IsEliminated && string.IsNullOrEmpty(slot.Status.text))
        {
            slot.Status.text = "ELIMINATED";
        }
    }

    public void SetStatus(int slotIndex, string status)
    {
        if (!IsValid(slotIndex)) return;

        _slots[slotIndex].Status.text = status ?? "";
    }

    public void SetPlayerCount(int count)
    {
        for (int i = 0; i < 4; i++)
        {
            _slots[i].Root.style.display =
                i < count ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }

    public void SetPlacement(int slotIndex, int placement)
    {
        if (!IsValid(slotIndex)) return;
        // Placement no longer renders as a badge number in the Fusion modern card.
        // Method intentionally left as a no-op to preserve existing coordinator calls.
    }

    public void ResetPlacements()
    {
        for (int i = 0; i < _slots.Length; i++)
        {
            _slots[i].Root.style.opacity = 1f;
        }
    }

    public void SetEliminated(int slotIndex, bool eliminated)
    {
        if (!IsValid(slotIndex)) return;

        _slots[slotIndex].Root.style.opacity =
            eliminated ? 0.4f : 1f;

        if (eliminated)
        {
            _slots[slotIndex].Status.text = "ELIMINATED";
        }
    }

    public void SetPlayerColor(int slotIndex, Color color)
    {
        if (!IsValid(slotIndex)) return;

        _slots[slotIndex].TopBar.style.backgroundColor = color;
        if (_slots[slotIndex].Prefix != null)
            _slots[slotIndex].Prefix.style.color = color;
    }

    // ------------------------------------
    // INTERNAL
    // ------------------------------------

    private bool IsValid(int index)
    {
        return index >= 0 && index < _slots.Length;
    }
    }
}
