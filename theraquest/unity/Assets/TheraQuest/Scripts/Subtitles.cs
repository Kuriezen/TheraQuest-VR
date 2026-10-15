using UnityEngine;
using UnityEngine.UI;

namespace TheraQuest
{
    /// <summary>
    /// Closed captions at the bottom of the view: the coach's instructions and every sound cue
    /// ("[Perfect chime] Perfect! 97%"). They follow the head, so they stay readable while walking
    /// around the clinic. On/off and size come from Settings.
    /// Call <see cref="Say"/> from anywhere.
    /// </summary>
    public class Subtitles : MonoBehaviour
    {
        static Subtitles instance;
        static readonly int[] FontSizes = { 30, 38, 48 };

        Canvas canvas;
        Image bg;
        Text text;
        float timer;

        /// <param name="sound">sound cue shown in brackets before the line, or null</param>
        public static void Say(string line, float seconds = 3f, string sound = null)
        {
            if (instance == null) return;
            string s = string.IsNullOrEmpty(sound) ? "" : $"<color=#BFEFEA><i>[{sound}]</i></color> ";
            instance.Show(s + StripTags(line), seconds);
        }

        void Awake()
        {
            instance = this;
            var cam = Camera.main != null ? Camera.main.transform : transform;
            canvas = UIKit.Canvas(cam, "Subtitles", new Vector3(0f, -0.32f, 1.25f), 0f, new Vector2(1200, 160));
            canvas.sortingOrder = 50;
            // No background box: white text with a dark outline and soft shadow reads on any background
            bg = UIKit.Box(canvas.transform, Vector2.zero, new Vector2(1200, 160), Color.clear, "Area");
            bg.enabled = false;
            text = UIKit.Label(canvas.transform, new Vector2(30, -14), new Vector2(1140, 132), 38, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.Stroke(text, new Color(0f, 0f, 0f, 0.85f), 2.5f);
            var shadow = text.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.5f);
            shadow.effectDistance = new Vector2(4f, -5f);
            UIKit.MakeOnTop(canvas, 50);
            canvas.gameObject.SetActive(false);
            GameSettings.Changed += Apply;
            Apply();
        }

        void OnDestroy() { GameSettings.Changed -= Apply; if (instance == this) instance = null; }

        void Apply()
        {
            text.fontSize = FontSizes[Mathf.Clamp(GameSettings.SubtitleSize, 0, FontSizes.Length - 1)];
            if (!GameSettings.Subtitles) canvas.gameObject.SetActive(false);
        }

        void Show(string line, float seconds)
        {
            timer = Mathf.Max(1.5f, seconds);
            if (!GameSettings.Subtitles) return;
            text.text = line;
            // fit the background to one or two lines
            float h = Mathf.Clamp(text.preferredHeight + 28f, 70f, 220f);
            ((RectTransform)bg.transform).sizeDelta = new Vector2(1200, h);
            ((RectTransform)text.transform).sizeDelta = new Vector2(1140, h - 28f);
            canvas.gameObject.SetActive(true);
        }

        void Update()
        {
            if (timer <= 0f) return;
            timer -= Time.unscaledDeltaTime;   // keeps counting while the game is paused
            if (timer <= 0f) canvas.gameObject.SetActive(false);
        }

        static string StripTags(string s) => System.Text.RegularExpressions.Regex.Replace(s ?? "", "<.*?>", "").Replace("\n", " ");
    }
}
