using System;
using UnityEngine;

namespace TheraQuest
{
    /// <summary>
    /// Player settings from the Settings menu, saved between sessions (PlayerPrefs).
    /// Anything that depends on them listens to <see cref="Changed"/>.
    /// </summary>
    public static class GameSettings
    {
        public static readonly string[] SubtitleSizes = { "Small", "Medium", "Large" };
        public static readonly string[] TurnModes = { "Snap 30°", "Smooth" };
        public static readonly string[] MoveSpeeds = { "Slow", "Normal", "Fast" };

        public static float Music { get; private set; }      // 0..1
        public static float Effects { get; private set; }    // 0..1
        public static bool Subtitles { get; private set; }
        public static int SubtitleSize { get; private set; } // index into SubtitleSizes
        public static int TurnMode { get; private set; }     // index into TurnModes
        public static int MoveSpeed { get; private set; }    // index into MoveSpeeds
        /// <summary>Show angles, accuracy % and tracker details (for the therapist). Off = simple patient view.</summary>
        public static bool ShowDetails { get; private set; }

        public static event Action Changed;

        static GameSettings()
        {
            Music = PlayerPrefs.GetFloat("TheraQuest.Music", 0.5f);
            Effects = PlayerPrefs.GetFloat("TheraQuest.Effects", 0.8f);
            Subtitles = PlayerPrefs.GetInt("TheraQuest.Subtitles", 1) == 1;
            SubtitleSize = PlayerPrefs.GetInt("TheraQuest.SubtitleSize", 1);
            TurnMode = PlayerPrefs.GetInt("TheraQuest.TurnMode", 0);
            MoveSpeed = PlayerPrefs.GetInt("TheraQuest.MoveSpeed", 1);
            ShowDetails = PlayerPrefs.GetInt("TheraQuest.ShowDetails", 0) == 1;
        }

        /// <summary>Volume steps of 10%.</summary>
        public static void StepMusic(int dir) { Music = Step(Music, dir); Save(); }
        public static void StepEffects(int dir) { Effects = Step(Effects, dir); Save(); }
        public static void ToggleSubtitles() { Subtitles = !Subtitles; Save(); }
        public static void StepSubtitleSize(int dir) { SubtitleSize = Wrap(SubtitleSize + dir, SubtitleSizes.Length); Save(); }
        public static void StepTurnMode(int dir) { TurnMode = Wrap(TurnMode + dir, TurnModes.Length); Save(); }
        public static void StepMoveSpeed(int dir) { MoveSpeed = Wrap(MoveSpeed + dir, MoveSpeeds.Length); Save(); }
        public static void ToggleDetails() { ShowDetails = !ShowDetails; Save(); }

        public static string Percent(float v) => v <= 0.001f ? "Off" : $"{Mathf.RoundToInt(v * 100)}%";

        static float Step(float v, int dir) => Mathf.Clamp01(Mathf.Round(v * 10f + dir) / 10f);
        static int Wrap(int i, int n) => (i % n + n) % n;

        static void Save()
        {
            PlayerPrefs.SetFloat("TheraQuest.Music", Music);
            PlayerPrefs.SetFloat("TheraQuest.Effects", Effects);
            PlayerPrefs.SetInt("TheraQuest.Subtitles", Subtitles ? 1 : 0);
            PlayerPrefs.SetInt("TheraQuest.SubtitleSize", SubtitleSize);
            PlayerPrefs.SetInt("TheraQuest.TurnMode", TurnMode);
            PlayerPrefs.SetInt("TheraQuest.MoveSpeed", MoveSpeed);
            PlayerPrefs.SetInt("TheraQuest.ShowDetails", ShowDetails ? 1 : 0);
            PlayerPrefs.Save();
            Changed?.Invoke();
        }
    }
}
