using UnityEngine;

namespace TheraQuest
{
    /// <summary>
    /// Walk around the clinic, on the laptop or in the headset. Put it on the camera Rig.
    ///   Desktop: W A S D to walk (Shift = faster), Q / E or Left / Right to turn,
    ///            hold the right mouse button and move the mouse to look around.
    ///   Quest:   left thumbstick to walk, right thumbstick to turn (snap 30° or smooth, from Settings).
    /// Walking follows the direction you look. Walls and furniture with colliders block you, and you
    /// stay inside the room. The exercise station is where the rig starts; R / Y brings you back to it.
    /// </summary>
    public class PlayerMover : MonoBehaviour
    {
        public HeadTracking head;
        [Tooltip("Room area you can walk in (x, z on the floor).")]
        public Vector2 roomMin = new Vector2(-4.5f, -2.5f), roomMax = new Vector2(4.5f, 5.0f);
        public float bodyRadius = 0.25f;
        public float mouseSensitivity = 0.15f;

        /// <summary>While true (menus open), the player can't move.</summary>
        [HideInInspector] public bool locked;

        static readonly float[] Speeds = { 0.9f, 1.5f, 2.3f };
        Vector3 stationPos;
        Quaternion stationRot;
        bool snapReady = true;

        Transform Cam => head != null ? head.transform : Camera.main.transform;

        void Awake()
        {
            stationPos = transform.position;
            stationRot = transform.rotation;
        }

        void Update()
        {
            if (locked) return;
            float dt = Time.deltaTime;
            Walk(dt);
            TurnAndLook(dt);
        }

        void Walk(float dt)
        {
            var input = InputHelper.Move;
            if (input.sqrMagnitude < 0.0001f) return;
            var fwd = Cam.forward; fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) fwd = transform.forward;
            fwd.Normalize();
            var right = new Vector3(fwd.z, 0f, -fwd.x);
            float speed = Speeds[Mathf.Clamp(GameSettings.MoveSpeed, 0, Speeds.Length - 1)] * (InputHelper.Sprint ? 1.6f : 1f);
            var delta = (fwd * input.y + right * input.x) * speed * dt;

            // Slide along anything solid between knee and chest height
            var body = new Vector3(Cam.position.x, transform.position.y, Cam.position.z);
            for (int pass = 0; pass < 2 && delta.sqrMagnitude > 1e-8f; pass++)
            {
                var dir = delta.normalized;
                float dist = delta.magnitude;
                if (Hit(body + Vector3.up * 0.45f, dir, dist, out var h) || Hit(body + Vector3.up * 1.2f, dir, dist, out h))
                    delta = Vector3.ProjectOnPlane(delta, new Vector3(h.normal.x, 0f, h.normal.z).normalized) * 0.9f;
                else break;
            }

            var p = transform.position + delta;
            var headOffset = new Vector3(Cam.position.x - transform.position.x, 0f, Cam.position.z - transform.position.z);
            var headAt = p + headOffset;
            headAt.x = Mathf.Clamp(headAt.x, roomMin.x, roomMax.x);
            headAt.z = Mathf.Clamp(headAt.z, roomMin.y, roomMax.y);
            transform.position = headAt - headOffset;
        }

        bool Hit(Vector3 from, Vector3 dir, float dist, out RaycastHit hit) =>
            Physics.SphereCast(from, bodyRadius, dir, out hit, dist + 0.05f, ~0, QueryTriggerInteraction.Ignore)
            && hit.collider.GetComponentInParent<PlayerMover>() == null;

        void TurnAndLook(float dt)
        {
            float turn = InputHelper.Turn;
            bool headset = head != null && head.HeadsetActive;
            if (GameSettings.TurnMode == 0 && headset)
            {
                // Snap turn: one 30° step each time the stick is pushed (comfortable in VR)
                if (Mathf.Abs(turn) > 0.7f && snapReady) { Rotate(Mathf.Sign(turn) * 30f); snapReady = false; }
                else if (Mathf.Abs(turn) < 0.3f) snapReady = true;
            }
            else Rotate(turn * 90f * dt);

            var look = InputHelper.Look;
            if (look.sqrMagnitude > 0f)
            {
                Rotate(look.x * mouseSensitivity);
                if (head != null && !headset) head.desktopPitch = Mathf.Clamp(head.desktopPitch - look.y * mouseSensitivity, -60f, 60f);
            }
        }

        /// <summary>Turn around the head, so turning doesn't move the view sideways.</summary>
        void Rotate(float degrees)
        {
            if (Mathf.Abs(degrees) > 1e-4f) transform.RotateAround(Cam.position, Vector3.up, degrees);
        }

        /// <summary>Back to the exercise station, facing the coach.</summary>
        public void ReturnToStation()
        {
            transform.SetPositionAndRotation(stationPos, stationRot);
            if (head != null)
            {
                head.desktopPitch = 0f;
                head.Recenter();   // in the headset: put your head over the station, facing the coach
            }
        }
    }
}
