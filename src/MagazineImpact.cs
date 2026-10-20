using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using BepInEx;
using BepInEx.Configuration;
using EFT.Interactive;
using EFT.InventoryLogic;
using HarmonyLib;
using UnityEngine;

namespace MagazineImpact
{
    [BepInPlugin(Id, "Magazine Impact", "0.2.1")]
    [BepInProcess("EscapeFromTarkov.exe")]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Id = "local.magazineimpact";
        internal static Plugin Instance;
        private Harmony harmony;
        private ConfigEntry<bool> active, debug, test2d, test3d, useSurfaces;
        private ConfigEntry<SurfaceKind> testSurface;
        private ConfigEntry<float> volume, minSpeed, fullSpeed, cooldown, maxDistance;
        private readonly ConditionalWeakTable<LootItem, ImpactState> states =
            new ConditionalWeakTable<LootItem, ImpactState>();
        private RuntimeClip sound;
        private SurfaceSoundBank surfaceSounds;
        private readonly Dictionary<RuntimeClip, int> loggedGenerations = new Dictionary<RuntimeClip, int>();
        private volatile bool configurationChanged;
        private bool collisionErrorReported;
        private int diagnosticImpacts;

        private void Awake()
        {
            active = Config.Bind("General", "Enabled", true, "Play magazine collision sounds.");
            debug = Config.Bind("General", "Debug logging", false, "Log accepted impacts to BepInEx/LogOutput.log.");
            volume = Config.Bind("Sound", "Volume", 0.7f,
                new ConfigDescription("Maximum impact volume.", new AcceptableValueRange<float>(0f, 1f)));
            minSpeed = Config.Bind("Collision", "Minimum impact speed", 0.45f,
                new ConfigDescription("Ignore normal collision speeds below this value (m/s).", new AcceptableValueRange<float>(0.05f, 5f)));
            fullSpeed = Config.Bind("Collision", "Full volume speed", 3.5f,
                new ConfigDescription("Normal collision speed producing maximum volume (m/s).", new AcceptableValueRange<float>(0.1f, 15f)));
            cooldown = Config.Bind("Collision", "Minimum interval", 0.10f,
                new ConfigDescription("Minimum seconds between sounds for the same magazine.", new AcceptableValueRange<float>(0.02f, 1f)));
            maxDistance = Config.Bind("Sound", "Audible distance", 22f,
                new ConfigDescription("Maximum playback distance in metres, passed to the EFT sound system.", new AcceptableValueRange<float>(2f, 80f)));
            test2d = Config.Bind("Diagnostics", "Play test sound 2D", false,
                "Turn on once in raid. Resets automatically; plays a nonspatial sound after 1 second. Close F12 to listen.");
            test3d = Config.Bind("Diagnostics", "Play test sound 3D", false,
                "Turn on once in raid. Resets automatically; plays 2 metres in front of the game listener after 1 second.");
            useSurfaces = Config.Bind("Sound", "Use surface sounds", true,
                "Choose a sound from sounds/<surface or family> using the game's hit surface. Missing banks use impact.wav.");
            testSurface = Config.Bind("Diagnostics", "Test surface", SurfaceKind.Wood,
                "Surface used by both test buttons. Wood selects S&W recordings; Soil/Gravel selects G3. Other missing banks use impact.wav.");
            test2d.Value = test3d.Value = false;

            try
            {
                sound = LoadSound(Path.Combine(Path.GetDirectoryName(Info.Location), "impact.wav"));
                surfaceSounds = new SurfaceSoundBank(Path.Combine(Path.GetDirectoryName(Info.Location), "sounds"),
                    message => Logger.LogInfo(message), message => Logger.LogWarning(message));
                AudioSettings.OnAudioConfigurationChanged += OnAudioConfigurationChanged;

                var collision = AccessTools.DeclaredMethod(typeof(LootItem), "OnCollisionEnter", new[] { typeof(Collision) });
                var start = AccessTools.DeclaredMethod(typeof(LootItem), "StartPhysics", Type.EmptyTypes);
                var drop = AccessTools.DeclaredMethod(typeof(LootItem), "PlayDropSound", Type.EmptyTypes);
                if (collision == null || start == null || drop == null)
                    throw new MissingMethodException("Expected SPT 4.1.6 LootItem methods were not found.");

                Instance = this;
                harmony = new Harmony(Id);
                harmony.Patch(collision, prefix: new HarmonyMethod(typeof(Plugin), nameof(CollisionPrefix)));
                harmony.Patch(start, postfix: new HarmonyMethod(typeof(Plugin), nameof(StartPhysicsPostfix)));
                harmony.Patch(drop, prefix: new HarmonyMethod(typeof(Plugin), nameof(DropSoundPrefix)));
                Logger.LogInfo("Magazine Impact 0.2.1 ready. Material-based sounds; " + surfaceSounds.ClipCount +
                    " surface clips cached. Audio reset recovery enabled.");
            }
            catch (Exception error)
            {
                AudioSettings.OnAudioConfigurationChanged -= OnAudioConfigurationChanged;
                if (harmony != null) harmony.UnpatchSelf();
                Instance = null;
                Logger.LogError("Magazine Impact disabled: " + error);
                ReleaseAudio();
            }
        }

        private static bool IsMagazine(LootItem loot)
        {
            // Checks the root item only: a rifle containing a magazine is not a loose magazine.
            return loot != null && loot.Item is Magazine;
        }

        private static bool CollisionPrefix(LootItem __instance, Collision __0)
        {
            var plugin = Instance;
            if (plugin == null || !plugin.active.Value || !IsMagazine(__instance)) return true;
            try
            {
                plugin.OnImpact(__instance, __0);
                // Replace the stock sound for magazines, including deliberately filtered contacts.
                // LootItem.OnCollisionEnter in the supplied DLL only handles drop audio/surface bookkeeping.
                return false;
            }
            catch (Exception error)
            {
                if (!plugin.collisionErrorReported)
                {
                    plugin.collisionErrorReported = true;
                    plugin.Logger.LogError("Impact failed; allowing stock collision handler. " + error);
                }
                return true;
            }
        }

        private static void StartPhysicsPostfix(LootItem __instance)
        {
            // Loot GameObjects are pooled. Reset for every physics activation, not just creation.
            if (Instance != null) Instance.states.Remove(__instance);
        }

        private static bool DropSoundPrefix(LootItem __instance)
        {
            // Stock fallback uses a downward ray when physics finishes, not the impact event.
            // Suppress it for magazines to avoid a duplicate or a sound without a collision.
            return Instance == null || !Instance.active.Value || !IsMagazine(__instance);
        }

        private void OnImpact(LootItem loot, Collision collision)
        {
            if (collision == null || collision.contactCount == 0) return;
            float speed = 0f;
            Vector3 point = collision.GetContact(0).point;
            for (int i = 0; i < collision.contactCount; i++)
            {
                ContactPoint contact = collision.GetContact(i);
                float normalSpeed = Mathf.Abs(Vector3.Dot(collision.relativeVelocity, contact.normal));
                if (normalSpeed > speed) { speed = normalSpeed; point = contact.point; }
            }
            var state = states.GetValue(loot, key => new ImpactState());
            float now = Time.unscaledTime;
            if (!ImpactPolicy.ShouldPlay(speed, minSpeed.Value, now, state.LastTime, cooldown.Value)) return;
            // Consume the contact even if all voices are busy, so collider chatter cannot queue audio.
            state.LastTime = now;
            float gain = volume.Value * ImpactPolicy.Gain(speed, minSpeed.Value, fullSpeed.Value);
            string resolvedBy;
            string surface = SurfaceResolver.Resolve(collision.collider, point, out resolvedBy);
            string family;
            RuntimeClip selected = SelectSound(surface, out family);
            string playback;
            bool played = GameAudio.PlayImpact(GetReadyClip(selected), point, gain, maxDistance.Value, out playback);
            if (debug.Value || diagnosticImpacts++ < 3)
                Logger.LogInfo("Magazine collision: normal speed=" + speed.ToString("F2") +
                    "m/s, position=" + point + ", collider=" + collision.collider.name +
                    "; surface=" + surface + " (" + resolvedBy + "), bank=" + family +
                    "; sourceAcquired=" + played + "; " + playback);
        }

        private void Update()
        {
            if (configurationChanged)
            {
                configurationChanged = false;
                Logger.LogInfo("Audio configuration changed; runtime clip invalidated for recreation at next playback.");
            }
            if (test2d != null && test2d.Value)
            {
                test2d.Value = false;
                StartCoroutine(TestSound(false));
            }
            if (test3d != null && test3d.Value)
            {
                test3d.Value = false;
                StartCoroutine(TestSound(true));
            }
        }

        private IEnumerator TestSound(bool spatial)
        {
            yield return new WaitForSecondsRealtime(1f);
            try
            {
                string status;
                if (!GameAudio.IsReady)
                {
                    Logger.LogInfo("Test sound: BetterAudio/listener not ready. Run the test inside a raid.");
                    yield break;
                }
                string family;
                RuntimeClip selected = SelectSound(testSurface.Value.ToString(), out family);
                GameAudio.PlayTest(GetReadyClip(selected), spatial, out status);
                Logger.LogInfo("Test sound " + (spatial ? "3D" : "2D") + ": surface=" + testSurface.Value +
                    ", bank=" + family + "; " + status);
            }
            catch (Exception error) { Logger.LogError("Test sound failed: " + error); }
        }

        private RuntimeClip LoadSound(string path)
        {
            float[] samples;
            int channels, rate;
            try
            {
                Wav.ReadPcm16(File.ReadAllBytes(path), out samples, out channels, out rate);
                Logger.LogInfo("Cached impact.wav PCM: frames=" + samples.Length / channels + ", channels=" + channels + ", rate=" + rate + ". Native clip deferred until playback.");
            }
            catch (Exception error)
            {
                Logger.LogWarning("Using generated test impact: " + error.Message);
                rate = 44100;
                channels = 1;
                samples = new float[6615];
                var random = new System.Random(7416);
                for (int i = 0; i < samples.Length; i++)
                {
                    double t = (double)i / rate;
                    samples[i] = (float)(0.7 * (random.NextDouble() * 2 - 1) * Math.Exp(-85 * t) +
                        0.22 * Math.Sin(2 * Math.PI * 1900 * t) * Math.Exp(-42 * t));
                }
            }
            return new RuntimeClip("MagazineImpact", samples, channels, rate);
        }

        private RuntimeClip SelectSound(string surface, out string family)
        {
            family = "fallback";
            return useSurfaces.Value && surfaceSounds != null ? surfaceSounds.Select(surface, sound, out family) : sound;
        }

        private AudioClip GetReadyClip(RuntimeClip selected)
        {
            AudioClip result = selected.Get();
            int loggedGeneration;
            if (!loggedGenerations.TryGetValue(selected, out loggedGeneration) || selected.Generation != loggedGeneration)
            {
                loggedGenerations[selected] = selected.Generation;
                Logger.LogInfo("Runtime clip '" + result.name + "' generation=" + selected.Generation + ": samples=" + result.samples +
                    ", length=" + result.length.ToString("F3") + "s, loadState=" + result.loadState);
            }
            return result;
        }

        private void OnAudioConfigurationChanged(bool deviceWasChanged)
        {
            // Mark only; recreate from cached PCM on the main thread at the next playback.
            if (sound != null) sound.Invalidate();
            if (surfaceSounds != null) surfaceSounds.Invalidate();
            configurationChanged = true;
        }

        private void OnDestroy()
        {
            AudioSettings.OnAudioConfigurationChanged -= OnAudioConfigurationChanged;
            if (harmony != null) harmony.UnpatchSelf();
            if (Instance == this) Instance = null;
            ReleaseAudio();
        }

        private void ReleaseAudio()
        {
            if (sound != null) { sound.Dispose(); sound = null; }
            if (surfaceSounds != null) { surfaceSounds.Dispose(); surfaceSounds = null; }
            loggedGenerations.Clear();
        }

        private sealed class ImpactState
        {
            public float LastTime = float.NegativeInfinity;
        }
    }
}
