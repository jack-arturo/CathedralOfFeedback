using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using CathedralOfFeedback;
using Object = UnityEngine.Object;

/// <summary>
/// Offline eyes and ears. Runs the real synth, analyser and feedback loop for a scripted
/// performance without Play Mode or a headset, then writes stills, the raw field and a WAV
/// of the mix to Previews/, plus loudness and sub-100 Hz statistics in the log.
///   Unity -batchmode -quit -projectPath . -executeMethod CathedralPreview.Run -logFile -
/// Needs a graphics device: do not pass -nographics.
/// </summary>
public static class CathedralPreview
{
    const int Rate = 48000, Fps = 72;

    [MenuItem("Cathedral/Render Preview")]
    public static void Run()
    {
        int code = 0;
        try { RunInner(); }
        catch (Exception e) { Debug.LogError("PREVIEW FAILED: " + e); code = 1; }
        if (Application.isBatchMode) EditorApplication.Exit(code);
    }

    static void RunInner()
    {
        string outDir = "Previews";
        Directory.CreateDirectory(outDir);
        CathedralBuilder.BuildScene();

        Object.FindFirstObjectByType<CathedralArchitect>().Build();
        var field = Object.FindFirstObjectByType<FeedbackField>(); field.viewer = null; field.Init();
        var analyzer = Object.FindFirstObjectByType<SpectrumAnalyzer>();
        var dsp = new CathedralDsp(Rate);
        analyzer.Bind(dsp);
        var orbs = Object.FindObjectsByType<GrabbableOrb>(FindObjectsSortMode.None);
        Array.Sort(orbs, (a, b) => a.orbIndex.CompareTo(b.orbIndex));

        float seconds = 20f;
        int frames = (int)(seconds * Fps);
        var block = new float[2];
        var mix = new float[(int)(seconds * Rate) * 2];
        int mixPos = 0; double sampleAcc = 0;
        float[] shots = { 3f, 9f, 13f, 19.5f };
        int shot = 0;
        var bandAvg = new float[SpectrumAnalyzer.BandCount];
        float dt = 1f / Fps;
        // CATHEDRAL_PREVIEW_WAKE=0 renders the room asleep (the first thing a new wearer sees); default is fully awake.
        float wake = float.TryParse(Environment.GetEnvironmentVariable("CATHEDRAL_PREVIEW_WAKE"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float w) ? w : 1f;

        for (int f = 0; f < frames; f++)
        {
            float t = f * dt;
            // Scripted performance: idle, then one orb, then both.
            bool lead = t > 4f, deep = t > 11f;
            float energy = Mathf.Clamp01(0.27f + (lead ? 0.38f : 0f) * Mathf.Clamp01((t - 4f) / 2.5f) + (deep ? 0.3f : 0f) * Mathf.Clamp01((t - 11f) / 2.5f));
            dsp.Energy = energy; analyzer.Energy = energy;
            Vector3 p0 = new Vector3(0.25f + 0.35f * Mathf.Sin(t * 1.7f), 1.3f + 0.55f * Mathf.Sin(t * 1.1f), 0.55f + 0.15f * Mathf.Cos(t * 0.8f));
            Vector3 p1 = new Vector3(-0.3f + 0.3f * Mathf.Sin(t * 1.3f + 1f), 1.2f + 0.5f * Mathf.Sin(t * 0.7f + 2f), 0.5f);
            SetVoice(dsp, 0, lead, p0, 220f, 0.09f, 0.6f, field, orbs.Length > 0 ? orbs[0] : null);
            SetVoice(dsp, 5, deep, p1, 110f, 0.55f, 0.3f, field, orbs.Length > 1 ? orbs[1] : null);

            sampleAcc += Rate / (double)Fps;
            int n = (int)sampleAcc; sampleAcc -= n;
            if (block.Length != n * 2) block = new float[n * 2];
            dsp.Render(block, 2);
            if (mixPos + block.Length <= mix.Length) { Array.Copy(block, 0, mix, mixPos, block.Length); mixPos += block.Length; }

            // From 13.5 s: a planted pattern, as if the player had left six notes around the room.
            if (f == (int)(13.5f * Fps))
            {
                int[] steps = { 0, 3, 6, 8, 11, 14 };
                for (int k = 0; k < steps.Length; k++)
                {
                    dsp.Pattern[steps[k] * CathedralDsp.NotesPerStep] = new CathedralDsp.StepNote
                        { Freq = GrabbableOrb.FreqForNote((k * 3) % 10, 220f), Gain = 0.16f, Pan = k % 2 == 0 ? -0.5f : 0.5f };
                    dsp.PatternCount[steps[k]] = 1;
                }
                dsp.OneShot(1, 147f, 0.5f, 0f);          // and one column strike
            }

            dsp.Build = t > 16f && t < 18f ? Mathf.Clamp01((t - 16f) / 1.5f) : 0f;
            if (f == (int)(18f * Fps)) { dsp.Drop(); field.Flash(1f); }
            field.Surge = dsp.Build;

            analyzer.Tick(dt, dsp.BeatAt(dsp.SamplePos));
            Shader.SetGlobalVector("_CathedralPlayhead", new Vector4(t > 13.5f ? 1f : 0f, (float)((analyzer.BeatPosition * 4.0) % 16.0), 0f, 0f));
            for (int b = 0; b < bandAvg.Length; b++) bandAvg[b] += analyzer.GetBand(b) / frames;
            float bars = (float)(analyzer.BeatPosition / 4.0);
            CathedralDirector.PushGlobals(Mathf.Clamp01(t / 7f), 0f, t, Mathf.Repeat(bars * 0.045f, 1f), 1.7f, wake);
            field.Tick(dt);

            if (shot < shots.Length && t >= shots[shot])
            {
                string tag = $"t{shots[shot]:00.0}".Replace(".", "_");
                Shoot($"{outDir}/{tag}_front.png", 0f, 0f, 96f);
                Shoot($"{outDir}/{tag}_up.png", -62f, 20f, 96f);
                if (shot >= 1) Shoot($"{outDir}/{tag}_down.png", 32f, -35f, 96f);
                DumpField(field.Texture, $"{outDir}/{tag}_field.png");
                shot++;
            }
        }

        Array.Resize(ref mix, mixPos);
        WriteWav($"{outDir}/mix.wav", mix);
        AudioStats(mix);
        Debug.Log("PREVIEW bands avg: " + string.Join(" ", Array.ConvertAll(bandAvg, x => x.ToString("F2"))));
        Debug.Log($"PREVIEW zones spring={Nave.ZoneSpring:F3} mid={Nave.ZoneMid} top={Nave.ZoneTop} thetaMax={Nave.ThetaMax * Mathf.Rad2Deg:F1} apex={Nave.ApexHeight:F1}m");
        Debug.Log("PREVIEW OK -> " + Path.GetFullPath(outDir));
    }

    static void SetVoice(CathedralDsp dsp, int voice, bool held, Vector3 pos, float root, float hue, float speed, FeedbackField field, GrabbableOrb orb)
    {
        int note = GrabbableOrb.NoteForHeight(pos.y);
        dsp.Voices[voice] = new CathedralDsp.VoiceParams
        {
            Freq = GrabbableOrb.FreqForNote(note, root), Gain = held ? 0.42f : 0f, Bright = Mathf.Clamp01(0.12f + speed * 0.35f),
            Pan = Mathf.Clamp(pos.x * 2f, -1f, 1f), Gate = held, Fast = speed > 0.9f,
        };
        if (orb != null && held) orb.transform.position = pos;
        if (held) field.AddSplat(pos, GrabbableOrb.ColorForNote(note, hue), 1.3f + speed * 0.9f, 0.055f);
    }

    static void Shoot(string path, float pitch, float yaw, float fov)
    {
        const int size = 1024;
        var go = new GameObject("PreviewCam") { hideFlags = HideFlags.HideAndDontSave };
        var cam = go.AddComponent<Camera>();
        cam.transform.position = new Vector3(0f, 1.65f, 0f);
        cam.transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
        cam.fieldOfView = fov; cam.nearClipPlane = 0.05f; cam.farClipPlane = 60f;
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.black; cam.allowHDR = false;
        var rt = new RenderTexture(size, size, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
        cam.targetTexture = rt; cam.Render();
        SavePng(rt, path);
        cam.targetTexture = null; rt.Release(); Object.DestroyImmediate(rt); Object.DestroyImmediate(go);
    }

    static void DumpField(RenderTexture field, string path)
    {
        var rt = new RenderTexture(field.width, field.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        Graphics.Blit(field, rt);
        SavePng(rt, path);
        rt.Release(); Object.DestroyImmediate(rt);
    }

    static void SavePng(RenderTexture rt, string path)
    {
        var prev = RenderTexture.active; RenderTexture.active = rt;
        var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply();
        RenderTexture.active = prev;
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
    }

    static void WriteWav(string path, float[] stereo)
    {
        using var w = new BinaryWriter(File.Create(path));
        int bytes = stereo.Length * 2;
        w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + bytes);
        w.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); w.Write(16); w.Write((short)1); w.Write((short)2);
        w.Write(Rate); w.Write(Rate * 4); w.Write((short)4); w.Write((short)16);
        w.Write(System.Text.Encoding.ASCII.GetBytes("data")); w.Write(bytes);
        foreach (float s in stereo) w.Write((short)Mathf.RoundToInt(Mathf.Clamp(s, -1f, 1f) * 32767f));
    }

    /// <summary>Peak, RMS and where the energy sits. Headset speakers reproduce almost nothing below 100 Hz.</summary>
    static void AudioStats(float[] stereo)
    {
        const int n = 8192;
        var re = new float[n]; var im = new float[n];
        double sub = 0, low = 0, mid = 0, high = 0, sumSq = 0; float peak = 0f;
        int frames = stereo.Length / 2;
        for (int i = 0; i < frames; i++)
        {
            float m = (stereo[i * 2] + stereo[i * 2 + 1]) * 0.5f;
            sumSq += m * m; peak = Mathf.Max(peak, Mathf.Abs(stereo[i * 2]), Mathf.Abs(stereo[i * 2 + 1]));
        }
        float binHz = Rate / (float)n;
        for (int start = Rate * 8; start + n < frames; start += n / 2)      // analyse the full arrangement, after the build-up
        {
            for (int i = 0; i < n; i++)
            {
                re[i] = (stereo[(start + i) * 2] + stereo[(start + i) * 2 + 1]) * 0.5f * (0.5f - 0.5f * Mathf.Cos(2f * Mathf.PI * i / (n - 1)));
                im[i] = 0f;
            }
            Fft.Transform(re, im);
            for (int k = 1; k < n / 2; k++)
            {
                double p = re[k] * re[k] + im[k] * im[k]; float hz = k * binHz;
                if (hz < 100f) sub += p; else if (hz < 300f) low += p; else if (hz < 3000f) mid += p; else high += p;
            }
        }
        double total = sub + low + mid + high + 1e-20;
        Debug.Log($"PREVIEW audio peak={peak:F3} rms={Math.Sqrt(sumSq / Math.Max(1, frames)):F3} " +
                  $"sub100Hz={sub / total * 100:F1}% 100-300Hz={low / total * 100:F1}% 300-3kHz={mid / total * 100:F1}% above3k={high / total * 100:F1}%");
    }
}
