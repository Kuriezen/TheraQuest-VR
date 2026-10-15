using UnityEngine;

namespace TheraQuest
{
    public enum Tone { Neutral, Good, Warn, Bad }

    /// <summary>
    /// Feedback Generation Module (Section 3.6.3):
    ///   Feedback Decision Engine  -> LiveMessage()
    ///   Visual Cue Generator      -> message + tone colour shown by HUD and figure
    ///   Audio Prompt Generator    -> rep sounds from the mp3s in Assets/TheraQuest
    ///                                (Miss / Great / Awesome / Perfect / Victory),
    ///                                falling back to generated tones if a clip is missing
    ///   Corrective Instruction    -> ExerciseEvaluator.Describe(), logged per rep
    /// </summary>
    public class FeedbackManager : MonoBehaviour
    {
        public bool audioOn = true;

        [Header("Rep sounds")]
        public AudioClip missSound;     // invalid rep or safety flag
        public AudioClip greatSound;    // 80-90%
        public AudioClip awesomeSound;  // 91-95%
        public AudioClip perfectSound;  // 96-100%
        public AudioClip victorySound;  // session completed

        AudioSource src;
        AudioClip good, great, bad, levelUp, badge, warning, click;

#if UNITY_EDITOR
        // Auto-assign the mp3s in Assets/TheraQuest so existing scenes pick them up without manual wiring.
        void Reset() => LoadDefaultClips();
        void OnValidate() => LoadDefaultClips();

        void LoadDefaultClips()
        {
            AudioClip Load(AudioClip current, string file) =>
                current != null ? current : UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/TheraQuest/" + file);
            missSound = Load(missSound, "miss sound.mp3");
            greatSound = Load(greatSound, "great sound.mp3");
            awesomeSound = Load(awesomeSound, "Awesome sound.mp3");
            perfectSound = Load(perfectSound, "Perfect sound.mp3");
            victorySound = Load(victorySound, "victory sound.mp3");
        }
#endif

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
            click = MakeChime(new[] { 1320f }, 0.035f);
            src.ignoreListenerPause = true;   // menu clicks still play while the game is paused
            GameSettings.Changed += ApplyVolume;
            ApplyVolume();
        }

        void OnDestroy() => GameSettings.Changed -= ApplyVolume;

        /// <summary>Settings > Effects volume scales every sound effect.</summary>
        void ApplyVolume() => src.volume = GameSettings.Effects;

        public void PlayClick() { if (audioOn) src.PlayOneShot(click, 0.5f); }

        /// <summary>What a rep sound is called in the subtitles.</summary>
        public static string RepSoundName(RepResult r)
        {
            switch (Grade(r))
            {
                case RepGrade.Perfect: return "Perfect chime";
                case RepGrade.Awesome: return "Awesome chime";
                case RepGrade.Great: return "Great chime";
                default: return "Miss tone";
            }
        }

        public static (string text, Tone tone) LiveMessage(ExerciseEvaluator ev, string fault)
        {
            var ex = ev.Exercise;
            bool numbers = GameSettings.ShowDetails;   // degrees only in the therapist view
            switch (ev.State)
            {
                case LiveState.Resting:
                    return ($"Nice set! Rest time… {Mathf.CeilToInt(ev.RestRemaining)}", Tone.Neutral);
                case LiveState.Unsafe:
                    return (numbers ? $"STOP! Past the safe limit of {ex.safetyMax:0}°. Lower slowly" : "STOP! That's too far. Lower slowly", Tone.Bad);
                case LiveState.Fault: return (fault ?? "Check your posture", Tone.Bad);
                case LiveState.TooFar:
                    return (numbers ? $"Too far. Ease back to {ex.targetAngle:0}°" : "A little too far! Come back down a bit", Tone.Bad);
                case LiveState.Holding: return ($"Hold it… {Mathf.CeilToInt(ex.holdSeconds - ev.HoldTime)}", Tone.Good);
                case LiveState.OnTarget:
                    return (ex.holdSeconds > 0f ? "You did it! Now lower slowly" : "Perfect spot! Now lower slowly", Tone.Good);
                case LiveState.Rising:
                    float left = ex.targetAngle - ev.CurrentAngle;
                    return (numbers ? $"{Mathf.RoundToInt(left)}° to target" : left < ex.targetAngle * 0.2f ? "Almost there!" : "Higher! Keep going", Tone.Warn);
                default: return (ev.CurrentAngle < ex.targetAngle * 0.25f ? ex.cue : "Keep going", Tone.Neutral);
            }
        }

        public enum RepGrade { Miss, Great, Awesome, Perfect }

        /// <summary>Miss: invalid rep or safety flag. Great: up to 90%. Awesome: 91-95%. Perfect: 96-100%.</summary>
        public static RepGrade Grade(RepResult r)
        {
            if (!r.valid || r.pastSafetyMax) return RepGrade.Miss;
            if (r.accuracy >= 96) return RepGrade.Perfect;
            if (r.accuracy >= 91) return RepGrade.Awesome;
            return RepGrade.Great; // valid reps score 85+, so this covers the 80-90% band
        }

        public void PlayRep(RepResult r)
        {
            if (!audioOn) return;
            switch (Grade(r))
            {
                case RepGrade.Perfect: src.PlayOneShot(perfectSound != null ? perfectSound : great, 0.8f); break;
                case RepGrade.Awesome: src.PlayOneShot(awesomeSound != null ? awesomeSound : great, 0.8f); break;
                case RepGrade.Great: src.PlayOneShot(greatSound != null ? greatSound : good, 0.8f); break;
                default: src.PlayOneShot(missSound != null ? missSound : bad, 0.8f); break;
            }
        }

        public void PlayVictory() { if (audioOn) src.PlayOneShot(victorySound != null ? victorySound : levelUp, 0.9f); }

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
                case Tone.Good: return UIKit.Green;
                case Tone.Warn: return UIKit.Orange;
                case Tone.Bad: return UIKit.Red;
                default: return UIKit.Ink;
            }
        }
    }
}
