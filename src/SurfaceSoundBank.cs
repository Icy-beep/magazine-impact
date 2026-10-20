using System;
using System.Collections.Generic;
using System.IO;

namespace MagazineImpact
{
    internal sealed class SurfaceSoundBank : IDisposable
    {
        private readonly Dictionary<string, RuntimeClip[]> banks = new Dictionary<string, RuntimeClip[]>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> last = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly Random random = new Random();
        internal int ClipCount { get; private set; }

        internal SurfaceSoundBank(string root, Action<string> info, Action<string> warning)
        {
            if (!Directory.Exists(root)) { info("No sounds directory; using the original test impact."); return; }
            foreach (string folder in Directory.GetDirectories(root))
            {
                var loaded = new List<RuntimeClip>();
                string key = Path.GetFileName(folder).ToLowerInvariant();
                string[] files = Directory.GetFiles(folder, "*.wav");
                Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                foreach (string file in files)
                {
                    try
                    {
                        float[] samples; int channels, rate;
                        Wav.ReadPcm16(File.ReadAllBytes(file), out samples, out channels, out rate);
                        loaded.Add(new RuntimeClip(key + "/" + Path.GetFileNameWithoutExtension(file), samples, channels, rate));
                        ClipCount++;
                    }
                    catch (Exception error) { warning("Skipped surface WAV " + file + ": " + error.Message); }
                }
                if (loaded.Count > 0)
                {
                    banks[key] = loaded.ToArray();
                    info("Surface sound bank '" + key + "': " + loaded.Count + " clips cached.");
                }
            }
        }

        internal RuntimeClip Select(string surface, RuntimeClip fallback, out string family)
        {
            // An exact surface directory, e.g. sounds/MetalThin, takes priority over its family.
            family = surface.ToLowerInvariant();
            RuntimeClip[] clips;
            if (!banks.TryGetValue(surface, out clips))
            {
                family = SurfacePolicy.Family(surface);
                if (!banks.TryGetValue(family, out clips))
                { family = "fallback"; return fallback; }
            }
            int previous;
            int index = random.Next(clips.Length);
            if (clips.Length > 1 && last.TryGetValue(family, out previous))
                index = (previous + 1 + random.Next(clips.Length - 1)) % clips.Length;
            last[family] = index;
            return clips[index];
        }

        internal void Invalidate()
        {
            foreach (RuntimeClip[] clips in banks.Values)
                foreach (RuntimeClip clip in clips) clip.Invalidate();
        }

        public void Dispose()
        {
            foreach (RuntimeClip[] clips in banks.Values)
                foreach (RuntimeClip clip in clips) clip.Dispose();
            banks.Clear();
            last.Clear();
        }
    }
}
