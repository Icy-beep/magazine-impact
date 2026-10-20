using System;
using UnityEngine;

namespace MagazineImpact
{
    // Keep PCM independently of the native audio object. Unity audio configuration changes
    // discard script-created clip data, even when the managed AudioClip object survives.
    internal sealed class RuntimeClip : IDisposable
    {
        private readonly float[] pcm;
        private readonly int channels, rate;
        private readonly string name;
        private AudioClip clip;
        private volatile bool invalidated;
        internal int Generation { get; private set; }

        internal RuntimeClip(string name, float[] pcm, int channels, int rate)
        {
            if (pcm == null || pcm.Length == 0 || channels < 1 || channels > 2 ||
                pcm.Length % channels != 0 || rate < 8000 || rate > 192000)
                throw new ArgumentException("Invalid PCM format.");
            this.name = name;
            this.pcm = (float[])pcm.Clone();
            this.channels = channels;
            this.rate = rate;
        }

        internal void Invalidate() { invalidated = true; }

        internal AudioClip Get()
        {
            int frames = pcm.Length / channels;
            if (!invalidated && IsValid(clip, frames)) return clip;
            Release();
            AudioClip candidate = AudioClip.Create(name, frames, channels, rate, false);
            if (candidate == null) throw new InvalidOperationException("AudioClip.Create returned null.");
            try
            {
                candidate.hideFlags = HideFlags.DontUnloadUnusedAsset;
                if (!candidate.SetData(pcm, 0) || !IsValid(candidate, frames))
                    throw new InvalidOperationException("Runtime AudioClip has no valid PCM buffer after creation.");
                clip = candidate;
                invalidated = false;
                Generation++;
                return clip;
            }
            catch
            {
                UnityEngine.Object.Destroy(candidate);
                throw;
            }
        }

        private bool IsValid(AudioClip value, int frames)
        {
            return value != null && value.samples == frames && value.channels == channels &&
                value.frequency == rate && value.length > 0;
        }

        private void Release()
        {
            if (clip != null) UnityEngine.Object.Destroy(clip);
            clip = null;
        }

        public void Dispose() { Release(); }
    }
}
