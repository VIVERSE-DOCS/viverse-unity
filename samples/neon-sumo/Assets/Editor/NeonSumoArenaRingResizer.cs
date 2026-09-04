using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using NeonSumo;
using UnityEngine.ProBuilder;

namespace NeonSumo.Editor
{
    internal static class NeonSumoArenaRingResizer
    {
        private const float RadiusEpsilon = 0.0001f;

        public static void SetRingWidths(NeonSumoArena arena, float targetWidth)
        {
            if (!NeonSumoArenaEditorUtility.TryGetValidSnapshot(arena, out var snapshot, out string error))
            {
                Debug.LogWarning($"[NeonSumoArena] {error}");
                return;
            }

            if (targetWidth <= 0f)
            {
                Debug.LogWarning("[NeonSumoArena] Target width must be > 0.");
                return;
            }

            if (snapshot.Rings.Count < 2)
            {
                Debug.LogWarning("[NeonSumoArena] Need at least 2 rings to set widths.");
                return;
            }

            var entries = new List<RingEntry>(snapshot.Rings);
            float coreRadius = snapshot.CoreRadius;

            var objectsToUndo = new List<Object>();
            foreach (var entry in entries)
            {
                var pb = entry.Transform.GetComponentInChildren<ProBuilderMesh>(true);
                if (pb != null) objectsToUndo.Add(pb);
            }
            if (objectsToUndo.Count > 0)
                Undo.RecordObjects(objectsToUndo.ToArray(), "Set Ring Widths");

            int scaled = 0;
            float currentInnerRadius = coreRadius;

            for (int ringIdx = entries.Count - 1; ringIdx >= 0; ringIdx--)
            {
                var entry = entries[ringIdx];
                var pb = entry.Transform.GetComponentInChildren<ProBuilderMesh>(true);
                if (pb == null)
                {
                    Debug.LogWarning($"[NeonSumoArena] {entry.Name} has no ProBuilderMesh - skipping.");
                    continue;
                }

                float oldInnerRadius = ringIdx + 1 < entries.Count ? entries[ringIdx + 1].Radius : coreRadius;
                float oldOuterRadius = entry.Radius;
                float currentWidth = oldOuterRadius - oldInnerRadius;
                if (currentWidth <= 0f)
                {
                    Debug.LogWarning($"[NeonSumoArena] {entry.Name}: invalid geometry (outer <= inner), skipping.");
                    continue;
                }

                float newOuterRadius = currentInnerRadius + targetWidth;

                RemapRingRadius(pb, oldInnerRadius, oldOuterRadius, currentInnerRadius, newOuterRadius);
                scaled++;
                currentInnerRadius = newOuterRadius;
            }

            EditorUtility.SetDirty(arena);
            if (scaled > 0)
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(arena.gameObject.scene);
            Debug.Log($"[NeonSumoArena] Repositioned {scaled} rings to width {targetWidth} (innermost to outermost, no overlap)");
        }

        private static void RemapRingRadius(
            ProBuilderMesh mesh,
            float oldInnerRadius,
            float oldOuterRadius,
            float newInnerRadius,
            float newOuterRadius)
        {
            var positions = mesh.positions;
            var remapped = new List<Vector3>(positions.Count);
            float oldWidth = oldOuterRadius - oldInnerRadius;

            for (int i = 0; i < positions.Count; i++)
            {
                remapped.Add(RemapPoint(positions[i], oldInnerRadius, oldWidth, newInnerRadius, newOuterRadius));
            }

            mesh.positions = remapped;
            mesh.ToMesh();
            mesh.Refresh();
        }

        private static Vector3 RemapPoint(
            Vector3 p,
            float oldInnerRadius,
            float oldWidth,
            float newInnerRadius,
            float newOuterRadius)
        {
            float r = Mathf.Sqrt(p.x * p.x + p.z * p.z);
            if (r < RadiusEpsilon)
                return p;

            float tNorm = (r - oldInnerRadius) / oldWidth;
            float newR = Mathf.Lerp(newInnerRadius, newOuterRadius, tNorm);
            float factor = newR / r;
            return new Vector3(p.x * factor, p.y, p.z * factor);
        }
    }
}
