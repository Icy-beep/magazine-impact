using System;
using System.IO;
using MagazineImpact;

internal static class SurfaceBankTests
{
    private static int checks;
    private static void Check(bool ok, string message)
    { checks++; if (!ok) throw new Exception(message); }

    public static void Main(string[] args)
    {
        string root = args[0];
        if (args.Length > 1 && args[1] == "--packaged")
        {
            var debug = new RuntimeClip("debug", new float[] { .1f, -.1f }, 1, 44100);
            int errors = 0;
            using (var complete = new SurfaceSoundBank(root, x => { }, x => errors++))
            {
                Check(errors == 0 && complete.ClipCount == 12, "All twelve packaged WAV files load.");
                foreach (SurfaceKind kind in Enum.GetValues(typeof(SurfaceKind)))
                {
                    string selectedFamily;
                    var clip = complete.Select(kind.ToString(), debug, out selectedFamily);
                    Check(!object.ReferenceEquals(clip, debug) && selectedFamily != "fallback", "Recorded sound for " + kind);
                    Check(clip.Get().samples > 0, "Playable samples for " + kind);
                }
                string unknownFamily;
                Check(object.ReferenceEquals(debug, complete.Select("Unknown", debug, out unknownFamily)), "Unknown retains debug fallback.");
            }
            debug.Dispose();
            Console.WriteLine("PASS: " + checks + " packaged surface checks.");
            return;
        }
        var fallback = new RuntimeClip("fallback", new float[] { .1f, -.1f }, 1, 44100);
        int warnings = 0;
        using (var bank = new SurfaceSoundBank(root, s => { }, s => warnings++))
        {
            Check(bank.ClipCount == 4 && warnings == 1, "Load 3 bundled and 1 override clip; reject corrupt WAV.");
            Check(UnityEngine.AudioClip.Created == 0, "Loading a bank must only cache PCM, not create native clips.");
            string family;
            var woodA = bank.Select("Wood", fallback, out family);
            Check(family == "wood" && !object.ReferenceEquals(woodA, fallback), "Wood chooses wood bank.");
            var woodB = bank.Select("WoodThin", fallback, out family);
            Check(family == "wood" && !object.ReferenceEquals(woodA, woodB), "WoodThin shares family and avoids immediate repeat.");
            var woodC = bank.Select("WoodThick", fallback, out family);
            Check(object.ReferenceEquals(woodA, woodC), "Two wood variants alternate even across submaterials.");
            var ground = bank.Select("Soil", fallback, out family);
            Check(family == "ground", "Soil chooses ground.");
            Check(object.ReferenceEquals(ground, bank.Select("Gravel", fallback, out family)), "Gravel uses the same ground recording.");
            Check(object.ReferenceEquals(fallback, bank.Select("Concrete", fallback, out family)) && family == "fallback", "Missing concrete keeps original impact.");
            Check(object.ReferenceEquals(fallback, bank.Select("Grass", fallback, out family)), "Grass is not silently relabelled as gravel.");
            Check(object.ReferenceEquals(fallback, bank.Select("Metal", fallback, out family)), "No metal recording: fallback.");
            Check(!object.ReferenceEquals(fallback, bank.Select("MetalThin", fallback, out family)) && family == "metalthin", "Exact surface overrides family.");
            Check(object.ReferenceEquals(fallback, bank.Select("Unknown", fallback, out family)), "Unknown collider keeps fallback.");
            var before = ground.Get();
            bank.Invalidate();
            var after = ground.Get();
            Check(before.Destroyed && !object.ReferenceEquals(before, after) && after.samples > 0, "Reset recovery applies to surface clips.");
            foreach (string path in Directory.GetFiles(root, "*.wav", SearchOption.AllDirectories)) File.Delete(path);
            Check(bank.Select("Soil", fallback, out family).Get().samples > 0, "Playback must not reread WAV files per impact.");
        }
        foreach (SurfaceKind surface in Enum.GetValues(typeof(SurfaceKind)))
            Check(SurfacePolicy.Family(surface.ToString()) != "default", "Map every known surface: " + surface);
        Check(SurfacePolicy.Family("Other") == "default", "Unknown enum name should map safely.");
        fallback.Dispose();
        Console.WriteLine("PASS: " + checks + " surface bank regression checks.");
    }
}
