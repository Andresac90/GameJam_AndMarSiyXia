using UnityEngine;

/// <summary>
/// Generates placeholder SFX at runtime so the game has sound without audio assets.
/// Assign real AudioClips in the inspector of each component to override them.
/// </summary>
public static class ProceduralAudio
{
    private const int SampleRate = 44100;

    private static AudioClip gunshot;
    private static AudioClip knock;
    private static AudioClip splat;
    private static AudioClip burst;

    // Unity null checks (not ??=): with domain reload off, statics outlive destroyed clips.
    public static AudioClip Gunshot => gunshot != null ? gunshot : gunshot = CreateGunshot();
    public static AudioClip Knock => knock != null ? knock : knock = CreateKnock();
    public static AudioClip Splat => splat != null ? splat : splat = CreateSplat("Splat", 0.45f, 1f, 7);
    public static AudioClip Burst => burst != null ? burst : burst = CreateSplat("GoreBurst", 1.4f, 2.2f, 11);

    private static AudioClip CreateGunshot()
    {
        float[] data = new float[(int)(SampleRate * 0.6f)];
        System.Random rng = new System.Random(1);
        float lowpass = 0f;
        float phase = 0f;

        for (int i = 0; i < data.Length; i++)
        {
            float t = i / (float)SampleRate;
            float noise = (float)rng.NextDouble() * 2f - 1f;
            lowpass += (noise - lowpass) * 0.3f;

            float frequency = 55f + 90f * Mathf.Exp(-t * 30f);
            phase += 2f * Mathf.PI * frequency / SampleRate;

            float crack = noise * Mathf.Exp(-t * 70f);
            float body = lowpass * Mathf.Exp(-t * 8f);
            float thump = Mathf.Sin(phase) * Mathf.Exp(-t * 12f);

            data[i] = crack * 0.7f + body * 0.9f + thump;
        }

        return Build("Gunshot", data);
    }

    private static AudioClip CreateKnock()
    {
        float[] data = new float[(int)(SampleRate * 0.2f)];
        System.Random rng = new System.Random(2);
        float lowpass = 0f;

        for (int i = 0; i < data.Length; i++)
        {
            float t = i / (float)SampleRate;
            float noise = (float)rng.NextDouble() * 2f - 1f;
            lowpass += (noise - lowpass) * 0.2f;

            float tone = Mathf.Sin(2f * Mathf.PI * 95f * t) +
                         Mathf.Sin(2f * Mathf.PI * 190f * t) * 0.4f;

            data[i] = tone * Mathf.Exp(-t * 35f) +
                      lowpass * Mathf.Exp(-t * 120f) * 0.8f;
        }

        return Build("Knock", data);
    }

    private static AudioClip CreateSplat(string name, float seconds, float weight, int seed)
    {
        float[] data = new float[(int)(SampleRate * seconds)];
        System.Random rng = new System.Random(seed);
        float lowpass = 0f;
        float phase = 0f;
        float crackle = 0f;

        for (int i = 0; i < data.Length; i++)
        {
            float t = i / (float)SampleRate;
            float noise = (float)rng.NextDouble() * 2f - 1f;
            lowpass += (noise - lowpass) * 0.08f;

            // Random wet pops that decay over the clip.
            if (rng.NextDouble() < 0.0015 * Mathf.Exp(-t * 3f / weight))
                crackle = (float)rng.NextDouble() * 0.8f + 0.2f;
            crackle *= 0.992f;

            float frequency = Mathf.Lerp(130f, 35f, Mathf.Clamp01(t / (0.3f * weight)));
            phase += 2f * Mathf.PI * frequency / SampleRate;

            float squelch = lowpass * Mathf.Exp(-t * 6f / weight) *
                            (0.6f + 0.4f * Mathf.Sin(2f * Mathf.PI * 23f * t));
            float thump = Mathf.Sin(phase) * Mathf.Exp(-t * 9f / weight);

            data[i] = squelch * 2.5f + thump * 0.8f + crackle * noise;
        }

        return Build(name, data);
    }

    private static AudioClip Build(string name, float[] data)
    {
        float peak = 0.0001f;
        foreach (float sample in data)
            peak = Mathf.Max(peak, Mathf.Abs(sample));

        float gain = 0.9f / peak;
        for (int i = 0; i < data.Length; i++)
            data[i] *= gain;

        AudioClip clip = AudioClip.Create(name, data.Length, 1, SampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
