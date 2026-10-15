using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;

namespace TheraQuest
{
    /// <summary>
    /// HUD Display Manager + Exercise Prompt Display + Feedback Overlay, in a simple cartoon style for patients:
    ///   left card   - level, exercise, a "how far" power meter (green zone + star = target, red line = stop),
    ///                 hold / rest bar, rep dots, points and combo
    ///   right card  - today's quest checklist and badges
    ///   banner      - the coach's speech bubble
    /// No degrees or percentages unless Settings > Show numbers (therapist) is on; the dashboard has all the data.
    /// The panels live in the player's view like a game overlay (Just Dance style), not beside the coach:
    ///   laptop screen - locked to the screen edges (top-left status, top-right quest, top-centre bubble)
    ///   headset       - gently follow the head, level, in the upper view, side cards angled toward the player
    /// They draw on top of the scene, so walls and the coach never hide them. 1000 canvas units = 1 metre (before scaling).
    /// </summary>
    public class HUDController : MonoBehaviour
    {
        const float MeterW = 568f, MeterH = 74f;

        // left card
        Text levelTxt, exNameTxt, setTxt, sideTxt, numbersTxt, holdLabel, repsCount, scoreTxt, comboTxt, sourceTxt, stopTxt, starTxt;
        Image xpFill, meterFill, zone, stopLine, holdTrack, holdFill, setPill, sidePill, pointsTile;
        Transform dotRoot;
        readonly List<Image> dots = new List<Image>();
        int dotsShown = -1, filledShown, lastScore;

        // right card
        Text questCount;
        Transform questRoot;
        readonly List<QuestRow> quest = new List<QuestRow>();
        readonly List<Image> badgeChips = new List<Image>();
        Text badgeCount;

        // banner, pop, summary
        Image bubbleFill, bubbleIcon;
        UIKit.CardRef bubbleCard;
        Bounce bubbleBounce;
        float bubbleH = -1f;
        Text bubbleIconTxt, feedbackTxt, popTxt, sumTitle, sumBody, sumStars;
        GameObject summary;
        string lastFeedback = "";
        float popTimer;
        Vector2 popStart;

        sealed class QuestRow { public Image bg, circle, barFill; public Text mark, name, count; }

        Canvas leftCanvas, rightCanvas, bubbleCanvas, summaryCanvas;
        Transform view;
        bool viewPlaced;

        const float GlassAlpha = 0.55f;      // see-through cards
        const float BubbleShowSeconds = 6f;  // calm messages fade after this long
        CanvasGroup bubbleGroup;
        float bubbleChangedAt;
        Tone bubbleTone;

        // Quest card: open on menus, minimised during a session; M / left stick click toggles
        UIKit.CardRef questCard;
        readonly List<GameObject> questFullOnly = new List<GameObject>();
        Text questHint;
        bool? questOpenByUser;
        bool questRunning;
        float questH = 900f;
        IList<ExerciseDef> lastExs;
        IList<ExerciseEvaluator> lastEvs;
        int lastActive;
        bool QuestOpen => questOpenByUser ?? !questRunning;

        void Awake()
        {
            BuildLeft();
            BuildRight();
            BuildBanner();
            BuildSummary();
            // Move the panels into an overlay that follows the camera, and draw them on top of the scene
            view = new GameObject("View HUD").transform;
            foreach (var c in new[] { leftCanvas, rightCanvas, bubbleCanvas, summaryCanvas })
            {
                c.transform.SetParent(view, false);
                UIKit.MakeOnTop(c, c == summaryCanvas ? 35 : 30);
            }
            bubbleGroup = bubbleCanvas.gameObject.AddComponent<CanvasGroup>();
            bubbleChangedAt = Time.unscaledTime;
        }

        void OnDestroy() { if (view != null) Destroy(view.gameObject); }

        void LateUpdate()
        {
            var cam = Camera.main;
            if (cam == null) return;
            bool headset = XRSettings.isDeviceActive;
            var ct = cam.transform;
            if (headset && viewPlaced)
            {
                // Lazy follow in VR: stays level and catches up smoothly (a face-locked panel is uncomfortable)
                float k = 1f - Mathf.Exp(-5f * Time.unscaledDeltaTime);
                view.position = Vector3.Lerp(view.position, ct.position, Mathf.Min(1f, k * 2f));
                view.rotation = Quaternion.Slerp(view.rotation, Quaternion.Euler(0f, ct.eulerAngles.y, 0f), k);
            }
            else if (headset) view.SetPositionAndRotation(ct.position, Quaternion.Euler(0f, ct.eulerAngles.y, 0f));
            else view.SetPositionAndRotation(ct.position, ct.rotation);   // laptop: locked to the screen
            viewPlaced = true;
            UpdateQuestCard();
            UpdateBubbleFade();
            LayoutView(cam, headset);
        }

        /// <summary>Places the panels at the edges of the view, leaving the centre (the coach) clear.</summary>
        void LayoutView(Camera cam, bool headset)
        {
            const float Scale = 0.00055f;              // canvas units -> metres
            Vector2 card = new Vector2(640f, 900f) * Scale;
            float d, x, y, bubbleY, yaw;
            if (headset)
            {
                d = 1.5f; x = 0.82f; y = 0.18f; bubbleY = 0.62f; yaw = 22f;
            }
            else
            {
                d = 1.2f;
                float halfH = d * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
                float halfW = halfH * cam.aspect, margin = 0.045f;
                x = halfW - margin - card.x / 2f;
                y = halfH - margin - card.y / 2f;
                bubbleY = halfH - margin - 150f * Scale / 2f;
                yaw = 0f;
            }
            Place(leftCanvas, new Vector3(-x, y, d), -yaw, Scale);
            float rightY = y + (card.y - questH * Scale) / 2f;   // same top edge as the left card while it shrinks
            Place(rightCanvas, new Vector3(x, rightY, d), yaw, Scale);
            Place(bubbleCanvas, new Vector3(0f, bubbleY, d), 0f, Scale);
            Place(summaryCanvas, new Vector3(0f, headset ? 0.1f : 0f, d * 0.95f), 0f, Scale * 1.1f);
        }

        static void Place(Canvas c, Vector3 pos, float yaw, float scale)
        {
            var t = c.transform;
            t.localPosition = pos;
            t.localRotation = Quaternion.Euler(0f, yaw, 0f);
            t.localScale = Vector3.one * scale;
        }

        static Text Label(Transform p, Vector2 pos, Vector2 size, int fs, Color c, TextAnchor a = TextAnchor.UpperLeft, FontStyle st = FontStyle.Normal) =>
            UIKit.Label(p, pos, size, fs, c, a, st);
        static Image Box(Transform p, Vector2 pos, Vector2 size, Color c, string name = "Box", bool round = true) =>
            UIKit.Box(p, pos, size, c, name, round);

        // ---------------------------------------------------------------- builders
        void BuildLeft()
        {
            leftCanvas = UIKit.Canvas(transform, "HUD Left", Vector3.zero, 0f, new Vector2(640, 900));
            var c = leftCanvas.transform;
            var leftCard = UIKit.Card(c, new Vector2(640, 900), UIKit.Cream, UIKit.Teal);
            leftCard.Glass(GlassAlpha);
            var card = leftCard.fill.transform;

            levelTxt = UIKit.Pill(card, new Vector2(28, -36), new Vector2(180, 56), UIKit.Purple, "LEVEL 1", 30, Color.white);
            Box(card, new Vector2(224, -51), new Vector2(372, 26), UIKit.Track, "XP track");
            xpFill = Box(card, new Vector2(224, -51), new Vector2(26, 26), UIKit.Yellow, "XP fill");

            exNameTxt = Label(card, new Vector2(28, -110), new Vector2(570, 110), 46, UIKit.Ink, TextAnchor.UpperLeft, FontStyle.Bold);
            setTxt = UIKit.Pill(card, new Vector2(28, -226), new Vector2(200, 52), UIKit.Orange, "GET READY", 26, Color.white);
            setPill = (Image)setTxt.transform.parent.GetComponent<Graphic>();
            sideTxt = UIKit.Pill(card, new Vector2(240, -226), new Vector2(230, 52), UIKit.Blue, "", 26, Color.white);
            sidePill = (Image)sideTxt.transform.parent.GetComponent<Graphic>();

            // Power meter
            Label(card, new Vector2(28, -306), new Vector2(300, 30), 24, UIKit.Muted, TextAnchor.UpperLeft, FontStyle.Bold).text = "HOW FAR";
            numbersTxt = Label(card, new Vector2(28, -306), new Vector2(MeterW, 30), 24, UIKit.Muted, TextAnchor.UpperRight, FontStyle.Bold);
            Box(card, new Vector2(28, -342), new Vector2(MeterW, MeterH), UIKit.Track, "Meter track");
            zone = Box(card, new Vector2(28, -342), new Vector2(60, MeterH), new Color(UIKit.Green.r, UIKit.Green.g, UIKit.Green.b, 0.35f), "Target zone");
            meterFill = Box(card, new Vector2(28, -342), new Vector2(MeterH, MeterH), UIKit.Teal, "Meter fill");
            stopLine = Box(card, new Vector2(28, -334), new Vector2(8, MeterH + 16), UIKit.Red, "Stop line", false);
            stopTxt = Label(card, new Vector2(0, -422), new Vector2(90, 26), 20, UIKit.Red, TextAnchor.UpperCenter, FontStyle.Bold);
            stopTxt.text = "STOP";
            starTxt = UIKit.Stroke(Label(card, new Vector2(0, -300), new Vector2(80, 70), 60, UIKit.Yellow, TextAnchor.MiddleCenter, FontStyle.Bold), UIKit.Outline, 3f);
            starTxt.text = "★";

            // Hold / rest bar
            holdLabel = Label(card, new Vector2(28, -456), new Vector2(110, 30), 24, UIKit.Muted, TextAnchor.UpperLeft, FontStyle.Bold);
            holdTrack = Box(card, new Vector2(140, -456), new Vector2(456, 28), UIKit.Track, "Hold track");
            holdFill = Box(card, new Vector2(140, -456), new Vector2(28, 28), UIKit.Green, "Hold fill");

            // Rep dots
            Label(card, new Vector2(28, -510), new Vector2(200, 30), 24, UIKit.Muted, TextAnchor.UpperLeft, FontStyle.Bold).text = "REPS";
            repsCount = Label(card, new Vector2(28, -506), new Vector2(MeterW, 36), 32, UIKit.Ink, TextAnchor.UpperRight, FontStyle.Bold);
            dotRoot = new GameObject("Dots", typeof(RectTransform)).transform;
            dotRoot.SetParent(card, false);
            var dr = (RectTransform)dotRoot;
            dr.anchorMin = dr.anchorMax = new Vector2(0, 1);
            dr.pivot = new Vector2(0, 1);
            dr.anchoredPosition = new Vector2(28, -552);

            // Points and combo tiles
            pointsTile = Box(card, new Vector2(28, -630), new Vector2(276, 140), UIKit.Yellow, "Points tile");
            Label(pointsTile.transform, new Vector2(20, -14), new Vector2(240, 30), 24, UIKit.Ink, TextAnchor.UpperLeft, FontStyle.Bold).text = "POINTS";
            scoreTxt = Label(pointsTile.transform, new Vector2(20, -46), new Vector2(240, 80), 62, UIKit.Ink, TextAnchor.UpperLeft, FontStyle.Bold);
            UIKit.Bouncy(pointsTile);
            var comboTile = Box(card, new Vector2(320, -630), new Vector2(276, 140), UIKit.Pink, "Combo tile");
            Label(comboTile.transform, new Vector2(20, -14), new Vector2(240, 30), 24, Color.white, TextAnchor.UpperLeft, FontStyle.Bold).text = "COMBO";
            comboTxt = Label(comboTile.transform, new Vector2(20, -46), new Vector2(240, 80), 62, Color.white, TextAnchor.UpperLeft, FontStyle.Bold);

            sourceTxt = Label(card, new Vector2(28, -790), new Vector2(MeterW, 90), 24, UIKit.Muted);
        }

        void BuildRight()
        {
            rightCanvas = UIKit.Canvas(transform, "HUD Right", Vector3.zero, 0f, new Vector2(640, 900));
            var c = rightCanvas.transform;
            questCard = UIKit.Card(c, new Vector2(640, 900), UIKit.Cream, UIKit.Orange);
            questCard.Glass(GlassAlpha);
            var card = questCard.fill.transform;
            Label(card, new Vector2(28, -36), new Vector2(400, 56), 42, UIKit.Ink, TextAnchor.UpperLeft, FontStyle.Bold).text = "Today's quest";
            questCount = UIKit.Pill(card, new Vector2(440, -36), new Vector2(156, 52), UIKit.Teal, "0 / 0", 28, Color.white);
            questRoot = new GameObject("Quest rows", typeof(RectTransform)).transform;
            questRoot.SetParent(card, false);
            var qr = (RectTransform)questRoot;
            qr.anchorMin = qr.anchorMax = new Vector2(0, 1);
            qr.pivot = new Vector2(0, 1);
            qr.anchoredPosition = new Vector2(20, -110);

            var badgesTitle = Label(card, new Vector2(28, -712), new Vector2(300, 40), 30, UIKit.Ink, TextAnchor.UpperLeft, FontStyle.Bold);
            badgesTitle.text = "Badges";
            badgeCount = Label(card, new Vector2(28, -712), new Vector2(568, 40), 26, UIKit.Muted, TextAnchor.UpperRight, FontStyle.Bold);
            questFullOnly.Add(badgesTitle.gameObject);
            questFullOnly.Add(badgeCount.gameObject);
            for (int i = 0; i < GamificationManager.BadgeNames.Count; i++)
            {
                var chip = Box(card, new Vector2(28 + i * 80, -758), new Vector2(64, 64), UIKit.Track, "Badge");
                Label(chip.transform, Vector2.zero, new Vector2(64, 64), 40, UIKit.Ink, TextAnchor.MiddleCenter, FontStyle.Bold).text = "★";
                badgeChips.Add(chip);
                questFullOnly.Add(chip.gameObject);
            }
            questHint = Label(card, new Vector2(28, -838), new Vector2(568, 40), 22, UIKit.Muted);
        }

        void BuildBanner()
        {
            bubbleCanvas = UIKit.Canvas(transform, "Coach Bubble", Vector3.zero, 0f, new Vector2(1200, 150));
            var c = bubbleCanvas.transform;
            // The bubble grows downward to fit what the coach says (see FitBubble); the holder bounces as one piece.
            var holder = new GameObject("Bubble", typeof(RectTransform)).transform;
            holder.SetParent(c, false);
            var hr = (RectTransform)holder;
            hr.anchorMin = hr.anchorMax = hr.pivot = new Vector2(0, 1);
            hr.sizeDelta = new Vector2(1200, 150);
            bubbleCard = UIKit.Card(holder, new Vector2(1200, 150), UIKit.Cream, UIKit.Teal);
            bubbleCard.Glass(GlassAlpha + 0.1f);
            bubbleFill = bubbleCard.fill;
            bubbleBounce = UIKit.Bouncy(hr);
            var f = bubbleFill.transform;
            bubbleIcon = Box(f, new Vector2(18, -26), new Vector2(92, 92), UIKit.Teal, "Icon");
            bubbleIconTxt = Label(bubbleIcon.transform, Vector2.zero, new Vector2(92, 92), 56, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            // Text starts below the strip on top of the card
            feedbackTxt = Label(f, new Vector2(128, -34), new Vector2(1040, 100), 42, UIKit.Ink, TextAnchor.MiddleLeft, FontStyle.Bold);

            popTxt = UIKit.Stroke(Label(c, new Vector2(0, -170), new Vector2(1200, 110), 84, UIKit.Yellow, TextAnchor.MiddleCenter, FontStyle.Bold), UIKit.Outline, 4f);
            UIKit.Bouncy(popTxt);
            popStart = popTxt.rectTransform.anchoredPosition;
            popTxt.text = "";
        }

        void BuildSummary()
        {
            var c = summaryCanvas = UIKit.Canvas(transform, "Summary", Vector3.zero, 0f, new Vector2(900, 940));
            c.sortingOrder = 10;
            summary = c.gameObject;
            var card = UIKit.Card(c.transform, new Vector2(900, 940), UIKit.Cream, UIKit.Yellow).fill.transform;
            sumTitle = UIKit.Stroke(Label(card, new Vector2(0, -36), new Vector2(884, 90), 72, UIKit.Orange, TextAnchor.UpperCenter, FontStyle.Bold), UIKit.Outline, 3f);
            sumStars = UIKit.Stroke(Label(card, new Vector2(0, -130), new Vector2(884, 140), 124, UIKit.Yellow, TextAnchor.UpperCenter, FontStyle.Bold), UIKit.Outline, 4f);
            UIKit.Bouncy(sumStars);
            sumBody = Label(card, new Vector2(60, -290), new Vector2(764, 620), 34, UIKit.Ink, TextAnchor.UpperLeft);
            summary.SetActive(false);
        }

        // ---------------------------------------------------------------- updates
        public void SetPatient(PatientInfo p, ProgramInfo prog, bool online, PhaseInfo phase = null)
        {
            GamificationManager.Level(p.xp, out int lv, out int into, out int need);
            levelTxt.text = $"LEVEL {lv}";
            xpFill.rectTransform.sizeDelta = new Vector2(Mathf.Max(26f, 372f * Mathf.Clamp01((float)into / need)), 26);
            int i = 0, earned = 0;
            foreach (var kv in GamificationManager.BadgeNames)
            {
                bool on = System.Array.IndexOf(p.badges ?? new string[0], kv.Key) >= 0;
                if (on) earned++;
                if (i < badgeChips.Count) badgeChips[i++].color = on ? UIKit.Yellow : UIKit.Track;
            }
            badgeCount.text = $"{earned} of {GamificationManager.BadgeNames.Count}";
        }

        public void SetExercise(ExerciseDef ex, ExerciseEvaluator ev, float angle, int score, float mult)
        {
            if (ex == null) return;
            exNameTxt.text = ex.name;
            int sets = Mathf.Max(1, ex.sets);
            setTxt.text = ev == null ? "GET READY" : $"SET {ev.SetNumber} / {sets}";
            setPill.color = ev == null ? UIKit.Muted : UIKit.Orange;
            sidePill.gameObject.SetActive(ex.side == "L" || ex.side == "R");
            sideTxt.text = (ex.side == "L" ? "LEFT " : "RIGHT ") + (ex.joint ?? "").ToUpper();

            // Power meter, scaled to this exercise so small and big movements both fill nicely
            float max = Mathf.Max(30f, Mathf.Max(ex.targetAngle + ex.tolerance, ex.safetyMax) * 1.12f);
            float X(float deg) => Mathf.Clamp01(deg / max) * MeterW;
            float lo = X(ex.targetAngle - ex.tolerance), hi = X(ex.targetAngle + ex.tolerance);
            zone.rectTransform.anchoredPosition = new Vector2(28 + lo, -342);
            zone.rectTransform.sizeDelta = new Vector2(Mathf.Max(MeterH * 0.6f, hi - lo), MeterH);
            meterFill.rectTransform.sizeDelta = new Vector2(Mathf.Max(MeterH, X(angle)), MeterH);
            var state = ev != null ? ev.State : LiveState.Idle;
            meterFill.color = state == LiveState.Unsafe || state == LiveState.Fault ? UIKit.Red
                            : state == LiveState.TooFar ? UIKit.Orange
                            : state == LiveState.OnTarget || state == LiveState.Holding ? UIKit.Green : UIKit.Teal;
            starTxt.rectTransform.anchoredPosition = new Vector2(28 + X(ex.targetAngle) - 40, -270);
            bool hasStop = ex.safetyMax > 0f;
            stopLine.enabled = stopTxt.enabled = hasStop;
            stopLine.rectTransform.anchoredPosition = new Vector2(28 + X(ex.safetyMax) - 4, -334);
            stopTxt.rectTransform.anchoredPosition = new Vector2(28 + X(ex.safetyMax) - 45, -422);
            numbersTxt.text = GameSettings.ShowDetails ? $"{Mathf.RoundToInt(angle)}° / {ex.targetAngle:0}° ± {ex.tolerance:0}" : "";

            // Hold or rest bar
            bool resting = ev != null && ev.Resting, hold = ex.holdSeconds > 0f;
            holdLabel.enabled = holdTrack.enabled = holdFill.enabled = resting || hold;
            if (resting)
            {
                holdLabel.text = "REST";
                holdFill.color = UIKit.Blue;
                holdFill.rectTransform.sizeDelta = new Vector2(Mathf.Max(28f, 456f * Mathf.Clamp01(ev.RestRemaining / Mathf.Max(1f, ex.restSeconds))), 28);
            }
            else if (hold)
            {
                holdLabel.text = "HOLD";
                holdFill.color = UIKit.Green;
                float h = ev != null ? Mathf.Clamp01(ev.HoldTime / ex.holdSeconds) : 0f;
                holdFill.rectTransform.sizeDelta = new Vector2(Mathf.Max(28f, 456f * h), 28);
            }

            // Rep dots for this set
            int filled = ev != null ? ev.RepsInSet : 0;
            if (dotsShown != ex.reps) BuildDots(ex.reps);
            for (int i = 0; i < dots.Count; i++) dots[i].color = i < filled ? UIKit.Teal : UIKit.Track;
            if (filled > filledShown && filled - 1 < dots.Count) UIKit.Bouncy(dots[filled - 1]).Punch(0.45f);
            filledShown = filled;
            repsCount.text = $"{filled} / {ex.reps}";

            if (score > lastScore) UIKit.Bouncy(pointsTile).Punch(0.08f);
            lastScore = score;
            scoreTxt.text = score.ToString();
            comboTxt.text = $"×{mult:0.0}";
        }

        void BuildDots(int reps)
        {
            foreach (Transform d in dotRoot) Destroy(d.gameObject);
            dots.Clear();
            int n = Mathf.Clamp(reps, 1, 30);
            float step = Mathf.Min(47f, MeterW / n), size = step - 7f;
            for (int i = 0; i < n; i++) dots.Add(Box(dotRoot, new Vector2(i * step, 0), new Vector2(size, size), UIKit.Track, "Dot"));
            dotsShown = reps;
            filledShown = 0;
        }

        public void SetQuest(IList<ExerciseDef> exs, IList<ExerciseEvaluator> evs, int activeIndex, bool running)
        {
            lastExs = exs; lastEvs = evs; lastActive = activeIndex; questRunning = running;
            bool open = QuestOpen;
            int focus = running ? activeIndex : FirstUnfinished(exs, evs);
            float rowH = !open ? 72f : exs.Count > 7 ? 56f : exs.Count > 5 ? 64f : 72f, gap = 8f;
            while (quest.Count < exs.Count) quest.Add(MakeQuestRow());
            int done = 0;
            for (int i = 0; i < quest.Count; i++)
            {
                var row = quest[i];
                bool exists = i < exs.Count;
                row.bg.gameObject.SetActive(exists && (open || i == focus));
                if (!exists) continue;
                var ex = exs[i];
                int v = evs != null && i < evs.Count && evs[i] != null ? evs[i].ValidReps : 0;
                bool finished = v >= ex.TotalReps, current = running && i == activeIndex;
                if (finished) done++;
                row.bg.rectTransform.anchoredPosition = new Vector2(0, open ? -i * (rowH + gap) : 0f);
                row.bg.rectTransform.sizeDelta = new Vector2(584, rowH);
                row.bg.color = current ? new Color(UIKit.Teal.r, UIKit.Teal.g, UIKit.Teal.b, 0.22f) : finished ? new Color(UIKit.Green.r, UIKit.Green.g, UIKit.Green.b, 0.14f) : UIKit.Row;
                float cs = rowH - 20f;
                row.circle.rectTransform.anchoredPosition = new Vector2(12, -10);
                row.circle.rectTransform.sizeDelta = new Vector2(cs, cs);
                row.circle.color = finished ? UIKit.Green : current ? UIKit.Teal : UIKit.Track;
                row.mark.rectTransform.sizeDelta = new Vector2(cs, cs);
                row.mark.text = finished ? "✓" : current ? "▶" : (i + 1).ToString();
                row.mark.color = finished || current ? Color.white : UIKit.Ink;
                row.mark.fontSize = Mathf.RoundToInt(cs * 0.55f);
                row.name.text = ex.name;
                row.name.resizeTextMaxSize = rowH < 60 ? 23 : 27;
                row.name.rectTransform.anchoredPosition = new Vector2(cs + 26, -6);
                row.name.rectTransform.sizeDelta = new Vector2(420 - cs, rowH - 22);
                row.count.text = $"{v}/{ex.TotalReps}";
                row.count.rectTransform.sizeDelta = new Vector2(120, rowH - 22);
                float frac = ex.TotalReps > 0 ? Mathf.Clamp01((float)v / ex.TotalReps) : 0f;
                ((RectTransform)row.barFill.transform.parent).anchoredPosition = new Vector2(cs + 26, -(rowH - 18));
                row.barFill.rectTransform.sizeDelta = new Vector2(Mathf.Max(10f, 420f * frac), 10);
                row.barFill.color = finished ? UIKit.Green : UIKit.Teal;
            }
            questCount.text = $"{done} / {exs.Count}";
            foreach (var go in questFullOnly) go.SetActive(open);
            questHint.text = open ? "M = hide   ·   Esc / B = menu   ·   R / Y = back to your spot" : "M = show all exercises";
            questHint.rectTransform.anchoredPosition = new Vector2(28, open ? -838f : -(110f + rowH + 14f));
        }

        static int FirstUnfinished(IList<ExerciseDef> exs, IList<ExerciseEvaluator> evs)
        {
            for (int i = 0; i < exs.Count; i++)
                if (evs == null || i >= evs.Count || evs[i] == null || evs[i].ValidReps < exs[i].TotalReps) return i;
            return 0;
        }

        /// <summary>Animates the quest card between its full and minimised height.</summary>
        void UpdateQuestCard()
        {
            if (InputHelper.QuestTogglePressed && lastExs != null)
            {
                questOpenByUser = !QuestOpen;
                SetQuest(lastExs, lastEvs, lastActive, questRunning);
            }
            float target = QuestOpen ? 900f : 110f + 72f + 70f;
            questH = Mathf.Lerp(questH, target, 1f - Mathf.Exp(-12f * Time.unscaledDeltaTime));
            if (Mathf.Abs(questH - target) < 0.5f) questH = target;
            rightCanvas.GetComponent<RectTransform>().sizeDelta = new Vector2(640f, questH);
            questCard.Resize(new Vector2(640f, questH));
        }

        /// <summary>Calm messages fade after a few seconds; warnings stay. Anything new brings the bubble back.</summary>
        void UpdateBubbleFade()
        {
            bool show = bubbleTone == Tone.Bad || bubbleTone == Tone.Warn || Time.unscaledTime - bubbleChangedAt < BubbleShowSeconds;
            bubbleGroup.alpha = Mathf.MoveTowards(bubbleGroup.alpha, show ? 1f : 0f, Time.unscaledDeltaTime * (show ? 6f : 1.5f));
        }

        QuestRow MakeQuestRow()
        {
            var r = new QuestRow { bg = Box(questRoot, Vector2.zero, new Vector2(584, 72), UIKit.Row, "Quest row") };
            r.circle = Box(r.bg.transform, new Vector2(12, -10), new Vector2(52, 52), UIKit.Track, "Circle");
            r.mark = Label(r.circle.transform, Vector2.zero, new Vector2(52, 52), 28, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            r.name = Label(r.bg.transform, new Vector2(78, -6), new Vector2(380, 50), 27, UIKit.Ink, TextAnchor.UpperLeft, FontStyle.Bold);
            r.name.verticalOverflow = VerticalWrapMode.Truncate;   // shrink long names to fit the row
            r.name.resizeTextForBestFit = true;
            r.name.resizeTextMinSize = 16;
            r.count = Label(r.bg.transform, new Vector2(450, -8), new Vector2(120, 50), 24, UIKit.Muted, TextAnchor.UpperRight, FontStyle.Bold);
            var track = Box(r.bg.transform, new Vector2(78, -54), new Vector2(420, 10), UIKit.Track, "Bar");
            r.barFill = Box(track.transform, Vector2.zero, new Vector2(10, 10), UIKit.Teal, "Bar fill");
            return r;
        }

        public void SetSource(string text) { sourceTxt.text = text; }

        public void SetFeedback(string text, Tone tone)
        {
            feedbackTxt.text = text;
            var c = FeedbackManager.ToneColor(tone);
            var fillColor = tone == Tone.Neutral ? UIKit.Cream : Color.Lerp(UIKit.Cream, c, 0.28f);
            fillColor.a = GlassAlpha + 0.1f;
            bubbleFill.color = fillColor;
            bubbleIcon.color = tone == Tone.Neutral ? UIKit.Teal : c;
            bubbleIconTxt.text = tone == Tone.Good ? "✓" : tone == Tone.Neutral ? "☺" : "!";
            // Bounce when the coach says something new (not every frame of a countdown)
            string key = tone + text.Split('…')[0];
            if (key != lastFeedback) bubbleBounce.Punch(0.06f);
            if (key != lastFeedback) { bubbleChangedAt = Time.unscaledTime; bubbleTone = tone; }
            lastFeedback = key;
            FitBubble();
        }

        /// <summary>Grows or shrinks the bubble so every line of the message fits inside it.</summary>
        void FitBubble()
        {
            float textH = Mathf.Max(60f, feedbackTxt.preferredHeight);
            float h = Mathf.Clamp(textH + 60f, 150f, 340f);
            if (Mathf.Abs(h - bubbleH) < 1f) return;
            bubbleH = h;
            bubbleCard.Resize(new Vector2(1200, h));
            feedbackTxt.rectTransform.sizeDelta = new Vector2(1040, h - 52f);
            bubbleIcon.rectTransform.anchoredPosition = new Vector2(18, -(h - 16f - 92f) / 2f - 6f);
            popStart = new Vector2(popStart.x, -(h + 20f) - 55f);   // +points pop just below the bubble (its pivot is its centre)
        }

        public void Pop(string text, bool good)
        {
            popTxt.text = text;
            popTxt.color = good ? UIKit.Yellow : new Color(1f, 0.62f, 0.6f);
            popTimer = 1f;
            UIKit.Bouncy(popTxt).Punch(0.35f);
        }

        public void ShowSummary(string title, int stars, string body)
        {
            sumTitle.text = title;
            sumStars.text = new string('★', stars) + $"<color=#D8D2C4>{new string('★', 3 - stars)}</color>";
            sumBody.text = body;
            summary.SetActive(true);
            UIKit.Bouncy(sumStars).Punch(0.3f);
        }

        public void HideSummary() { summary.SetActive(false); }
        public bool SummaryVisible => summary.activeSelf;

        void Update()
        {
            if (popTimer > 0f)
            {
                popTimer -= Time.deltaTime;
                float k = 1f - popTimer;
                popTxt.rectTransform.anchoredPosition = popStart + new Vector2(0, 70f * k);
                var c = popTxt.color; c.a = Mathf.Clamp01(popTimer * 1.6f); popTxt.color = c;
                if (popTimer <= 0f) popTxt.text = "";
            }
        }
    }
}
