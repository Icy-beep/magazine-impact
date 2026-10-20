using Comfort.Common;
using UnityEngine;

namespace MagazineImpact
{
    internal static class GameAudio
    {
        internal static bool IsReady
        {
            get
            {
                var audio = Singleton<BetterAudio>.Instance;
                return audio != null && audio.ListenerTransform != null;
            }
        }

        internal static bool PlayImpact(AudioClip clip, Vector3 point, float gain, float range, out string status)
        {
            if (clip == null || clip.samples <= 0 || clip.length <= 0)
            {
                status = "Rejected invalid/empty audio clip";
                return false;
            }
            var audio = Singleton<BetterAudio>.Instance;
            if (audio == null || audio.ListenerTransform == null)
            {
                status = "BetterAudio/listener not ready";
                return false;
            }
            float distance = Vector3.Distance(audio.ListenerTransform.position, point);
            // Use the game's pooled sources and mixer routes.
            // No occlusion in this diagnostic build: isolate routing from propagation/occlusion.
            BetterSource source = audio.PlayAtPoint(point, clip, BetterAudio.AudioSourceGroupType.Collisions,
                Mathf.CeilToInt(range), gain, EOcclusionTest.None, null, true, true, true, false);
            status = "EFT audio: clip=" + clip.name + ", clipSamples=" + clip.samples + ", clipLength=" + clip.length.ToString("F3") +
                "s, distance=" + distance.ToString("F2") + "m; " + Describe(source) +
                "; listenerVolume=" + AudioListener.volume.ToString("F2") + "; listenerPause=" + AudioListener.pause;
            return source != null;
        }

        internal static void PlayTest(AudioClip clip, bool spatial, out string status)
        {
            if (clip == null || clip.samples <= 0 || clip.length <= 0)
            {
                status = "Rejected invalid/empty audio clip";
                return;
            }
            var audio = Singleton<BetterAudio>.Instance;
            if (audio == null || audio.ListenerTransform == null)
            {
                status = "BetterAudio/listener not ready. Run the test inside a raid.";
                return;
            }
            if (spatial)
            {
                Vector3 point = audio.ListenerTransform.position + audio.ListenerTransform.forward * 2f;
                PlayImpact(clip, point, 1f, 30f, out status);
            }
            else
            {
                audio.PlayNonspatial(clip, BetterAudio.AudioSourceGroupType.NonspatialBypass, 0f, 1f, null);
                status = "NonspatialBypass requested; clip=" + clip.name + ", samples=" + clip.samples +
                    ", loadState=" + clip.loadState + ", listenerVolume=" + AudioListener.volume.ToString("F2") +
                    ", listenerPause=" + AudioListener.pause;
            }
        }

        private static string Describe(BetterSource source)
        {
            if (source == null) return "no source returned (out of range or pool unavailable)";
            AudioSource unity = source.source1;
            if (unity == null) return "BetterSource returned without source1";
            return "source=" + unity.name + ", active=" + unity.gameObject.activeInHierarchy +
                ", enabled=" + unity.enabled + ", playing=" + unity.isPlaying +
                ", mute=" + unity.mute + ", volume=" + unity.volume.ToString("F3") +
                ", mixer=" + (unity.outputAudioMixerGroup == null ? "none" : unity.outputAudioMixerGroup.name);
        }
    }
}
