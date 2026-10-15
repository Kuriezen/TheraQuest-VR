using System.Collections.Generic;
using UnityEngine;

namespace TheraQuest
{
    /// <summary>
    /// Furnishes the clinic around the exercise station at startup, from primitives (no 3D assets):
    /// side and front walls, windows, treatment table, parallel bars, exercise balls, dumbbell rack,
    /// step box, plants, wall posters, warm lights, and a teal marker on the exercise spot.
    /// Works with scenes made by an older TheraQuest > Create Scene (it adds only what's missing).
    /// Props keep their colliders, so PlayerMover walks around them.
    /// The area in front of the station (coach and HUD panels) is kept clear.
    /// </summary>
    public class ClinicRoom : MonoBehaviour
    {
        const float Height = 3.5f;

        readonly Dictionary<Color, Material> mats = new Dictionary<Color, Material>();
        Transform root;

        static readonly Color Wall = new Color(0.82f, 0.88f, 0.86f);
        static readonly Color Trim = new Color(0.04f, 0.54f, 0.49f);
        static readonly Color Dark = new Color(0.20f, 0.26f, 0.28f);
        static readonly Color Wood = new Color(0.55f, 0.40f, 0.27f);
        static readonly Color Sky = new Color(0.62f, 0.82f, 0.95f);

        void Start()
        {
            if (GameObject.Find("Clinic Room") != null) return;
            root = new GameObject("Clinic Room").transform;

            var wallMat = FindMaterial("Back wall") ?? Mat(Wall);
            if (GameObject.Find("Back wall") == null) Block("Back wall", new Vector3(0, Height / 2, 5.5f), new Vector3(10, Height, 0.1f), wallMat);
            Block("Left wall", new Vector3(-5f, Height / 2, 1.25f), new Vector3(0.1f, Height, 8.6f), wallMat);
            Block("Right wall", new Vector3(5f, Height / 2, 1.25f), new Vector3(0.1f, Height, 8.6f), wallMat);
            Block("Front wall", new Vector3(0, Height / 2, -3f), new Vector3(10, Height, 0.1f), wallMat);

            // Accent stripe and skirting around the room
            foreach (var (pos, size) in new[] {
                (new Vector3(0, 0, 5.44f), new Vector3(10, 1, 0.02f)), (new Vector3(0, 0, -2.94f), new Vector3(10, 1, 0.02f)),
                (new Vector3(-4.94f, 0, 1.25f), new Vector3(0.02f, 1, 8.6f)), (new Vector3(4.94f, 0, 1.25f), new Vector3(0.02f, 1, 8.6f)) })
            {
                Decor("Stripe", pos + Vector3.up * 1.0f, Vector3.Scale(size, new Vector3(1, 0.08f, 1)), Trim);
                Decor("Skirting", pos + Vector3.up * 0.06f, Vector3.Scale(size, new Vector3(1, 0.12f, 1)), Dark);
            }

            // Windows on the left wall
            foreach (float z in new[] { -1.2f, 1.6f, 4.2f })
            {
                Decor("Window", new Vector3(-4.93f, 1.75f, z), new Vector3(0.02f, 1.3f, 1.5f), Sky);
                Decor("Window frame", new Vector3(-4.92f, 1.75f, z), new Vector3(0.03f, 1.3f, 0.05f), Color.white);
                Decor("Window frame", new Vector3(-4.92f, 1.75f, z), new Vector3(0.03f, 0.05f, 1.5f), Color.white);
                Block("Window sill", new Vector3(-4.88f, 1.07f, z), new Vector3(0.14f, 0.05f, 1.6f), Mat(Color.white));
            }

            // Exercise spot: where the patient stands, faces the coach, and the session starts
            Decor("Rug", new Vector3(0, 0.003f, 0.1f), new Vector3(2.4f, 0.004f, 2.4f), new Color(0.10f, 0.28f, 0.29f), PrimitiveType.Cylinder);
            Decor("Exercise spot", new Vector3(0, 0.006f, 0f), new Vector3(0.9f, 0.004f, 0.9f), Trim, PrimitiveType.Cylinder);
            Sign(new Vector3(0, 0.02f, 0.62f), Quaternion.Euler(90, 0, 0), new Vector2(700, 120), "EXERCISE SPOT", 64);

            // Treatment table (left side)
            Block("Table top", new Vector3(-3.7f, 0.68f, 1.4f), new Vector3(0.8f, 0.12f, 1.9f), Mat(Trim));
            Block("Table base", new Vector3(-3.7f, 0.31f, 1.4f), new Vector3(0.6f, 0.62f, 1.6f), Mat(Dark));
            Decor("Pillow", new Vector3(-3.7f, 0.78f, 0.65f), new Vector3(0.5f, 0.08f, 0.3f), Color.white);

            // Exercise balls
            Ball(new Vector3(-3.3f, 0.33f, 3.9f), 0.65f, new Color(0.93f, 0.45f, 0.38f));
            Ball(new Vector3(-2.5f, 0.28f, 4.6f), 0.55f, new Color(0.35f, 0.55f, 0.90f));
            Ball(new Vector3(-4.1f, 0.25f, 4.7f), 0.5f, new Color(0.96f, 0.77f, 0.32f));

            // Parallel bars (right side)
            Decor("Bars platform", new Vector3(3.55f, 0.02f, 1.4f), new Vector3(1.2f, 0.04f, 2.9f), Wood);
            foreach (float x in new[] { 3.2f, 3.9f })
            {
                foreach (float z in new[] { 0.1f, 2.7f })
                    Block("Bar post", new Vector3(x, 0.47f, z), new Vector3(0.06f, 0.47f, 0.06f), Mat(Dark), PrimitiveType.Cylinder);
                var rail = Block("Bar rail", new Vector3(x, 0.95f, 1.4f), new Vector3(0.05f, 1.3f, 0.05f), Mat(new Color(0.85f, 0.87f, 0.88f)), PrimitiveType.Cylinder);
                rail.transform.rotation = Quaternion.Euler(90, 0, 0);
            }

            // Dumbbell rack and step box (back right)
            Block("Dumbbell rack", new Vector3(3.9f, 0.4f, 4.7f), new Vector3(1.1f, 0.8f, 0.45f), Mat(Dark));
            var colors = new[] { Trim, new Color(0.93f, 0.45f, 0.38f), new Color(0.35f, 0.55f, 0.90f), new Color(0.96f, 0.77f, 0.32f) };
            for (int i = 0; i < 4; i++)
            {
                var d = Decor("Dumbbell", new Vector3(3.5f + i * 0.27f, 0.86f, 4.7f), new Vector3(0.09f, 0.14f, 0.09f), colors[i], PrimitiveType.Cylinder);
                d.transform.rotation = Quaternion.Euler(90, 0, 0);
            }
            Block("Step box", new Vector3(2.3f, 0.1f, 4.6f), new Vector3(0.8f, 0.2f, 0.5f), Mat(Trim));

            // Plants in the corners
            foreach (var p in new[] { new Vector3(-4.4f, 0, 5.0f), new Vector3(4.4f, 0, -2.4f), new Vector3(-4.4f, 0, -2.4f), new Vector3(1.8f, 0, 5.0f) })
                Plant(p);

            // Posters
            Poster(new Vector3(-4.93f, 1.9f, 2.9f), -90f, new Vector2(1000, 300), "TheraQuest Clinic", "Every rep brings you\ncloser to your goals", 54);
            Poster(new Vector3(4.93f, 1.85f, -1.3f), 90f, new Vector2(900, 520), "Move safely", "Stay inside your safe range.\nIf something hurts, stop and tell your therapist.", 56);
            Poster(new Vector3(4.93f, 1.95f, 1.4f), 90f, new Vector2(900, 440), "Breathe", "Breathe out as you lift,\nbreathe in as you lower.", 56);
            Poster(new Vector3(4.93f, 1.85f, 3.9f), 90f, new Vector2(900, 440), "Small steps", "A little every day\nadds up to big progress.", 56);
            Poster(new Vector3(0, 1.9f, -2.93f), 180f, new Vector2(1500, 420), "Welcome!", "Walk to the teal spot and press Start (Space / A)\nfor today's quest. Press Esc / B for the menu.", 60);

            // Warm room lights (the sun still comes in from the directional light)
            Lamp(new Vector3(-2f, 3.1f, 1.2f));
            Lamp(new Vector3(2.2f, 3.1f, 3.6f));
            Lamp(new Vector3(0f, 3.1f, -1.6f));
        }

        Material FindMaterial(string objectName)
        {
            var go = GameObject.Find(objectName);
            var r = go != null ? go.GetComponent<Renderer>() : null;
            return r != null ? r.sharedMaterial : null;
        }

        Material Mat(Color c)
        {
            if (!mats.TryGetValue(c, out var m)) mats[c] = m = FigureVisualizer.MakeMat(c, false);
            return m;
        }

        /// <summary>Solid prop: keeps its collider so the player walks around it.</summary>
        GameObject Block(string name, Vector3 pos, Vector3 scale, Material mat, PrimitiveType type = PrimitiveType.Cube)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(root, false);
            go.transform.position = pos;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            return go;
        }

        /// <summary>Decoration you can't bump into (no collider).</summary>
        GameObject Decor(string name, Vector3 pos, Vector3 scale, Color c, PrimitiveType type = PrimitiveType.Cube)
        {
            var go = Block(name, pos, scale, Mat(c), type);
            Destroy(go.GetComponent<Collider>());
            return go;
        }

        void Ball(Vector3 pos, float size, Color c) => Block("Exercise ball", pos, Vector3.one * size, Mat(c), PrimitiveType.Sphere);

        void Plant(Vector3 at)
        {
            Block("Plant pot", at + new Vector3(0, 0.22f, 0), new Vector3(0.42f, 0.22f, 0.42f), Mat(new Color(0.72f, 0.42f, 0.30f)), PrimitiveType.Cylinder);
            var leaf = new Color(0.25f, 0.55f, 0.32f);
            Decor("Leaves", at + new Vector3(0, 0.75f, 0), new Vector3(0.75f, 0.7f, 0.75f), leaf, PrimitiveType.Sphere);
            Decor("Leaves", at + new Vector3(0.12f, 1.1f, -0.05f), new Vector3(0.5f, 0.5f, 0.5f), leaf * 1.15f, PrimitiveType.Sphere);
        }

        void Lamp(Vector3 pos)
        {
            Decor("Lamp", pos + Vector3.up * 0.25f, new Vector3(0.5f, 0.04f, 0.5f), new Color(1f, 0.96f, 0.86f), PrimitiveType.Cylinder);
            var light = new GameObject("Lamp light").AddComponent<Light>();
            light.transform.SetParent(root, false);
            light.transform.position = pos;
            light.type = LightType.Point;
            light.range = 7f;
            light.intensity = 1.6f;
            light.color = new Color(1f, 0.92f, 0.80f);
            light.shadows = LightShadows.None;
        }

        void Poster(Vector3 pos, float yaw, Vector2 size, string title, string body, int bodySize)
        {
            var c = UIKit.Canvas(root, "Poster: " + title, pos, yaw, size);
            var rt = (RectTransform)c.transform;
            rt.pivot = new Vector2(0.5f, 0.5f);
            c.transform.position = pos;
            var t = c.transform;
            var card = UIKit.Card(t, size, UIKit.Cream, UIKit.Teal);
            // Title and body are measured, then the card is sized to fit them (below the strip, with padding)
            var head = UIKit.Label(t, new Vector2(40, -40), new Vector2(size.x - 80, 10), Mathf.RoundToInt(bodySize * 1.25f), UIKit.Teal,
                                   TextAnchor.UpperCenter, FontStyle.Bold);
            head.text = title;
            float headH = head.preferredHeight;
            head.rectTransform.sizeDelta = new Vector2(size.x - 80, headH);
            var text = UIKit.Label(t, new Vector2(40, -40 - headH - 12), new Vector2(size.x - 80, 10), bodySize, UIKit.Ink, TextAnchor.UpperCenter);
            text.text = body;
            float bodyH = text.preferredHeight;
            text.rectTransform.sizeDelta = new Vector2(size.x - 80, bodyH);
            var fit = new Vector2(size.x, 40 + headH + 12 + bodyH + 44);
            rt.sizeDelta = fit;   // pivot is the centre, so the poster stays centred where it was placed
            card.Resize(fit);
        }

        void Sign(Vector3 pos, Quaternion rot, Vector2 size, string text, int fontSize)
        {
            var c = UIKit.Canvas(root, "Sign: " + text, pos, 0f, size);
            ((RectTransform)c.transform).pivot = new Vector2(0.5f, 0.5f);
            c.transform.SetPositionAndRotation(pos, rot);
            UIKit.Label(c.transform, Vector2.zero, size, fontSize, UIKit.Ink, TextAnchor.MiddleCenter, FontStyle.Bold).text = text;
        }
    }
}
