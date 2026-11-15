using UnityEngine;

namespace CathedralOfFeedback
{
    /// <summary>
    /// Conducts the piece: how much the player is doing becomes the arrangement's energy (the
    /// music builds when you play and settles when you stop), held orb height sweeps the drum
    /// filter, the building lights up on entry, and the palette drifts bar by bar. Also owns
    /// Quest performance settings and a periodic frame-rate log line.
    /// </summary>
    [DefaultExecutionOrder(50)]
    public class CathedralDirector : MonoBehaviour
    {
        [SerializeField] float exposure = 1.7f, revealSeconds = 7f;
        [SerializeField] float idleEnergy = 0.27f;
        [SerializeField] float renderScale = 1.25f, targetHz = 90f;
        public OVRPassthroughLayer passthroughLayer;
        public Transform head;
        public Renderer shellRenderer;

        static readonly int MixId = Shader.PropertyToID("_CathedralMix");
        static readonly int CenterId = Shader.PropertyToID("_CathedralCenter");

        public float Energy { get; private set; }
        public float Passthrough { get; set; }

        float time, drumOpen = 1f, passMix, windowMix;
        static readonly int ViewId = Shader.PropertyToID("_CathedralView");
        float calibTime, calibPeak, rigOffset, playerHeight = 1.6f, unwornTime;
        Vector2 lastHeadFlat;
        int lastKey; float keyHue, keyHueTarget;
        bool centred;
        /// <summary>Virtual: just the building. Windows: the real room in stereo passthrough behind the lancet glass. Open: the walls dissolve into the real room.</summary>
        public enum Mode { Virtual, Windows, Open }
        int mode;
        float perfTime, perfWorst; int perfFrames;

        public int ModeIndex => mode;

        public void CycleMode()
        {
            mode = (mode + 1) % 3;
            Debug.Log("[Cathedral] mode " + (Mode)mode);
            if (Narrator.Instance != null) Narrator.Instance.OnMode((Mode)mode);
            var audioHost = CathedralAudio.Instance;
            if (audioHost != null && audioHost.Dsp != null) audioHost.Dsp.Chime(880f * Mathf.Pow(2f, new[] { 0f, 3f, 7f }[mode] / 12f));
        }

        public static void PushGlobals(float reveal, float passthrough, float time, float hue, float exposure)
        {
            Shader.SetGlobalVector(MixId, new Vector4(reveal, passthrough, time, hue));
            Shader.SetGlobalVector(CenterId, new Vector4(0f, Nave.EyeHeight, 0f, exposure));
        }

        void Awake() { Energy = idleEnergy; PushGlobals(0f, 0f, 0f, 0f, exposure); }

        void Start()
        {
#if !UNITY_EDITOR
            try
            {
                OVRManager.foveatedRenderingLevel = OVRManager.FoveatedRenderingLevel.High;
                OVRManager.useDynamicFoveatedRendering = true;
                OVRManager.suggestedCpuPerfLevel = OVRManager.ProcessorPerformanceLevel.SustainedHigh;
                OVRManager.suggestedGpuPerfLevel = OVRManager.ProcessorPerformanceLevel.SustainedHigh;
                // Measured on Quest 3: about 3 ms of GPU per frame at native resolution and 72 Hz.
                // Spend the headroom on sharper lead lines and a 90 Hz display.
                UnityEngine.XR.XRSettings.eyeTextureResolutionScale = renderScale;
                var display = OVRManager.display;
                if (display != null)
                    foreach (float hz in display.displayFrequenciesAvailable)
                        if (Mathf.Abs(hz - targetHz) < 0.5f) { display.displayFrequency = hz; Debug.Log($"[Cathedral] display {hz} Hz, render scale {renderScale}"); }
            }
            catch (System.Exception e) { Debug.LogWarning("[Cathedral] perf settings: " + e.Message); }
#endif
        }

        void Update()
        {
            float dt = Time.deltaTime;
            time += dt;

            float activity = 0f, heightSum = 0f; int held = 0;
            var orbs = GrabbableOrb.All;
            for (int i = 0; i < orbs.Count; i++)
            {
                if (!orbs[i].IsHeld) continue;
                held++; heightSum += orbs[i].Height01;
                activity += 0.34f + Mathf.Min(orbs[i].Speed, 2f) * 0.12f;
            }
            if (SeedGarden.Instance != null) activity += Mathf.Min(SeedGarden.Instance.Count, 12) * 0.03f;
            if (OrbString.Instance != null) activity += OrbString.Instance.Build * 0.25f;
            float target = Mathf.Clamp01(idleEnergy + activity);
            Energy = Mathf.Lerp(Energy, target, 1f - Mathf.Exp(-dt / (target > Energy ? 2.5f : 11f)));

            // The original trick, kept: raise the orb and the beat opens up, lower it and it goes underwater.
            float open = held > 0 ? Mathf.Lerp(0.3f, 1f, heightSum / held) : 1f;
            drumOpen = Mathf.Lerp(drumOpen, open, 1f - Mathf.Exp(-dt * 5f));
            // Menu gesture on either hand steps through the three ways of mixing in the real room.
            var hands = OrbGrabber.All;
            for (int i = 0; i < hands.Count; i++) if (hands[i].MenuDown) CycleMode();
            // Also on the M key, so a Bluetooth keyboard or `adb shell input keyevent KEYCODE_M` can drive it.
            if (Input.GetKeyDown(KeyCode.M)) CycleMode();
            Passthrough = mode == (int)Mode.Open ? 1f : 0f;
            windowMix = Mathf.MoveTowards(windowMix, mode == (int)Mode.Windows ? 1f : 0f, dt * 0.8f);
            Shader.SetGlobalVector(ViewId, new Vector4(windowMix, 0f, 0f, 0f));
            passMix = Mathf.MoveTowards(passMix, Passthrough, dt * 0.8f);
            if (passthroughLayer != null)
            {
                float through = Mathf.Max(passMix, windowMix);
                bool show = through > 0.003f;
                if (passthroughLayer.hidden == show) passthroughLayer.hidden = !show;
                if (show) passthroughLayer.textureOpacity = Mathf.Clamp01(through * 1.25f);
            }

            // Floor calibration. Nobody's eyes are 2.1 m up, and nobody plays from inside the
            // floor: when the headset's floor estimate is that far out (common when it has no
            // boundary for the room), slide the rig so the virtual floor meets the player's feet.
            // Checked continuously, so it also catches the next person to put the headset on.
            // The building forms around whoever is wearing the headset: the tracking origin can be
            // metres away from where they stand (it moves with the guardian), so when someone puts
            // the headset on, slide the rig until they are under the lantern.
            bool worn = Application.isEditor || OVRPlugin.userPresent;
            if (!worn) unwornTime += dt;
            else
            {
                if (head != null && (!centred || unwornTime > 10f) && time > 0.5f)
                {
                    Vector3 hp = head.position;
                    if (new Vector2(hp.x, hp.z).sqrMagnitude > 0.09f)
                    {
                        head.root.position -= new Vector3(hp.x, 0f, hp.z);
                        Debug.Log($"[Cathedral] recentred on the wearer (was {hp.x:F1},{hp.z:F1} from the middle)");
                    }
                    centred = true;
                }
                else if (head != null && centred)
                {
                    // The tracking origin can also move while the headset is worn: the OS re-finds
                    // its space a few seconds after it is put on, or the boundary is redrawn. Nobody
                    // walks a metre in one frame and nobody walks out through the glass, so in
                    // either case put the wearer back under the lantern.
                    Vector3 hp = head.position;
                    Vector2 flat = new Vector2(hp.x, hp.z);
                    bool jumped = (flat - lastHeadFlat).magnitude > 1f;
                    if (jumped || flat.magnitude > Nave.Radius - 1.5f)
                    {
                        head.root.position -= new Vector3(hp.x, 0f, hp.z);
                        Debug.Log($"[Cathedral] tracking space moved ({(jumped ? "jump" : "outside")}): recentred (was {hp.x:F1},{hp.z:F1} from the middle)");
                    }
                }
                if (head != null) lastHeadFlat = new Vector2(head.position.x, head.position.z);
                unwornTime = 0f;
            }

            if (head != null)
            {
                float y = head.position.y;
                // Sustained, not momentary: a jump or a reach should not move the world.
                bool high = y > 2.1f, low = y < 0.45f;
                calibTime = high || low ? calibTime + dt : 0f;
                calibPeak = high || low ? (high ? Mathf.Max(calibPeak, y) : y) : 0f;
                if (calibTime > 1.5f)
                {
                    float shift = Nave.EyeHeight + 0.12f - calibPeak;
                    rigOffset += shift; calibTime = 0f; calibPeak = 0f;
                    Debug.Log($"[Cathedral] eyes at {y:F2} m: floor estimate is off, shifting rig {shift:+0.00;-0.00} m");
                }
                Transform rig = head.root;
                Vector3 rp = rig.position;
                rp.y = Mathf.MoveTowards(rp.y, rigOffset, dt * 0.7f);
                rig.position = rp;
                // Standing height follows the tallest recent pose, and forgets it slowly.
                playerHeight = Mathf.Max(Mathf.Lerp(playerHeight, y, 1f - Mathf.Exp(-dt / 20f)), Mathf.Min(y, 2.0f));
                GrabbableOrb.PlayerHeight = Mathf.Clamp(playerHeight, 1.0f, 2.0f);
            }

            var audioHost = CathedralAudio.Instance;
            if (audioHost != null && audioHost.Dsp != null) { audioHost.Dsp.Energy = Energy; audioHost.Dsp.DrumOpen = drumOpen; }
            var an = SpectrumAnalyzer.Instance;
            float bars = 0f;
            if (an != null) { an.Energy = Energy; bars = (float)(an.BeatPosition / 4.0); }

            // Each drop moves the music to a new key; the colour of the light moves with it. New
            // light is written at the foot of the walls, so the new palette climbs up through the old.
            if (audioHost != null && audioHost.Dsp != null && audioHost.Dsp.KeyIndex != lastKey)
            {
                lastKey = audioHost.Dsp.KeyIndex; keyHueTarget += 0.29f;
                Debug.Log($"[Cathedral] key change: now key {lastKey}");
            }
            keyHue = Mathf.MoveTowards(keyHue, keyHueTarget, Time.deltaTime * 0.4f);
            PushGlobals(Mathf.Clamp01(time / revealSeconds), passMix, time, Mathf.Repeat(bars * 0.045f + keyHue, 1f), exposure);

            perfFrames++; perfTime += Time.unscaledDeltaTime; perfWorst = Mathf.Max(perfWorst, Time.unscaledDeltaTime);
            if (perfTime >= 5f)
            {
                int handsTracked = 0;
                for (int i = 0; i < hands.Count; i++) if (hands[i].HandMode) handsTracked++;
                string gpu = "";
#if !UNITY_EDITOR
                try
                {
                    float? ms = OVRPlugin.GetPerfMetricsFloat(OVRPlugin.PerfMetrics.App_GpuTime_Float);
                    float? util = OVRPlugin.GetPerfMetricsFloat(OVRPlugin.PerfMetrics.System_GpuUtilPercentage_Float);
                    gpu = $"gpu {(ms.HasValue ? ms.Value * 1000f : -1f):F1}ms util {(util.HasValue ? util.Value * 100f : -1f):F0}% ";
                }
                catch (System.Exception) { }
#endif
                string pose = head != null ? $"head {head.position.x:F1},{head.position.y:F2},{head.position.z:F1} fwd {head.forward.x:F1},{head.forward.y:F1},{head.forward.z:F1}" : "no head";
                Debug.Log($"[Cathedral] fps {perfFrames / perfTime:F1} worst {perfWorst * 1000f:F1}ms energy {Energy:F2} held {held} hands {handsTracked} " +
                          gpu + $"notes {(SeedGarden.Instance != null ? SeedGarden.Instance.Count : 0)} audio {(an != null ? an.Level : -1f):F3} mode {(Mode)mode} pass {passMix:F1} beat {(an != null ? an.BeatPosition : 0):F0} {pose} rig {rigOffset:F2}");
                perfFrames = 0; perfTime = 0f; perfWorst = 0f;
            }
        }
    }
}
