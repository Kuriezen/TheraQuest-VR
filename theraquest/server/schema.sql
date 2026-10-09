-- TheraQuest database (SQLite). Mirrors the ERD in Section 3.5.2.
PRAGMA foreign_keys = ON;

CREATE TABLE IF NOT EXISTS physical_therapist (
  therapist_id   INTEGER PRIMARY KEY AUTOINCREMENT,
  name           TEXT NOT NULL,
  license_no     TEXT,
  email          TEXT
);

CREATE TABLE IF NOT EXISTS rehabilitation_program (
  program_id     INTEGER PRIMARY KEY AUTOINCREMENT,
  therapist_id   INTEGER NOT NULL REFERENCES physical_therapist(therapist_id),
  name           TEXT NOT NULL,
  description    TEXT,
  schedule_days  TEXT NOT NULL DEFAULT '1,3,5',   -- 0=Sun ... 6=Sat
  max_reps       INTEGER NOT NULL DEFAULT 14      -- cap for gamified progression
);

-- Rehabilitation protocols (e.g. total knee replacement): Protocol -> Phases -> Exercises.
CREATE TABLE IF NOT EXISTS protocol (
  protocol_id    INTEGER PRIMARY KEY AUTOINCREMENT,
  name           TEXT NOT NULL,
  condition      TEXT,
  source         TEXT                              -- where the protocol comes from (cite it)
);

CREATE TABLE IF NOT EXISTS protocol_phase (
  phase_id          INTEGER PRIMARY KEY AUTOINCREMENT,
  protocol_id       INTEGER NOT NULL REFERENCES protocol(protocol_id),
  phase_no          INTEGER NOT NULL,
  name              TEXT NOT NULL,
  weeks             TEXT,                          -- e.g. "Weeks 2-6"
  vr_allowed        INTEGER NOT NULL DEFAULT 1,    -- 0 = immobilization / passive phase, done with the therapist
  goals             TEXT,
  precautions       TEXT,
  -- Progression criteria (the system suggests the next phase; the therapist decides)
  min_sessions      INTEGER NOT NULL DEFAULT 3,    -- complete sessions in a row that must meet the criteria
  min_accuracy      REAL NOT NULL DEFAULT 85,      -- ROM goal: every exercise at or above this accuracy
  max_pain_increase INTEGER NOT NULL DEFAULT 2     -- pain after - pain before, 0-10 scale
);

CREATE TABLE IF NOT EXISTS phase_exercise (
  phase_id       INTEGER NOT NULL REFERENCES protocol_phase(phase_id),
  code           TEXT NOT NULL,                    -- exercise type from server.py LIBRARY
  target_angle   REAL NOT NULL,
  tolerance      REAL NOT NULL,
  safety_max     REAL NOT NULL DEFAULT 0,
  hold_seconds   REAL NOT NULL DEFAULT 0,
  sets           INTEGER NOT NULL DEFAULT 1,
  reps           INTEGER NOT NULL DEFAULT 8,
  rest_seconds   REAL NOT NULL DEFAULT 30,
  sort_order     INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE IF NOT EXISTS patient (
  patient_id     INTEGER PRIMARY KEY AUTOINCREMENT,
  therapist_id   INTEGER NOT NULL REFERENCES physical_therapist(therapist_id),
  program_id     INTEGER REFERENCES rehabilitation_program(program_id),
  code           TEXT UNIQUE NOT NULL,             -- e.g. P-001 (use codes, not real names, in testing)
  name           TEXT,
  condition      TEXT,
  xp             INTEGER NOT NULL DEFAULT 0,
  phase_id       INTEGER REFERENCES protocol_phase(phase_id),   -- NULL = custom program, no protocol
  phase_started_at TEXT,                           -- YYYY-MM-DDTHH:MM:SS
  created_at     TEXT NOT NULL DEFAULT (datetime('now','localtime'))
);

CREATE TABLE IF NOT EXISTS phase_history (
  history_id     INTEGER PRIMARY KEY AUTOINCREMENT,
  patient_id     INTEGER NOT NULL REFERENCES patient(patient_id),
  phase_id       INTEGER REFERENCES protocol_phase(phase_id),   -- NULL = switched to a custom program
  changed_at     TEXT NOT NULL,
  note           TEXT
);

CREATE TABLE IF NOT EXISTS exercise (
  exercise_id    INTEGER PRIMARY KEY AUTOINCREMENT,
  program_id     INTEGER NOT NULL REFERENCES rehabilitation_program(program_id),
  code           TEXT NOT NULL,                    -- exercise type from server.py LIBRARY (drives Unity figure + posture checks)
  name           TEXT NOT NULL,
  joint          TEXT NOT NULL,
  measure        TEXT,                             -- tracker angle: shoulderElev | elbowFlex | hipElev | kneeFlex | kneeExt
  view           TEXT,                             -- camera position: front | side
  position       TEXT,                             -- start position shown to the patient
  side           TEXT NOT NULL DEFAULT '',         -- operated side: L | R | '' = whichever the camera sees best
  target_angle   REAL NOT NULL,                    -- degrees (therapist standard)
  tolerance      REAL NOT NULL DEFAULT 10,         -- +/- degrees (5-10 per Section 3.9.1)
  safety_max     REAL NOT NULL DEFAULT 0,          -- hard limit in degrees; 0 = none
  hold_seconds   REAL NOT NULL DEFAULT 0,          -- hold at the top for a rep to count
  sets           INTEGER NOT NULL DEFAULT 1,
  reps           INTEGER NOT NULL DEFAULT 8,       -- per set
  rest_seconds   REAL NOT NULL DEFAULT 30,         -- between sets
  stage          INTEGER NOT NULL DEFAULT 1,       -- Progression Controller level
  cue            TEXT,
  sort_order     INTEGER NOT NULL DEFAULT 0,
  active         INTEGER NOT NULL DEFAULT 1        -- 0 = removed from the program (history kept)
);

CREATE TABLE IF NOT EXISTS session (
  session_id     INTEGER PRIMARY KEY AUTOINCREMENT,
  patient_id     INTEGER NOT NULL REFERENCES patient(patient_id),
  program_id     INTEGER REFERENCES rehabilitation_program(program_id),
  started_at     TEXT NOT NULL,
  ended_at       TEXT NOT NULL,
  input_source   TEXT,                             -- webcam | keyboard | simulated
  points         INTEGER NOT NULL DEFAULT 0,
  stars          INTEGER NOT NULL DEFAULT 0,
  status         TEXT NOT NULL,                    -- complete | incomplete
  pain_before    INTEGER,                          -- 0-10 check-in in VR; NULL = not recorded
  pain_after     INTEGER
);

CREATE TABLE IF NOT EXISTS performance_record (
  record_id         INTEGER PRIMARY KEY AUTOINCREMENT,
  session_id        INTEGER NOT NULL REFERENCES session(session_id),
  exercise_id       INTEGER NOT NULL REFERENCES exercise(exercise_id),
  target_reps       INTEGER NOT NULL,
  valid_reps        INTEGER NOT NULL,
  attempts          INTEGER NOT NULL,
  avg_accuracy      REAL NOT NULL,                 -- %
  mean_angle_error  REAL NOT NULL,                 -- degrees, |theta_expected - theta_detected|
  unsafe_reps       INTEGER NOT NULL DEFAULT 0,    -- reps that went past safety_max
  feedback_log      TEXT                           -- corrective feedback given (JSON list)
);

CREATE TABLE IF NOT EXISTS adherence_record (
  adherence_id    INTEGER PRIMARY KEY AUTOINCREMENT,
  patient_id      INTEGER NOT NULL REFERENCES patient(patient_id),
  session_id      INTEGER REFERENCES session(session_id),
  session_date    TEXT NOT NULL,                   -- YYYY-MM-DD
  status          TEXT NOT NULL,                   -- complete | incomplete | missed
  completion_flag INTEGER NOT NULL                 -- 1 = passed prescribed requirements
);

CREATE TABLE IF NOT EXISTS patient_badge (
  patient_id   INTEGER NOT NULL REFERENCES patient(patient_id),
  badge_code   TEXT NOT NULL,
  earned_at    TEXT NOT NULL DEFAULT (datetime('now','localtime')),
  PRIMARY KEY (patient_id, badge_code)
);
