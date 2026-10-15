using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Rendering;

namespace TheraQuest
{
    /// <summary>
    /// Rigged coach for any Humanoid character (Mixamo, Ready Player Me, VRoid, ...):
    ///   Coach - stands in front of the player and demonstrates the current exercise,
    ///           moving between the start pose and the therapist's target angle.
    ///   Ghost - a translucent copy that follows the patient's live angle, tinted by the feedback tone.
    /// Bones come from Animator.GetBoneTransform (HumanBodyBones), so bone names don't matter.
    /// The Animator is switched off and the pose is written in LateUpdate: every limb is aimed at a
    /// direction in the body's frame (+Z forward, +Y up, +X the character's right), so it works
    /// whatever the rig's rest pose (T-pose or A-pose).
    /// Motions match the tracker angles (tracker/angles.py), picked by exercise code:
    ///   shoulderAbd (+ scaption), shoulderFlex (+ wallSlide, forwardReach), supineFlex (cane, both arms),
    ///   shoulderExt, elbowFlex (+ bicepsCurl), wallPushUp, hipAbd (+ side-lying), hipExt, hipFlex (slr),
    ///   march, kneeFlex (heelSlide, hamstringCurl), miniSquat (+ kneeWallSlide), miniLunge, kneeExt, sitStand.
    /// Several characters can be listed; the patient picks one on the start screen (remembered between sessions).
    /// The patient also picks the guide (remembered too):
    ///   Coach      - the coach demonstrates and the ghost follows the patient
    ///   Silhouette - no coach: only the patient's silhouette, plus a gold marker showing where the
    ///                moving limb has to reach (the target angle)
    /// With no character assigned, or none that is Humanoid, the primitive FigureVisualizer is shown.
    /// Smoothness: the live angle is smoothed (the tracker sends ~8-30 updates a second), every bone eases
    /// toward its new pose instead of snapping, the coach breathes and glances at the player, and a
    /// springy "pop" covers the jump when the exercise, posture or avatar changes.
    /// </summary>
    public class HumanoidCoach : MonoBehaviour
    {
        [System.Serializable]
        public class Option
        {
            public string name = "Coach";
            [Tooltip("A model or prefab whose Rig > Animation Type is Humanoid.")]
            public GameObject model;
        }

        [Tooltip("Coaches the patient can pick from on the start screen. Leave empty to use the primitive figure.")]
        public Option[] characters = new Option[0];
        [Tooltip("Shown instead when no Humanoid character is assigned.")]
        public FigureVisualizer fallback;
        [Tooltip("Optional looping idle animation (e.g. Mixamo \"Breathing Idle\", put in a CoachAnimations folder). " +
                 "While standing, every limb that isn't exercising plays it, so the coach breathes and shifts naturally.")]
        public AnimationClip idleClip;

        [Header("Coach demo")]
        [Tooltip("Seconds to move from the start pose to the target, and the same to come back.")]
        public float moveSeconds = 1.6f;
        [Tooltip("Pause in the start pose between demo reps.")]
        public float pauseSeconds = 0.8f;
        [Tooltip("Move the coach's opposite limb so it reads like a mirror (your right arm = its left).")]
        public bool mirror = true;

        [Header("Ghost (patient's live angle)")]
        [Tooltip("Ghost position relative to the coach. Zero = drawn over the coach, so you match your ghost to it.")]
        public Vector3 ghostOffset = Vector3.zero;
        [Range(0.05f, 1f)] public float ghostAlpha = 0.4f;
        [Tooltip("Opacity of the patient's figure in Silhouette mode, where it is the only figure.")]
        [Range(0.05f, 1f)] public float silhouetteAlpha = 0.65f;

        [Header("Smoothness")]
        [Tooltip("Seconds the live angle takes to catch up (hides tracker jitter and low frame rates).")]
        public float angleSmoothing = 0.12f;
        [Tooltip("How quickly bones ease into each new pose (higher = snappier).")]
        public float poseEasing = 16f;
        [Tooltip("The coach glances toward the player, up to this many degrees.")]
        public float lookAtPlayer = 30f;
        [Tooltip("Metres the overlaid ghost sits behind the coach (along the view), so it only shows where the patient differs.")]
        public float ghostDepthOffset = 0.04f;

        public enum Guide { Coach, Silhouette }

        /// <summary>Show the patient's ghost over the coach (set by SessionManager: only while exercising).</summary>
        [HideInInspector] public bool ghostVisible = true;

        public bool Ready => built.Count > 0;
        public int Count => built.Count;
        public string CoachName => Ready ? built[selected].name : "";
        public Guide CurrentGuide { get; private set; }
        public string GuideName => CurrentGuide == Guide.Coach ? "Coach to follow" : "Silhouette + target angle";

        enum Motion { ShoulderAbd, Scaption, ShoulderFlex, SupineFlex, ShoulderExt, ElbowFlex, WallPushUp, HipAbd, HipExt, HipFlex, March,
                      KneeFlex, MiniSquat, MiniLunge, KneeExt, SitStand }
        enum Posture { Standing, Seated, Lying, SideLying }

        static readonly Color GhostNeutral = new Color(0.18f, 0.76f, 0.68f);
        static readonly Color TargetColor = new Color(0.96f, 0.77f, 0.32f, 0.6f);
        const string PrefKey = "TheraQuest.Coach", GuideKey = "TheraQuest.Guide";
        const float MarkerWidth = 0.07f;

        sealed class Built { public string name; public GameObject root; public Figure coach, ghost; }

        readonly List<Built> built = new List<Built>();
        int selected;
        Material ghostMat, propMat;
        Transform marker, markerLimb, markerEnd;
        ExerciseDef current;
        string currentCode, currentSide;
        Motion motion;
        Posture posture;
        int side;               // +1 = drive the character's right limbs, -1 = left
        float liveAngle, liveTarget, liveVel, demoTime, popTime = 10f;

        void Awake()
        {
            if (characters != null)
                foreach (var option in characters)
                    if (option != null && option.model != null) Build(option);
            if (fallback != null) fallback.gameObject.SetActive(!Ready);
            if (!Ready) return;
            BuildMarker();
            CurrentGuide = PlayerPrefs.GetInt(GuideKey, 0) == 1 ? Guide.Silhouette : Guide.Coach;
            Select(Mathf.Clamp(PlayerPrefs.GetInt(PrefKey, 0), 0, built.Count - 1));
        }

        /// <summary>Switches between following the coach and silhouette + target, and remembers the choice.</summary>
        public void ToggleGuide()
        {
            if (!Ready) return;
            CurrentGuide = CurrentGuide == Guide.Coach ? Guide.Silhouette : Guide.Coach;
            PlayerPrefs.SetInt(GuideKey, (int)CurrentGuide);
            Select(selected);
        }

        /// <summary>Gold limb + ball at the end: where the moving limb has to reach (Silhouette mode).</summary>
        void BuildMarker()
        {
            var mat = FigureVisualizer.MakeMat(TargetColor, true);
            marker = new GameObject("Target marker").transform;
            marker.SetParent(transform, false);
            markerLimb = Figure.Primitive(PrimitiveType.Capsule, marker, mat);
            markerEnd = Figure.Primitive(PrimitiveType.Sphere, marker, mat);
            markerEnd.localScale = Vector3.one * MarkerWidth * 2f;
            marker.gameObject.SetActive(false);
        }

        /// <summary>Switches to the next (step 1) or previous (step -1) coach and remembers the choice.</summary>
        public void Cycle(int step)
        {
            if (built.Count > 1) Select(((selected + step) % built.Count + built.Count) % built.Count);
        }

        void Select(int i)
        {
            selected = i;
            for (int j = 0; j < built.Count; j++) built[j].root.SetActive(j == i);
            built[i].coach.SetBodyVisible(CurrentGuide == Guide.Coach);   // its chair / bed stay
            currentCode = null;   // re-run Configure on the next Show (ghost size depends on the guide)
            built[i].coach.Snap();
            built[i].ghost.Snap();
            popTime = 0f;
            PlayerPrefs.SetInt(PrefKey, i);
        }

        void Build(Option option)
        {
            var model = option.model;
            if (model == gameObject || transform.IsChildOf(model.transform))
            {
                Debug.LogWarning($"[TheraQuest] Put {model.name} on its own object, not on the HumanoidCoach. Skipping it.");
                return;
            }
            var anim = model.GetComponentInChildren<Animator>(true);
            if (anim == null || anim.avatar == null || !anim.avatar.isHuman)
            {
                Debug.LogWarning($"[TheraQuest] {model.name} has no Humanoid Animator (set Rig > Animation Type to Humanoid). Skipping it.");
                return;
            }

            if (ghostMat == null)
            {
                propMat = FigureVisualizer.MakeMat(new Color(0.25f, 0.32f, 0.34f), false);
                ghostMat = FigureVisualizer.MakeMat(new Color(GhostNeutral.r, GhostNeutral.g, GhostNeutral.b, ghostAlpha), true);
            }

            string name = string.IsNullOrEmpty(option.name) ? model.name : option.name;
            var b = new Built { name = name, root = new GameObject(name) };
            b.root.transform.SetParent(transform, false);
            // A prefab or model asset is instantiated; a character already in the scene is used as is.
            var coachGo = model.scene.IsValid() ? model : Instantiate(model);
            var ghostGo = Instantiate(coachGo);   // copy before the coach's Animator is switched off
            coachGo.name = "Coach (" + model.name + ")";
            ghostGo.name = "Ghost (" + model.name + ")";
            b.coach = Figure.Create(b.root.transform, "Coach", coachGo, Vector3.zero, propMat, null, idleClip);
            b.ghost = Figure.Create(b.root.transform, "Ghost", ghostGo, ghostOffset, ghostMat, ghostMat, idleClip);
            b.ghost.showProps = ghostOffset.sqrMagnitude > 0.01f;   // overlaid ghost shares the coach's chair / bed
            built.Add(b);
        }

        void OnDestroy()
        {
            foreach (var b in built) { b.coach.Dispose(); b.ghost.Dispose(); }
        }

#if UNITY_EDITOR
        // Pick up an idle animation dropped into a CoachAnimations folder, so existing scenes get it too.
        void OnValidate()
        {
            if (idleClip != null) return;
            foreach (var guid in UnityEditor.AssetDatabase.FindAssets("t:AnimationClip"))
            {
                string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                if (!path.Replace('\\', '/').Contains("/CoachAnimations/")) continue;
                foreach (var asset in UnityEditor.AssetDatabase.LoadAllAssetsAtPath(path))
                    if (asset is AnimationClip clip && !clip.name.StartsWith("__preview__")) { idleClip = clip; return; }
            }
        }
#endif

        public void Show(ExerciseDef ex, float angle, Tone tone)
        {
            if (ex == null) return;
            if (!Ready)
            {
                if (fallback != null) fallback.Show(ex, angle, tone);
                return;
            }
            if (ex.code != currentCode || ex.side != currentSide) Configure(ex);
            current = ex;
            liveTarget = angle;
            var c = tone == Tone.Neutral ? GhostNeutral : FeedbackManager.ToneColor(tone);
            c.a = CurrentGuide == Guide.Coach ? ghostAlpha : silhouetteAlpha;
            ghostMat.color = c;
        }

        void Configure(ExerciseDef ex)
        {
            currentCode = ex.code;
            currentSide = ex.side;
            motion = MotionFor(ex);
            posture = ex.code == "slr" || ex.code == "heelSlide" || ex.code == "supineFlex" ? Posture.Lying
                    : ex.code == "sidelyingHipAbd" ? Posture.SideLying
                    : motion == Motion.KneeExt || motion == Motion.SitStand ? Posture.Seated : Posture.Standing;
            int patient = ex.side == "L" ? -1 : 1;   // "" = right
            side = mirror ? -patient : patient;

            // Front view for movements out to the side, side view (moving limb toward the player) otherwise,
            // matching where the tracker camera should be.
            bool front = motion == Motion.ShoulderAbd || motion == Motion.Scaption || motion == Motion.HipAbd;
            float yaw = front ? 0f : side > 0 ? -90f : 90f;
            foreach (var b in built)
            {
                b.coach.Configure(posture, yaw, side);
                b.ghost.Configure(posture, yaw, side);
                b.coach.Snap();   // new posture: jump straight there, hidden by the pop
                b.ghost.Snap();
            }
            demoTime = 0f;
            liveAngle = liveTarget;
            liveVel = 0f;
            popTime = 0f;
        }

        static Motion MotionFor(ExerciseDef ex)
        {
            switch (ex.code)
            {
                case "shoulderAbd": return Motion.ShoulderAbd;
                case "scaption": return Motion.Scaption;
                case "shoulderFlex": case "wallSlide": case "forwardReach": return Motion.ShoulderFlex;
                case "supineFlex": return Motion.SupineFlex;
                case "shoulderExt": return Motion.ShoulderExt;
                case "elbowFlex": case "bicepsCurl": return Motion.ElbowFlex;
                case "wallPushUp": return Motion.WallPushUp;
                case "hipAbd": case "sidelyingHipAbd": return Motion.HipAbd;
                case "hipExt": return Motion.HipExt;
                case "slr": return Motion.HipFlex;
                case "march": return Motion.March;
                case "heelSlide": case "hamstringCurl": return Motion.KneeFlex;
                case "miniSquat": case "kneeWallSlide": return Motion.MiniSquat;
                case "miniLunge": return Motion.MiniLunge;
                case "kneeExt": return Motion.KneeExt;
                case "sitToStand": return Motion.SitStand;
            }
            // Unknown code: go by the tracker angle and camera view.
            bool front = ex.view == "front";
            switch (ex.measure)
            {
                case "elbowFlex": return Motion.ElbowFlex;
                case "hipElev": return front ? Motion.HipAbd : Motion.HipFlex;
                case "kneeFlex": return Motion.KneeFlex;
                case "kneeExt": return Motion.KneeExt;
                default: return front ? Motion.ShoulderAbd : Motion.ShoulderFlex;
            }
        }

        void LateUpdate()
        {
            if (!Ready || current == null) return;
            float dt = Time.deltaTime;   // 0 while paused: everything holds still
            demoTime += dt;
            liveAngle = Mathf.SmoothDamp(liveAngle, liveTarget, ref liveVel, angleSmoothing, Mathf.Infinity, Mathf.Max(dt, 1e-5f));
            var b = built[selected];
            bool coachMode = CurrentGuide == Guide.Coach;
            // Gentle breathing and a slow side-to-side weight shift, in degrees (the idle animation does this when there is one)
            float breath = Mathf.Sin(Time.time * 1.6f) * 1.2f;
            float sway = Mathf.Sin(Time.time * 0.7f) * 1.5f;
            if (coachMode)
            {
                b.coach.Pose(motion, posture, current.targetAngle * DemoPhase(), side, breath, sway);
                if (posture != Posture.Lying && posture != Posture.SideLying && Camera.main != null)
                    b.coach.LookAt(Camera.main.transform.position, lookAtPlayer);
                b.coach.Settle(dt, poseEasing);
            }
            else
            {
                // Pose the silhouette at the target first, just to read where the moving limb would be.
                b.ghost.Pose(motion, posture, current.targetAngle, side, 0f, 0f);
                var (from, to) = b.ghost.Segment(motion, side);
                PlaceMarker(from, to);
            }
            marker.gameObject.SetActive(!coachMode);
            b.ghost.SetBodyVisible(ghostVisible || !coachMode);   // silhouette mode: the ghost is the only figure
            b.ghost.Pose(motion, posture, liveAngle, side, coachMode ? breath : 0f, coachMode ? sway : 0f);
            if (coachMode && posture != Posture.Lying && posture != Posture.SideLying && Camera.main != null)
                b.ghost.LookAt(Camera.main.transform.position, lookAtPlayer);
            b.ghost.Settle(dt, poseEasing);

            // Over the coach, push the ghost a few centimetres away from the camera: wherever it matches the coach
            // it is hidden behind the coach's body (no flicker, no patches), and the teal only shows where the
            // patient's limb is somewhere else. From the player's point of view the push is invisible.
            bool overlay = coachMode && ghostOffset.sqrMagnitude < 0.01f && Camera.main != null;
            b.ghost.Nudge(overlay ? Camera.main.transform.forward * ghostDepthOffset : Vector3.zero);

            // Springy pop when the exercise, posture or avatar changes (grows from the floor, slight overshoot)
            popTime += Time.unscaledDeltaTime;
            float x = Mathf.Clamp01(popTime / 0.35f) - 1f;
            float scale = 0.6f + 0.4f * (1f + 2.70158f * x * x * x + 1.70158f * x * x);
            b.root.transform.localScale = Vector3.one * scale;
        }

        void PlaceMarker(Vector3 from, Vector3 to)
        {
            var d = to - from;
            float len = d.magnitude;
            if (len < 1e-4f) return;
            markerLimb.position = (from + to) / 2f;
            markerLimb.rotation = Quaternion.FromToRotation(Vector3.up, d);
            markerLimb.localScale = new Vector3(MarkerWidth, len / 2f, MarkerWidth);   // capsule is 2 units tall
            markerEnd.position = to;
        }

        /// <summary>0 = start pose, 1 = target. One demo rep: rise, hold, lower, pause.</summary>
        float DemoPhase()
        {
            float move = Mathf.Max(0.2f, moveSeconds), hold = Mathf.Max(0.5f, current.holdSeconds);
            float t = demoTime % (move * 2f + hold + Mathf.Max(0f, pauseSeconds));
            if (t < move) return Ease(t / move);
            if ((t -= move) < hold) return 1f;
            if ((t -= hold) < move) return Ease(1f - t / move);
            return 0f;
        }

        static float Ease(float x) { x = Mathf.Clamp01(x); return x * x * x * (x * (6f * x - 15f) + 10f); }
        static float Sin(float deg) => Mathf.Sin(deg * Mathf.Deg2Rad);
        static float Cos(float deg) => Mathf.Cos(deg * Mathf.Deg2Rad);

        // Limb directions in the body frame (limbs hang along -Y, the body faces +Z, s = +1 right / -1 left).
        static Vector3 Sag(float a) => new Vector3(0f, -Cos(a), Sin(a));                     // swing forward and up
        static Vector3 Out(int s, float a) => new Vector3(s * Sin(a), -Cos(a), 0f);          // out to the side
        static Vector3 Scap(int s, float a) => new Vector3(s * Sin(a) * 0.866f, -Cos(a), Sin(a) * 0.5f); // 30° forward of the side

        /// <summary>One posed copy of the character: slot (view yaw) > pivot (lying) > character.</summary>
        sealed class Figure
        {
            const float BedTop = 0.44f;

            public bool showProps = true;
            Vector3 offset;
            Transform slot, pivot, chair, bed, bedSide, hips, spine, head;
            GameObject body;
            readonly Transform[] upperArm = new Transform[2], lowerArm = new Transform[2], hand = new Transform[2];
            readonly Transform[] upperLeg = new Transform[2], lowerLeg = new Transform[2], foot = new Transform[2];
            Transform[] posed;
            Quaternion[] rest, last;
            readonly Vector3[] toeLocal = new Vector3[2];
            readonly bool[] legPosed = new bool[2];
            Transform pillowSide;
            Vector3 hipsRestLocal, hipsRestPivot, hipsLast;
            bool hasLast;
            float thigh, shin;
            float armOut = 10f;   // degrees the resting arms hang out from the body, measured so the hands clear the hips
            bool animated;        // an idle clip drives the limbs the exercise doesn't use (standing only)
            PlayableGraph graph;

            public void Dispose() { if (graph.IsValid()) graph.Destroy(); }

            static int I(int s) => s > 0 ? 1 : 0;   // array index: 0 = left, 1 = right

            public static Figure Create(Transform owner, string name, GameObject go, Vector3 offset, Material propMat, Material skin, AnimationClip idle)
            {
                var f = new Figure();
                f.slot = new GameObject(name).transform;
                f.slot.SetParent(owner, false);
                f.slot.localPosition = f.offset = offset;
                f.pivot = new GameObject(name + " pose").transform;
                f.pivot.SetParent(f.slot, false);

                var anim = go.GetComponentInChildren<Animator>(true);
                f.hips = anim.GetBoneTransform(HumanBodyBones.Hips);
                f.spine = anim.GetBoneTransform(HumanBodyBones.Spine);
                var head = f.head = anim.GetBoneTransform(HumanBodyBones.Head);
                f.upperArm[0] = anim.GetBoneTransform(HumanBodyBones.LeftUpperArm);
                f.upperArm[1] = anim.GetBoneTransform(HumanBodyBones.RightUpperArm);
                f.lowerArm[0] = anim.GetBoneTransform(HumanBodyBones.LeftLowerArm);
                f.lowerArm[1] = anim.GetBoneTransform(HumanBodyBones.RightLowerArm);
                f.hand[0] = anim.GetBoneTransform(HumanBodyBones.LeftHand);
                f.hand[1] = anim.GetBoneTransform(HumanBodyBones.RightHand);
                f.upperLeg[0] = anim.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
                f.upperLeg[1] = anim.GetBoneTransform(HumanBodyBones.RightUpperLeg);
                f.lowerLeg[0] = anim.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
                f.lowerLeg[1] = anim.GetBoneTransform(HumanBodyBones.RightLowerLeg);
                f.foot[0] = anim.GetBoneTransform(HumanBodyBones.LeftFoot);
                f.foot[1] = anim.GetBoneTransform(HumanBodyBones.RightFoot);
                // Without an idle clip the Animator is off and the whole pose is written by hand in LateUpdate.
                // With one, the Animator plays it (no controller asset needed) and LateUpdate writes the exercise on top.
                f.animated = idle != null;
                if (f.animated)
                {
                    anim.applyRootMotion = false;
                    anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    f.graph = PlayableGraph.Create(name + " idle");
                    var output = AnimationPlayableOutput.Create(f.graph, "Idle", anim);
                    output.SetSourcePlayable(AnimationClipPlayable.Create(f.graph, idle));
                    f.graph.Play();
                }
                else anim.enabled = false;

                // Turn the model so its body faces +Z with +Y up, whatever its import rotation, hips over the pivot.
                f.body = go;
                var root = go.transform;
                root.SetParent(f.pivot, false);
                root.localPosition = Vector3.zero;
                var right = root.InverseTransformDirection(f.upperArm[1].position - f.upperArm[0].position);
                var up = root.InverseTransformDirection(head.position - f.hips.position);
                root.localRotation = Quaternion.Inverse(Quaternion.LookRotation(Vector3.Cross(right, up), up));
                var h = f.pivot.InverseTransformPoint(f.hips.position);
                root.localPosition = new Vector3(-h.x, 0f, -h.z);

                f.posed = new[] { f.hips, f.spine, f.upperArm[0], f.upperArm[1], f.lowerArm[0], f.lowerArm[1],
                                  f.upperLeg[0], f.upperLeg[1], f.lowerLeg[0], f.lowerLeg[1], f.head, f.foot[0], f.foot[1] };
                f.rest = new Quaternion[f.posed.Length];
                f.last = new Quaternion[f.posed.Length];
                for (int i = 0; i < f.posed.Length; i++) f.rest[i] = f.posed[i].localRotation;
                // Which way the toes point, in each foot's own space (the rest pose faces +Z)
                for (int i = 0; i < 2; i++) f.toeLocal[i] = f.foot[i].InverseTransformDirection(f.pivot.forward);
                f.hipsRestLocal = f.hips.localPosition;
                f.hipsRestPivot = f.pivot.InverseTransformPoint(f.hips.position);
                f.thigh = (f.Length(f.upperLeg[0], f.lowerLeg[0]) + f.Length(f.upperLeg[1], f.lowerLeg[1])) / 2f;
                f.shin = (f.Length(f.lowerLeg[0], f.foot[0]) + f.Length(f.lowerLeg[1], f.foot[1])) / 2f;

                // Resting arm angle for this character: the hands must hang clear of the hips and thighs.
                // shoulder x, hip-joint x and arm length are read from the rig, so any body shape works.
                float shoulderX = (Mathf.Abs(f.pivot.InverseTransformPoint(f.upperArm[0].position).x) +
                                   Mathf.Abs(f.pivot.InverseTransformPoint(f.upperArm[1].position).x)) / 2f;
                float hipX = (Mathf.Abs(f.pivot.InverseTransformPoint(f.upperLeg[0].position).x) +
                              Mathf.Abs(f.pivot.InverseTransformPoint(f.upperLeg[1].position).x)) / 2f;
                float arm = (f.Length(f.upperArm[0], f.lowerArm[0]) + f.Length(f.lowerArm[0], f.hand[0]) +
                             f.Length(f.upperArm[1], f.lowerArm[1]) + f.Length(f.lowerArm[1], f.hand[1])) / 2f;
                float clearX = hipX + 0.16f;   // thigh and hip flesh plus the hand's own thickness
                f.armOut = Mathf.Clamp(Mathf.Asin(Mathf.Clamp((clearX - shoulderX) / Mathf.Max(0.3f, arm), -1f, 1f)) * Mathf.Rad2Deg, 9f, 26f);

                foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                {
                    if (skin != null)
                    {
                        var mats = new Material[r.sharedMaterials.Length];
                        for (int i = 0; i < mats.Length; i++) mats[i] = skin;
                        r.sharedMaterials = mats;
                        r.shadowCastingMode = ShadowCastingMode.Off;
                    }
                    if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = true;   // bones move far from the bind pose
                }

                // Chair sized to the character: hips on the seat, thighs level, feet on the floor.
                f.chair = new GameObject("Chair").transform;
                f.chair.SetParent(f.slot, false);
                float seatTop = Mathf.Max(0.2f, f.hipsRestPivot.y - f.thigh - 0.08f);
                PropBuilder.Chair(f.chair, seatTop, -f.thigh - 0.17f, -0.02f);

                // Treatment tables: along the view for lying on the back (head toward -Z), across it for side-lying
                f.bed = new GameObject("Bed").transform;
                f.bed.SetParent(f.slot, false);
                PropBuilder.Bed(f.bed, BedTop, new Vector2(0f, 0.1f), 2.1f, 0.8f, false);
                f.bedSide = new GameObject("Bed (side-lying)").transform;
                f.bedSide.SetParent(f.slot, false);
                f.pillowSide = PropBuilder.Bed(f.bedSide, BedTop, Vector2.zero, 2.1f, 0.8f, true);
                return f;
            }

            float Length(Transform a, Transform b) => pivot.InverseTransformVector(b.position - a.position).magnitude;

            public void SetBodyVisible(bool visible) => body.SetActive(visible);

            /// <summary>World start and end of the limb segment that the exercise moves (as currently posed).</summary>
            public (Vector3 from, Vector3 to) Segment(Motion m, int s)
            {
                int i = I(s);
                switch (m)
                {
                    case Motion.ShoulderAbd: case Motion.Scaption: case Motion.ShoulderFlex: case Motion.SupineFlex: case Motion.ShoulderExt:
                        return (upperArm[i].position, hand[i].position);
                    case Motion.ElbowFlex: case Motion.WallPushUp: return (lowerArm[i].position, hand[i].position);
                    case Motion.HipAbd: case Motion.HipFlex: case Motion.HipExt: return (upperLeg[i].position, foot[i].position);
                    case Motion.KneeFlex: case Motion.KneeExt: return (lowerLeg[i].position, foot[i].position);
                    default: return (upperLeg[i].position, lowerLeg[i].position);   // march, mini squat, sit to stand: thigh
                }
            }

            public static Transform Primitive(PrimitiveType type, Transform parent, Material mat)
            {
                var go = GameObject.CreatePrimitive(type);
                Object.Destroy(go.GetComponent<Collider>());
                go.transform.SetParent(parent, false);
                var r = go.GetComponent<Renderer>();
                r.sharedMaterial = mat;
                r.shadowCastingMode = ShadowCastingMode.Off;
                return go.transform;
            }

            public void Configure(Posture p, float yaw, int s)
            {
                bool lying = p == Posture.Lying, sideLying = p == Posture.SideLying;
                slot.localRotation = Quaternion.Euler(0f, yaw, 0f);
                // Lying on the back: face up, head toward -Z, hips just above the bed.
                // Side-lying: rolled onto the other side so the moving leg (s) is on top, hips over the bed's middle.
                pivot.localRotation = lying ? Quaternion.Euler(-90f, 0f, 0f) : sideLying ? Quaternion.Euler(0f, 0f, 90f * s) : Quaternion.identity;
                pivot.localPosition = lying ? new Vector3(0f, BedTop + 0.1f, hipsRestPivot.y)
                                    : sideLying ? new Vector3(s * hipsRestPivot.y, BedTop + 0.2f, 0f) : Vector3.zero;
                chair.gameObject.SetActive(showProps && p == Posture.Seated);
                bed.gameObject.SetActive(showProps && lying);
                bedSide.gameObject.SetActive(showProps && sideLying);
                // pillow under the head, which points to -X when rolled one way and +X the other
                var pp = pillowSide.localPosition;
                pillowSide.localPosition = new Vector3(-s * Mathf.Abs(pp.x), pp.y, pp.z);

                slot.localPosition = offset;
            }

            /// <summary>Moves the figure by a world-space offset from its place (used to sit the ghost behind the coach).</summary>
            public void Nudge(Vector3 world) => slot.localPosition = offset + slot.parent.InverseTransformVector(world);

            /// <summary>Next pose is applied as is (after a posture change), not eased from the old one.</summary>
            public void Snap() => hasLast = false;

            /// <summary>Eases every bone from last frame's pose toward the pose just written (removes snapping).</summary>
            public void Settle(float dt, float rate)
            {
                float k = hasLast ? 1f - Mathf.Exp(-rate * dt) : 1f;
                for (int i = 0; i < posed.Length; i++)
                {
                    if (hasLast) posed[i].localRotation = Quaternion.Slerp(last[i], posed[i].localRotation, k);
                    last[i] = posed[i].localRotation;
                }
                if (hasLast) hips.localPosition = Vector3.Lerp(hipsLast, hips.localPosition, k);
                hipsLast = hips.localPosition;
                hasLast = true;
            }

            /// <summary>Turns the head toward a point (the player), limited to maxDeg from straight ahead.</summary>
            public void LookAt(Vector3 target, float maxDeg)
            {
                if (head == null || maxDeg <= 0f) return;
                var face = pivot.TransformDirection(Vector3.forward);
                var want = target - head.position;
                if (want.sqrMagnitude < 1e-4f) return;
                var dir = Vector3.RotateTowards(face, want.normalized, maxDeg * Mathf.Deg2Rad, 0f);
                head.rotation = Quaternion.FromToRotation(face, dir) * head.rotation;
            }

            /// <summary>
            /// Poses the character at angle a (the tracker's ROM value) for the exercise. breath and sway add
            /// small idle movements (degrees); the limbs that don't exercise relax naturally.
            /// </summary>
            public void Pose(Motion m, Posture p, float a, int s, float breath, float sway)
            {
                // Standing with an idle clip: start from the animated pose (natural breathing, weight shift, arms).
                // Otherwise start from the rest pose and add the small hand-made idle movements.
                bool useAnim = animated && p == Posture.Standing;
                if (!useAnim)
                {
                    for (int i = 0; i < posed.Length; i++) posed[i].localRotation = rest[i];
                    hips.localPosition = hipsRestLocal;
                }
                legPosed[0] = legPosed[1] = false;
                a = Mathf.Clamp(a, 0f, 180f);
                int o = -s;
                bool lying = p == Posture.Lying || p == Posture.SideLying;
                if (!lying && !useAnim)
                {
                    if (breath != 0f) Lean(breath);
                    if (sway != 0f) spine.rotation = Quaternion.AngleAxis(sway, pivot.forward) * spine.rotation;
                }

                switch (m)
                {
                    case Motion.MiniSquat:                                               // feet stay on the floor
                    {
                        float f = a / 2f;
                        Shift((thigh + shin) * (Cos(f) - 1f), (shin - thigh) * Sin(f));
                        Lean(f * 0.6f);
                        Leg(s, f, a);
                        Leg(o, f, a);
                        break;
                    }
                    case Motion.SitStand:                                                // 0 = seated, 90 = standing
                    {
                        float f = 90f - Mathf.Min(a, 90f);
                        Shift(thigh * (Cos(f) - 1f), -thigh * Sin(f));
                        Lean(30f * Sin(2f * f));                                         // lean forward mid-rise
                        Leg(s, f, f);
                        Leg(o, f, f);
                        break;
                    }
                    case Motion.KneeExt:                                                 // seated, 0 = knee at 90°
                        Shift(-thigh, -thigh);
                        Leg(s, 90f, 90f - Mathf.Min(a, 90f));
                        Leg(o, 90f, 90f);
                        break;
                    case Motion.HipAbd: LegDir(s, Out(s, a), Out(s, a)); break;
                    case Motion.HipFlex:                                                 // straight leg
                        Leg(s, a, 0f);
                        if (p == Posture.Lying) Leg(o, 45f, 90f);                        // other knee bent, foot flat
                        break;
                    case Motion.March: Leg(s, a, a); break;                              // shin stays vertical
                    case Motion.HipExt: Leg(s, -a, 0f); break;                           // straight leg back
                    case Motion.MiniLunge:                                               // operated leg in front
                    {
                        Shift((thigh + shin) * (Cos(a / 2f) - 1f) * 0.8f, 0f);
                        Lean(a * 0.15f);
                        Leg(s, a * 0.55f, a);
                        Leg(o, -15f - a * 0.2f, a * 0.6f);
                        break;
                    }
                    case Motion.KneeFlex:
                        if (p == Posture.Lying) Leg(s, a / 2f, a);                       // heel stays on the bed
                        else Leg(s, 0f, a);
                        break;
                }

                // Standing legs that the exercise doesn't move: soft knees instead of locked straight
                if (p == Posture.Standing && !useAnim)
                    for (int side = -1; side <= 1; side += 2)
                        if (!legPosed[I(side)]) Leg(side, 3f, 7f);

                // Arms last, so they hang down even when the trunk leans. Resting arms keep a soft elbow
                // (straight along the body when lying down); some exercises move the other arm naturally.
                Vector3 upper = RestUpper(s, p, sway), fore = RestFore(s, p, sway);
                Vector3 oUpper = RestUpper(o, p, -sway), oFore = RestFore(o, p, -sway);
                bool sArm = false, oArm = false;   // arms the exercise moves (the idle clip keeps the others)
                switch (m)
                {
                    case Motion.ShoulderAbd: upper = fore = Out(s, a); sArm = true; break;
                    case Motion.Scaption: upper = fore = Scap(s, a); sArm = true; break;
                    case Motion.ShoulderFlex: upper = fore = Sag(a); sArm = true; break;
                    case Motion.ShoulderExt: upper = fore = Sag(-a); sArm = true; break;
                    case Motion.ElbowFlex: fore = Sag(a); sArm = true; break;
                    case Motion.March:                                                   // opposite arm swings forward
                        sArm = oArm = true;
                        oUpper = Sag(a * 0.4f); oFore = Sag(a * 0.4f + 30f);
                        upper = Sag(-a * 0.25f); fore = Sag(-a * 0.25f + 15f);
                        break;
                    case Motion.MiniSquat: case Motion.MiniLunge:                        // arms forward for balance
                    {
                        float r = Mathf.Min(70f, a * 1.2f);
                        sArm = oArm = true;
                        upper = oUpper = Sag(r);
                        fore = oFore = Sag(r + 12f);
                        break;
                    }
                    case Motion.SitStand:                                                // reach forward while rising
                    {
                        float r = 50f * Sin(2f * (90f - Mathf.Min(a, 90f)));
                        sArm = oArm = true;
                        upper = oUpper = Sag(r);
                        fore = oFore = Sag(r + 20f);
                        break;
                    }
                    case Motion.SupineFlex:                                              // both hands on the cane
                        Arm(s, Sag(a), Sag(a));
                        Arm(o, Sag(a), Sag(a));
                        return;
                    case Motion.WallPushUp:                                              // hands stay on the wall
                        Lean(15f + a * 0.1f);
                        Arm(s, Sag(90f - a / 2f), Sag(90f + a / 2f));
                        Arm(o, Sag(90f - a / 2f), Sag(90f + a / 2f));
                        return;
                }
                if (sArm || !useAnim) Arm(s, upper, fore);
                if (oArm || !useAnim) Arm(o, oUpper, oFore);
            }

            /// <summary>
            /// Resting upper arm: hangs out just enough to clear the hips (armOut), drifting slightly with the idle sway.
            /// Seated, the arm comes forward so the hands rest on the thighs.
            /// </summary>
            Vector3 RestUpper(int s, Posture p, float sway)
            {
                if (p == Posture.Seated) return (Out(s, armOut * 0.6f) + Vector3.forward * 0.45f).normalized;
                return Out(s, armOut + sway * 0.4f);
            }

            /// <summary>Resting forearm: a soft elbow (slightly forward) standing, on the thigh seated, straight lying down.</summary>
            Vector3 RestFore(int s, Posture p, float sway)
            {
                if (p == Posture.Lying || p == Posture.SideLying) return Out(s, armOut + 2f);
                if (p == Posture.Seated) return (Sag(78f) + new Vector3(s * 0.12f, 0f, 0f)).normalized;
                return (Out(s, armOut + 3f + sway * 0.4f) + Vector3.forward * 0.24f).normalized;
            }

            void Shift(float dy, float dz) => hips.position = pivot.TransformPoint(hipsRestPivot + new Vector3(0f, dy, dz));
            void Lean(float deg) => spine.rotation = Quaternion.AngleAxis(deg, pivot.right) * spine.rotation;

            /// <summary>Leg in the sagittal plane: hip flexion and knee bend in degrees.</summary>
            void Leg(int s, float hipFlex, float kneeBend) => LegDir(s, Sag(hipFlex), Sag(hipFlex - kneeBend));
            void LegDir(int s, Vector3 thighDir, Vector3 shinDir)
            {
                Aim(upperLeg[I(s)], lowerLeg[I(s)], thighDir);
                Aim(lowerLeg[I(s)], foot[I(s)], shinDir);
                AimFoot(s, shinDir);
                legPosed[I(s)] = true;
            }

            /// <summary>
            /// Natural ankle: toes point forward (body +Z) at a right angle to the shin, and up (body +Y)
            /// when the shin itself points forward, blending smoothly in between. Lying down, that means toes up.
            /// </summary>
            void AimFoot(int s, Vector3 shinDir)
            {
                var shin = shinDir.normalized;
                var fwd = Vector3.forward - Vector3.Dot(Vector3.forward, shin) * shin;
                var up = Vector3.up - Vector3.Dot(Vector3.up, shin) * shin;
                var toes = (fwd + up * Mathf.Clamp01(0.6f - fwd.magnitude)).normalized;
                var f = foot[I(s)];
                var cur = f.TransformDirection(toeLocal[I(s)]);
                if (toes.sqrMagnitude > 1e-6f && cur.sqrMagnitude > 1e-6f)
                    f.rotation = Quaternion.FromToRotation(cur, pivot.TransformDirection(toes)) * f.rotation;
            }
            void Arm(int s, Vector3 upperDir, Vector3 foreDir)
            {
                Aim(upperArm[I(s)], lowerArm[I(s)], upperDir);
                Aim(lowerArm[I(s)], hand[I(s)], foreDir);
            }

            /// <summary>Rotates bone so the segment to its child points along dir (body frame).</summary>
            void Aim(Transform bone, Transform child, Vector3 dir)
            {
                var cur = child.position - bone.position;
                if (cur.sqrMagnitude > 1e-8f)
                    bone.rotation = Quaternion.FromToRotation(cur, pivot.TransformDirection(dir)) * bone.rotation;
            }
        }
    }
}
