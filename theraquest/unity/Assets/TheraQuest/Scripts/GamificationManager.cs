using System.Collections.Generic;
using UnityEngine;

namespace TheraQuest
{
    /// <summary>
    /// Gamification Module (Section 3.6.3): Score Calculator, Reward Engine,
    /// and the client half of the Progression Controller (the server applies stage-ups).
    ///
    ///   Points per valid rep  = 10 x accuracy%, x combo multiplier (1.0 .. 2.0)
    ///   Invalid rep           = 2 points for effort, combo resets
    ///   Completed session     = +50 bonus
    ///   Stars                 = 3 if avg accuracy >= 90, 2 if >= 75, else 1 (0 if ended early)
    ///   Badges earned in-session: perfect (5 reps >= 95% in a row), combo10, fullrom (within 2°)
    ///   Server adds: first, streak3, adh80, lv5
    /// </summary>
    public class GamificationManager
    {
        public const int CompletionBonus = 50;

        public static readonly Dictionary<string, string> BadgeNames = new Dictionary<string, string>
        {
            { "first", "First steps" }, { "perfect", "Perfect form" }, { "combo10", "Combo 10" },
            { "fullrom", "Bullseye" }, { "streak3", "On a roll" }, { "adh80", "Consistent" }, { "lv5", "Level 5" }
        };

        public int Points { get; private set; }
        public int Combo { get; private set; }
        public float Multiplier => 1f + Mathf.Min(Mathf.Max(Combo - 1, 0), 10) * 0.1f;
        public readonly List<string> EarnedThisSession = new List<string>();

        int perfectRun;
        readonly HashSet<string> alreadyOwned;

        public GamificationManager(IEnumerable<string> ownedBadges)
        {
            alreadyOwned = new HashSet<string>(ownedBadges ?? new string[0]);
        }

        /// <returns>points awarded for this rep</returns>
        public int ScoreRep(RepResult r)
        {
            int pts;
            if (r.valid)
            {
                Combo++;
                perfectRun = r.accuracy >= 95 ? perfectRun + 1 : 0;
                pts = Mathf.RoundToInt(10f * r.accuracy / 100f * Multiplier);
                if (r.error <= 2f) Award("fullrom");
                if (Combo >= 10) Award("combo10");
                if (perfectRun >= 5) Award("perfect");
            }
            else
            {
                Combo = 0;
                perfectRun = 0;
                pts = 2;
            }
            Points += pts;
            return pts;
        }

        public void AddCompletionBonus() { Points += CompletionBonus; }

        public static int Stars(float avgAccuracy, bool completed)
        {
            if (!completed) return 0;
            return avgAccuracy >= 90f ? 3 : avgAccuracy >= 75f ? 2 : 1;
        }

        /// <summary>Same curve as server.py level_from_xp.</summary>
        public static void Level(int xp, out int level, out int into, out int need)
        {
            level = 1; need = 100; int acc = 0;
            while (xp >= acc + need) { acc += need; level++; need = 100 + (level - 1) * 60; }
            into = xp - acc;
        }

        public event System.Action<string> BadgeUnlocked;

        void Award(string code)
        {
            if (alreadyOwned.Contains(code) || EarnedThisSession.Contains(code)) return;
            EarnedThisSession.Add(code);
            BadgeUnlocked?.Invoke(code);
        }
    }
}
