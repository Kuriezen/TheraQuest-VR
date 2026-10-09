using UnityEngine;

namespace TheraQuest
{
    public enum Tone { Neutral, Good, Warn, Bad }

    /// <summary>
    /// Feedback Generation Module (Section 3.6.3):
    ///   Feedback Decision Engine  -> LiveMessage()
    ///   Visual Cue Generator      -> message + tone colour shown by HUD and figure
    ///   Audio Prompt Generator    -> short generated tones (no audio files needed)
    ///   Corrective Instruction    -> ExerciseEvaluator.Describe(), logged per rep
    /// </summary>
    public class FeedbackManager : MonoBehaviour
    {
        public bool audioOn = true;
        AudioSource src;
        AudioClip good, great, bad, levelUp, badge, warning;

        void Awake()
        {
            src = gameObject.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.spatialBlend = 0f;
            good = MakeChime(new[] { 660f, 880f }, 0.09f);
            great = MakeChime(new[] { 660f, 880f, 1175f }, 0.08f);
            bad = MakeChime(new[] { 220f, 196f }, 0.14f);
            levelUp = MakeChime(new[] { 523f, 659f, 784f, 1047f }, 0.1f);
            badge = MakeChime(new[] { 880f, 1319f }, 0.12f);
            warning = MakeChime(new[] { 440f, 330f, 440f, 330f }, 0.12f);
        }

        public static (string text, Tone tone) LiveMessage(ExerciseEvaluator ev, string fault)
        {
            var ex = ev.Exercise;
            switch (ev.State)
            {
                case LiveState.Resting:
                    return ($"Set {ev.SetNumber - 1} of {ev.Sets} done. Rest {Mathf.CeilToInt(ev.RestRemaining)} s", Tone.Neutral);
                case LiveState.Unsafe: return ($"STOP. Past the safe limit of {ex.safetyMax:0}°. Lower slowly", Tone.Bad);
                case LiveState.Fault: return (fault ?? "Check your posture", Tone.Bad);
                case LiveState.TooFar: return ($"Too far. Ease back to {ex.targetAngle:0}°", Tone.Bad);
                case LiveState.Holding: return ($"Hold it… {Mathf.CeilToInt(ex.holdSeconds - ev.HoldTime)}", Tone.Good);
                case LiveState.OnTarget:
                    return (ex.holdSeconds > 0f ? "Held! Now lower slowly" : "Right on target. Now lower slowly", Tone.Good);
                case LiveState.Rising: return ($"{Mathf.RoundToInt(ex.targetAngle - ev.CurrentAngle)}° to target", Tone.Warn);
                default: return (ev.CurrentAngle < ex.targetAngle * 0.25f ? ex.cue : "Keep going", Tone.Neutral);
            }
        }

        public void PlayRep(RepResult r)
        {
            if (!audioOn) return;
            src.PlayOneShot(!r.valid ? bad : r.accuracy >= 95 ? great : good, 0.6f);
        }

        public void PlayLevelUp() { if (audioOn) src.PlayOneShot(levelUp, 0.7f); }
        public void PlayBadge() { if (audioOn) src.PlayOneShot(badge, 0.7f); }
        public void PlayWarning() { if (audioOn) src.PlayOneShot(warning, 0.9f); }

        static AudioClip MakeChime(float[] notes, float noteLen)
        {
            const int rate = 44100;
            int per = Mathf.RoundToInt(rate * noteLen);
            var data = new float[per * notes.Length];
            for (int n = 0; n < notes.Length; n++)
                for (int i = 0; i < per; i++)
                {
                    float t = (float)i / rate;
                    float env = Mathf.Exp(-t * 18f) * Mathf.Min(1f, i / 200f);
                    data[n * per + i] = Mathf.Sin(2f * Mathf.PI * notes[n] * t) * env * 0.5f;
                }
            var clip = AudioClip.Create("chime", data.Length, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        public static Color ToneColor(Tone t)
        {
            switch (t)
            {
                case Tone.Good: return new Color(0.27f, 0.80f, 0.52f);
                case Tone.Warn: return new Color(0.94f, 0.71f, 0.24f);
                case Tone.Bad: return new Color(0.95f, 0.41f, 0.36f);
                default: return new Color(0.89f, 0.95f, 0.93f);
            }
        }
    }
}
