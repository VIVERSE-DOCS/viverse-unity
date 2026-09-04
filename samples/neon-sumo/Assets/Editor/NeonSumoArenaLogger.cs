using UnityEngine;

namespace NeonSumo.Editor
{
    /// <summary>
    /// Logs arena ring dimensions to the console. Reusable from inspector and menu items.
    /// </summary>
    internal static class NeonSumoArenaLogger
    {
        public static void Log(ArenaSnapshot snapshot)
        {
            if (snapshot.DimensionRows == null) return;

            foreach (var row in snapshot.DimensionRows)
            {
                Debug.Log($"[NeonSumoArena] {row.Name}: outer={row.OuterRadius:F2} inner={row.InnerRadius:F2} width={row.Width:F2}");
            }

            if (snapshot.CoreTransform != null)
            {
                Debug.Log($"[NeonSumoArena] {snapshot.CoreTransform.name} (Core): radius={snapshot.CoreRadius:F2}");
            }
        }
    }
}
