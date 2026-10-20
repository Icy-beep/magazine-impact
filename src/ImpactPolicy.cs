using System;

namespace MagazineImpact
{
    internal static class ImpactPolicy
    {
        internal static bool ShouldPlay(float speed, float minimum, float now, float last, float interval)
        {
            return !float.IsNaN(speed) && !float.IsInfinity(speed) && speed >= minimum && now - last >= interval;
        }

        internal static float Gain(float speed, float minimum, float full)
        {
            float denominator = Math.Max(0.05f, full - minimum);
            float t = Math.Max(0f, Math.Min(1f, (speed - minimum) / denominator));
            return 0.2f + 0.8f * (float)Math.Sqrt(t);
        }
    }
}
