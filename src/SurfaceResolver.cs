using System;
using UnityEngine;

namespace MagazineImpact
{
    internal static class SurfaceResolver
    {
        internal static string Resolve(Collider collider, Vector3 point, out string method)
        {
            if (collider != null)
            {
                // Terrain and other specialized ballistics components can resolve at the hit point.
                BaseBallistic ballistic = collider.GetComponent<BaseBallistic>();
                method = "native";
                if (ballistic == null)
                {
                    ballistic = collider.GetComponentInParent<BaseBallistic>();
                    method = "parent";
                }
                if (ballistic != null) return ballistic.GetSurfaceSound(point).ToString();

                // Only a fallback when the physical collider has no game surface component.
                string marker = "_BALLISTIC_";
                string name = collider.name;
                int start = name.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                if (start >= 0)
                {
                    string tail = name.Substring(start + marker.Length);
                    foreach (string surface in Enum.GetNames(typeof(SurfaceKind)))
                        if (string.Equals(tail, surface, StringComparison.OrdinalIgnoreCase))
                        { method = "collider-name"; return surface; }
                }
            }
            method = "unknown";
            return "Unknown";
        }
    }
}
