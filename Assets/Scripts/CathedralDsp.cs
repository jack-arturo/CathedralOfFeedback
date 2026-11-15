using System;

namespace CathedralOfFeedback
{
    /// <summary>
    /// The whole soundtrack as one sample-accurate synth: drums, bass, organ, arpeggio, ten
    /// playable orb/echo voices and a cathedral reverb. Pure C# with no Unity calls, so the
    /// same code runs on the audio thread on Quest and offline in the preview harness.
    ///
    /// Everything is voiced to survive headset speakers, which roll off below ~100 Hz: the
    /// kick is a pitch sweep with a saturated body, the bass carries its pitch in harmonics.
    /// </summary>
    public sealed class CathedralDsp
    {
        public const int MaxVoices = 10;

        public struct VoiceParams
        {
            public float Freq, Gain, Bright, Pan;
            public bool Gate, Fast;
        }

        public readonly int Rate;
        /// <summary>Written by the main thread, read by the audio thread. Races are benign.</summary>
        public readonly VoiceParams[] Voices = new VoiceParams[MaxVoices];
        public float Bpm = 120f;
        public volatile float Energy = 0.3f;     // 0..1, arrangement density
        public volatile float DrumOpen = 1f;     // 0..1, low-pass on drums and bass
        public volatile float Master = 0.85f;
        public volatile float ReverbWet = 0.32f;
        /// <summary>0..1 build-up: the kick drops out, a snare roll and a riser come in.</summary>
        public volatile float Build;
        volatile bool dropFlag;
        /// <summary>End a build-up: big kick, crash, a chord of bells, and the whole piece moves to its next key.</summary>
        public void Drop() { dropFlag = true; }

        // Four keys, a drop apart: A minor, C minor, D minor, G minor, and round again. Everything
        // pitched goes through keyRatio (the sequenced parts, the orbs, bells and column drums),
        // so callers keep thinking in A minor and the room stays in tune with itself.
        volatile int overdriveReq; int overdriveSteps;
        /// <summary>A climax: for this many bars the arrangement runs flat out and the arpeggio leaps an octave.</summary>
        public void Overdrive(int bars) { overdriveReq = bars; }

        static readonly int[] KeySemis = { 0, 3, 5, -2 };
        volatile int keyIndex;
        float keyRatio = 1f;
        /// <summary>Which of the four keys the piece is in (changes on every drop).</summary>
        public int KeyIndex => keyIndex;
        /// <summary>Frequency ratio of the current key to the home key.</summary>
        public float KeyRatio => Semi(KeySemis[keyIndex]);
        float riseLow, riseBand, buildSm;
        volatile float chime;
        /// <summary>Ring a bell at the given pitch on the next audio block (UI feedback).</summary>
        public void Chime(float hz) { chime = hz; }

        // ---- bells: the planted-note pattern (one bar of sixteenths) and immediate one-shots
        public const int Steps = 16, NotesPerStep = 4, BellVoices = 10;
        public struct StepNote { public float Freq, Gain, Pan; }
        /// <summary>Step pattern written by the main thread: Pattern[step * NotesPerStep + k], k &lt; PatternCount[step].</summary>
        public readonly StepNote[] Pattern = new StepNote[Steps * NotesPerStep];
        public readonly int[] PatternCount = new int[Steps];

        struct Shot { public int Kind; public float Freq, Gain, Pan; }
        readonly Shot[] shots = new Shot[16];
        volatile int shotWrite; int shotRead;
        /// <summary>Play a sound now. kind 0 = glass bell, 1 = column drum.</summary>
        public void OneShot(int kind, float freq, float gain, float pan)
        {
            int w = shotWrite;
            shots[w & 15] = new Shot { Kind = kind, Freq = freq, Gain = gain, Pan = pan };
            shotWrite = w + 1;
        }

        readonly float[] blP1 = new float[BellVoices], blP2 = new float[BellVoices], blEnv = new float[BellVoices],
            blFreq = new float[BellVoices], blRatio = new float[BellVoices], blDecay = new float[BellVoices],
            blDrop = new float[BellVoices], blGl = new float[BellVoices], blGr = new float[BellVoices];
        int blNext;

        void TrigBell(int kind, float freq, float gain, float pan)
        {
            int v = blNext; blNext = (blNext + 1) % BellVoices;
            freq *= keyRatio;
            blFreq[v] = freq; blEnv[v] = gain; blP1[v] = 0f; blP2[v] = 0f;
            float ang = (Clamp(pan, -1f, 1f) + 1f) * 0.7853982f;
            blGl[v] = (float)Math.Cos(ang); blGr[v] = (float)Math.Sin(ang);
            if (kind == 0) { blRatio[v] = 2.756f; blDecay[v] = bellDecay; blDrop[v] = 0f; }     // inharmonic partial: glass
            else { blRatio[v] = 1.5f; blDecay[v] = drumDecay; blDrop[v] = freq * 1.3f; }        // pitch drop: struck stone
        }

        long samplePos;
        public long SamplePos => System.Threading.Interlocked.Read(ref samplePos);
        public double SamplesPerBeat => Rate * 60.0 / Bpm;
        public double BeatAt(long sample) => sample / SamplesPerBeat;

        // Mono tap of the final mix for the spectrum analyser.
        const int TapSize = 4096;
        readonly float[] tap = new float[TapSize];
        int tapPos;

        public void CopyTap(float[] dst)
        {
            int n = dst.Length, p = tapPos;
            for (int i = 0; i < n; i++) dst[i] = tap[(p - n + i + TapSize) & (TapSize - 1)];
        }

        // ---------------------------------------------------------------- sequencer
        int step = -1;
        double nextStep;
        uint rng = 0x1234567u;

        // i - VI - III - VIIsus in A minor; every chord tone stays inside the orb's pentatonic.
        static readonly int[][] Chords =
        {
            new[] { 0, 3, 7 }, new[] { -4, 0, 3 }, new[] { 3, 7, 10 }, new[] { -2, 3, 5 },
        };
        static readonly int[] BassRoot = { 0, -4, 3, -2 };          // semitones from A2 (110 Hz)
        static readonly int[] ArpPattern = { 0, 1, 2, 3, 2, 1, 4, 2, 0, 2, 3, 5, 4, 3, 2, 1 };
        static readonly int[] ArpPatternB = { 3, 2, 4, 2, 5, 4, 3, 1, 0, 1, 3, 2, 4, 5, 3, 0 };
        static readonly int[] BassSteps = { 0, 3, 6, 8, 11, 14 };

        // ---------------------------------------------------------------- instruments
        float kEnv, kPhase, kPitch, kClick;
        float hEnv, hDecay, hPrev;
        float cEnv, cLow, cBand;
        float bPhase, bEnv, bFenv, bFreq = 110f, bLp1, bLp2;
        readonly float[] aPhase = new float[2], aEnv = new float[2], aFreq = new float[2], aPan = new float[2];
        int aNext;
        readonly float[] oPhase = new float[9], oFreq = new float[3], oTarget = new float[3];
        float oLevel, oTrem;

        readonly float[] vPhaseA = new float[MaxVoices], vPhaseB = new float[MaxVoices], vEnv = new float[MaxVoices],
            vFenv = new float[MaxVoices], vSus = new float[MaxVoices], vFreq = new float[MaxVoices],
            vLow = new float[MaxVoices], vBand = new float[MaxVoices], vGl = new float[MaxVoices], vGr = new float[MaxVoices];

        float dLp1, dLp2, hpL1, hpL2, hpR1, hpR2;

        // ---------------------------------------------------------------- reverb
        sealed class Comb
        {
            readonly float[] buf; int pos; float store; public float Feedback = 0.9f, Damp = 0.28f;
            public Comb(int len) { buf = new float[len]; }
            public float Tick(float x)
            {
                float y = buf[pos];
                store = y * (1f - Damp) + store * Damp;
                buf[pos] = x + store * Feedback + 1e-18f;
                if (++pos >= buf.Length) pos = 0;
                return y;
            }
            public void Clear() { Array.Clear(buf, 0, buf.Length); store = 0f; }
        }

        sealed class Allpass
        {
            readonly float[] buf; int pos;
            public Allpass(int len) { buf = new float[len]; }
            public float Tick(float x)
            {
                float b = buf[pos];
                buf[pos] = x + b * 0.5f;
                if (++pos >= buf.Length) pos = 0;
                return b - x;
            }
            public void Clear() { Array.Clear(buf, 0, buf.Length); }
        }

        readonly Comb[] combL, combR;
        readonly Allpass[] apL, apR;
        readonly float[] pre; int prePos;

        readonly float kDecay, kPitchDecay, cDecay, bDecay, bFDecay, aDecay, vDecay, vFDecay, bellDecay, drumDecay;

        public CathedralDsp(int sampleRate)
        {
            Rate = Mathf2.Max(sampleRate, 8000);
            float sc = Rate / 48000f;
            int[] cl = { 1687, 1601, 2053, 2251, 1867, 2399 };
            combL = new Comb[cl.Length]; combR = new Comb[cl.Length];
            for (int i = 0; i < cl.Length; i++)
            {
                combL[i] = new Comb((int)(cl[i] * sc)) { Feedback = 0.905f };
                combR[i] = new Comb((int)((cl[i] + 37) * sc)) { Feedback = 0.905f };
            }
            apL = new[] { new Allpass((int)(556 * sc)), new Allpass((int)(441 * sc)) };
            apR = new[] { new Allpass((int)(579 * sc)), new Allpass((int)(464 * sc)) };
            pre = new float[(int)(1700 * sc)];

            kDecay = Decay(0.15f); kPitchDecay = Decay(0.030f); cDecay = Decay(0.11f);
            bDecay = Decay(0.20f); bFDecay = Decay(0.07f); aDecay = Decay(0.22f);
            vDecay = Decay(0.30f); vFDecay = Decay(0.10f); bellDecay = Decay(0.6f); drumDecay = Decay(0.34f);
            for (int i = 0; i < 3; i++) oFreq[i] = oTarget[i] = 220f * Semi(Chords[0][i]);
            for (int i = 0; i < MaxVoices; i++) { vFreq[i] = 220f; vGl[i] = vGr[i] = 0.7f; }
        }

        float Decay(float seconds) => (float)Math.Exp(-1.0 / (seconds * Rate));
        static float Semi(float st) => (float)Math.Pow(2.0, st / 12.0);
        float Noise() { rng = rng * 1664525u + 1013904223u; return (rng >> 9) * (1f / 4194304f) - 1f; }
        static float Soft(float x) => x / (1f + Math.Abs(x));

        static class Mathf2 { public static int Max(int a, int b) => a > b ? a : b; }

        void OnStep(int s)
        {
            int st = s & 15, bar = (s >> 4) & 3, phrase = (s >> 6) & 3;     // a phrase is four bars
            float e = Energy;
            if (overdriveReq > 0) { overdriveSteps = overdriveReq * 16; overdriveReq = 0; }
            bool over = overdriveSteps > 0;
            if (over) { overdriveSteps--; e = Math.Max(e, 0.95f); }

            // Drums thicken with energy: a lone heartbeat, then four on the floor, hats, claps.
            float bu = Build;
            if (bu > 0.5f) { /* the kick holds its breath during a build-up */ }
            else if (e > 0.22f ? (st & 3) == 0 : st == 0) { kEnv = e > 0.22f ? 1f : 0.7f; kPhase = 0f; kPitch = 260f; kClick = 1f; }
            else if (e > 0.78f && st == 14) { kEnv = 0.6f; kPhase = 0f; kPitch = 200f; kClick = 0.6f; }
            if (e > 0.36f && (st & 3) == 2) { hEnv = 0.9f; hDecay = Decay(st == 14 ? 0.11f : 0.035f); }
            else if (e > 0.66f && (st & 1) == 1) { hEnv = 0.45f; hDecay = Decay(0.022f); }
            if (e > 0.5f && (st == 4 || st == 12)) cEnv = 1f;
            // Last bar of every second phrase: a short fill into the turnaround.
            if (e > 0.55f && bar == 3 && (phrase & 1) == 1 && st >= 12) { cEnv = Math.Max(cEnv, 0.5f + 0.15f * (st - 12)); if (st == 15) { hEnv = 1f; hDecay = Decay(0.2f); } }
            // Snare roll: eighths, then sixteenths, swelling through the bar.
            if (bu > 0.25f && (bu > 0.6f || (st & 1) == 0)) cEnv = Math.Max(cEnv, bu * (0.3f + 0.7f * (st + 1) / 16f));

            if (e > 0.3f)
                for (int i = 0; i < BassSteps.Length; i++)
                    if (BassSteps[i] == st)
                    {
                        float semi = BassRoot[bar] + (st == 6 ? 12 : st == 14 ? 7 : 0);
                        bFreq = 110f * keyRatio * Semi(semi); bEnv = 1f; bFenv = 1f;
                    }

            if (st == 0) for (int i = 0; i < 3; i++) oTarget[i] = 220f * keyRatio * Semi(Chords[bar][i]);

            // Arpeggio: eighths when quiet, sixteenths once things are moving.
            if (e > 0.45f || (st & 1) == 0)
            {
                // The arpeggio alternates between two figures phrase by phrase so the loop keeps moving.
                int k = ((phrase & 1) == 0 ? ArpPattern : ArpPatternB)[st];
                int v = aNext; aNext ^= 1;
                aFreq[v] = 440f * keyRatio * (over ? 2f : 1f) * Semi(Chords[bar][k % 3] + 12 * (k / 3));
                aEnv[v] = 0.55f + 0.45f * e;
                aPan[v] = ((st & 2) == 0 ? -0.6f : 0.6f);
            }

            // Planted notes: whatever the player has left hanging in this bay of the room.
            int planted = Math.Min(PatternCount[st], NotesPerStep);
            for (int k = 0; k < planted; k++)
            {
                StepNote n = Pattern[st * NotesPerStep + k];
                TrigBell(0, n.Freq, n.Gain, n.Pan);
            }

            // Orb and echo voices pluck on the grid, so anyone holding an orb is in time.
            for (int i = 0; i < MaxVoices; i++)
                if (Voices[i].Gate && (Voices[i].Fast || (st & 1) == 0)) { vEnv[i] = 1f; vFenv[i] = 1f; }
        }

        /// <summary>Renders interleaved audio. Adds to nothing: overwrites <paramref name="data"/>.</summary>
        public void Render(float[] data, int channels)
        {
            int frames = data.Length / channels;
            double spStep = SamplesPerBeat / 4.0;
            float inv = 1f / Rate, twoPi = (float)(2.0 * Math.PI);
            float e = Energy, master = Master, wet = ReverbWet;
            float drumA = Math.Min(1f, twoPi * (220f * (float)Math.Pow(80.0, Clamp01(DrumOpen))) * inv);
            float oTargetLevel = 0.035f + 0.05f * e;
            float hpA = Math.Min(1f, twoPi * 100f * inv);
            long pos = samplePos;
            if (dropFlag)
            {
                dropFlag = false;
                // Move to the next key, and carry what is already sounding along with it.
                int nextKey = (keyIndex + 1) % KeySemis.Length;
                float shift = Semi(KeySemis[nextKey]) / keyRatio;
                keyRatio *= shift; keyIndex = nextKey;
                bFreq *= shift;
                for (int i = 0; i < 3; i++) { oTarget[i] *= shift; oFreq[i] *= shift; }
                for (int i = 0; i < 2; i++) aFreq[i] *= shift;
                kEnv = 1.5f; kPhase = 0f; kPitch = 340f; kClick = 1f;
                hEnv = 1.6f; hDecay = Decay(0.45f);
                TrigBell(0, 440f, 0.22f, -0.6f); TrigBell(0, 523.25f, 0.2f, 0.6f); TrigBell(0, 659.25f, 0.2f, 0f);
            }
            while (shotRead != shotWrite) { Shot sh = shots[shotRead & 15]; shotRead++; TrigBell(sh.Kind, sh.Freq, sh.Gain, sh.Pan); }
            float ring = chime;
            if (ring > 0f) { chime = 0f; int cv = aNext; aNext ^= 1; aFreq[cv] = ring * keyRatio; aEnv[cv] = 1.8f; aPan[cv] = 0f; }

            for (int f = 0; f < frames; f++, pos++)
            {
                if (pos >= nextStep) { step++; OnStep(step); nextStep += spStep; }

                // ---- drums + bass (dry bus, filtered by DrumOpen)
                float drum = 0f, send = 0f;
                if (kEnv > 1e-4f)
                {
                    kPhase += (104f + kPitch) * inv; if (kPhase >= 1f) kPhase -= 1f;
                    float body = (float)Math.Sin(twoPi * kPhase) * kEnv;
                    drum += Soft(body * 3.4f) * 0.58f + Noise() * kClick * 0.22f;
                    kEnv *= kDecay; kPitch *= kPitchDecay; kClick *= 0.985f;
                }
                if (hEnv > 1e-4f)
                {
                    float n = Noise(), hp = n - hPrev; hPrev = n;
                    float h = hp * hEnv * 0.30f;
                    drum += h; send += h * 0.3f; hEnv *= hDecay;
                }
                if (cEnv > 1e-4f)
                {
                    float n = Noise();
                    cLow += 0.24f * cBand; float high = n - cLow - 0.55f * cBand; cBand += 0.24f * high;
                    float c = cBand * cEnv * 0.40f;
                    drum += c; send += c * 0.6f; cEnv *= cDecay;
                }
                if (bEnv > 1e-4f)
                {
                    bPhase += bFreq * inv; if (bPhase >= 1f) bPhase -= 1f;
                    float saw = bPhase * 2f - 1f;
                    float sq = (bPhase * 2f % 1f) < 0.5f ? 0.5f : -0.5f;       // octave up: audible on small speakers
                    float a = Math.Min(1f, twoPi * (420f + bFenv * 1900f) * inv);
                    bLp1 += a * (saw * 0.3f + sq * 1.6f - bLp1); bLp2 += a * (bLp1 - bLp2);
                    drum += bLp2 * bEnv * 0.42f;
                    bEnv *= bDecay; bFenv *= bFDecay;
                }
                dLp1 += drumA * (drum - dLp1); dLp2 += drumA * (dLp1 - dLp2);
                float dry = dLp2;

                // ---- melodic bus (stereo)
                float mL = 0f, mR = 0f;

                // Organ: three chord tones, three drawbars each, slow tremolo.
                oLevel += (oTargetLevel - oLevel) * 0.00005f;
                oTrem += 0.9f * inv; if (oTrem >= 1f) oTrem -= 1f;
                float trem = 0.82f + 0.18f * (float)Math.Sin(twoPi * oTrem);
                float org = 0f;
                for (int i = 0; i < 3; i++)
                {
                    oFreq[i] += (oTarget[i] - oFreq[i]) * 0.0004f;
                    float w = oFreq[i] * inv;
                    int k = i * 3;
                    oPhase[k] += w; if (oPhase[k] >= 1f) oPhase[k] -= 1f;
                    oPhase[k + 1] += w * 2f; if (oPhase[k + 1] >= 1f) oPhase[k + 1] -= 1f;
                    oPhase[k + 2] += w * 3f; if (oPhase[k + 2] >= 1f) oPhase[k + 2] -= 1f;
                    org += (float)Math.Sin(twoPi * oPhase[k]) + 0.5f * (float)Math.Sin(twoPi * oPhase[k + 1])
                         + 0.25f * (float)Math.Sin(twoPi * oPhase[k + 2]);
                }
                org *= oLevel * trem; mL += org; mR += org;

                for (int i = 0; i < 2; i++)
                {
                    if (aEnv[i] < 1e-4f) continue;
                    aPhase[i] += aFreq[i] * inv; if (aPhase[i] >= 1f) aPhase[i] -= 1f;
                    float tri = Math.Abs(aPhase[i] * 4f - 2f) - 1f;
                    float s = (tri * 0.7f + (float)Math.Sin(twoPi * 2f * aPhase[i]) * 0.3f) * aEnv[i] * 0.12f;
                    mL += s * (1f - aPan[i]) * 0.5f * 2f * 0.5f + s * 0.25f;
                    mR += s * (1f + aPan[i]) * 0.5f * 2f * 0.5f + s * 0.25f;
                    aEnv[i] *= aDecay;
                }

                for (int i = 0; i < MaxVoices; i++)
                {
                    VoiceParams p = Voices[i];
                    vSus[i] += ((p.Gate ? 1f : 0f) - vSus[i]) * 0.0006f;
                    float amp = vEnv[i] * 0.9f + vSus[i] * 0.2f;
                    if (amp < 1e-4f) continue;
                    if (p.Gate) vFreq[i] += (p.Freq * keyRatio - vFreq[i]) * 0.003f;
                    float w = vFreq[i] * inv;
                    vPhaseA[i] += w; if (vPhaseA[i] >= 1f) vPhaseA[i] -= 1f;
                    vPhaseB[i] += w * 1.006f; if (vPhaseB[i] >= 1f) vPhaseB[i] -= 1f;
                    float osc = (vPhaseA[i] + vPhaseB[i]) - 1f;                 // two detuned saws
                    float fc = 420f + p.Bright * 2200f + vFenv[i] * (1500f + p.Bright * 5600f);
                    float fk = Math.Min(0.85f, twoPi * fc * inv);
                    vLow[i] += fk * vBand[i];
                    float high = osc - vLow[i] - 0.55f * vBand[i];
                    vBand[i] += fk * high;
                    float sub = (float)Math.Sin(twoPi * vPhaseA[i]) * 0.35f * (1f - p.Bright);
                    float s = (vLow[i] + sub) * amp * p.Gain;
                    float ang = (Clamp(p.Pan, -1f, 1f) + 1f) * 0.7853982f;
                    vGl[i] += ((float)Math.Cos(ang) - vGl[i]) * 0.002f;
                    vGr[i] += ((float)Math.Sin(ang) - vGr[i]) * 0.002f;
                    mL += s * vGl[i]; mR += s * vGr[i];
                    vEnv[i] *= vDecay; vFenv[i] *= vFDecay;
                }

                // Riser: filtered noise that climbs with the build-up.
                buildSm += (Build - buildSm) * 0.0006f;
                if (buildSm > 0.01f)
                {
                    float rf = Math.Min(0.8f, twoPi * (400f + buildSm * buildSm * 7000f) * inv);
                    riseLow += rf * riseBand;
                    float rh = Noise() - riseLow - 0.35f * riseBand;
                    riseBand += rf * rh;
                    float rs = riseBand * buildSm * buildSm * 0.09f;
                    mL += rs; mR += rs;
                }

                for (int i = 0; i < BellVoices; i++)
                {
                    if (blEnv[i] < 1e-4f) continue;
                    float bf = (blFreq[i] + blDrop[i]) * inv;
                    blP1[i] += bf; if (blP1[i] >= 1f) blP1[i] -= 1f;
                    blP2[i] += bf * blRatio[i]; if (blP2[i] >= 1f) blP2[i] -= (float)Math.Floor(blP2[i]);
                    float bs = ((float)Math.Sin(twoPi * blP1[i]) + 0.4f * (float)Math.Sin(twoPi * blP2[i])) * blEnv[i];
                    mL += bs * blGl[i]; mR += bs * blGr[i];
                    blEnv[i] *= blDecay[i]; blDrop[i] *= kPitchDecay;
                }

                // ---- reverb: predelay, six damped combs per side, two allpasses
                send += (mL + mR) * 0.5f;
                float pd = pre[prePos]; pre[prePos] = send; if (++prePos >= pre.Length) prePos = 0;
                float rl = 0f, rr = 0f;
                for (int i = 0; i < combL.Length; i++) { rl += combL[i].Tick(pd); rr += combR[i].Tick(pd); }
                rl = apL[1].Tick(apL[0].Tick(rl * 0.16f)); rr = apR[1].Tick(apR[0].Tick(rr * 0.16f));

                float L = dry + mL + rl * wet, R = dry + mR + rr * wet;
                hpL1 += hpA * (L - hpL1); L -= hpL1; hpL2 += hpA * (L - hpL2); L -= hpL2;
                hpR1 += hpA * (R - hpR1); R -= hpR1; hpR2 += hpA * (R - hpR2); R -= hpR2;
                L = Soft(L * master * 1.8f); R = Soft(R * master * 1.8f);

                // A NaN or runaway value would circulate in the reverb forever. Catch it here.
                if (!(Math.Abs(L) < 8f) || !(Math.Abs(R) < 8f)) { Panic(); L = 0f; R = 0f; }

                tap[tapPos] = (L + R) * 0.5f; tapPos = (tapPos + 1) & (TapSize - 1);
                int o = f * channels;
                if (channels == 1) data[o] = (L + R) * 0.5f;
                else { data[o] = L; data[o + 1] = R; for (int c = 2; c < channels; c++) data[o + c] = 0f; }
            }
            System.Threading.Interlocked.Exchange(ref samplePos, pos);
        }

        public int Panics { get; private set; }

        /// <summary>Clear every filter and delay line. Called if the output ever stops being a sane number.</summary>
        void Panic()
        {
            Panics++;
            foreach (var c in combL) c.Clear(); foreach (var c in combR) c.Clear();
            foreach (var a in apL) a.Clear(); foreach (var a in apR) a.Clear();
            Array.Clear(pre, 0, pre.Length);
            dLp1 = dLp2 = hpL1 = hpL2 = hpR1 = hpR2 = bLp1 = bLp2 = cLow = cBand = hPrev = 0f;
            kEnv = hEnv = cEnv = bEnv = 0f; oLevel = 0f; riseLow = riseBand = 0f;
            for (int i = 0; i < BellVoices; i++) blEnv[i] = 0f;
            for (int i = 0; i < MaxVoices; i++) { vLow[i] = vBand[i] = vEnv[i] = vFenv[i] = vSus[i] = 0f; vFreq[i] = 220f; vGl[i] = vGr[i] = 0.7f; }
            for (int i = 0; i < 3; i++) oFreq[i] = oTarget[i];
        }

        static float Clamp01(float x) => x < 0f ? 0f : x > 1f ? 1f : x;
        static float Clamp(float x, float a, float b) => x < a ? a : x > b ? b : x;
    }
}
