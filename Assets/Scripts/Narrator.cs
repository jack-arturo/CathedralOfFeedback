using UnityEngine;

namespace CathedralOfFeedback
{
    /// <summary>
    /// AutoJack's voice. A short tour that teaches the gestures in the order people find them:
    /// each line waits for the player to do the thing (and confirms it), or nudges them after a
    /// while if they have not. The music ducks under the voice. The tour starts again when the
    /// headset has been off for a while, so every new wearer gets welcomed.
    /// </summary>
    [DefaultExecutionOrder(60)]
    public class Narrator : MonoBehaviour
    {
        public static Narrator Instance { get; private set; }

        [SerializeField] AudioSource voice, effects;
        [SerializeField] AudioClip welcome, grabHint, held, plantHint, plantDone, throwHint, both, drop, prism, world;
        [SerializeField] AudioClip bell, choir;
        [Tooltip("Seconds off the head before the tour is reset for the next wearer.")]
        [SerializeField] float resetAfter = 20f;
        [SerializeField, Range(0, 1)] float duckTo = 0.5f;

        int stage;
        float timer, gap = 10f, wornTime, offTime, heldFor, duck, dropAt = -1f;
        bool saidGrabHint, saidPlantHint, saidWorld, saidDrop, dropped;
        AudioClip pending;

        void Awake() { Instance = this; }
        void OnDestroy() { if (Instance == this) Instance = null; }

        void ResetTour()
        {
            stage = 0; timer = 0f; wornTime = 0f; heldFor = 0f; dropAt = -1f; pending = null;
            saidGrabHint = saidPlantHint = saidWorld = saidDrop = dropped = false;
            if (voice != null) voice.Stop();
        }

        void Say(AudioClip clip)
        {
            if (clip == null || voice == null) return;
            if (voice.isPlaying) { pending = clip; return; }
            voice.clip = clip; voice.Play(); gap = 0f;
        }

        /// <summary>The string was brought back together: bell, choir, and (once) a word from AutoJack.</summary>
        public void OnDrop()
        {
            dropped = true;
            if (effects != null)
            {
                if (bell != null) effects.PlayOneShot(bell, 0.85f);
                if (choir != null) effects.PlayOneShot(choir, 0.55f);
            }
            if (!saidDrop) { saidDrop = true; dropAt = Time.time + 0.9f; }
        }

        /// <summary>The player changed room mode. The first time the real room appears, say so.</summary>
        public void OnMode(CathedralDirector.Mode mode)
        {
            if (saidWorld || (mode != CathedralDirector.Mode.Windows && mode != CathedralDirector.Mode.Open)) return;
            saidWorld = true;
            if (effects != null && choir != null) effects.PlayOneShot(choir, 0.4f);
            Say(world);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            bool speaking = voice != null && voice.isPlaying;

            // Duck the music under the voice.
            duck = Mathf.MoveTowards(duck, speaking ? 1f : 0f, dt * (speaking ? 6f : 1.5f));
            var audioHost = CathedralAudio.Instance;
            if (audioHost != null && audioHost.Dsp != null) audioHost.Dsp.Master = Mathf.Lerp(0.85f, 0.85f * duckTo, duck);

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
                    else if (timer > 9f && !saidPlantHint) { saidPlantHint = true; timer = 0f; Say(plantHint); }
                    else if (saidPlantHint && timer > 14f) { stage = 3; timer = 0f; }
                    break;
                case 3:     // throw
                    if (timer > 7f) { Say(throwHint); stage = 4; timer = 0f; }
                    break;
                case 4:     // the string
                    if ((timer > 12f || holding >= 2) && gap > 1.5f) { Say(both); stage = 5; timer = 0f; }
                    break;
                case 5:     // the room
                    if ((timer > 16f || dropped) && gap > 2.5f) { if (!saidWorld) Say(prism); stage = 6; }
                    break;
            }
        }
    }
}
