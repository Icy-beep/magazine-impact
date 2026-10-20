// Regression harness: only the Unity audio boundary is simulated. RuntimeClip.cs is production code.
using System;
using MagazineImpact;

namespace UnityEngine
{
    public enum HideFlags { DontUnloadUnusedAsset = 32 }
    public class Object
    {
        internal bool Destroyed;
        public static void Destroy(Object value) { value.Destroyed = true; }
    }
    public sealed class AudioClip : Object
    {
        internal static int Created;
        internal static bool FailNextSetData;
        internal float[] Data;
        public int samples, channels, frequency;
        public float length { get { return frequency == 0 ? 0 : (float)samples / frequency; } }
        public HideFlags hideFlags;
        public static AudioClip Create(string name, int frames, int channels, int rate, bool stream)
        {
            Created++;
            return new AudioClip { samples = frames, channels = channels, frequency = rate };
        }
        public bool SetData(float[] data, int offset)
        {
            if (FailNextSetData) { FailNextSetData = false; return false; }
            Data = (float[])data.Clone(); return true;
        }
        internal void SimulateAudioReset() { samples = 0; channels = 0; frequency = 0; Data = null; }
    }
}

internal static class ClipLifecycleTests
{
    private static int checks;
    private static void Check(bool ok, string message)
    {
        checks++; if (!ok) throw new Exception(message);
    }
    public static void Main()
    {
        float[] pcm = { .1f, .2f, -.1f, -.2f };
        var sound = new RuntimeClip("test", pcm, 1, 44100);
        Check(UnityEngine.AudioClip.Created == 0, "Startup must not allocate a native clip.");
        pcm[0] = 0; // Caller mutation must not corrupt retained sound data.
        var first = sound.Get();
        Check(first.samples == 4 && first.Data[0] == .1f, "PCM should be retained independently.");
        Check(first.hideFlags == UnityEngine.HideFlags.DontUnloadUnusedAsset, "Protect clip against unused-asset unload.");
        Check(object.ReferenceEquals(first, sound.Get()) && sound.Generation == 1, "Valid clip should be reused.");
        first.SimulateAudioReset(); // The exact samples=0 condition from the user's log.
        var recovered = sound.Get();
        Check(!object.ReferenceEquals(first, recovered) && first.Destroyed, "Empty native buffer must be replaced.");
        Check(recovered.samples == 4 && recovered.Data[0] == .1f && sound.Generation == 2, "Recover complete original PCM.");
        sound.Invalidate();
        Check(!recovered.Destroyed && UnityEngine.AudioClip.Created == 2, "Audio callback should only mark dirty.");
        var third = sound.Get();
        Check(recovered.Destroyed && sound.Generation == 3, "Configuration reset forces recreation despite valid metadata.");
        sound.Dispose();
        Check(third.Destroyed, "Dispose releases the native clip.");

        var retry = new RuntimeClip("retry", new float[] { .4f, -.4f }, 1, 48000);
        UnityEngine.AudioClip.FailNextSetData = true;
        bool failed = false;
        try { retry.Get(); } catch (InvalidOperationException) { failed = true; }
        Check(failed && retry.Generation == 0, "Failed SetData must not cache a silent clip.");
        Check(retry.Get().samples == 2 && retry.Generation == 1, "Next playback should retry after transient create failure.");
        retry.Dispose();

        var stereo = new RuntimeClip("stereo", new float[] { 1, 0, -1, 0 }, 2, 48000);
        Check(stereo.Get().samples == 2, "Frame count must account for channel count.");
        stereo.Dispose();
        Console.WriteLine("PASS: " + checks + " clip lifecycle regression checks.");
    }
}
