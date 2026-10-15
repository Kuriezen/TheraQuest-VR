using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

namespace TheraQuest
{
    /// <summary>
    /// Main menu, pause menu and settings, as a world-space panel that opens in front of the player
    /// (works on the laptop screen and in the headset).
    ///   Up / Down (arrows or thumbstick) choose, Left / Right change a value,
    ///   Space / Enter / A select, Esc / B go back. On the laptop the mouse can click too.
    /// SessionManager calls <see cref="HandleInput"/> every frame while the menu is open.
    /// </summary>
    public class MenuController : MonoBehaviour
    {
        public enum Page { Main, Pause, Settings }

        public SessionManager session;

        public bool IsOpen => canvas != null && canvas.gameObject.activeSelf;
        public Page Current { get; private set; }

        sealed class Item
        {
            public Func<string> label;
            public Func<string> value;          // shown as ‹ value › when the item can be changed
            public Action select;
            public Action<int> adjust;          // -1 / +1
            public Func<bool> enabled = () => true;
        }

        sealed class Row { public Image bg; public Text label, value; }

        const float Width = 1000f, RowH = 84f, RowGap = 12f, Top = 300f;

        Canvas canvas;
        UIKit.CardRef card;
        Text title, subtitle, info, footer;
        readonly List<Item> items = new List<Item>();
        readonly List<Row> rows = new List<Row>();
        Transform rowRoot;
        int sel, lastSel = -1;
        Page back;                              // where Settings returns to

        void Awake()
        {
            canvas = UIKit.Canvas(transform, "Menu", Vector3.zero, 0f, new Vector2(Width, 900));
            canvas.sortingOrder = 40;
            ((RectTransform)canvas.transform).pivot = new Vector2(0.5f, 0.5f);
            canvas.gameObject.AddComponent<GraphicRaycaster>();
            var t = canvas.transform;
            card = UIKit.Card(t, new Vector2(Width, 900), UIKit.Cream, UIKit.Teal, "Card");
            title = UIKit.Stroke(UIKit.Label(t, new Vector2(50, -40), new Vector2(Width - 100, 80), 70, UIKit.Orange, TextAnchor.UpperLeft, FontStyle.Bold), UIKit.Outline, 3f);
            subtitle = UIKit.Label(t, new Vector2(52, -124), new Vector2(Width - 100, 40), 30, UIKit.Muted);
            info = UIKit.Label(t, new Vector2(52, -170), new Vector2(Width - 100, 120), 28, UIKit.Ink);
            rowRoot = new GameObject("Rows", typeof(RectTransform)).transform;
            rowRoot.SetParent(t, false);
            var rr = (RectTransform)rowRoot;
            rr.anchorMin = rr.anchorMax = new Vector2(0, 1);
            rr.pivot = new Vector2(0, 1);
            footer = UIKit.Label(t, Vector2.zero, new Vector2(Width - 100, 40), 24, UIKit.Muted, TextAnchor.MiddleCenter);
            footer.text = "↑ ↓ choose   ·   ‹ › change   ·   Space / A select   ·   Esc / B back";
            UIKit.MakeOnTop(canvas, 40);
            canvas.gameObject.SetActive(false);
            EnsureEventSystem();
        }

        static void EnsureEventSystem()
        {
            if (EventSystem.current != null || FindFirstObjectByType<EventSystem>() != null) return;
            var go = new GameObject("EventSystem", typeof(EventSystem));
#if ENABLE_INPUT_SYSTEM
            go.AddComponent<InputSystemUIInputModule>().AssignDefaultActions();
#else
            go.AddComponent<StandaloneInputModule>();
#endif
        }

        // ------------------------------------------------------------------ pages
        public void Open(Page page)
        {
            if (page == Page.Settings && Current != Page.Settings) back = IsOpen ? Current : Page.Main;
            Current = page;
            items.Clear();
            switch (page)
            {
                case Page.Main: BuildMain(); break;
                case Page.Pause: BuildPause(); break;
                default: BuildSettings(); break;
            }
            Layout();
            if (!IsOpen) PlaceInFront();
            canvas.gameObject.SetActive(true);
            canvas.worldCamera = Camera.main;
            sel = FirstEnabled(0, 1);
            Refresh();
        }

        public void Close() => canvas.gameObject.SetActive(false);

        void BuildMain()
        {
            title.text = "TheraQuest";
            subtitle.text = session.MenuSubtitle;
            info.text = session.MenuInfo;
            var coach = session.coach;
            Add(() => "Start today's quest", select: session.StartQuest, enabled: () => session.CanStart);
            Add(() => "Explore the clinic", select: session.FreeRoam);
            if (coach != null && coach.Ready && coach.Count > 1)
                Add(() => "Coach", value: () => coach.CoachName, adjust: d => coach.Cycle(d));
            if (coach != null && coach.Ready)
                Add(() => "Guide", value: () => coach.GuideName, adjust: d => coach.ToggleGuide());
            Add(() => "Settings", select: () => Open(Page.Settings));
            Add(() => "Quit", select: session.Quit);
        }

        void BuildPause()
        {
            title.text = "Paused";
            subtitle.text = session.PauseSubtitle;
            info.text = "Take your time. Your reps so far are kept.";
            Add(() => "Resume", select: session.Resume);
            Add(() => "Back to the exercise spot", select: () => { session.ReturnToStation(); session.Resume(); });
            Add(() => "Settings", select: () => Open(Page.Settings));
            Add(() => "End session and save", select: session.EndSessionEarly);
        }

        void BuildSettings()
        {
            title.text = "Settings";
            subtitle.text = "Saved on this computer";
            info.text = "";
            Add(() => "Music volume", value: () => GameSettings.Percent(GameSettings.Music), adjust: GameSettings.StepMusic);
            Add(() => "Effects volume", value: () => GameSettings.Percent(GameSettings.Effects),
                adjust: d => { GameSettings.StepEffects(d); session.feedback.PlayClick(); });
            Add(() => "Subtitles", value: () => GameSettings.Subtitles ? "On" : "Off", adjust: d =>
            {
                GameSettings.ToggleSubtitles();
                if (GameSettings.Subtitles) Subtitles.Say("Subtitles are on. Sounds are shown in brackets.", 2.5f, "Click");
            });
            Add(() => "Subtitle size", value: () => GameSettings.SubtitleSizes[GameSettings.SubtitleSize], adjust: d =>
            {
                GameSettings.StepSubtitleSize(d);
                Subtitles.Say("This is how big the subtitles look.", 2.5f);
            });
            Add(() => "Motion input", value: () => session.motion.SourceLabel, adjust: d =>
            {
                session.motion.NextSource();
                if (d < 0) session.motion.NextSource();   // three modes: twice forward = once back
            });
            Add(() => "Turning", value: () => GameSettings.TurnModes[GameSettings.TurnMode], adjust: GameSettings.StepTurnMode);
            Add(() => "Walking speed", value: () => GameSettings.MoveSpeeds[GameSettings.MoveSpeed], adjust: GameSettings.StepMoveSpeed);
            Add(() => "Show numbers (therapist)", value: () => GameSettings.ShowDetails ? "On" : "Off", adjust: d => GameSettings.ToggleDetails());
            Add(() => "Back", select: GoBack);
        }

        void Add(Func<string> label, Action select = null, Action<int> adjust = null, Func<string> value = null, Func<bool> enabled = null)
        {
            items.Add(new Item { label = label, select = select, adjust = adjust, value = value, enabled = enabled ?? (() => true) });
        }

        void GoBack()
        {
            if (Current == Page.Settings) Open(back);
            else if (Current == Page.Pause) session.Resume();
            else session.FreeRoam();   // Esc on the main menu: close it and look around
        }

        // ------------------------------------------------------------------ layout
        void Layout()
        {
            foreach (Transform c in rowRoot) Destroy(c.gameObject);
            rows.Clear();
            bool hasInfo = !string.IsNullOrEmpty(info.text);
            float top = hasInfo ? Top : 190f;
            ((RectTransform)rowRoot).anchoredPosition = new Vector2(50, -top);
            for (int i = 0; i < items.Count; i++)
            {
                int index = i;
                var bg = UIKit.Box(rowRoot, new Vector2(0, -i * (RowH + RowGap)), new Vector2(Width - 100, RowH), UIKit.Row, "Row " + i, true);
                bg.raycastTarget = true;
                var button = bg.gameObject.AddComponent<Button>();
                button.transition = Selectable.Transition.None;
                button.navigation = new Navigation { mode = Navigation.Mode.None };
                button.onClick.AddListener(() => Click(index));
                var hover = bg.gameObject.AddComponent<EventTrigger>();
                var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
                enter.callback.AddListener(_ => { if (items[index].enabled()) { sel = index; Refresh(); } });
                hover.triggers.Add(enter);
                rows.Add(new Row
                {
                    bg = bg,
                    label = UIKit.Label(bg.transform, new Vector2(34, -14), new Vector2(560, RowH - 20), 36, UIKit.Ink, TextAnchor.MiddleLeft, FontStyle.Bold),
                    value = UIKit.Label(bg.transform, new Vector2(500, -14), new Vector2(Width - 100 - 534, RowH - 20), 34, UIKit.Orange, TextAnchor.MiddleRight, FontStyle.Bold),
                });
            }
            float h = top + items.Count * (RowH + RowGap) + 90f;
            ((RectTransform)canvas.transform).sizeDelta = new Vector2(Width, h);
            card.Resize(new Vector2(Width, h));
            ((RectTransform)footer.transform).anchoredPosition = new Vector2(50, -(h - 66f));
        }

        void PlaceInFront()
        {
            var cam = Camera.main != null ? Camera.main.transform : transform;
            var fwd = cam.forward; fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            fwd.Normalize();
            canvas.transform.position = cam.position + fwd * 1.5f + Vector3.down * 0.15f;
            canvas.transform.rotation = Quaternion.LookRotation(fwd, Vector3.up);
        }

        void Refresh()
        {
            for (int i = 0; i < rows.Count; i++)
            {
                var it = items[i];
                bool on = it.enabled(), chosen = i == sel;
                rows[i].bg.color = chosen ? UIKit.Teal : UIKit.Row;
                rows[i].label.text = (chosen ? "▶  " : "     ") + it.label();
                rows[i].label.color = !on ? new Color(UIKit.Muted.r, UIKit.Muted.g, UIKit.Muted.b, 0.6f) : chosen ? Color.white : UIKit.Ink;
                rows[i].value.color = chosen ? Color.white : UIKit.Orange;
                rows[i].value.text = it.value != null ? (chosen ? $"‹  {it.value()}  ›" : it.value()) : "";
                if (chosen && i != lastSel) UIKit.Bouncy(rows[i].bg).Punch(0.05f);
            }
        }

        // ------------------------------------------------------------------ input
        public void HandleInput()
        {
            if (!IsOpen) return;
            if (InputHelper.UpPressed) Move(-1);
            if (InputHelper.DownPressed) Move(1);
            if (InputHelper.LeftPressed) Adjust(-1);
            if (InputHelper.RightPressed) Adjust(1);
            if (InputHelper.StartPressed) Activate(sel);
            else if (InputHelper.BackPressed) GoBack();
            if (IsOpen) Refresh();
            lastSel = sel;
        }

        void Move(int dir)
        {
            int next = FirstEnabled(sel + dir, dir);
            if (next != sel) { sel = next; session.feedback.PlayClick(); }
        }

        int FirstEnabled(int from, int dir)
        {
            for (int k = 0; k < items.Count; k++)
            {
                int i = ((from + k * dir) % items.Count + items.Count) % items.Count;
                if (items[i].enabled()) return i;
            }
            return 0;
        }

        void Adjust(int dir)
        {
            if (sel < items.Count && items[sel].adjust != null && items[sel].enabled()) items[sel].adjust(dir);
        }

        void Activate(int i)
        {
            if (i >= items.Count || !items[i].enabled()) return;
            session.feedback.PlayClick();
            if (items[i].select != null) items[i].select();
            else items[i].adjust?.Invoke(1);
        }

        void Click(int i)
        {
            sel = i;
            Activate(i);
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);   // so Space doesn't click it again
            if (IsOpen) Refresh();
        }
    }
}
