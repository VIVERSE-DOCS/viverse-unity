using System.Collections.Generic;
using UnityEngine;

namespace NeonSumo
{
    /// <summary>
    /// Shared utility for measuring arena geometry. Used by NeonSumoArena (runtime) and NeonSumoArenaEditor.
    /// </summary>
    public static class ArenaMeasurementUtility
    {
        private static readonly List<Renderer> s_Renderers = new List<Renderer>();
        private static readonly List<Collider> s_Colliders = new List<Collider>();

        /// <summary>
        /// Returns the horizontal (XZ) radius of a transform based on Renderer/Collider bounds, or scale fallback.
        /// </summary>
        public static float GetHorizontalRadius(Transform target)
        {
            if (target == null) return 0f;

            Bounds? combined = null;

            s_Renderers.Clear();
            target.GetComponentsInChildren(false, s_Renderers);
            for (int i = 0; i < s_Renderers.Count; i++)
            {
                Renderer r = s_Renderers[i];
                if (r == null) continue;
                if (combined == null)
                    combined = r.bounds;
                else
                {
                    var b = combined.Value;
                    b.Encapsulate(r.bounds);
                    combined = b;
                }
            }

            if (!combined.HasValue)
            {
                s_Colliders.Clear();
                target.GetComponentsInChildren(false, s_Colliders);
                for (int i = 0; i < s_Colliders.Count; i++)
                {
                    Collider c = s_Colliders[i];
                    if (c == null) continue;
                    if (combined == null)
                        combined = c.bounds;
                    else
                    {
                        var b = combined.Value;
                        b.Encapsulate(c.bounds);
                        combined = b;
                    }
                }
            }

            if (combined.HasValue)
            {
                var extents = combined.Value.extents;
                return Mathf.Max(extents.x, extents.z);
            }

            var lossy = target.lossyScale;
            return Mathf.Max(lossy.x, lossy.z) * 0.5f;
        }
    }
}
