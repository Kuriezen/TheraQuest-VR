using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace TheraQuest
{
    /// <summary>
    /// Runs a rehabilitation session from start to finish (the System Flowchart, Section 3.6.1):
    ///   load program -> wait for Start -> pain check-in (0-10) -> exercise 1..n (evaluate, feedback, score)
    ///   -> pain check-in -> session summary -> upload to server (Adherence Monitoring) -> ready again.
    /// If the patient's protocol phase is not for VR (immobilization or passive motion), no session starts.
    /// </summary>
    public class SessionManager : MonoBehaviour
    {
        const int HighPain = 7;   // pain before the session at or above this shows a caution first

        [Header("References (filled in by TheraQuest > Create Scene)")]
        public ApiClient api;
        public MotionInput motion;
        public MotionReceiver receiver;
        public FeedbackManager feedback;
        public HUDController hud;
        public FigureVisualizer figure;
        public HeadTracking head;

        enum Phase { Loading, Ready, PainBefore, Running, PainAfter, Uploading, Summary }
        Phase phase = Phase.Loading;

        ProgramResponse program;
        bool online, canStart;
        string readyMessage;
        readonly List<ExerciseEvaluator> evaluators = new List<ExerciseEvaluator>();
        int index;
        GamificationManager game;
        DateTime startedAt;
        float repMessageTimer;
        bool awaitingReady;          // patient is getting into the start position for the next exercise
        float autoReadyTimer;        // simulated mode continues on its own
        LiveState lastState;

        int painValue, painBefore = -1;
        bool painWarned;
        float painAutoTimer;
        SessionUpload pendingUpload;
        bool pendingCompleted;
        int sumValid, sumAttempts, sumUnsafe;
        float sumAcc, sumErr;

        ExerciseDef Current => program != null && program.exercises.Length > 0 ? program.exercises[Mathf.Clamp(index, 0, program.exercises.Length - 1)] : null;
        ExerciseEvaluator CurrentEval => phase == Phase.Running && index < evaluators.Count ? evaluators[index] : null;
        bool Simulated => motion.source == InputSource.Simulated;

        IEnumerator Start()
        {
            hud.SetFeedback("Loading your program…", Tone.Neutral);
            yield return LoadProgram();
        }

        IEnumerator LoadProgram()
        {
            phase = Phase.Loading;
            yield return api.LoadProgram((p, ok) => { program = p; online = ok; });
            if (program.phase == null) program.phase = new PhaseInfo();
            if (program.exercises == null) program.exercises = new ExerciseDef[0];
            index = 0;
            evaluators.Clear();
            hud.SetPatient(program.patient, program.program, online, program.phase);
            hud.SetQuest(program.exercises, null, 0, false);

            canStart = program.phase.vrAllowed && program.exercises.Length > 0;
            if (!program.phase.vrAllowed)
                readyMessage = $"{program.phase.name} ({program.phase.weeks}) is done with your therapist.\n<size=36>No VR session in this phase.</size>";
            else if (program.exercises.Length == 0)
                readyMessage = "No exercises prescribed yet. Ask your therapist.";
            else
                readyMessage = online ? "Press Start (Space / A) to begin today's quest"
                                      : "Server offline. Using default program. Press Start to begin";
            hud.SetFeedback(readyMessage, canStart ? Tone.Neutral : Tone.Warn);
            phase = Phase.Ready;
        }

        void Update()
        {
            InputHelper.Tick();
            float dt = Time.deltaTime;

            if (InputHelper.NextSourcePressed) motion.NextSource();
            if (InputHelper.RecenterPressed && head != null) head.Recenter();

            var ex = Current;
            motion.Sample(ex, dt);
            UpdateSourceText();

            switch (phase)
            {
                case Phase.Ready:
                    if (InputHelper.StartPressed && canStart) OpenPain(Phase.PainBefore, Simulated ? UnityEngine.Random.Range(0, 4) : 0);
                    break;

                case Phase.PainBefore:
                    if (InputHelper.EndPressed)
                    {
                        hud.HidePain();
                        hud.SetFeedback(readyMessage, Tone.Neutral);
                        phase = Phase.Ready;
                    }
                    else if (PainPicked(dt))
                    {
                        if (painValue >= HighPain && !painWarned) { painWarned = true; ShowPainPicker(); break; }
                        painBefore = painValue;
                        hud.HidePain();
                        BeginSession();
                    }
                    break;

                case Phase.Running:
                    if (InputHelper.EndPressed) { FinishSession(endedEarly: true); break; }
                    if (awaitingReady)
                    {
                        if (InputHelper.StartPressed || (Simulated && (autoReadyTimer -= dt) <= 0f))
                        {
                            awaitingReady = false;
                            ShowRepMessage(ex.cue, Tone.Neutral, 2f);
                        }
                        else hud.SetFeedback($"{ex.name}: {ex.position}\n<size=36>Camera {ex.CameraText} · press Start when ready</size>", Tone.Neutral);
                        break;
                    }
                    var ev = CurrentEval;
                    if (motion.HasReading || ev.Resting) ev.Update(motion.Angle, motion.Fault, dt);
                    if (ev.State == LiveState.Unsafe)
                    {
                        if (lastState != LiveState.Unsafe) feedback.PlayWarning();
                        repMessageTimer = 0f;   // the STOP warning always shows right away
                    }
                    lastState = ev.State;
                    if (repMessageTimer > 0f) repMessageTimer -= dt;
                    else if (!motion.HasReading && !ev.Resting)
                        hud.SetFeedback($"Step into view so your {ex.joint.ToLower()} is visible", Tone.Warn);
                    else
                    {
                        var (msg, tone) = FeedbackManager.LiveMessage(ev, motion.Fault);
                        hud.SetFeedback(msg, tone);
                    }
                    break;

                case Phase.PainAfter:
                    if (InputHelper.EndPressed) { hud.HidePain(); Upload(-1); }      // skip the check-in
                    else if (PainPicked(dt)) { hud.HidePain(); Upload(painValue); }
                    break;

                case Phase.Summary:
                    if (InputHelper.StartPressed) { hud.HideSummary(); StartCoroutine(LoadProgram()); }
                    break;
            }

            var cur = CurrentEval;
            Tone figTone = cur == null ? Tone.Neutral :
                cur.State == LiveState.OnTarget || cur.State == LiveState.Holding ? Tone.Good :
                cur.State == LiveState.TooFar || cur.State == LiveState.Fault || cur.State == LiveState.Unsafe ? Tone.Bad : Tone.Neutral;
            figure.Show(ex, motion.Angle, figTone);
            hud.SetExercise(ex, cur, motion.Angle, game != null ? game.Points : 0, game != null ? game.Multiplier : 1f);
        }

        void UpdateSourceText()
        {
            string status;
            if (motion.source == InputSource.Webcam)
                status = receiver.IsReceiving
                    ? $"<color=#45CC85>● Tracker connected</color> · {receiver.PacketsPerSecond:0} fps · side {motion.MeasuredSide}"
                    : "<color=#F2695D>● No tracker data</color>\nRun  python tracker.py  then step into view.\nOr press Tab / X to switch to keyboard or simulated input.";
            else if (motion.source == InputSource.Keyboard)
                status = "Move the limb with Up / Down arrows or the right thumbstick.";
            else
                status = "A simulated patient is doing the reps, with natural variation and occasional form errors.";
            hud.SetSource($"<b>Input: {motion.SourceLabel}</b>\n{status}\n{(head != null && head.HeadsetActive ? "Headset: on" : "Headset: not detected (desktop view)")}");
        }

        // ------------------------------------------------------------- pain check-in
        void OpenPain(Phase which, int start)
        {
            phase = which;
            painValue = Mathf.Clamp(start, 0, 10);
            painWarned = false;
            painAutoTimer = 2.5f;
            ShowPainPicker();
        }

        void ShowPainPicker()
        {
            bool before = phase == Phase.PainBefore;
            string hint;
            if (painWarned)
                hint = $"<color=#F2695D>{painValue}/10 is high pain. Exercising may not be safe today.</color>\n" +
                       "Press Start to continue gently, or Esc / B to stop and contact your therapist.";
            else
                hint = "Use Left / Right (or the right thumbstick) to choose, then press Start.\n" +
                       (before ? "Esc / B to go back." : "Esc / B to skip.") +
                       (Simulated ? "\n<color=#8EA7A4>Simulated mode picks a value for you.</color>" : "");
            hud.ShowPain(before ? "How much pain do you have right now?" : "How much pain do you have now?", painValue, hint);
        }

        /// <returns>true when the patient confirms the selected value</returns>
        bool PainPicked(float dt)
        {
            int old = painValue;
            if (InputHelper.LeftPressed) painValue = Mathf.Max(0, painValue - 1);
            if (InputHelper.RightPressed) painValue = Mathf.Min(10, painValue + 1);
            if (painValue != old) { painWarned = false; ShowPainPicker(); }
            return InputHelper.StartPressed || (Simulated && !painWarned && (painAutoTimer -= dt) <= 0f);
        }

        // ------------------------------------------------------------- session flow
        void BeginSession()
        {
            evaluators.Clear();
            foreach (var e in program.exercises)
            {
                var ev = new ExerciseEvaluator(e);
                ev.RepCompleted += OnRep;
                evaluators.Add(ev);
            }
            index = 0;
            game = new GamificationManager(program.patient.badges);
            game.BadgeUnlocked += code =>
            {
                feedback.PlayBadge();
                ShowRepMessage("Badge unlocked: " + GamificationManager.BadgeNames[code], Tone.Good, 2f);
            };
            startedAt = DateTime.Now;
            phase = Phase.Running;
            hud.SetQuest(program.exercises, evaluators, index, true);
            PrepareExercise();
        }

        void PrepareExercise()
        {
            awaitingReady = true;
            autoReadyTimer = 2.5f;
            repMessageTimer = 0f;
            lastState = LiveState.Idle;
        }

        void OnRep(RepResult r)
        {
            var ev = CurrentEval;
            int pts = game.ScoreRep(r);
            feedback.PlayRep(r);
            string extra = r.valid && game.Combo > 1 ? $"  ×{game.Multiplier:0.0}" : "";
            hud.Pop($"+{pts}{extra}", r.valid);
            if (r.valid) ShowRepMessage(r.accuracy >= 95 ? $"Excellent! {r.accuracy}%" : $"Good rep · {r.accuracy}%", Tone.Good, 1.2f);
            else ShowRepMessage("Rep not counted. " + ev.Describe(r), Tone.Bad, 1.8f);

            if (ev.Done)
            {
                if (index < evaluators.Count - 1)
                {
                    index++;
                    PrepareExercise();
                }
                else FinishSession(endedEarly: false);
            }
            hud.SetQuest(program.exercises, evaluators, index, phase == Phase.Running);
        }

        void ShowRepMessage(string text, Tone tone, float seconds)
        {
            hud.SetFeedback(text, tone);
            repMessageTimer = seconds;
        }

        /// <summary>Scores the session, then asks for the pain check-in before uploading.</summary>
        void FinishSession(bool endedEarly)
        {
            bool allDone = true;
            foreach (var e in evaluators) allDone &= e.Done;
            pendingCompleted = !endedEarly && allDone;
            if (pendingCompleted) game.AddCompletionBonus();

            pendingUpload = new SessionUpload
            {
                patient_id = program.patient.id,
                started_at = startedAt.ToString("yyyy-MM-ddTHH:mm:ss"),
                ended_at = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
                ended_early = endedEarly,
                input_source = motion.SourceLabel,
                points = game.Points,
                pain_before = painBefore,
                badges_earned = new List<string>(game.EarnedThisSession)
            };
            float accSum = 0; int accN = 0; float errSum = 0;
            sumValid = sumAttempts = sumUnsafe = 0;
            foreach (var e in evaluators)
            {
                pendingUpload.exercises.Add(e.ToResult());
                foreach (var a in e.Accuracies) { accSum += a; accN++; }
                foreach (var x in e.Errors) errSum += x;
                sumValid += e.ValidReps; sumAttempts += e.Attempts; sumUnsafe += e.UnsafeReps;
            }
            sumAcc = accN > 0 ? accSum / accN : 0f;
            sumErr = accN > 0 ? errSum / accN : 0f;
            pendingUpload.stars = GamificationManager.Stars(sumAcc, pendingCompleted);

            hud.SetFeedback(pendingCompleted ? "Quest cleared! One last question" : "Session ended. One last question", Tone.Neutral);
            int start = painBefore >= 0 ? painBefore : 0;
            if (Simulated) start = Mathf.Clamp(start + UnityEngine.Random.Range(0, 3), 0, 10);
            OpenPain(Phase.PainAfter, start);
        }

        void Upload(int painAfter)
        {
            phase = Phase.Uploading;
            var upload = pendingUpload;
            bool completed = pendingCompleted;
            upload.pain_after = painAfter;

            hud.SetFeedback("Saving session…", Tone.Neutral);
            StartCoroutine(api.UploadSession(upload, resp =>
            {
                var sb = new StringBuilder();
                sb.AppendLine($"Valid reps   <b>{sumValid} / {sumAttempts}</b>");
                sb.AppendLine($"Accuracy     <b>{sumAcc:0}%</b>");
                sb.AppendLine($"Mean angle error   <b>{sumErr:0.0}°</b>");
                if (sumUnsafe > 0) sb.AppendLine($"<color=#F2695D>Past the safe limit   <b>{sumUnsafe} reps</b> (your therapist will see this)</color>");
                if (upload.pain_before >= 0 && upload.pain_after >= 0)
                {
                    int up = upload.pain_after - upload.pain_before;
                    sb.AppendLine($"Pain         <b>{upload.pain_before} → {upload.pain_after}</b>");
                    if (up > 2) sb.AppendLine($"<color=#F2695D>Your pain went up by {up} points. Rest, and tell your therapist if it hasn't settled within 2 hours.</color>");
                }
                sb.AppendLine($"Points       <b>{game.Points}</b>" + (completed ? $"  (includes +{GamificationManager.CompletionBonus} completion bonus)" : ""));
                sb.AppendLine();
                if (resp != null)
                {
                    bool levelUp = false;
                    foreach (var n in resp.notes) { sb.AppendLine("<color=#F5C451>• " + n + "</color>"); levelUp |= n.StartsWith("Level up"); }
                    if (levelUp) feedback.PlayLevelUp();
                    program.patient = resp.patient;
                    hud.SetPatient(resp.patient, program.program, true, program.phase);
                    sb.AppendLine($"\nSaved as <b>{resp.status}</b>. Your therapist can see it now.");
                }
                else
                {
                    foreach (var b in game.EarnedThisSession) sb.AppendLine("<color=#F5C451>• Badge: " + GamificationManager.BadgeNames[b] + "</color>");
                    sb.AppendLine("\n<color=#F0B43C>Server offline. Saved on this computer; it will upload next time.</color>");
                }
                sb.AppendLine("\nPress Start to continue.");
                hud.ShowSummary(completed ? "Quest cleared!" : "Progress saved", upload.stars, sb.ToString());
                hud.SetFeedback(completed ? "Great work today" : "Session ended early", Tone.Neutral);
                phase = Phase.Summary;
            }));
        }
    }
}
