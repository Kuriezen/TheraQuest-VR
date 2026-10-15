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
    ///   Action              Keyboard / mouse           Quest controller
    ///   Start / select      Space or Enter             A (right) or right trigger
    ///   Pause / back        Esc, P or Backspace        B (right) or Menu (left)
    ///   Menu: choose        Up / Down arrows           either thumbstick up / down
    ///   Menu: change value  Left / Right arrows        either thumbstick left / right
    ///   Walk                W A S D (Shift = faster)   left thumbstick
    ///   Turn                Q / E, Left / Right, or    right thumbstick left / right
    ///                       hold the right mouse button and move the mouse to look around
    ///   Back to the station R                          Y (left)
    ///   Next input mode     Tab                        X (left)
    ///   Show / hide quest   M                          left thumbstick click
    ///   Angle up / down     Up / Down arrows           right thumbstick up / down (keyboard input mode)
    ///
    /// SessionManager calls <see cref="Tick"/> once at the start of every frame.
    /// </summary>
    public static class InputHelper
    {
        enum B { Start, Back, Source, Recenter, Left, Right, Up, Down, Quest, Count }
        static readonly bool[] now = new bool[(int)B.Count];
        static readonly bool[] before = new bool[(int)B.Count];

        public static bool StartPressed => Pressed(B.Start);
        public static bool BackPressed => Pressed(B.Back);
        public static bool NextSourcePressed => Pressed(B.Source);
        public static bool RecenterPressed => Pressed(B.Recenter);
        public static bool QuestTogglePressed => Pressed(B.Quest);
        public static bool LeftPressed => Pressed(B.Left);
        public static bool RightPressed => Pressed(B.Right);
        public static bool UpPressed => Pressed(B.Up);
        public static bool DownPressed => Pressed(B.Down);
        public static float AngleAxis { get; private set; }   // -1..1, keyboard input mode
        public static Vector2 Move { get; private set; }      // -1..1, x = strafe, y = forward
        public static float Turn { get; private set; }        // -1..1
        public static Vector2 Look { get; private set; }      // mouse movement in pixels while the right button is held
        public static bool Sprint { get; private set; }

        static bool Pressed(B b) => now[(int)b] && !before[(int)b];

        public static void Tick()
        {
            for (int i = 0; i < now.Length; i++) before[i] = now[i];
            now[(int)B.Start] = Key(KeyCode.Space) || Key(KeyCode.Return) || Xr(XRNode.RightHand, XRUsages.primaryButton) || Xr(XRNode.RightHand, XRUsages.triggerButton);
            now[(int)B.Back] = Key(KeyCode.Escape) || Key(KeyCode.P) || Key(KeyCode.Backspace) ||
                               Xr(XRNode.RightHand, XRUsages.secondaryButton) || Xr(XRNode.LeftHand, XRUsages.menuButton);
            now[(int)B.Source] = Key(KeyCode.Tab) || Xr(XRNode.LeftHand, XRUsages.primaryButton);
            now[(int)B.Recenter] = Key(KeyCode.R) || Xr(XRNode.LeftHand, XRUsages.secondaryButton);
            now[(int)B.Quest] = Key(KeyCode.M) || Xr(XRNode.LeftHand, XRUsages.primary2DAxisClick);

            Vector2 rs = Stick(XRNode.RightHand), ls = Stick(XRNode.LeftHand);

            float axis = 0f;
            if (Key(KeyCode.UpArrow)) axis += 1f;
            if (Key(KeyCode.DownArrow)) axis -= 1f;
            if (Mathf.Abs(rs.y) > 0.2f) axis = rs.y;
            AngleAxis = Mathf.Clamp(axis, -1f, 1f);

            now[(int)B.Left] = Key(KeyCode.LeftArrow) || rs.x < -0.6f || ls.x < -0.6f;
            now[(int)B.Right] = Key(KeyCode.RightArrow) || rs.x > 0.6f || ls.x > 0.6f;
            now[(int)B.Up] = Key(KeyCode.UpArrow) || rs.y > 0.6f || ls.y > 0.6f;
            now[(int)B.Down] = Key(KeyCode.DownArrow) || rs.y < -0.6f || ls.y < -0.6f;

            var move = new Vector2((Key(KeyCode.D) ? 1f : 0f) - (Key(KeyCode.A) ? 1f : 0f),
                                   (Key(KeyCode.W) ? 1f : 0f) - (Key(KeyCode.S) ? 1f : 0f));
            if (ls.sqrMagnitude > 0.04f) move = ls;
            Move = Vector2.ClampMagnitude(move, 1f);

            float turn = (Key(KeyCode.E) || Key(KeyCode.RightArrow) ? 1f : 0f) - (Key(KeyCode.Q) || Key(KeyCode.LeftArrow) ? 1f : 0f);
            if (Mathf.Abs(rs.x) > 0.2f) turn = rs.x;
            Turn = Mathf.Clamp(turn, -1f, 1f);
            Sprint = Key(KeyCode.LeftShift);
            Look = MouseLook();
        }

        static Vector2 Stick(XRNode node)
        {
            var dev = XRInputDevices.GetDeviceAtXRNode(node);
            return dev.isValid && dev.TryGetFeatureValue(XRUsages.primary2DAxis, out Vector2 v) ? v : Vector2.zero;
        }

        static Vector2 MouseLook()
        {
#if ENABLE_INPUT_SYSTEM
            var m = Mouse.current;
            return m != null && m.rightButton.isPressed ? m.delta.ReadValue() : Vector2.zero;
#else
            return Input.GetMouseButton(1) ? new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y")) * 10f : Vector2.zero;
#endif
        }

        static bool Key(KeyCode k)
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb == null) return false;
            switch (k)
            {
                case KeyCode.Space: return kb.spaceKey.isPressed;
                case KeyCode.Return: return kb.enterKey.isPressed || kb.numpadEnterKey.isPressed;
                case KeyCode.Escape: return kb.escapeKey.isPressed;
                case KeyCode.Backspace: return kb.backspaceKey.isPressed;
                case KeyCode.Tab: return kb.tabKey.isPressed;
                case KeyCode.LeftShift: return kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
                case KeyCode.UpArrow: return kb.upArrowKey.isPressed;
                case KeyCode.DownArrow: return kb.downArrowKey.isPressed;
                case KeyCode.LeftArrow: return kb.leftArrowKey.isPressed;
                case KeyCode.RightArrow: return kb.rightArrowKey.isPressed;
                case KeyCode.W: return kb.wKey.isPressed;
                case KeyCode.A: return kb.aKey.isPressed;
                case KeyCode.S: return kb.sKey.isPressed;
                case KeyCode.D: return kb.dKey.isPressed;
                case KeyCode.Q: return kb.qKey.isPressed;
                case KeyCode.E: return kb.eKey.isPressed;
                case KeyCode.P: return kb.pKey.isPressed;
                case KeyCode.R: return kb.rKey.isPressed;
                case KeyCode.M: return kb.mKey.isPressed;
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
