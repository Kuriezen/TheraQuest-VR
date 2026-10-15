using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace TheraQuest
{
    /// <summary>
    /// Minimal VR camera rig that needs no extra packages.
    /// With a headset on Quest Link, it follows your head. Without one, the camera
    /// stays at seated/standing eye height so the app runs on the laptop screen.
    /// Put this on the Main Camera, which should be a child of an empty "Rig" object.
    /// </summary>
    public class HeadTracking : MonoBehaviour
    {
        public float desktopEyeHeight = 1.55f;
        /// <summary>Look up / down in desktop mode (set by PlayerMover from the mouse).</summary>
        [HideInInspector] public float desktopPitch;
        public bool HeadsetActive { get; private set; }

        Transform rig;
        static readonly List<XRInputSubsystem> subsystems = new List<XRInputSubsystem>();
        bool originSet;

        void Awake() { rig = transform.parent; }
        void OnEnable() { Application.onBeforeRender += Apply; }
        void OnDisable() { Application.onBeforeRender -= Apply; }
        void Update() { Apply(); }

        void Apply()
        {
            var head = InputDevices.GetDeviceAtXRNode(XRNode.CenterEye);
            if (!head.isValid) head = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            HeadsetActive = head.isValid && XRSettings.isDeviceActive;

            if (!HeadsetActive)
            {
                transform.localPosition = new Vector3(0, desktopEyeHeight, 0);
                transform.localRotation = Quaternion.Euler(desktopPitch, 0, 0);
                return;
            }

            if (!originSet)
            {
                SubsystemManager.GetSubsystems(subsystems);
                foreach (var s in subsystems) s.TrySetTrackingOriginMode(TrackingOriginModeFlags.Floor);
                originSet = true;
            }

            if (head.TryGetFeatureValue(CommonUsages.centerEyePosition, out Vector3 pos) ||
                head.TryGetFeatureValue(CommonUsages.devicePosition, out pos))
                transform.localPosition = pos;
            if (head.TryGetFeatureValue(CommonUsages.centerEyeRotation, out Quaternion rot) ||
                head.TryGetFeatureValue(CommonUsages.deviceRotation, out rot))
                transform.localRotation = rot;
        }

        /// <summary>Turn the rig so the patient faces the exercise area from where they stand now.</summary>
        public void Recenter()
        {
            if (rig == null || !HeadsetActive) return;
            float yaw = transform.localEulerAngles.y;
            rig.rotation = Quaternion.Euler(0, -yaw, 0);
            Vector3 headWorld = transform.position;
            rig.position -= new Vector3(headWorld.x, 0, headWorld.z);
        }
    }
}
