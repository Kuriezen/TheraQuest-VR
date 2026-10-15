using UnityEngine;

namespace TheraQuest
{
    /// <summary>
    /// Background music at the Settings > Music volume. Assign your own looping track to <see cref="music"/>;
    /// without one, a calm 32-second ambient loop (soft pads and a light arpeggio) is generated at startup.
    /// </summary>
    public class MusicPlayer : MonoBehaviour
    {
        [Tooltip("Optional looping track. Leave empty for the generated ambient loop.")]
        public AudioClip music;
        [Range(0f, 1f)] public float maxVolume = 0.45f;

        AudioSource src;

        void Awake()
        {
            src = gameObject.AddComponent<AudioSource>();
            src.clip = music != null ? music : Generate();
            src.loop = true;
            src.playOnAwake = false;
            src.spatialBlend = 0f;
            src.ignoreListenerPause = true;
            Apply();
            src.Play();
            GameSettings.Changed += Apply;
        }

        void OnDestroy() => GameSettings.Changed -= Apply;

        void Apply() { if (src != null) src.volume = GameSettings.Music * maxVolume; }

        // C major family: Cmaj7 - Am7 - Fmaj7 - G6, 8 s each
        static readonly float[][] Chords =
        {
            new[] { 130.81f, 164.81f, 196.00f, 246.94f },
            new[] { 110.00f, 130.81f, 164.81f, 196.00f },
            new[] { 87.31f, 130.81f, 174.61f, 220.00f },
            new[] { 98.00f, 146.83f, 196.00f, 246.94f },
        };

        static AudioClip Generate()
        {
            const int rate = 22050;
            const float chordLen = 8f, step = 0.5f;
            int total = Mathf.RoundToInt(rate * chordLen * Chords.Length);
            var data = new float[total];
            float twoPi = 2f * Mathf.PI;

            for (int c = 0; c < Chords.Length; c++)
            {
                int start = Mathf.RoundToInt(c * chordLen * rate), len = Mathf.RoundToInt(chordLen * rate);
                // Pad: chord tones plus a slightly detuned copy, slow swell in and out (overlaps the next chord)
                for (int i = 0; i < len + rate; i++)
                {
                    float t = (float)i / rate;
                    float env = Mathf.Clamp01(t / 2f) * Mathf.Clamp01((chordLen + 1f - t) / 2.5f);
                    float s = 0f;
                    foreach (var f in Chords[c])
                        s += Mathf.Sin(twoPi * f * t) + 0.5f * Mathf.Sin(twoPi * f * 1.003f * t);
                    data[(start + i) % total] += s * env * 0.035f;
                }
                // Arpeggio: one octave up, a soft pluck every half second
                for (int n = 0; n < chordLen / step; n++)
                {
                    float f = Chords[c][(n * 3 + c) % 4] * 2f;
                    int at = start + Mathf.RoundToInt(n * step * rate);
                    for (int i = 0; i < rate; i++)
                    {
                        float t = (float)i / rate;
                        float env = Mathf.Exp(-t * 4f) * Mathf.Min(1f, i / 120f);
                        data[(at + i) % total] += Mathf.Sin(twoPi * f * t) * env * 0.05f;
                    }
                }
            }
            float peak = 0f;
            foreach (var v in data) peak = Mathf.Max(peak, Mathf.Abs(v));
            if (peak > 0.9f) for (int i = 0; i < total; i++) data[i] *= 0.9f / peak;

            var clip = AudioClip.Create("TheraQuest ambient", total, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
