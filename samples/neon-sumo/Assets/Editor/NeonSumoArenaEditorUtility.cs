using System.Collections.Generic;
using UnityEngine;
using NeonSumo;

namespace NeonSumo.Editor
{
    internal readonly struct RingEntry
    {
        public Transform Transform { get; }
        public float Radius { get; }
        public string Name { get; }

        public RingEntry(Transform transform, float radius)
        {
            Transform = transform;
            Radius = radius;
            Name = transform != null ? transform.name : "<null>";
        }
    }

    internal readonly struct RingDimensionRow
    {
        public Transform Transform { get; }
        public string Name { get; }
        public float OuterRadius { get; }
        public float InnerRadius { get; }
        public float Width => OuterRadius - InnerRadius;

        public RingDimensionRow(Transform transform, string name, float outerRadius, float innerRadius)
        {
            Transform = transform;
            Name = name;
            OuterRadius = outerRadius;
            InnerRadius = innerRadius;
        }
    }

    /// <summary>
    /// Consolidated snapshot of arena ring geometry. Use BuildSnapshot to create.
    /// </summary>
    internal readonly struct ArenaSnapshot
    {
        public IReadOnlyList<RingEntry> Rings { get; }
        public float CoreRadius { get; }
        public Transform CoreTransform { get; }
        public IReadOnlyList<RingDimensionRow> DimensionRows { get; }

        public ArenaSnapshot(
            IReadOnlyList<RingEntry> rings,
            Transform coreTransform,
            float coreRadius,
            IReadOnlyList<RingDimensionRow> dimensionRows)
        {
            Rings = rings;
            CoreTransform = coreTransform;
            CoreRadius = coreRadius;
            DimensionRows = dimensionRows;
        }
    }

    internal static class NeonSumoArenaEditorUtility
    {
        /// <summary>
        /// Builds a consolidated snapshot of arena ring geometry. Single source of truth for inspector and tools.
        /// </summary>
        public static ArenaSnapshot BuildSnapshot(NeonSumoArena arena)
        {
            var entries = GetSortedRingEntries(arena, out int nullCount);
            if (nullCount > 0)
            {
                Debug.LogWarning($"[NeonSumoArena] {nullCount} null ring(s) in ringTransforms - skipped.");
            }

            float coreRadius = GetCoreRadius(arena);
            var rows = BuildDimensionRows(entries, coreRadius);

            return new ArenaSnapshot(entries, arena?.CoreTransform, coreRadius, rows);
        }

        /// <summary>
        /// Attempts to get a valid snapshot. Returns false with error message if arena is invalid.
        /// </summary>
        public static bool TryGetValidSnapshot(NeonSumoArena arena, out ArenaSnapshot snapshot, out string error)
        {
            snapshot = default;
            error = null;

            if (arena == null)
            {
                error = "Arena is null.";
                return false;
            }

            snapshot = BuildSnapshot(arena);

            if (snapshot.Rings == null || snapshot.Rings.Count == 0)
            {
                error = "No rings assigned. Assign ringTransforms or enable autoCollectRingsFromChildren.";
                return false;
            }

            return true;
        }

        public static List<RingEntry> GetSortedRingEntries(NeonSumoArena arena)
        {
            return GetSortedRingEntries(arena, out _);
        }

        private static List<RingEntry> GetSortedRingEntries(NeonSumoArena arena, out int nullCount)
        {
            nullCount = 0;
            var result = new List<RingEntry>();
            var rings = arena?.RingTransforms;
            if (rings == null) return result;

            foreach (var ring in rings)
            {
                if (ring == null)
                {
                    nullCount++;
                    continue;
                }
                result.Add(new RingEntry(ring, ArenaMeasurementUtility.GetHorizontalRadius(ring)));
            }

            result.Sort((a, b) => b.Radius.CompareTo(a.Radius));
            return result;
        }

        public static float GetCoreRadius(NeonSumoArena arena)
        {
            var core = arena?.CoreTransform;
            return core != null ? ArenaMeasurementUtility.GetHorizontalRadius(core) : 0f;
        }

        public static List<RingDimensionRow> BuildDimensionRows(List<RingEntry> entries, float coreRadius)
        {
            var rows = new List<RingDimensionRow>(entries.Count);
            for (int i = 0; i < entries.Count; i++)
            {
                float outer = entries[i].Radius;
                float inner = i + 1 < entries.Count ? entries[i + 1].Radius : coreRadius;
                rows.Add(new RingDimensionRow(entries[i].Transform, entries[i].Name, outer, inner));
            }
            return rows;
        }
    }
}
