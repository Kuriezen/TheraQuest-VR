# TheraQuest VR: working prototype

A gamified VR physical therapy system with webcam motion tracking and adherence monitoring.
It runs on one Windows laptop and shows in a Meta Quest through **Quest Link**, or on the laptop screen when no headset is plugged in.
No Android build is needed.

```
 Webcam ──► tracker.py (Python + MediaPipe)  ──UDP 5005──►  Unity app (VR or desktop)
                                                               │  HTTP 8000
                                                               ▼
                                     server.py (SQLite database) ──► Therapist dashboard (browser)
```

| Folder | What it is | Manuscript module (Section 3.6.3) |
|---|---|---|
| `tracker/` | Webcam → MediaPipe Pose → joint angles → Unity | Motion Tracking |
| `unity/Assets/TheraQuest/` | VR scene, scoring, feedback, game, session flow | VR Environment, Exercise Evaluation, Feedback Generation, Gamification |
| `server/` | Database, API and therapist dashboard | Adherence Monitoring, Data Layer |

---

## 1. Install the tools (one time)

1. **Python 3.11** from python.org. During setup tick **"Add python.exe to PATH"**.
   Use 3.11 or 3.12: MediaPipe may not install on the newest Python yet.
2. **Unity Hub** → install **Unity 6 LTS**. The default modules are enough. You do **not** need Android Build Support.
3. **Meta Quest Link** app for Windows (only for VR). Connect the Quest with a USB-C cable or Air Link.
4. A **webcam**. The laptop's built-in one works for testing. A USB webcam on a tripod is better for real sessions.

## 2. Start the server and dashboard

Double-click **`start_server.bat`**, or run:

```
cd server
python server.py
```

Open **http://localhost:8000**. That's the therapist monitor.
The first run creates `theraquest.db` with a test therapist, patient **P-001** and a 3-exercise program
(shoulder abduction 90°, elbow flexion 130°, seated knee extension 80°, ±10°, 8 reps, Mon/Wed/Fri).
Edit the prescription right on the dashboard. `python server.py --reset` wipes the database.

## 3. Start the motion tracker

First time only:

```
cd tracker
python -m venv .venv
.venv\Scripts\activate
pip install -r requirements.txt
python test_angles.py          # optional: checks the angle math (12 tests should pass)
```

Then, each time, double-click **`start_tracker.bat`**, or run:

```
cd tracker
.venv\Scripts\activate
python tracker.py              # built-in webcam
python tracker.py --camera 1   # USB webcam
python tracker.py --fake       # no camera: sends simulated reps to Unity (to test the link)
```

A preview window shows your skeleton and the live angles. The first run downloads the pose model (about 6 MB).

**Where to put the camera.** The tracker measures angles in 2D, so the camera must see the movement side-on to it:

| Exercise | Camera position |
|---|---|
| Shoulder abduction, scaption | **In front** of the patient, whole upper body in view |
| Shoulder flexion, wall slide | **At the side** of the moving arm |
| Elbow flexion | **At the side** of the moving arm |
| Standing hip abduction | **In front**, whole body in view |
| Straight leg raise, heel slide | **At the side**, patient lying on their back, whole body in view |
| Standing march, mini squat, sit to stand | **At the side**, whole body in view |
| Seated knee extension | **At the side** of the moving leg, patient seated |

Unity shows the start position and camera view before each exercise and waits for Start.

The tracker sends 5 angles (`shoulderElev`, `elbowFlex`, `hipElev`, `kneeFlex`, `kneeExt`) for the best-seen side and for each side separately (`L`, `R`).
When the therapist sets an exercise's operated side, Unity uses that side; otherwise it uses whichever side the camera sees best.

Use good lighting, keep 2–3 m away, and don't stand in front of a window.

## 4. Set up the Unity project

1. Unity Hub → **New project** → **Universal 3D** template. Name it `TheraQuestVR`.
2. Copy the folder `unity/Assets/TheraQuest` into your project's `Assets` folder.
3. Wait for Unity to compile, then use the menu **TheraQuest → Create Scene**.
   This builds the room, the coach figure, the HUD and all the managers, and connects everything.
4. Press **Play**. You'll see the scene on the laptop screen (desktop mode).

**Controls**

| Action | Keyboard | Quest controller |
|---|---|---|
| Start session / continue | Space | A or right trigger |
| End session early | Esc | B |
| Switch input: webcam → keyboard → simulated | Tab | X |
| Move limb (keyboard mode) | Up / Down | Right thumbstick |
| Recenter view | R | Y |

Try **Tab** twice to reach "simulated" mode, then press **Space**. The full session runs by itself, including made-up pain scores, then uploads to the dashboard.

## 5. Show it in the Quest (Quest Link)

1. In the **Meta Quest Link** PC app: Settings → General → **OpenXR Runtime → Set Meta Quest Link as active**.
2. Unity: **Edit → Project Settings → XR Plug-in Management** → click Install if asked.
   On the **Windows** tab, tick **OpenXR** and **Initialize XR on Startup**.
3. Under **XR Plug-in Management → OpenXR**, add the **Oculus Touch Controller Profile** to Interaction Profiles.
   Fix any yellow warnings with the "Fix All" button.
4. Put on the headset, enable **Link** (Quick Settings → Link), then press **Play** in Unity.
   The scene appears in the headset with head tracking. Press **Y** (or R) to recenter.

Without the headset connected, the same Play button falls back to desktop mode.

**Make a standalone .exe:** File → Build Profiles → Windows → Build. Run the .exe while the server and tracker are running.

## 6. A full test run (checklist)

1. `start_server.bat` → dashboard opens.
2. `start_tracker.bat` → preview window shows your skeleton.
3. Unity → Play. The HUD's left panel should say **"Tracker connected · ~30 fps"**.
4. Stand where the camera sees you, press Start, and do the reps.
   - Each rep is scored by **Angle Error = |θexpected − θdetected|**. It counts if it's within the tolerance.
   - Points, combo, badges and stars appear in the HUD. Feedback appears in the banner with tones.
5. When the session finishes, refresh the dashboard. You'll see the session, its accuracy, the adherence calendar and any stage-up.

## 7. Protocols, phases and pain check-in

Rehab protocols are stored as **Protocol → Phases → Exercises** (tables `protocol`, `protocol_phase`, `phase_exercise`).
Two example protocols are loaded on first start: **Total knee replacement** (3 phases) and **Rotator cuff repair, large to massive** (4 phases).
Their numbers are **placeholders** that follow the structure of the published protocols. Replace them with your partner PT's protocol (`PROTOCOLS` in `server.py`, or edit the tables) and get their sign-off.

- **Assign a phase** on the dashboard (Protocol & phase panel). Its exercises replace the prescription, and the therapist can still change any value. "Custom program" leaves the exercises as they are, with no protocol.
- **Phases marked "not for VR"** (immobilization, passive motion) have no exercises. Unity shows "done with your therapist" and won't start a session. TheraQuest is only for patients cleared for active exercise; put this in Scope and Limitations.
- **Pain check-in:** before and after every session, the patient picks 0–10 with Left / Right (or the thumbstick) and presses Start. A score of 7 or more before the session shows a caution first. The dashboard's session log shows "before → after" and highlights increases over 2.
- **Progression check:** the dashboard checks the most recent 3 complete sessions in the phase. Each must have every exercise at ≥ 85% accuracy, pain up by 2 points or less, and no reps past the safety limit. When everything is met it shows **"Ready for Phase II?"**, and nothing changes until the therapist presses **Approve**. These thresholds are columns on `protocol_phase` (`min_sessions`, `min_accuracy`, `max_pain_increase`).
- Every phase change is logged in `phase_history`.

Not measured: how long soreness lasts after a session (the protocols use "under 2 hours"). The patient is told to report it.

## 8. How the rules work (keep these consistent with Chapter 3)

- **Rep counter:** a rep starts above 60% of the target angle and ends below 25%. The peak angle is scored.
- **Accuracy:** within tolerance = 85–100% (linear), outside = 85% minus 3% per extra degree.
- **Posture checks:** trunk lean > 12° (shoulder exercises, hip abduction, march), upper arm > 30° from trunk (elbow flexion), knee bent > 15° (straight leg raise), trunk lean > 35° (mini squat). Any of them invalidates the rep.
- **Safety max:** going past it shows a red STOP warning with a warning tone. The rep isn't counted, and it's logged as unsafe for the therapist (0 = no limit).
- **Hold:** when set, the patient must stay inside target ± tolerance for that many seconds (up to 0.3 s of jitter is forgiven).
- **Sets:** reps are per set. After each set there's a rest countdown, and reps aren't counted during it.
- **Points:** 10 × accuracy × combo (×1.0 to ×2.0); invalid rep = 2; completed session +50.
- **Stars:** 3 at ≥ 90% average accuracy, 2 at ≥ 75%, otherwise 1. Ending early gives 0.
- **Progression:** a completed session with ≥ 85% accuracy and no unsafe reps on an exercise adds +2 reps per set next time, up to the therapist's cap.
- **Adherence status:** *complete* = every exercise reached its rep target; *incomplete* = started but not finished; *missed* = a scheduled day with no session.
- **XP levels:** 100 XP for level 2, then +60 more XP per level.

Thresholds live in `MotionInput.cs` (posture), `ExerciseEvaluator.cs` (scoring), `GamificationManager.cs` (points, badges) and `server.py` (progression, adherence, and the exercise `LIBRARY` defaults). The library's targets, safety limits, holds, sets and rest are **placeholders**. Tune them with your physical therapist and document the final values in your manuscript.

## 9. Troubleshooting

| Problem | Fix |
|---|---|
| HUD says "No tracker data" | Start `tracker.py`. Allow Python **and** Unity through Windows Firewall (Private networks) when asked. Check that nothing else uses UDP port 5005. |
| HUD says "(offline)" | Start `server.py` first, then press Play again. Offline sessions are saved and upload automatically later. |
| `pip install mediapipe` fails | Use Python 3.11 or 3.12: `py -3.11 -m venv .venv` |
| Camera won't open | Close Zoom, Teams or the Camera app. Try `--camera 1`. |
| Angles jump around | Improve the lighting, move back so the whole limb is in frame, use `--model full`. |
| Nothing in the headset | Quest Link must be the active OpenXR runtime, and Link must be on *before* pressing Play. |
| Pink objects | The project isn't using URP or Built-in correctly. Use the Universal 3D template. |

## 10. Next steps for the capstone

- **Therapist login:** the dashboard has no authentication yet. Add it before testing with real patients.
- **Patient privacy:** use patient codes (P-001), not names. Keep `theraquest.db` on the clinic laptop. This supports your Ethical Considerations section and the Data Privacy Act of 2012 (RA 10173).
- **Adding exercises:** on the dashboard, pick one from **Add exercise**. To create a new type, add it to `LIBRARY` in `server.py`, plus a pose in `FigureVisualizer.cs` and (if needed) a posture check in `MotionInput.cs`. A new measured angle also goes in `tracker/angles.py` and `AngleSet`.
- **Evaluation data:** each `performance_record` stores accuracy and mean angle error per exercise. Export it for your confusion-matrix and angle-error analysis (Section 3.9).
- **Avatar:** replace the primitive coach figure with a rigged character (e.g. from Mixamo) driven by the same angles.
