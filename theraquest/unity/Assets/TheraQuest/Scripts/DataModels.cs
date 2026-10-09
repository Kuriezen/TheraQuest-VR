using System;
using System.Collections.Generic;

namespace TheraQuest
{
    // ---------------------------------------------------------------
    // Packet sent by tracker.py over UDP (must match the Python field names)
    // ---------------------------------------------------------------
    /// <summary>The angles the tracker measures (see tracker/angles.py). -1 = not visible.</summary>
    [Serializable]
    public class AngleSet
    {
        public float shoulderElev = -1f;
        public float elbowFlex = -1f;
        public float hipElev = -1f;
        public float kneeFlex = -1f;
        public float kneeExt = -1f;

        public float Get(string measure)
        {
            switch (measure)
            {
                case "shoulderElev": return shoulderElev;
                case "elbowFlex": return elbowFlex;
                case "hipElev": return hipElev;
                case "kneeFlex": return kneeFlex;
                case "kneeExt": return kneeExt;
                default: return -1f;
            }
        }
    }

    /// <summary>Top-level angles come from whichever side the camera sees best; L and R hold each side.</summary>
    [Serializable]
    public class MotionPacket : AngleSet
    {
        public float ts;
        public bool visible;
        public string side;
        public float trunkLean;
        public float elbowDrift;
        public AngleSet L = new AngleSet();
        public AngleSet R = new AngleSet();

        /// <param name="operatedSide">"L", "R", or "" for the best-seen side</param>
        public AngleSet For(string operatedSide) => operatedSide == "L" ? L : operatedSide == "R" ? R : this;
    }

    // ---------------------------------------------------------------
    // GET /api/patients/{id}/program
    // ---------------------------------------------------------------
    [Serializable]
    public class PatientInfo
    {
        public int id;
        public string code;
        public string name;
        public int xp;
        public int level;
        public int xpIntoLevel;
        public int xpForNext;
        public string[] badges = new string[0];
        public int streak;
    }

    [Serializable]
    public class ProgramInfo
    {
        public int id;
        public string name;
        public int[] days = new int[0];
        public int maxReps;
    }

    [Serializable]
    public class ExerciseDef
    {
        public int id;
        public string code;       // exercise type from the server LIBRARY (shoulderAbd, slr, miniSquat, ...)
        public string name;
        public string joint;
        public string measure;    // tracker angle: shoulderElev | elbowFlex | hipElev | kneeFlex | kneeExt
        public string view;       // camera position: front | side
        public string position;   // start position
        public string side;       // operated side: L | R | "" = whichever the camera sees best
        public float targetAngle;
        public float tolerance;
        public float safetyMax;   // hard limit; 0 = none
        public float holdSeconds; // hold within the target band for a rep to count; 0 = none
        public int sets = 1;
        public float restSeconds;
        public int baseReps;
        public int reps;          // per set; effective reps for today (includes progression stage)
        public int stage;
        public string cue;

        public int TotalReps => reps * Math.Max(1, sets);
        public string CameraText => view == "front" ? "in front of you" : "at your side";
    }

    /// <summary>The patient's protocol phase. Empty name = custom program without a protocol.</summary>
    [Serializable]
    public class PhaseInfo
    {
        public int id;
        public string protocol = "";
        public string name = "";
        public string weeks = "";
        public bool vrAllowed = true;   // false = immobilization / passive phase, done with the therapist
        public string goals = "";
        public string precautions = "";
    }

    [Serializable]
    public class ProgramResponse
    {
        public PatientInfo patient;
        public ProgramInfo program;
        public PhaseInfo phase = new PhaseInfo();
        public ExerciseDef[] exercises;
    }

    // ---------------------------------------------------------------
    // POST /api/sessions
    // ---------------------------------------------------------------
    [Serializable]
    public class ExerciseResult
    {
        public int exercise_id;
        public int target_reps;
        public int valid_reps;
        public int attempts;
        public float avg_accuracy;
        public float mean_angle_error;
        public int unsafe_reps;
        public List<string> feedback = new List<string>();
    }

    [Serializable]
    public class SessionUpload
    {
        public int patient_id;
        public string started_at;
        public string ended_at;
        public bool ended_early;
        public string input_source;
        public int points;
        public int stars;
        public int pain_before = -1;    // 0-10 check-in; -1 = skipped
        public int pain_after = -1;
        public List<string> badges_earned = new List<string>();
        public List<ExerciseResult> exercises = new List<ExerciseResult>();
    }

    [Serializable]
    public class SessionResponse
    {
        public int session_id;
        public string status;
        public string[] notes = new string[0];
        public string[] new_badges = new string[0];
        public PatientInfo patient;
    }

    // ---------------------------------------------------------------
    // Result of one repetition (Exercise Evaluation Module output)
    // ---------------------------------------------------------------
    public struct RepResult
    {
        public float peak;        // highest angle reached in this rep
        public float error;       // |target - peak|  (Angle Error, Section 3.9.1)
        public int accuracy;      // 0..100
        public bool valid;
        public bool overshoot;
        public bool pastSafetyMax;
        public float held;        // longest hold inside the target band, seconds
        public bool heldLongEnough;
        public string fault;      // posture error, or null
    }
}
