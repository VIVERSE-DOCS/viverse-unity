using UnityEngine;
using UnityEditor;
using NeonSumo;

namespace NeonSumo.Editor
{
    [CustomEditor(typeof(NeonSumoArena))]
    public class NeonSumoArenaEditor : UnityEditor.Editor
    {
        private const float DefaultTargetWidth = 5f;
        private const float NameColumnWidth = 140f;
        private const float ValueColumnWidth = 80f;

        private float _targetRingWidth = DefaultTargetWidth;

        [MenuItem("NeonSumo/Set Ring Widths to 5")]
        private static void MenuSetRingWidths()
        {
            var arena = Object.FindFirstObjectByType<NeonSumoArena>();
            if (arena == null)
            {
                Debug.LogWarning("[NeonSumoArena] No NeonSumoArena found in scene.");
                return;
            }
            NeonSumoArenaRingResizer.SetRingWidths(arena, DefaultTargetWidth);
        }

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var arena = (NeonSumoArena)target;
            if (arena == null) return;

            var snapshot = NeonSumoArenaEditorUtility.BuildSnapshot(arena);

            EditorGUILayout.Space(10);
            DrawRingDimensionsSection(snapshot);

            EditorGUILayout.Space(10);
            DrawActionsSection(arena, snapshot);
        }

        private void DrawRingDimensionsSection(ArenaSnapshot snapshot)
        {
            EditorGUILayout.LabelField("Ring Dimensions", EditorStyles.boldLabel);

            if (snapshot.DimensionRows == null || snapshot.DimensionRows.Count == 0)
            {
                EditorGUILayout.HelpBox("No rings assigned. Assign ringTransforms or enable autoCollectRingsFromChildren.", MessageType.Info);
                return;
            }

            EditorGUI.indentLevel++;
            foreach (var row in snapshot.DimensionRows)
            {
                DrawRingDimensionRow(row);
            }
            if (snapshot.CoreTransform != null)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"{snapshot.CoreTransform.name} (Core)", GUILayout.Width(NameColumnWidth));
                EditorGUILayout.LabelField($"Radius: {snapshot.CoreRadius:F2}", GUILayout.Width(ValueColumnWidth));
                EditorGUILayout.EndHorizontal();
            }
            EditorGUI.indentLevel--;
        }

        private static void DrawRingDimensionRow(RingDimensionRow row)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(row.Name, GUILayout.Width(NameColumnWidth));
            EditorGUILayout.LabelField($"Outer: {row.OuterRadius:F2}", GUILayout.Width(ValueColumnWidth));
            EditorGUILayout.LabelField($"Inner: {row.InnerRadius:F2}", GUILayout.Width(ValueColumnWidth));
            EditorGUILayout.LabelField($"Width: {row.Width:F2}", GUILayout.Width(ValueColumnWidth));
            EditorGUILayout.EndHorizontal();
        }

        private void DrawActionsSection(NeonSumoArena arena, ArenaSnapshot snapshot)
        {
            _targetRingWidth = EditorGUILayout.FloatField("Target Width", _targetRingWidth);

            EditorGUILayout.Space(5);
            if (GUILayout.Button("Log Ring Dimensions to Console"))
            {
                NeonSumoArenaLogger.Log(snapshot);
            }

            if (GUILayout.Button("Apply Ring Width (ProBuilder)"))
            {
                NeonSumoArenaRingResizer.SetRingWidths(arena, _targetRingWidth);
            }
        }
    }
}
