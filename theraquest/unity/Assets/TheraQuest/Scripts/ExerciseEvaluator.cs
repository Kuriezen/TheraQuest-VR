using System;
using System.Collections.Generic;
using UnityEngine;

namespace TheraQuest
{
    public enum LiveState { Idle, Rising, Holding, OnTarget, TooFar, Unsafe, Fault, Resting }

    /// <summary>
    /// Exercise Evaluation Module (Section 3.6.3):
    ///   Movement Validator, Accuracy Scorer, Rep Counter and Error Detector.
    ///
    /// A rep starts when the angle rises above 60% of the target and ends when it
    /// falls back below 25%. The highest angle in between (the peak) is scored:
    ///   Angle Error = |target - peak|
    ///   within tolerance  -> accuracy 85..100, rep is valid
    ///   outside tolerance -> accuracy drops 3% per extra degree, rep not counted
    /// The rep is also not counted when:
    ///   - a posture fault happened during it
    ///   - the angle went past the safety max (protocol limit; a STOP warning shows)
    ///   - the hold time is set and the patient didn't stay inside the target band long enough
    /// After every set (reps per set) there is a rest countdown, during which reps are ignored.
    /// </summary>
    public class ExerciseEvaluator
    {
        const float HoldGrace = 0.3f;   // seconds the angle may jitter out of the band without resetting the hold

        public ExerciseDef Exercise { get; private set; }
        public int ValidReps { get; private set; }
        public int Attempts { get; private set; }
        public int UnsafeReps { get; private set; }
        public LiveState State { get; private set; } = LiveState.Idle;
        public float CurrentAngle { get; private set; }
        public float HoldTime { get; private set; }       // current continuous hold in the band
        public float RestRemaining { get; private set; }
        public readonly List<int> Accuracies = new List<int>();
        public readonly List<float> Errors = new List<float>();
        public readonly List<string> FeedbackLog = new List<string>();

        public event Action<RepResult> RepCompleted;

        bool inRep, pastMax;
        float peak, bestHold, outOfBand;
        string repFault;

        public ExerciseEvaluator(ExerciseDef ex) { Exercise = ex; }

        public int Sets => Mathf.Max(1, Exercise.sets);
        public bool Done => ValidReps >= Exercise.TotalReps;
        public bool Resting => RestRemaining > 0f;
        /// <summary>1-based set the patient is on.</summary>
        public int SetNumber => Mathf.Min(ValidReps / Mathf.Max(1, Exercise.reps) + 1, Sets);
        public int RepsInSet => Done ? Exercise.reps : ValidReps % Mathf.Max(1, Exercise.reps);

        public void Update(float angle, string fault, float dt)
        {
            CurrentAngle = angle;
            if (Resting)
            {
                RestRemaining = Mathf.Max(0f, RestRemaining - dt);
                State = Resting ? LiveState.Resting : LiveState.Idle;
                return;
            }

            float target = Exercise.targetAngle, tol = Exercise.tolerance, max = Exercise.safetyMax;
            float up = target * 0.6f, down = target * 0.25f;

            if (!inRep)
            {
                State = LiveState.Idle;
                if (angle > up)
                {
                    inRep = true;
                    peak = angle;
                    repFault = fault;
                    pastMax = false;
                    HoldTime = bestHold = outOfBand = 0f;
                }
                return;
            }

            peak = Mathf.Max(peak, angle);
            if (fault != null && repFault == null) repFault = fault;
            bool overMax = max > 0f && angle > max;
            if (overMax) pastMax = true;

            // Hold timer: time spent continuously inside target ± tolerance with good posture
            bool inBand = angle >= target - tol && angle <= target + tol && fault == null;
            if (inBand) { HoldTime += dt; outOfBand = 0f; }
            else if ((outOfBand += dt) > HoldGrace) HoldTime = 0f;
            bestHold = Mathf.Max(bestHold, HoldTime);

            if (overMax) State = LiveState.Unsafe;
            else if (fault != null) State = LiveState.Fault;
            else if (angle > target + tol) State = LiveState.TooFar;
            else if (angle >= target - tol) State = HoldTime < Exercise.holdSeconds ? LiveState.Holding : LiveState.OnTarget;
            else State = LiveState.Rising;

            if (angle < down)
            {
                inRep = false;
                FinishRep();
            }
        }

        void FinishRep()
        {
            float target = Exercise.targetAngle, tol = Exercise.tolerance;
            float err = Mathf.Abs(peak - target);
            int acc = err <= tol
                ? Mathf.RoundToInt(100f - (err / tol) * 15f)
                : Mathf.RoundToInt(Mathf.Max(0f, 85f - (err - tol) * 3f));
            bool held = Exercise.holdSeconds <= 0f || bestHold >= Exercise.holdSeconds;
            var r = new RepResult
            {
                peak = peak,
                error = err,
                accuracy = acc,
                overshoot = peak > target + tol,
                pastSafetyMax = pastMax,
                held = bestHold,
                heldLongEnough = held,
                fault = repFault,
                valid = err <= tol && repFault == null && !pastMax && held
            };
            Attempts++;
            if (r.valid) ValidReps++;
            if (pastMax) UnsafeReps++;
            Accuracies.Add(acc);
            Errors.Add(err);
            if (!r.valid) FeedbackLog.Add(Describe(r));
            HoldTime = 0f;
            State = LiveState.Idle;
            if (r.valid && !Done && ValidReps % Mathf.Max(1, Exercise.reps) == 0 && Exercise.restSeconds > 0f)
            {
                RestRemaining = Exercise.restSeconds;
                State = LiveState.Resting;
            }
            RepCompleted?.Invoke(r);
        }

        public string Describe(RepResult r)
        {
            float tol = Exercise.tolerance, target = Exercise.targetAngle;
            if (r.pastSafetyMax) return $"Went past the safe limit of {Exercise.safetyMax:0}°";
            if (r.fault != null) return r.fault;
            if (r.overshoot) return $"Overshot by {Mathf.RoundToInt(r.peak - target - tol)}°";
            if (r.error > tol) return $"Short by {Mathf.RoundToInt(target - tol - r.peak)}°";
            if (!r.heldLongEnough) return $"Hold for {Exercise.holdSeconds:0.#} s at the top (held {r.held:0.0} s)";
            return "Good rep";
        }

        /// <summary>Simple, encouraging version of <see cref="Describe"/> for the patient (no numbers).</summary>
        public string Friendly(RepResult r)
        {
            if (r.pastSafetyMax) return "That went too far. Stay inside the safe zone";
            if (r.fault != null) return r.fault;
            if (r.overshoot) return "A bit too far. Stop at the star";
            if (r.error > Exercise.tolerance) return "Go a little higher next time";
            if (!r.heldLongEnough) return "Hold it at the top a little longer";
            return "Good rep";
        }

        public ExerciseResult ToResult()
        {
            float avgAcc = 0f, avgErr = 0f;
            foreach (var a in Accuracies) avgAcc += a;
            foreach (var e in Errors) avgErr += e;
            if (Accuracies.Count > 0) { avgAcc /= Accuracies.Count; avgErr /= Errors.Count; }
            return new ExerciseResult
            {
                exercise_id = Exercise.id,
                target_reps = Exercise.TotalReps,
                valid_reps = ValidReps,
                attempts = Attempts,
                avg_accuracy = Mathf.Round(avgAcc * 10f) / 10f,
                mean_angle_error = Mathf.Round(avgErr * 10f) / 10f,
                unsafe_reps = UnsafeReps,
                feedback = new List<string>(FeedbackLog)
            };
        }
    }
}
