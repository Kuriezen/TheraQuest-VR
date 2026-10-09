using UnityEngine;

namespace TheraQuest
{
    public enum InputSource { Webcam, Keyboard, Simulated }

    /// <summary>
    /// Gives the rest of the system one angle reading per frame, whatever the source:
    ///   Webcam    - live angles from tracker.py (MotionReceiver), for the exercise's operated side
    ///   Keyboard  - arrow keys / thumbstick move the limb (testing without a camera)
    ///   Simulated - an automatic patient with natural variation and occasional form errors
    /// Also hosts the posture checks of the Error Detector.
    /// </summary>
    public class MotionInput : MonoBehaviour
    {
        public MotionReceiver receiver;
        public InputSource source = InputSource.Webcam;

        [Header("Posture limits (Error Detector)")]
        public float maxTrunkLean = 12f;     // degrees from vertical: shoulder exercises, hip abduction, march
        public float maxElbowDrift = 30f;    // upper arm away from trunk during elbow flexion
        public float maxKneeBend = 15f;      // knee must stay straight during a straight leg raise
        public float maxSquatLean = 35f;     // forward trunk lean allowed in a mini squat

        [Header("Smoothing")]
        [Range(0f, 0.95f)] public float smoothing = 0.6f;

        public float Angle { get; private set; }
        public string Fault { get; private set; }
        public bool HasReading { get; private set; }
        /// <summary>Side being measured: the exercise's operated side, or the side the camera sees best.</summary>
        public string MeasuredSide { get; private set; }

        float keyboardAngle;
        float simTime, simPeak = 90f, simPeriod, simPlateau;
        bool simStarted, simFault;

        public void NextSource()
        {
            source = (InputSource)(((int)source + 1) % 3);
            keyboardAngle = 0f;
        }

        /// <summary>Corrective message for each exercise's posture check (null = no check).</summary>
        public static string FaultText(string code)
        {
            switch (code)
            {
                case "shoulderAbd": case "shoulderFlex": case "scaption": case "wallSlide": return "Keep your trunk upright";
                case "elbowFlex": return "Keep your elbow at your side";
                case "hipAbd": return "Don't lean to the side";
                case "march": return "Stand tall, don't lean back";
                case "slr": return "Keep your knee straight";
                case "miniSquat": return "Keep your chest up";
                default: return null;
            }
        }

        bool PostureFault(string code, MotionPacket p, AngleSet s)
        {
            switch (code)
            {
                case "shoulderAbd": case "shoulderFlex": case "scaption": case "wallSlide":
                case "hipAbd": case "march":
                    return p.trunkLean > maxTrunkLean;
                case "elbowFlex": return (s == p ? p.elbowDrift : s.shoulderElev) > maxElbowDrift;
                case "slr": return s.kneeFlex > maxKneeBend;
                case "miniSquat": return p.trunkLean > maxSquatLean;
                default: return false;
            }
        }

        public void Sample(ExerciseDef ex, float dt)
        {
            float raw = 0f;
            Fault = null;
            HasReading = true;
            string code = ex != null ? ex.code : "shoulderAbd";
            string measure = ex != null && !string.IsNullOrEmpty(ex.measure) ? ex.measure : "shoulderElev";
            float target = ex != null ? ex.targetAngle : 90f;

            switch (source)
            {
                case InputSource.Webcam:
                    var p = receiver != null ? receiver.Latest : null;
                    var s = p?.For(ex != null ? ex.side : "");
                    MeasuredSide = ex != null && !string.IsNullOrEmpty(ex.side) ? ex.side : p?.side;
                    if (receiver == null || !receiver.IsReceiving || p == null || !p.visible || s.Get(measure) < 0f)
                    {
                        HasReading = false;
                        return;
                    }
                    raw = s.Get(measure);
                    if (PostureFault(code, p, s)) Fault = FaultText(code);
                    break;

                case InputSource.Keyboard:
                    keyboardAngle = Mathf.Clamp(keyboardAngle + InputHelper.AngleAxis * 70f * dt, 0f, 170f);
                    raw = keyboardAngle;
                    break;

                case InputSource.Simulated:
                    // One rep = rise, hold at the peak, lower. Each rep gets a new peak and maybe a fault,
                    // a short hold, or (rarely) a movement past the safety max.
                    const float rise = 1.2f;
                    simTime += dt;
                    if (!simStarted || simTime >= simPeriod)
                    {
                        simStarted = true;
                        simTime = 0f;
                        float tol = ex != null ? ex.tolerance : 10f;
                        float noise = (Random.value + Random.value + Random.value - 1.5f) * tol * 1.4f;
                        simPeak = Mathf.Clamp(target + noise, 5f, 170f);
                        if (ex != null && ex.safetyMax > 0f && Random.value < 0.04f) simPeak = ex.safetyMax + 5f;
                        simFault = FaultText(code) != null && Random.value < 0.12f;
                        float hold = ex != null ? ex.holdSeconds : 0f;
                        simPlateau = hold > 0f ? (Random.value < 0.1f ? hold * 0.5f : hold + 0.4f) : 0.15f;
                        simPeriod = rise * 2f + simPlateau;
                    }
                    float k = simTime < rise ? Ease(simTime / rise)
                            : simTime < rise + simPlateau ? 1f
                            : Ease(1f - (simTime - rise - simPlateau) / rise);
                    raw = simPeak * k;
                    if (simFault && simTime > rise * 0.7f && simTime < rise + simPlateau) Fault = FaultText(code);
                    break;
            }

            float sm = source == InputSource.Webcam ? smoothing : 0f;
            Angle = Mathf.Lerp(raw, Angle, sm);
        }

        static float Ease(float x) => 0.5f - 0.5f * Mathf.Cos(Mathf.Clamp01(x) * Mathf.PI);

        public string SourceLabel => source == InputSource.Webcam ? "webcam" : source == InputSource.Keyboard ? "keyboard" : "simulated";
    }
}
