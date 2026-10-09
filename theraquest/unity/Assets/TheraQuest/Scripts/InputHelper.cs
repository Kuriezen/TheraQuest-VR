using UnityEngine;
using UnityEngine.XR;
using XRUsages = UnityEngine.XR.CommonUsages;
using XRInputDevices = UnityEngine.XR.InputDevices;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace TheraQuest
{
    /// <summary>
    /// One place for all controls, so the app works with either Unity input system
    /// and with Quest controllers (via Quest Link) without extra packages.
    ///
    ///   Action            Keyboard      Quest controller
    ///   Start / confirm   Space         A (right) or right trigger
    ///   End session       Esc           B (right)
    ///   Next input mode   Tab           X (left)
    ///   Recenter view     R             Y (left)
    ///   Angle up / down   Up / Down     right thumbstick up / down  (keyboard mode only)
    ///   Pain score - / +  Left / Right  right thumbstick left / right (pain check-in)
    ///
    /// SessionManager calls <see cref="Tick"/> once at the start of every frame.
    /// </summary>
    public static class InputHelper
    {
        enum B { Start, End, Source, Recenter, Left, Right, Count }
        static readonly bool[] now = new bool[(int)B.Count];
        static readonly bool[] before = new bool[(int)B.Count];

        public static bool StartPressed => Pressed(B.Start);
        public static bool EndPressed => Pressed(B.End);
        public static bool NextSourcePressed => Pressed(B.Source);
        public static bool RecenterPressed => Pressed(B.Recenter);
        public static bool LeftPressed => Pressed(B.Left);
        public static bool RightPressed => Pressed(B.Right);
        public static float AngleAxis { get; private set; }   // -1..1

        static bool Pressed(B b) => now[(int)b] && !before[(int)b];

        public static void Tick()
        {
            for (int i = 0; i < now.Length; i++) before[i] = now[i];
            now[(int)B.Start] = Key(KeyCode.Space) || Xr(XRNode.RightHand, XRUsages.primaryButton) || Xr(XRNode.RightHand, XRUsages.triggerButton);
            now[(int)B.End] = Key(KeyCode.Escape) || Xr(XRNode.RightHand, XRUsages.secondaryButton);
            now[(int)B.Source] = Key(KeyCode.Tab) || Xr(XRNode.LeftHand, XRUsages.primaryButton);
            now[(int)B.Recenter] = Key(KeyCode.R) || Xr(XRNode.LeftHand, XRUsages.secondaryButton);

            float axis = 0f;
            if (Key(KeyCode.UpArrow)) axis += 1f;
            if (Key(KeyCode.DownArrow)) axis -= 1f;
            var right = XRInputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            Vector2 stick = Vector2.zero;
            if (right.isValid) right.TryGetFeatureValue(XRUsages.primary2DAxis, out stick);
            if (Mathf.Abs(stick.y) > 0.2f) axis = stick.y;
            AngleAxis = Mathf.Clamp(axis, -1f, 1f);
            now[(int)B.Left] = Key(KeyCode.LeftArrow) || stick.x < -0.6f;
            now[(int)B.Right] = Key(KeyCode.RightArrow) || stick.x > 0.6f;
        }

        static bool Key(KeyCode k)
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb == null) return false;
            switch (k)
            {
                case KeyCode.Space: return kb.spaceKey.isPressed;
                case KeyCode.Escape: return kb.escapeKey.isPressed;
                case KeyCode.Tab: return kb.tabKey.isPressed;
                case KeyCode.R: return kb.rKey.isPressed;
                case KeyCode.UpArrow: return kb.upArrowKey.isPressed;
                case KeyCode.DownArrow: return kb.downArrowKey.isPressed;
                case KeyCode.LeftArrow: return kb.leftArrowKey.isPressed;
                case KeyCode.RightArrow: return kb.rightArrowKey.isPressed;
                default: return false;
            }
#else
            return Input.GetKey(k);
#endif
        }

        static bool Xr(XRNode node, InputFeatureUsage<bool> usage)
        {
            var dev = XRInputDevices.GetDeviceAtXRNode(node);
            return dev.isValid && dev.TryGetFeatureValue(usage, out bool v) && v;
        }
    }
}
