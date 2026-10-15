using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace TheraQuest
{
    /// <summary>
    /// Friendly-looking exercise furniture built from primitives (no assets): a padded chair with wooden legs
    /// and a treatment table with legs, a light mattress and a pillow. Colours stand out from the floor so the
    /// patient can see what the coach is sitting or lying on. No colliders (the player walks through the coach area).
    /// </summary>
    public static class PropBuilder
    {
        static readonly Color Wood = new Color(0.62f, 0.44f, 0.28f);
        static readonly Color Cushion = new Color(0.18f, 0.72f, 0.66f);
        static readonly Color Mattress = new Color(0.93f, 0.95f, 0.97f);
        static readonly Color Frame = new Color(0.30f, 0.38f, 0.45f);
        static readonly Color Pillow = new Color(1f, 0.85f, 0.45f);
        static readonly Dictionary<Color, Material> mats = new Dictionary<Color, Material>();

        static Material Mat(Color c)
        {
            if (!mats.TryGetValue(c, out var m) || m == null) mats[c] = m = FigureVisualizer.MakeMat(c, false);
            return m;
        }

        static Transform Part(PrimitiveType type, Transform parent, Vector3 pos, Vector3 scale, Color c, string name, Quaternion? rot = null)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = rot ?? Quaternion.identity;
            go.transform.localScale = scale;
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = Mat(c);
            r.shadowCastingMode = ShadowCastingMode.On;
            return go.transform;
        }

        /// <summary>Padded chair. Seat top at seatTop, seat spanning zBack..zFront (backrest at zBack), centred on x = 0.</summary>
        public static void Chair(Transform parent, float seatTop, float zBack, float zFront)
        {
            float depth = zFront - zBack, mid = (zBack + zFront) / 2f, w = 0.52f;
            Part(PrimitiveType.Cube, parent, new Vector3(0, seatTop - 0.11f, mid), new Vector3(w, 0.04f, depth), Wood, "Seat board");
            Part(PrimitiveType.Cube, parent, new Vector3(0, seatTop - 0.045f, mid), new Vector3(w - 0.02f, 0.09f, depth - 0.02f), Cushion, "Seat cushion");
            float legH = Mathf.Max(0.05f, seatTop - 0.13f);
            foreach (var x in new[] { -w / 2f + 0.04f, w / 2f - 0.04f })
                foreach (var z in new[] { zBack + 0.04f, zFront - 0.04f })
                    Part(PrimitiveType.Cylinder, parent, new Vector3(x, legH / 2f, z), new Vector3(0.05f, legH / 2f, 0.05f), Wood, "Leg");
            // Backrest: two posts and a rounded pad
            foreach (var x in new[] { -w / 2f + 0.04f, w / 2f - 0.04f })
                Part(PrimitiveType.Cylinder, parent, new Vector3(x, seatTop + 0.2f, zBack + 0.03f), new Vector3(0.045f, 0.3f, 0.045f), Wood, "Back post");
            Part(PrimitiveType.Capsule, parent, new Vector3(0, seatTop + 0.36f, zBack + 0.03f), new Vector3(0.22f, w / 2f, 0.07f), Cushion,
                 "Back pad", Quaternion.Euler(0, 0, 90));
        }

        /// <summary>
        /// Treatment table with its top at height top, centred at (x, z). alongX = the long side runs along X.
        /// Returns the pillow so the caller can move it to the head end.
        /// </summary>
        public static Transform Bed(Transform parent, float top, Vector2 centre, float length, float width, bool alongX)
        {
            Vector3 Size(float l, float h, float w2) => alongX ? new Vector3(l, h, w2) : new Vector3(w2, h, l);
            Vector3 At(float along, float y, float across) => alongX ? new Vector3(centre.x + along, y, centre.y + across)
                                                                     : new Vector3(centre.x + across, y, centre.y + along);
            Part(PrimitiveType.Cube, parent, At(0, top - 0.07f, 0), Size(length, 0.14f, width), Mattress, "Mattress");
            Part(PrimitiveType.Cube, parent, At(0, top - 0.17f, 0), Size(length + 0.04f, 0.08f, width + 0.04f), Frame, "Frame");
            float legH = Mathf.Max(0.05f, top - 0.21f);
            foreach (var a in new[] { -length / 2f + 0.08f, length / 2f - 0.08f })
                foreach (var b in new[] { -width / 2f + 0.06f, width / 2f - 0.06f })
                    Part(PrimitiveType.Cylinder, parent, At(a, legH / 2f, b), new Vector3(0.06f, legH / 2f, 0.06f), Frame, "Leg");
            return Part(PrimitiveType.Capsule, parent, At(-length / 2f + 0.22f, top + 0.04f, 0), new Vector3(0.18f, width * 0.32f, 0.3f),
                        Pillow, "Pillow", Quaternion.Euler(0, alongX ? 90 : 0, 90));   // lies across the bed
        }
    }
}
