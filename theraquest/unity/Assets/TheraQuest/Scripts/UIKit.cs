using UnityEngine;
using UnityEngine.UI;

namespace TheraQuest
{
    /// <summary>
    /// Shared cartoon look for every world-space panel (HUD, menus, posters): bright cream cards with
    /// a thick dark outline and a soft drop shadow, chunky rounded shapes, and a playful palette.
    /// The rounded-corner sprite is generated at runtime (no image assets needed).
    /// World-space canvases use 1000 canvas units = 1 metre.
    /// </summary>
    public static class UIKit
    {
        // Palette
        public static readonly Color Cream = new Color32(255, 251, 240, 250);
        public static readonly Color Outline = new Color32(43, 45, 66, 255);
        public static readonly Color Ink = new Color32(43, 45, 66, 255);
        public static readonly Color Muted = new Color32(110, 120, 140, 255);
        public static readonly Color Teal = new Color32(46, 196, 182, 255);
        public static readonly Color Orange = new Color32(255, 159, 28, 255);
        public static readonly Color Pink = new Color32(255, 93, 143, 255);
        public static readonly Color Yellow = new Color32(255, 210, 63, 255);
        public static readonly Color Green = new Color32(59, 178, 115, 255);
        public static readonly Color Red = new Color32(229, 72, 77, 255);
        public static readonly Color Purple = new Color32(123, 97, 255, 255);
        public static readonly Color Blue = new Color32(58, 134, 255, 255);
        public static readonly Color Track = new Color32(43, 45, 66, 28);

        // Names used by the panels
        public static readonly Color Panel = Cream;
        public static readonly Color PanelDeep = Cream;
        public static readonly Color Row = new Color32(43, 45, 66, 16);
        public static readonly Color Accent = Teal;
        public static readonly Color AccentDark = Teal;
        public static readonly Color Gold = Yellow;

        // For rich text: <color=#...>
        public const string HexInk = "#2B2D42", HexMuted = "#6E788C", HexGood = "#3BB273", HexWarn = "#E08600",
                            HexBad = "#E5484D", HexTeal = "#1FA597", HexGold = "#E0A800", HexPurple = "#7B61FF";

        static Font font;
        static Sprite rounded;
        static Material onTop;

        /// <summary>UI material that ignores depth, so view-attached panels are never hidden by walls or the coach.</summary>
        public static Material OnTopMaterial
        {
            get
            {
                if (onTop == null)
                {
                    onTop = new Material(UnityEngine.Canvas.GetDefaultCanvasMaterial()) { name = "UI (always on top)" };
                    onTop.SetInt("unity_GUIZTestMode", (int)UnityEngine.Rendering.CompareFunction.Always);
                }
                return onTop;
            }
        }

        /// <summary>Makes a canvas draw over the 3D scene (and everything added to it later).</summary>
        public static void MakeOnTop(Canvas c, int sortingOrder)
        {
            c.sortingOrder = sortingOrder;
            if (c.GetComponent<DrawOnTop>() == null) c.gameObject.AddComponent<DrawOnTop>();
            foreach (var g in c.GetComponentsInChildren<Graphic>(true)) g.material = OnTopMaterial;
        }

        static void ApplyOnTop(Graphic g, Transform parent)
        {
            if (parent != null && parent.GetComponentInParent<DrawOnTop>(true) != null) g.material = OnTopMaterial;
        }

        public static Font Font => font != null ? font : (font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));

        /// <summary>9-sliced rounded rectangle, 36 canvas units corner radius. Small boxes become pills and circles.</summary>
        public static Sprite Rounded => rounded != null ? rounded : (rounded = MakeRounded(96, 36));

        static Sprite MakeRounded(int size, int radius)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    // distance outside the inner rectangle, anti-aliased over one pixel
                    float dx = Mathf.Max(radius - x - 0.5f, x + 0.5f - (size - radius), 0f);
                    float dy = Mathf.Max(radius - y - 0.5f, y + 0.5f - (size - radius), 0f);
                    float a = Mathf.Clamp01(radius - Mathf.Sqrt(dx * dx + dy * dy) + 0.5f);
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                                 SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
        }

        public static Canvas Canvas(Transform parent, string name, Vector3 localPos, float yaw, Vector2 size)
        {
            var go = new GameObject(name, typeof(Canvas));
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.Euler(0, yaw, 0);
            go.transform.localScale = Vector3.one * 0.001f;
            var c = go.GetComponent<Canvas>();
            c.renderMode = RenderMode.WorldSpace;
            ((RectTransform)go.transform).sizeDelta = size;
            return c;
        }

        /// <summary>Box anchored at the parent's top-left; pos is the offset of its top-left corner.</summary>
        public static Image Box(Transform parent, Vector2 pos, Vector2 size, Color color, string name = "Box", bool round = false)
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
            if (round)
            {
                img.sprite = Rounded;
                img.type = Image.Type.Sliced;
            }
            img.raycastTarget = false;
            ApplyOnTop(img, parent);
            return img;
        }

        /// <summary>Cartoon card: soft shadow, thick outline, cream (or coloured) fill and a coloured strip on top.</summary>
        public sealed class CardRef
        {
            public Image shadow, outline, fill, strip;

            /// <summary>See-through card: translucent fill, soft outline, faint shadow (text and colours stay solid).</summary>
            public void Glass(float fillAlpha)
            {
                var f = fill.color; f.a = fillAlpha; fill.color = f;
                outline.color = new Color(Outline.r, Outline.g, Outline.b, 0.35f);
                shadow.color = new Color(0f, 0f, 0f, 0.08f);
            }

            public void Resize(Vector2 size)
            {
                shadow.rectTransform.sizeDelta = outline.rectTransform.sizeDelta = size;
                fill.rectTransform.sizeDelta = size - new Vector2(16, 16);
                strip.rectTransform.sizeDelta = new Vector2(size.x - 64, 14);
            }
        }

        public static CardRef Card(Transform parent, Vector2 size, Color fill, Color strip, string name = "Panel")
        {
            var c = new CardRef
            {
                shadow = Box(parent, new Vector2(12, -16), size, new Color(0f, 0f, 0f, 0.25f), name + " shadow", true),
                outline = Box(parent, Vector2.zero, size, Outline, name + " outline", true),
            };
            c.fill = Box(parent, new Vector2(8, -8), size - new Vector2(16, 16), fill, name, true);
            c.strip = Box(c.fill.transform, new Vector2(24, -14), new Vector2(size.x - 64, 14), strip, "Strip", true);
            return c;
        }

        public static Text Label(Transform parent, Vector2 pos, Vector2 size, int fontSize, Color color,
                                 TextAnchor align = TextAnchor.UpperLeft, FontStyle style = FontStyle.Normal)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            var t = go.GetComponent<Text>();
            t.font = Font;
            t.fontSize = fontSize;
            t.color = color;
            t.alignment = align;
            t.fontStyle = style;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.supportRichText = true;
            t.raycastTarget = false;
            ApplyOnTop(t, parent);
            return t;
        }

        /// <summary>Rounded coloured pill with centred bold text.</summary>
        public static Text Pill(Transform parent, Vector2 pos, Vector2 size, Color color, string text, int fontSize, Color textColor)
        {
            var bg = Box(parent, pos, size, color, "Pill", true);
            var t = Label(bg.transform, Vector2.zero, size, fontSize, textColor, TextAnchor.MiddleCenter, FontStyle.Bold);
            t.text = text;
            return t;
        }

        /// <summary>Cartoon stroke around text (thick outline).</summary>
        public static T Stroke<T>(T graphic, Color color, float width = 3f) where T : Graphic
        {
            var o = graphic.gameObject.AddComponent<UnityEngine.UI.Outline>();
            o.effectColor = color;
            o.effectDistance = new Vector2(width, -width);
            return graphic;
        }

        /// <summary>Adds a springy scale "pop" to a UI element; call Punch() on it.</summary>
        public static Bounce Bouncy(Component c)
        {
            var b = c.gameObject.GetComponent<Bounce>();
            return b != null ? b : c.gameObject.AddComponent<Bounce>();
        }
    }

    /// <summary>Marks a canvas whose UI draws on top of the scene (see UIKit.MakeOnTop).</summary>
    public class DrawOnTop : MonoBehaviour { }

    /// <summary>Springy scale punch around the element's centre, independent of pause (unscaled time).</summary>
    public class Bounce : MonoBehaviour
    {
        float t = 10f, amount;

        void Awake()
        {
            // Scale from the centre: move the pivot to the middle without moving the element
            if (transform is RectTransform rt)
            {
                var shift = Vector2.Scale(rt.sizeDelta, new Vector2(0.5f, 0.5f) - rt.pivot);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition += shift;
            }
        }

        public void Punch(float strength = 0.18f) { amount = strength; t = 0f; }

        void Update()
        {
            if (t > 1.2f) return;
            t += Time.unscaledDeltaTime;
            float s = 1f + amount * Mathf.Exp(-t * 7f) * Mathf.Cos(t * 22f);
            transform.localScale = t > 1.2f ? Vector3.one : new Vector3(s, s, 1f);
        }
    }
}
