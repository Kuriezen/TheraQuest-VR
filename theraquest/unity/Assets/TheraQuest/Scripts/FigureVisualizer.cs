using UnityEngine;

namespace TheraQuest
{
    /// <summary>
    /// A mirror "coach" figure built from primitives (no 3D assets needed).
    /// It copies the patient's measured angle in real time and shows a translucent
    /// ghost limb at the therapist's target angle, coloured by the live feedback tone.
    /// Each exercise code (see server.py LIBRARY) has its own posture (standing, seated
    /// on a chair, lying on a bed), camera-matching view and limb motion. The figure is
    /// mirrored when the operated side is the left.
    /// Swap this for a rigged avatar later (e.g. a Mixamo character) by driving its bones
    /// with the same angle.
    /// </summary>
    public class FigureVisualizer : MonoBehaviour
    {
        const float UpperArm = 0.30f, Forearm = 0.28f, Thigh = 0.42f, Shin = 0.42f;
        const float BedTop = 0.44f;

        enum Posture { Standing, Seated, Lying }

        Material skin, active, ghost, chairMat;
        Transform body, chair, bed;
        Transform rUpperArm, rForearm, rThigh, rShin, lUpperArm, lThigh, lShin;
        Transform ghostPivot, ghostSeg;
        Renderer[] activeRenderers = new Renderer[0];
        string currentCode, currentSide;
        Posture posture;

        void Awake()
        {
            skin = MakeMat(new Color(0.62f, 0.76f, 0.74f), false);
            active = MakeMat(new Color(0.94f, 0.71f, 0.24f), false);
            ghost = MakeMat(new Color(0.18f, 0.76f, 0.68f, 0.35f), true);
            chairMat = MakeMat(new Color(0.25f, 0.32f, 0.34f), false);
            Build();
        }

        Material MakeMat(Color c, bool transparent)
        {
            // Copy the default material of a primitive, so this works in URP and Built-in projects.
            var tmp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var m = new Material(tmp.GetComponent<Renderer>().sharedMaterial) { color = c };
            Destroy(tmp);
            if (transparent)
            {
                if (m.HasProperty("_Surface")) // URP
                {
                    m.SetFloat("_Surface", 1f);
                    m.SetFloat("_Blend", 0f);
                    m.SetOverrideTag("RenderType", "Transparent");
                    m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    m.SetInt("_ZWrite", 0);
                    m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                }
                else // Standard shader
                {
                    m.SetFloat("_Mode", 3f);
                    m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    m.SetInt("_ZWrite", 0);
                    m.EnableKeyword("_ALPHABLEND_ON");
                    m.renderQueue = 3000;
                }
            }
            return m;
        }

        Transform Part(PrimitiveType type, Transform parent, Vector3 localPos, Vector3 scale, Material mat, string name)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            return go.transform;
        }

        // A pivot with a capsule hanging "down" from it (local -Y), so rotating the pivot swings the limb.
        Transform Limb(Transform parent, Vector3 pivotPos, float length, float thickness, string name, out Transform segment)
        {
            var pivot = new GameObject(name).transform;
            pivot.SetParent(parent, false);
            pivot.localPosition = pivotPos;
            segment = Part(PrimitiveType.Capsule, pivot, new Vector3(0, -length / 2f, 0),
                           new Vector3(thickness, length / 2f, thickness), skin, name + " mesh");
            return pivot;
        }

        void Build()
        {
            body = new GameObject("Body").transform;
            body.SetParent(transform, false);
            Part(PrimitiveType.Sphere, body, new Vector3(0, 1.62f, 0), Vector3.one * 0.22f, skin, "Head");
            Part(PrimitiveType.Capsule, body, new Vector3(0, 1.25f, 0), new Vector3(0.30f, 0.30f, 0.18f), skin, "Torso");

            // Patient's right side is local -X (the figure faces the viewer, so it appears on the viewer's right).
            rUpperArm = Limb(body, new Vector3(-0.19f, 1.45f, 0), UpperArm, 0.08f, "R upper arm", out _);
            rForearm = Limb(rUpperArm, new Vector3(0, -UpperArm, 0), Forearm, 0.07f, "R forearm", out _);
            lUpperArm = Limb(body, new Vector3(0.19f, 1.45f, 0), UpperArm + Forearm, 0.08f, "L arm", out _);
            rThigh = Limb(body, new Vector3(-0.09f, 0.92f, 0), Thigh, 0.11f, "R thigh", out _);
            rShin = Limb(rThigh, new Vector3(0, -Thigh, 0), Shin, 0.09f, "R shin", out _);
            lThigh = Limb(body, new Vector3(0.09f, 0.92f, 0), Thigh, 0.11f, "L thigh", out _);
            lShin = Limb(lThigh, new Vector3(0, -Thigh, 0), Shin, 0.09f, "L shin", out _);

            chair = new GameObject("Chair").transform;
            chair.SetParent(transform, false);
            Part(PrimitiveType.Cube, chair, new Vector3(0, 0.47f, 0.05f), new Vector3(0.5f, 0.06f, 0.5f), chairMat, "Seat");
            Part(PrimitiveType.Cube, chair, new Vector3(0, 0.95f, -0.22f), new Vector3(0.5f, 0.9f, 0.05f), chairMat, "Back");
            Part(PrimitiveType.Cube, chair, new Vector3(0, 0.22f, 0.05f), new Vector3(0.44f, 0.44f, 0.44f), chairMat, "Base");

            bed = new GameObject("Bed").transform;
            bed.SetParent(transform, false);
            Part(PrimitiveType.Cube, bed, new Vector3(0, BedTop / 2f, -0.02f), new Vector3(0.7f, BedTop, 2.0f), chairMat, "Bed");

            ghostPivot = new GameObject("Target ghost").transform;
            ghostPivot.SetParent(body, false);
            ghostSeg = Part(PrimitiveType.Capsule, ghostPivot, Vector3.zero, Vector3.one, ghost, "Ghost mesh");
        }

        void SetActiveLimb(params Transform[] pivots)
        {
            foreach (var r in activeRenderers) r.sharedMaterial = skin;
            var list = new System.Collections.Generic.List<Renderer>();
            foreach (var p in pivots)
                foreach (var r in p.GetComponentsInChildren<Renderer>())
                    if (!r.transform.IsChildOf(ghostPivot)) list.Add(r);
            activeRenderers = list.ToArray();
        }

        static bool FrontView(string code) => code == "shoulderAbd" || code == "scaption" || code == "hipAbd";

        void Configure(ExerciseDef ex)
        {
            string code = currentCode = ex.code;
            currentSide = ex.side;
            posture = code == "kneeExt" || code == "sitToStand" ? Posture.Seated
                    : code == "slr" || code == "heelSlide" ? Posture.Lying : Posture.Standing;
            chair.gameObject.SetActive(posture == Posture.Seated);
            bed.gameObject.SetActive(posture == Posture.Lying);

            // Front view for movements out to the side, side view for forward/backward ones,
            // matching where the camera should be. Left side = mirrored figure.
            bool left = ex.side == "L";
            transform.localScale = new Vector3(left ? -1f : 1f, 1f, 1f);
            transform.localRotation = Quaternion.Euler(0, FrontView(code) ? 0f : left ? 90f : -90f, 0);

            // Lying on the back: head away from the feet along the bed, face up.
            body.localRotation = posture == Posture.Lying ? Quaternion.Euler(-90, 0, 0) : Quaternion.identity;
            body.localPosition = posture == Posture.Lying ? new Vector3(0, BedTop + 0.1f, 0.8f) : Vector3.zero;

            rUpperArm.localRotation = rForearm.localRotation = lUpperArm.localRotation = Quaternion.identity;
            rThigh.localRotation = rShin.localRotation = lThigh.localRotation = lShin.localRotation = Quaternion.identity;
            if (code == "slr") { lThigh.localRotation = Flex(45); lShin.localRotation = Bend(90); } // other knee bent, foot flat

            if (code == "elbowFlex") Ghost(rUpperArm, rForearm.localPosition, Forearm);
            else if (code == "shoulderAbd" || code == "scaption" || code == "shoulderFlex" || code == "wallSlide")
                Ghost(body, rUpperArm.localPosition, UpperArm + Forearm);
            else if (code == "hipAbd" || code == "slr") Ghost(body, rThigh.localPosition, Thigh + Shin);
            else if (code == "march") Ghost(body, rThigh.localPosition, Thigh);
            else Ghost(rThigh, rShin.localPosition, Shin);   // knee exercises: target knee angle

            // Colour the moving limb (done after the ghost is re-parented; SetActiveLimb skips it).
            if (code == "elbowFlex") SetActiveLimb(rForearm);
            else if (code == "kneeExt") SetActiveLimb(rShin);
            else if (ex.joint == "Hip" || ex.joint == "Knee") SetActiveLimb(rThigh);
            else SetActiveLimb(rUpperArm);
        }

        void Ghost(Transform parent, Vector3 pos, float len)
        {
            ghostPivot.SetParent(parent, false);
            ghostPivot.localPosition = pos;
            ghostSeg.localPosition = new Vector3(0, -len / 2f, 0);
            ghostSeg.localScale = new Vector3(0.05f, len / 2f, 0.05f);
        }

        // Limb rotations, in the body's frame (limbs hang along -Y, the figure faces +Z).
        static Quaternion Flex(float a) => Quaternion.Euler(-a, 0, 0);      // swing forward and up
        static Quaternion Bend(float a) => Quaternion.Euler(a, 0, 0);       // knee bend (shin swings back)
        static Quaternion Abduct(float a) => Quaternion.Euler(0, 0, -a);    // out to the side
        static Quaternion Scaption(float a) => Quaternion.AngleAxis(-a, Quaternion.Euler(0, 30, 0) * Vector3.forward);

        /// <summary>Rotation of the ghost limb at the target angle.</summary>
        static Quaternion GhostPose(string code, float target)
        {
            switch (code)
            {
                case "shoulderAbd": case "hipAbd": return Abduct(target);
                case "scaption": return Scaption(target);
                case "heelSlide": case "miniSquat": return Bend(target);
                case "kneeExt": case "sitToStand": return Bend(90f - target);
                default: return Flex(target);   // shoulderFlex, wallSlide, elbowFlex, march, slr
            }
        }

        /// <summary>Moves the figure to the patient's measured angle.</summary>
        void Pose(string code, float a)
        {
            switch (code)
            {
                case "shoulderAbd": rUpperArm.localRotation = Abduct(a); break;
                case "scaption": rUpperArm.localRotation = Scaption(a); break;
                case "shoulderFlex": case "wallSlide": rUpperArm.localRotation = Flex(a); break;
                case "elbowFlex": rForearm.localRotation = Flex(a); break;
                case "hipAbd": rThigh.localRotation = Abduct(a); break;
                case "slr": rThigh.localRotation = Flex(a); break;
                case "march": rThigh.localRotation = Flex(a); rShin.localRotation = Bend(a); break;   // shin stays vertical
                case "heelSlide":                                                                    // heel stays on the bed
                    rThigh.localRotation = Flex(a / 2f); rShin.localRotation = Bend(a); break;
                case "miniSquat":                                                                    // feet stay on the floor
                    rThigh.localRotation = lThigh.localRotation = Flex(a / 2f);
                    rShin.localRotation = lShin.localRotation = Bend(a);
                    body.localPosition = new Vector3(0, -(Thigh + Shin) * (1f - Mathf.Cos(a / 2f * Mathf.Deg2Rad)), 0);
                    break;
                case "sitToStand":                                                                   // 0 = seated, 90 = standing
                    float hip = 90f - Mathf.Clamp(a, 0f, 90f), r = hip * Mathf.Deg2Rad;
                    rThigh.localRotation = lThigh.localRotation = Flex(hip);
                    rShin.localRotation = lShin.localRotation = Bend(hip);
                    body.localPosition = new Vector3(0, Thigh * (Mathf.Cos(r) - 1f) + 0.02f * hip / 90f, 0.47f - Thigh * Mathf.Sin(r));
                    break;
                default:                                                                             // kneeExt, seated
                    rThigh.localRotation = lThigh.localRotation = Flex(90);
                    lShin.localRotation = Bend(90);
                    rShin.localRotation = Bend(90f - a);
                    body.localPosition = new Vector3(0, -0.40f, 0.05f);
                    break;
            }
        }

        public void Show(ExerciseDef ex, float angle, Tone tone)
        {
            if (ex == null) return;
            if (ex.code != currentCode || ex.side != currentSide) Configure(ex);
            Pose(ex.code, angle);
            ghostPivot.localRotation = GhostPose(ex.code, ex.targetAngle);

            active.color = tone == Tone.Neutral ? new Color(0.94f, 0.71f, 0.24f) : FeedbackManager.ToneColor(tone);
            foreach (var r in activeRenderers) r.sharedMaterial = active;
        }
    }
}
