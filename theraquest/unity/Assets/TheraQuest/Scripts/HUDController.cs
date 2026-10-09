using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace TheraQuest
{
    /// <summary>
    /// HUD Display Manager + Exercise Prompt Display + Feedback Overlay.
    /// Builds world-space panels at runtime (readable in the headset and on the laptop screen).
    /// 1000 canvas pixels = 1 metre.
    /// </summary>
    public class HUDController : MonoBehaviour
    {
        static readonly Color Panel = new Color(0.04f, 0.10f, 0.11f, 0.86f);
        static readonly Color Ink = new Color(0.89f, 0.95f, 0.93f);
        static readonly Color Muted = new Color(0.56f, 0.66f, 0.65f);
        static readonly Color Accent = new Color(0.18f, 0.76f, 0.68f);
        static readonly Color Gold = new Color(0.96f, 0.77f, 0.32f);

        Font font;
        Text patientTxt, levelTxt, sourceTxt, exNameTxt, angleTxt, repsLabel, repsTxt, scoreTxt, comboTxt, questTxt, badgesTxt, helpTxt;
        Text feedbackTxt, popTxt, sumTitle, sumBody, sumStars;
        Image xpFill, feedbackBg, angleBar, angleTargetBand, angleSafetyMax;
        GameObject summary, painPanel;
        Text painTitle, painHint;
        readonly Image[] painBoxes = new Image[11];
        float popTimer;
        Vector3 popStart;

        void Awake()
        {
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            BuildLeft();
            BuildRight();
            BuildBanner();
            BuildSummary();
            BuildPain();
        }

        // ---------------------------------------------------------------- builders
        Canvas MakeCanvas(string name, Vector3 pos, float yaw, Vector2 size)
        {
            var go = new GameObject(name, typeof(Canvas));
            go.transform.SetParent(transform, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(0, yaw, 0);
            go.transform.localScale = Vector3.one * 0.001f;
            var c = go.GetComponent<Canvas>();
            c.renderMode = RenderMode.WorldSpace;
            ((RectTransform)go.transform).sizeDelta = size;
            return c;
        }

        Image Box(Transform parent, Vector2 pos, Vector2 size, Color color, string name = "Box")
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            var img = go.GetComponent<Image>();
            img.color = color;
            return img;
        }

        Text Label(Transform parent, Vector2 pos, Vector2 size, int fontSize, Color color, TextAnchor align = TextAnchor.UpperLeft, FontStyle style = FontStyle.Normal)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            var t = go.GetComponent<Text>();
            t.font = font;
            t.fontSize = fontSize;
            t.color = color;
            t.alignment = align;
            t.fontStyle = style;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.supportRichText = true;
            return t;
        }

        void BuildLeft()
        {
            var c = MakeCanvas("HUD Left", new Vector3(-1.05f, 1.95f, 2.2f), -18f, new Vector2(640, 860)).transform;
            Box(c, Vector2.zero, new Vector2(640, 860), Panel, "Panel");
            Label(c, new Vector2(32, -26), new Vector2(580, 50), 40, Accent, TextAnchor.UpperLeft, FontStyle.Bold).text = "THERAQUEST";
            patientTxt = Label(c, new Vector2(32, -78), new Vector2(580, 40), 28, Muted);
            levelTxt = Label(c, new Vector2(32, -122), new Vector2(580, 40), 30, Gold, TextAnchor.UpperLeft, FontStyle.Bold);
            Box(c, new Vector2(32, -168), new Vector2(576, 16), new Color(1, 1, 1, 0.12f), "XP track");
            xpFill = Box(c, new Vector2(32, -168), new Vector2(0, 16), Gold, "XP fill");

            exNameTxt = Label(c, new Vector2(32, -214), new Vector2(580, 50), 38, Ink, TextAnchor.UpperLeft, FontStyle.Bold);
            angleTxt = Label(c, new Vector2(32, -266), new Vector2(580, 120), 104, Ink, TextAnchor.UpperLeft, FontStyle.Bold);

            // Angle gauge: 0..180 degrees, teal band = target ± tolerance, red line = safety max
            Box(c, new Vector2(32, -398), new Vector2(576, 22), new Color(1, 1, 1, 0.10f), "Gauge");
            angleTargetBand = Box(c, new Vector2(32, -398), new Vector2(10, 22), new Color(Accent.r, Accent.g, Accent.b, 0.45f), "Target band");
            angleSafetyMax = Box(c, new Vector2(32, -392), new Vector2(4, 34), FeedbackManager.ToneColor(Tone.Bad), "Safety max");
            angleBar = Box(c, new Vector2(32, -404), new Vector2(6, 34), Gold, "Needle");

            repsLabel = Label(c, new Vector2(32, -450), new Vector2(200, 30), 24, Muted);
            Label(c, new Vector2(232, -450), new Vector2(180, 30), 24, Muted).text = "SCORE";
            Label(c, new Vector2(432, -450), new Vector2(180, 30), 24, Muted).text = "COMBO";
            repsTxt = Label(c, new Vector2(32, -482), new Vector2(200, 80), 64, Ink, TextAnchor.UpperLeft, FontStyle.Bold);
            scoreTxt = Label(c, new Vector2(232, -482), new Vector2(200, 80), 64, Ink, TextAnchor.UpperLeft, FontStyle.Bold);
            comboTxt = Label(c, new Vector2(432, -482), new Vector2(200, 80), 64, Gold, TextAnchor.UpperLeft, FontStyle.Bold);

            sourceTxt = Label(c, new Vector2(32, -600), new Vector2(580, 240), 26, Muted);
        }

        void BuildRight()
        {
            var c = MakeCanvas("HUD Right", new Vector3(1.05f, 1.95f, 2.2f), 18f, new Vector2(640, 860)).transform;
            Box(c, Vector2.zero, new Vector2(640, 860), Panel, "Panel");
            Label(c, new Vector2(32, -26), new Vector2(580, 50), 36, Ink, TextAnchor.UpperLeft, FontStyle.Bold).text = "Today's quest";
            questTxt = Label(c, new Vector2(32, -86), new Vector2(580, 330), 32, Ink);
            Label(c, new Vector2(32, -420), new Vector2(580, 50), 32, Ink, TextAnchor.UpperLeft, FontStyle.Bold).text = "Badges";
            badgesTxt = Label(c, new Vector2(32, -466), new Vector2(580, 150), 26, Muted);
            helpTxt = Label(c, new Vector2(32, -640), new Vector2(580, 210), 23, Muted);
            helpTxt.text = "<b>Start</b>  Space / A or trigger\n<b>End early</b>  Esc / B\n<b>Input mode</b>  Tab / X\n<b>Recenter</b>  R / Y\n<b>Keyboard mode</b>  Up / Down arrows or right stick\n<b>Pain score</b>  Left / Right arrows or right stick";
        }

        void BuildBanner()
        {
            var c = MakeCanvas("Feedback Banner", new Vector3(0, 2.32f, 2.5f), 0, new Vector2(1200, 120)).transform;
            feedbackBg = Box(c, Vector2.zero, new Vector2(1200, 120), Panel, "Banner");
            feedbackTxt = Label(c, new Vector2(0, -14), new Vector2(1200, 100), 54, Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
            popTxt = Label(c, new Vector2(0, -140), new Vector2(1200, 100), 72, Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
            popStart = popTxt.rectTransform.anchoredPosition;
            popTxt.text = "";
        }

        void BuildSummary()
        {
            var c = MakeCanvas("Summary", new Vector3(0, 1.55f, 1.6f), 0, new Vector2(900, 920));
            c.sortingOrder = 10;
            summary = c.gameObject;
            var t = c.transform;
            Box(t, Vector2.zero, new Vector2(900, 920), new Color(0.03f, 0.08f, 0.09f, 0.96f), "Panel");
            sumTitle = Label(t, new Vector2(0, -40), new Vector2(900, 80), 64, Ink, TextAnchor.UpperCenter, FontStyle.Bold);
            sumStars = Label(t, new Vector2(0, -130), new Vector2(900, 110), 96, Gold, TextAnchor.UpperCenter, FontStyle.Bold);
            sumBody = Label(t, new Vector2(60, -260), new Vector2(780, 620), 32, Ink, TextAnchor.UpperLeft);
            summary.SetActive(false);
        }

        // Pain check-in: 0-10 numeric rating scale, picked with left / right and confirmed with Start.
        void BuildPain()
        {
            var c = MakeCanvas("Pain Check", new Vector3(0, 1.55f, 1.6f), 0, new Vector2(1000, 560));
            c.sortingOrder = 11;
            painPanel = c.gameObject;
            var t = c.transform;
            Box(t, Vector2.zero, new Vector2(1000, 560), new Color(0.03f, 0.08f, 0.09f, 0.96f), "Panel");
            painTitle = Label(t, new Vector2(0, -34), new Vector2(1000, 70), 52, Ink, TextAnchor.UpperCenter, FontStyle.Bold);
            Label(t, new Vector2(0, -108), new Vector2(1000, 40), 28, Muted, TextAnchor.UpperCenter).text = "0 = no pain   ·   10 = worst pain imaginable";
            for (int i = 0; i <= 10; i++)
            {
                painBoxes[i] = Box(t, new Vector2(32 + i * 86, -180), new Vector2(76, 110), PainColor(i), "Pain " + i);
                Label(painBoxes[i].transform, new Vector2(0, -20), new Vector2(76, 80), 56, new Color(0.04f, 0.10f, 0.11f),
                      TextAnchor.UpperCenter, FontStyle.Bold).text = i.ToString();
            }
            painHint = Label(t, new Vector2(50, -340), new Vector2(900, 200), 30, Ink, TextAnchor.UpperCenter);
            painPanel.SetActive(false);
        }

        static Color PainColor(int i)
        {
            var good = FeedbackManager.ToneColor(Tone.Good);
            var warn = FeedbackManager.ToneColor(Tone.Warn);
            var bad = FeedbackManager.ToneColor(Tone.Bad);
            return i <= 5 ? Color.Lerp(good, warn, i / 5f) : Color.Lerp(warn, bad, (i - 5) / 5f);
        }

        // ---------------------------------------------------------------- updates
        public void SetPatient(PatientInfo p, ProgramInfo prog, bool online, PhaseInfo phase = null)
        {
            string plan = phase != null && !string.IsNullOrEmpty(phase.name) ? $"{phase.protocol} · {phase.name}" : prog.name;
            patientTxt.text = $"{p.code} · {plan}" + (online ? "" : "   <color=#F0B43C>(offline)</color>");
            GamificationManager.Level(p.xp, out int lv, out int into, out int need);
            levelTxt.text = $"LV {lv}   {into} / {need} XP   ·   streak {p.streak}";
            xpFill.rectTransform.sizeDelta = new Vector2(576f * Mathf.Clamp01((float)into / need), 16);
            var sb = new StringBuilder();
            foreach (var kv in GamificationManager.BadgeNames)
            {
                bool on = System.Array.IndexOf(p.badges ?? new string[0], kv.Key) >= 0;
                sb.Append(on ? $"<color=#F5C451>■ {kv.Value}</color>   " : $"□ {kv.Value}   ");
            }
            badgesTxt.text = sb.ToString();
        }

        public void SetExercise(ExerciseDef ex, ExerciseEvaluator ev, float angle, int score, float mult)
        {
            if (ex == null) return;
            exNameTxt.text = ex.name;
            angleTxt.text = $"{Mathf.RoundToInt(angle)}°<size=44><color=#8EA7A4>  / {ex.targetAngle:0}° ± {ex.tolerance:0}</color></size>";
            int sets = Mathf.Max(1, ex.sets);
            repsLabel.text = sets > 1 ? $"REPS · SET {(ev != null ? ev.SetNumber : 1)}/{sets}" : "REPS";
            repsTxt.text = $"{(ev != null ? ev.RepsInSet : 0)}/{ex.reps}";
            scoreTxt.text = score.ToString();
            comboTxt.text = $"×{mult:0.0}";
            float px = 576f / 180f;
            angleTargetBand.rectTransform.anchoredPosition = new Vector2(32 + (ex.targetAngle - ex.tolerance) * px, -398);
            angleTargetBand.rectTransform.sizeDelta = new Vector2(ex.tolerance * 2f * px, 22);
            angleSafetyMax.enabled = ex.safetyMax > 0f;
            angleSafetyMax.rectTransform.anchoredPosition = new Vector2(32 + Mathf.Clamp(ex.safetyMax, 0, 180) * px - 2, -392);
            angleBar.rectTransform.anchoredPosition = new Vector2(32 + Mathf.Clamp(angle, 0, 180) * px - 3, -404);
        }

        public void SetQuest(IList<ExerciseDef> exs, IList<ExerciseEvaluator> evs, int activeIndex, bool running)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < exs.Count; i++)
            {
                var ex = exs[i];
                int v = evs != null && i < evs.Count && evs[i] != null ? evs[i].ValidReps : 0;
                bool done = v >= ex.TotalReps;
                bool current = running && i == activeIndex;
                string mark = done ? "<color=#45CC85>✓</color>" : (current ? "<color=#2FC2AE>▶</color>" : "○");
                string dose = (ex.sets > 1 ? $"{ex.sets}×{ex.reps}" : $"{ex.reps} reps") + (ex.holdSeconds > 0 ? $" · hold {ex.holdSeconds:0.#} s" : "");
                string line = $"{mark}  {ex.name}   <b>{v}/{ex.TotalReps}</b>\n     <size=24><color=#8EA7A4>{ex.targetAngle:0}° ± {ex.tolerance:0} · {dose}</color></size>\n";
                if (current) line += $"     <size=22><color=#8EA7A4>{ex.position}. Camera {ex.CameraText}</color></size>\n";
                sb.Append(current ? $"<color=#E3F1EE>{line}</color>" : line);
            }
            questTxt.fontSize = exs.Count > 4 ? 24 : 32;   // keep long programs above the badges
            questTxt.text = sb.ToString();
        }

        public void SetSource(string text) { sourceTxt.text = text; }

        public void SetFeedback(string text, Tone tone)
        {
            feedbackTxt.text = text;
            feedbackTxt.color = FeedbackManager.ToneColor(tone);
            var c = FeedbackManager.ToneColor(tone);
            feedbackBg.color = tone == Tone.Neutral ? Panel : new Color(c.r * 0.25f, c.g * 0.25f, c.b * 0.25f, 0.9f);
        }

        public void Pop(string text, bool good)
        {
            popTxt.text = text;
            popTxt.color = good ? Gold : new Color(1f, 0.7f, 0.67f);
            popTimer = 1f;
        }

        public void ShowSummary(string title, int stars, string body)
        {
            sumTitle.text = title;
            sumStars.text = new string('★', stars) + $"<color=#3A4A4C>{new string('★', 3 - stars)}</color>";
            sumBody.text = body;
            summary.SetActive(true);
        }

        public void HideSummary() { summary.SetActive(false); }

        public void ShowPain(string title, int value, string hint)
        {
            painTitle.text = title;
            painHint.text = hint;
            for (int i = 0; i <= 10; i++)
            {
                var col = PainColor(i);
                col.a = i == value ? 1f : 0.3f;
                painBoxes[i].color = col;
                painBoxes[i].rectTransform.localScale = Vector3.one * (i == value ? 1.12f : 1f);
            }
            painPanel.SetActive(true);
        }

        public void HidePain() { painPanel.SetActive(false); }
        public bool SummaryVisible => summary.activeSelf;

        void Update()
        {
            if (popTimer > 0f)
            {
                popTimer -= Time.deltaTime;
                float k = 1f - popTimer;
                popTxt.rectTransform.anchoredPosition = popStart + new Vector3(0, 60f * k, 0);
                var c = popTxt.color; c.a = Mathf.Clamp01(popTimer * 1.5f); popTxt.color = c;
                if (popTimer <= 0f) popTxt.text = "";
            }
        }
    }
}
