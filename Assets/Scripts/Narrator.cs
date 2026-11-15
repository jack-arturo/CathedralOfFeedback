using UnityEngine;

namespace CathedralOfFeedback
{
    /// <summary>
    /// AutoJack's voice. A short tour that teaches the gestures in the order people find them:
    /// each line waits for the player to do the thing (and confirms it), or nudges them after a
    /// while if they have not. The music drops well back under the voice. The tour starts again
    /// when the headset has been off for a while, so every new wearer gets welcomed. Also plays
    /// the recorded effects (bell, choir, breaking glass, the harvest swirl).
    /// </summary>
    [DefaultExecutionOrder(60)]
    public class Narrator : MonoBehaviour
    {
        public static Narrator Instance { get; private set; }
        public enum Fx { Bell, Choir, Shatter, Rise, Catch }

        [SerializeField] AudioSource voice, effects, choirSource;
        [SerializeField] AudioClip welcome, grabHint, held, plantHint, plantDone, throwHint, both, drop, prism, world, harvest, catchHint, catchDone, bloom, secretFirst, secretAll;
        [SerializeField] AudioClip bell, choir, shatter, rise, catchFx;
        [Tooltip("Seconds off the head before the tour is reset for the next wearer.")]
        [SerializeField] float resetAfter = 20f;
        [Tooltip("How far the music is pulled down while AutoJack speaks.")]
        [SerializeField, Range(0, 1)] float duckTo = 0.2f;

        int stage;
        float timer, gap = 10f, wornTime, offTime, heldFor, duck, dropAt = -1f;
        bool saidGrabHint, saidPlantHint, saidWorld, saidDrop, saidHarvest, saidCatchHint, saidCatchDone, saidBloom, saidSecret, dropped;
        AudioClip pending;

        void Awake() { Instance = this; }
        void OnDestroy() { if (Instance == this) Instance = null; }

        void ResetTour()
        {
            stage = 0; timer = 0f; wornTime = 0f; heldFor = 0f; dropAt = -1f; pending = null;
            saidGrabHint = saidPlantHint = saidWorld = saidDrop = saidHarvest = saidCatchHint = saidCatchDone = saidBloom = saidSecret = dropped = false;
            if (voice != null) voice.Stop();
        }

        void Say(AudioClip clip)
        {
            if (clip == null || voice == null) return;
            if (voice.isPlaying) { pending = clip; return; }
            voice.clip = clip; voice.Play(); gap = 0f;
        }

        /// <summary>Play one of the recorded effects.</summary>
        public void Effect(Fx kind, float gain)
        {
            if (effects == null) return;
            AudioClip clip = kind == Fx.Bell ? bell : kind == Fx.Choir ? choir : kind == Fx.Shatter ? shatter : kind == Fx.Rise ? rise : catchFx;
            if (clip == null) return;
            if (kind == Fx.Choir && choirSource != null)
            {
                // The choir is a held note, so it has to follow the piece into whatever key a drop has moved it to.
                var host = CathedralAudio.Instance;
                choirSource.pitch = host != null && host.Dsp != null ? host.Dsp.KeyRatio : 1f;
                choirSource.PlayOneShot(clip, gain);
            }
            else effects.PlayOneShot(clip, gain);
        }

        /// <summary>A drop (the string, or a harvest): bell, choir, and (once) a word from AutoJack.</summary>
        public void OnDrop()
        {
            dropped = true;
            Effect(Fx.Bell, 0.85f);
            Effect(Fx.Choir, 0.55f);
            if (!saidDrop) { saidDrop = true; dropAt = Time.time + 0.9f; }
        }

        /// <summary>A comet is on its way back from a column. The first time, say it can be caught.</summary>
        public void OnRebound()
        {
            if (saidCatchHint || stage < 1) return;
            saidCatchHint = true; Say(catchHint);
        }

        /// <summary>The first catch: say what a run of them does.</summary>
        public void OnCatch()
        {
            if (saidCatchDone) return;
            saidCatchDone = saidCatchHint = true; Say(catchDone);
        }

        /// <summary>A secret's gem lit. Say what the gems are the first time, and say so when all are found.</summary>
        public void OnSecret(int count, bool all)
        {
            if (all) Say(secretAll);
            else if (!saidSecret) { saidSecret = true; Say(secretFirst); }
        }

        /// <summary>All eight columns were lit. Say so the first time.</summary>
        public void OnBloom()
        {
            if (saidBloom) return;
            saidBloom = true; Say(bloom);
        }

        /// <summary>The player found the clap on their own; no need to explain it.</summary>
        public void OnHarvest() { saidHarvest = true; }

        /// <summary>The player changed room mode. The first time the real room appears, say so.</summary>
        public void OnMode(CathedralDirector.Mode mode)
        {
            if (saidWorld || (mode != CathedralDirector.Mode.Windows && mode != CathedralDirector.Mode.Open)) return;
            saidWorld = true;
            Effect(Fx.Choir, 0.4f);
            Say(world);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            bool speaking = voice != null && voice.isPlaying;

            // Pull the music back under the voice: quickly down, slowly back.
            duck = Mathf.MoveTowards(duck, speaking ? 1f : 0f, dt * (speaking ? 10f : 1.2f));
            var audioHost = CathedralAudio.Instance;
            if (audioHost != null && audioHost.Dsp != null) audioHost.Dsp.Master = Mathf.Lerp(0.85f, 0.85f * duckTo, duck) * CathedralDirector.MusicLevel;

            bool worn = Application.isEditor || OVRPlugin.userPresent;
            if (!worn)
            {
                offTime += dt;
                if (offTime > resetAfter && (stage != 0 || wornTime > 0f)) ResetTour();
                return;
            }
            offTime = 0f; wornTime += dt;

            if (speaking) { gap = 0f; return; }
            gap += dt;
            if (pending != null && gap > 0.6f) { var p = pending; pending = null; Say(p); return; }
            if (dropAt > 0f && Time.time > dropAt) { dropAt = -1f; Say(drop); return; }
            timer += dt;

            int holding = 0;
            var orbs = GrabbableOrb.All;
            for (int i = 0; i < orbs.Count; i++) if (orbs[i].IsHeld) holding++;
            heldFor = holding > 0 ? heldFor + dt : 0f;
            int planted = SeedGarden.Instance != null ? SeedGarden.Instance.Count : 0;

            // Once a few notes are hanging, tell them about the clap (unless they found it).
            if (!saidHarvest && stage >= 3 && planted >= 4 && gap > 2.5f) { saidHarvest = true; Say(harvest); return; }

            switch (stage)
            {
                case 0:     // arrive
                    if (wornTime > 3.5f) { Say(welcome); stage = 1; timer = 0f; }
                    break;
                case 1:     // take a light
                    if (heldFor > 0.6f) { Say(held); stage = 2; timer = 0f; }
                    else if (timer > 13f && !saidGrabHint) { saidGrabHint = true; Say(grabHint); }
                    break;
                case 2:     // leave a note
                    if (planted > 0 && gap > 1f) { Say(plantDone); stage = 3; timer = 0f; }
                    else if (timer > 6f && !saidPlantHint) { saidPlantHint = true; timer = 0f; Say(plantHint); }
                    else if (saidPlantHint && timer > 14f) { stage = 3; timer = 0f; }
                    break;
                case 3:     // throw
                    if (timer > 8f) { Say(throwHint); stage = 4; timer = 0f; }
                    break;
                case 4:     // the string
                    if ((timer > 14f || holding >= 2) && gap > 1.5f) { Say(both); stage = 5; timer = 0f; }
                    break;
                case 5:     // the room
                    if ((timer > 16f || dropped) && gap > 2.5f) { if (!saidWorld) Say(prism); stage = 6; }
                    break;
            }
        }
    }
}
