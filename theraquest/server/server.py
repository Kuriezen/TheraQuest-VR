"""
TheraQuest server: database + REST API + therapist dashboard.
Uses only the Python standard library (no pip install needed).

Run:   python server.py            -> http://localhost:8000  (dashboard)
       python server.py --port 8080
       python server.py --reset    -> delete the database and start fresh

API (used by Unity and the dashboard)
  GET  /api/patients                         list patients
  GET  /api/patients/{id}/program            today's program for Unity (effective reps include stage)
  PUT  /api/patients/{id}/program            therapist edits prescription
  GET  /api/protocols                        protocols with their phases
  POST /api/patients/{id}/phase              therapist assigns / approves a phase  {"phase_id": 3, "note": "..."}
  GET  /api/library                          exercises the therapist can add
  POST /api/patients/{id}/exercises          add one from the library  {"code": "slr"}
  DELETE /api/patients/{id}/exercises/{eid}  remove one (its history is kept)
  POST /api/sessions                         Unity uploads a finished session
  GET  /api/patients/{id}/dashboard          adherence + performance analytics
"""
import argparse
import json
import os
import sqlite3
import sys
from datetime import date, datetime, timedelta
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import urlparse

HERE = os.path.dirname(os.path.abspath(__file__))
DB_PATH = os.path.join(HERE, "theraquest.db")

BADGES = {
    "first": "First steps", "perfect": "Perfect form", "combo10": "Combo 10",
    "fullrom": "Bullseye", "streak3": "On a roll", "adh80": "Consistent", "lv5": "Level 5",
}

# Exercise library: every movement the tracker can measure with one webcam.
# `code` drives the Unity coach figure and posture checks; `measure` is the tracker angle.
# PLACEHOLDER NUMBERS: targets, tolerances, safety limits, holds, sets and rest must come
# from your physical therapist's protocol. The therapist can change them per patient.
#   code: (name, joint, measure, view, start position, target, tol, safety max, hold s, sets, reps, rest s, cue)
LIBRARY = {
    "shoulderAbd":  ("Shoulder abduction", "Shoulder", "shoulderElev", "front", "Standing, arm at your side, thumb up",
                     90, 10, 120, 0, 1, 8, 30, "Raise your arm out to the side"),
    "shoulderFlex": ("Shoulder flexion", "Shoulder", "shoulderElev", "side", "Standing, arm at your side, thumb forward",
                     90, 10, 120, 0, 1, 8, 30, "Raise your straight arm forward and up"),
    "scaption":     ("Scaption", "Shoulder", "shoulderElev", "front", "Standing, arm slightly forward, thumb up",
                     90, 10, 120, 0, 1, 8, 30, "Raise your arm at a slight angle in front of you"),
    "wallSlide":    ("Wall slide", "Shoulder", "shoulderElev", "side", "Facing a wall, forearm resting on it",
                     120, 10, 150, 2, 1, 8, 30, "Slide your hand up the wall"),
    "elbowFlex":    ("Elbow flexion", "Elbow", "elbowFlex", "side", "Standing, elbow at your side",
                     130, 10, 150, 0, 1, 8, 30, "Bend your elbow and bring your hand up"),
    "hipAbd":       ("Standing hip abduction", "Hip", "hipElev", "front", "Standing, holding a chair for balance",
                     25, 5, 40, 2, 1, 8, 30, "Lift your leg out to the side, toes forward"),
    "slr":          ("Straight leg raise", "Hip", "hipElev", "side", "Lying on your back, other knee bent",
                     45, 5, 60, 3, 2, 8, 30, "Tighten your thigh and lift your straight leg"),
    "march":        ("Standing march", "Hip", "hipElev", "side", "Standing, holding a chair for balance",
                     70, 10, 100, 0, 1, 10, 30, "Lift your knee up toward your chest"),
    "kneeExt":      ("Seated knee extension", "Knee", "kneeExt", "side", "Sitting tall, knee bent at 90",
                     80, 10, 0, 0, 1, 8, 30, "Sit tall and straighten your knee slowly"),
    "heelSlide":    ("Heel slide", "Knee", "kneeFlex", "side", "Lying on your back, legs straight",
                     90, 10, 110, 0, 2, 8, 30, "Slide your heel toward you, bending your knee"),
    "miniSquat":    ("Mini squat", "Knee", "kneeFlex", "side", "Standing, holding a chair, feet hip-width",
                     45, 10, 60, 0, 1, 8, 30, "Bend your knees a little, chest up"),
    "sitToStand":   ("Sit to stand", "Knee", "kneeExt", "side", "Sitting on a firm chair, feet flat",
                     85, 10, 0, 0, 1, 6, 30, "Lean forward and stand up tall"),
}

# Example protocols, loaded once into the protocol tables. They follow the structure of published
# post-op protocols, but EVERY NUMBER IS A PLACEHOLDER: replace them with the protocol your partner
# physical therapist actually uses, and have them sign it off. Phases with vr=False are immobilization
# or passive-motion phases done with the therapist; TheraQuest is only for active-ROM phases.
#   protocol: (name, condition, source, [phases])
#   phase:    (name, weeks, vr, goals, precautions, [(code, target, tol, safety max, hold s, sets, reps, rest s)])
PROTOCOLS = [
    ("Total knee replacement", "Post-op total knee arthroplasty", "Structure after OHSU Rehab Protocols - Total Knee (Nov 2023)", [
        ("Phase I", "Weeks 0-2", True, "Knee flexion at least 90°, full extension, independent straight leg raise",
         "No pillow under the knee; no kneeling",
         [("heelSlide", 90, 10, 100, 0, 2, 8, 30), ("kneeExt", 75, 10, 0, 0, 2, 8, 30), ("slr", 40, 5, 55, 3, 2, 8, 30)]),
        ("Phase II", "Weeks 2-6", True, "Knee flexion 0-110°, normal walking pattern",
         "Stop if pain rises more than 2 points or soreness lasts over 2 hours",
         [("heelSlide", 110, 10, 120, 0, 2, 8, 30), ("kneeExt", 85, 10, 0, 2, 2, 8, 30),
          ("miniSquat", 30, 10, 45, 0, 2, 8, 30), ("sitToStand", 85, 10, 0, 0, 2, 6, 45)]),
        ("Phase III", "Weeks 6-12", True, "Knee flexion 0-120°, functional strength and balance",
         "No running or jumping",
         [("heelSlide", 120, 10, 130, 0, 2, 10, 30), ("miniSquat", 45, 10, 60, 2, 2, 8, 30),
          ("sitToStand", 85, 10, 0, 0, 2, 8, 45), ("march", 70, 10, 100, 0, 2, 10, 30), ("hipAbd", 25, 5, 40, 2, 2, 8, 30)]),
    ]),
    ("Rotator cuff repair (large to massive)", "Post-op rotator cuff repair",
     "Structure after Mass General Brigham - Rotator Cuff Tear (Large to Massive) Protocol", [
        ("Phase I", "Weeks 0-6", False, "Protect the repair in the sling", "No shoulder motion", []),
        ("Phase II", "Weeks 6-12", False, "Passive range of motion with the therapist",
         "Passive only: forward elevation 0-100°, external rotation 0-30°", []),
        ("Phase III", "Weeks 12-16", True, "Active range of motion, forward elevation toward 120°",
         "No lifting; stop if pain rises",
         [("shoulderFlex", 100, 10, 120, 0, 2, 8, 30), ("scaption", 90, 10, 110, 0, 2, 8, 30), ("wallSlide", 110, 10, 130, 2, 2, 8, 30)]),
        ("Phase IV", "Weeks 16+", True, "Full active range of motion, early strengthening",
         "No heavy or overhead lifting",
         [("shoulderFlex", 140, 10, 160, 0, 3, 10, 30), ("shoulderAbd", 120, 10, 150, 0, 3, 10, 30), ("scaption", 120, 10, 150, 2, 3, 10, 30)]),
    ]),
]

# Columns added after the first version; added to existing databases on startup.
MIGRATIONS = {
    "patient": [("phase_id", "INTEGER"), ("phase_started_at", "TEXT")],
    "session": [("pain_before", "INTEGER"), ("pain_after", "INTEGER")],
    "exercise": [("measure", "TEXT"), ("view", "TEXT"), ("position", "TEXT"),
                 ("side", "TEXT NOT NULL DEFAULT ''"), ("safety_max", "REAL NOT NULL DEFAULT 0"),
                 ("hold_seconds", "REAL NOT NULL DEFAULT 0"), ("sets", "INTEGER NOT NULL DEFAULT 1"),
                 ("rest_seconds", "REAL NOT NULL DEFAULT 30"), ("active", "INTEGER NOT NULL DEFAULT 1")],
    "performance_record": [("unsafe_reps", "INTEGER NOT NULL DEFAULT 0")],
}


# ----------------------------------------------------------------- database
def db():
    con = sqlite3.connect(DB_PATH)
    con.row_factory = sqlite3.Row
    con.execute("PRAGMA foreign_keys = ON")
    return con


def init_db():
    con = db()
    with open(os.path.join(HERE, "schema.sql"), encoding="utf-8") as f:
        con.executescript(f.read())
    migrate(con)
    if con.execute("SELECT COUNT(*) FROM physical_therapist").fetchone()[0] == 0:
        seed(con)
    if con.execute("SELECT COUNT(*) FROM protocol").fetchone()[0] == 0:
        seed_protocols(con)
    con.commit()
    con.close()


def migrate(con):
    for table, cols in MIGRATIONS.items():
        have = {r[1] for r in con.execute(f"PRAGMA table_info({table})")}
        for name, decl in cols:
            if name not in have:
                con.execute(f"ALTER TABLE {table} ADD COLUMN {name} {decl}")
    # Exercises created before the library existed: fill in its details
    for code, lib in LIBRARY.items():
        con.execute("UPDATE exercise SET measure=?, view=?, position=?, safety_max=? WHERE code=? AND measure IS NULL",
                    (lib[2], lib[3], lib[4], lib[7], code))


def add_exercise(con, program_id, code, rx=None):
    """Add a library exercise to a program. `rx` overrides the library's prescription numbers."""
    if code not in LIBRARY:
        raise ValueError(f"Unknown exercise '{code}'")
    name, joint, measure, view, position, tgt, tol, smax, hold, sets, reps, rest, cue = LIBRARY[code]
    v = dict(target_angle=tgt, tolerance=tol, safety_max=smax, hold_seconds=hold, sets=sets, reps=reps, rest_seconds=rest)
    v.update({k: x for k, x in (rx or {}).items() if k in v and x is not None})
    order = con.execute("SELECT COALESCE(MAX(sort_order), -1) + 1 FROM exercise WHERE program_id=?", (program_id,)).fetchone()[0]
    con.execute("""INSERT INTO exercise(program_id, code, name, joint, measure, view, position, target_angle, tolerance,
                   safety_max, hold_seconds, sets, reps, rest_seconds, cue, sort_order)
                   VALUES (?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?)""",
                (program_id, code, name, joint, measure, view, position, v["target_angle"], v["tolerance"],
                 v["safety_max"], v["hold_seconds"], v["sets"], v["reps"], v["rest_seconds"], cue, order))


def seed_protocols(con):
    for name, condition, source, phases in PROTOCOLS:
        prid = con.execute("INSERT INTO protocol(name, condition, source) VALUES (?,?,?)",
                           (name, condition, source)).lastrowid
        for no, (pname, weeks, vr, goals, precautions, exs) in enumerate(phases, 1):
            phid = con.execute("""INSERT INTO protocol_phase(protocol_id, phase_no, name, weeks, vr_allowed, goals, precautions)
                                  VALUES (?,?,?,?,?,?,?)""", (prid, no, pname, weeks, int(vr), goals, precautions)).lastrowid
            for i, (code, tgt, tol, smax, hold, sets, reps, rest) in enumerate(exs):
                con.execute("""INSERT INTO phase_exercise(phase_id, code, target_angle, tolerance, safety_max,
                               hold_seconds, sets, reps, rest_seconds, sort_order) VALUES (?,?,?,?,?,?,?,?,?,?)""",
                            (phid, code, tgt, tol, smax, hold, sets, reps, rest, i))


def seed(con):
    """Starter data so the system works on first run. Replace with your real test setup."""
    tid = con.execute("INSERT INTO physical_therapist(name, license_no, email) VALUES (?,?,?)",
                      ("Test Therapist", "PT-0000", "therapist@example.com")).lastrowid
    pid = con.execute("""INSERT INTO rehabilitation_program(therapist_id, name, description, schedule_days, max_reps)
                         VALUES (?,?,?,?,?)""",
                      (tid, "Post-op shoulder & knee", "Orthopedic ROM program", "1,3,5", 14)).lastrowid
    for code in ("shoulderAbd", "elbowFlex", "kneeExt"):
        add_exercise(con, pid, code)
    con.execute("INSERT INTO patient(therapist_id, program_id, code, name, condition) VALUES (?,?,?,?,?)",
                (tid, pid, "P-001", "Test Patient", "Post-op shoulder"))


# ------------------------------------------------------------ game helpers
def level_from_xp(xp):
    """Same curve as Unity's GamificationManager: 100 XP for level 2, +60 per level after."""
    lv, need, acc = 1, 100, 0
    while xp >= acc + need:
        acc += need
        lv += 1
        need = 100 + (lv - 1) * 60
    return {"level": lv, "xp_into_level": xp - acc, "xp_for_next": need}


def effective_reps(ex, max_reps):
    return min(max_reps, ex["reps"] + 2 * (ex["stage"] - 1))


def schedule(program_row):
    return [int(d) for d in program_row["schedule_days"].split(",") if d.strip() != ""]


def py_dow(d):
    """Python weekday (Mon=0) -> our convention (Sun=0)."""
    return (d.weekday() + 1) % 7


# ----------------------------------------------------- adherence analytics
def day_statuses(con, patient_id, program, days=14, today=None):
    today = today or date.today()
    sched = schedule(program)
    start = today - timedelta(days=days - 1)
    rows = con.execute("""SELECT session_date, status FROM adherence_record
                          WHERE patient_id=? AND session_date>=?""", (patient_id, start.isoformat())).fetchall()
    created = con.execute("SELECT created_at FROM patient WHERE patient_id=?", (patient_id,)).fetchone()[0][:10]
    by_day = {}
    for r in rows:
        prev = by_day.get(r["session_date"])
        if prev != "complete":
            by_day[r["session_date"]] = r["status"]
    out = []
    for i in range(days):
        d = start + timedelta(days=i)
        k = d.isoformat()
        scheduled = py_dow(d) in sched and k >= created   # no "missed" before the patient was enrolled
        if k in by_day:
            st = by_day[k]
        elif not scheduled:
            st = "rest"
        else:
            st = "pending" if d == today else "missed"
        out.append({"date": k, "dow": py_dow(d), "status": st, "scheduled": scheduled})
    return out


def streak(con, patient_id, program):
    n = 0
    for d in reversed(day_statuses(con, patient_id, program, days=60)):
        if d["status"] in ("pending", "rest"):
            continue
        if d["status"] == "complete":
            n += 1
        else:
            break
    return n


def adherence_rate(statuses):
    sched = [d for d in statuses if d["scheduled"] and d["status"] != "pending"]
    done = sum(1 for d in sched if d["status"] == "complete")
    return {"rate": round(100 * done / len(sched)) if sched else 0, "done": done, "scheduled": len(sched)}


# ------------------------------------------------------- protocol phases
def phase_info(con, p):
    """The patient's current protocol phase (empty fields = custom program without a protocol)."""
    ph = p["phase_id"] and con.execute("""SELECT ph.*, pr.name AS protocol FROM protocol_phase ph
                                          JOIN protocol pr ON pr.protocol_id = ph.protocol_id
                                          WHERE ph.phase_id=?""", (p["phase_id"],)).fetchone()
    if not ph:
        return {"id": 0, "protocolId": 0, "protocol": "", "name": "", "phaseNo": 0, "weeks": "", "vrAllowed": True,
                "goals": "", "precautions": "", "startedAt": ""}
    return {"id": ph["phase_id"], "protocolId": ph["protocol_id"], "protocol": ph["protocol"], "name": ph["name"],
            "phaseNo": ph["phase_no"], "weeks": ph["weeks"], "vrAllowed": bool(ph["vr_allowed"]),
            "goals": ph["goals"], "precautions": ph["precautions"], "startedAt": p["phase_started_at"] or ""}


def set_phase(con, patient_id, body):
    """
    Therapist assigns a protocol phase (or phase_id null = keep the exercises as a custom program).
    The phase's exercises replace the patient's program; the therapist can still adjust any value.
    """
    p = con.execute("SELECT * FROM patient WHERE patient_id=?", (patient_id,)).fetchone()
    if not p:
        raise ValueError("Unknown patient")
    phase_id = body.get("phase_id")
    now = datetime.now().isoformat(timespec="seconds")
    if phase_id:
        phase_id = int(phase_id)
        if not con.execute("SELECT 1 FROM protocol_phase WHERE phase_id=?", (phase_id,)).fetchone():
            raise ValueError("Unknown phase")
        con.execute("UPDATE exercise SET active=0 WHERE program_id=? AND active=1", (p["program_id"],))
        for r in con.execute("SELECT * FROM phase_exercise WHERE phase_id=? ORDER BY sort_order", (phase_id,)).fetchall():
            add_exercise(con, p["program_id"], r["code"], dict(r))
    con.execute("UPDATE patient SET phase_id=?, phase_started_at=? WHERE patient_id=?",
                (phase_id or None, now if phase_id else None, patient_id))
    con.execute("INSERT INTO phase_history(patient_id, phase_id, changed_at, note) VALUES (?,?,?,?)",
                (patient_id, phase_id or None, now, body.get("note", "")))
    con.commit()
    return get_program(con, patient_id)


def readiness(con, p):
    """
    Progression suggestion for the current phase. The system only suggests; the therapist approves.
    Criteria, checked on the most recent `min_sessions` complete sessions in this phase:
      - enough complete sessions (every exercise reached all its sets and reps)
      - ROM goal: every exercise at or above `min_accuracy`
      - pain after minus pain before at most `max_pain_increase` (pain must be recorded)
      - no reps past the safety limit
    """
    if not p["phase_id"]:
        return None
    ph = con.execute("SELECT * FROM protocol_phase WHERE phase_id=?", (p["phase_id"],)).fetchone()
    nxt = con.execute("SELECT * FROM protocol_phase WHERE protocol_id=? AND phase_no>? ORDER BY phase_no LIMIT 1",
                      (ph["protocol_id"], ph["phase_no"])).fetchone()
    out = {"next": {"id": nxt["phase_id"], "name": nxt["name"], "weeks": nxt["weeks"]} if nxt else None,
           "ready": False, "criteria": [], "note": ""}
    if not ph["vr_allowed"]:
        out["note"] = "This phase is done with the therapist, so TheraQuest has no session data to judge it."
        return out

    n, acc, pain = ph["min_sessions"], ph["min_accuracy"], ph["max_pain_increase"]
    rows = con.execute("""
        SELECT s.started_at, s.pain_before, s.pain_after, MIN(r.avg_accuracy) AS min_acc,
               COALESCE(SUM(r.unsafe_reps),0) AS unsafe
        FROM session s LEFT JOIN performance_record r ON r.session_id = s.session_id
        WHERE s.patient_id=? AND s.status='complete' AND s.started_at>=?
        GROUP BY s.session_id ORDER BY s.started_at DESC LIMIT ?""", (p["patient_id"], p["phase_started_at"], n)).fetchall()
    enough = len(rows) >= n

    def pain_ok(r):
        return r["pain_before"] is not None and r["pain_after"] is not None and r["pain_after"] - r["pain_before"] <= pain

    def crit(label, ok, detail):
        out["criteria"].append({"label": label, "met": bool(enough and ok), "detail": detail if rows else "no complete sessions yet"})

    out["criteria"].append({"label": f"{n} complete sessions in this phase (all sets and reps done)", "met": enough,
                            "detail": f"{len(rows)} of {n}"})
    crit(f"ROM goal: every exercise at {acc:.0f}% accuracy or more", all((r["min_acc"] or 0) >= acc for r in rows),
         ", ".join(f"{r['min_acc'] or 0:.0f}%" for r in rows))
    crit(f"Pain went up by {pain} points or less", all(pain_ok(r) for r in rows),
         ", ".join("not recorded" if r["pain_before"] is None or r["pain_after"] is None
                   else f"{r['pain_before']}→{r['pain_after']}" for r in rows))
    crit("No reps past the safety limit", all(r["unsafe"] == 0 for r in rows),
         ", ".join(str(r["unsafe"]) for r in rows) + " unsafe reps")
    out["ready"] = nxt is not None and all(c["met"] for c in out["criteria"])
    return out


def list_protocols(con):
    out = []
    for pr in con.execute("SELECT * FROM protocol ORDER BY protocol_id").fetchall():
        phases = []
        for ph in con.execute("SELECT * FROM protocol_phase WHERE protocol_id=? ORDER BY phase_no", (pr["protocol_id"],)).fetchall():
            exs = con.execute("SELECT code FROM phase_exercise WHERE phase_id=? ORDER BY sort_order", (ph["phase_id"],)).fetchall()
            phases.append({"id": ph["phase_id"], "phaseNo": ph["phase_no"], "name": ph["name"], "weeks": ph["weeks"],
                           "vrAllowed": bool(ph["vr_allowed"]), "goals": ph["goals"], "precautions": ph["precautions"],
                           "exercises": [LIBRARY[e["code"]][0] for e in exs if e["code"] in LIBRARY]})
        out.append({"id": pr["protocol_id"], "name": pr["name"], "condition": pr["condition"], "source": pr["source"],
                    "phases": phases})
    return out


# ------------------------------------------------------------- API logic
def get_program(con, patient_id):
    p = con.execute("SELECT * FROM patient WHERE patient_id=?", (patient_id,)).fetchone()
    if not p:
        return None
    prog = con.execute("SELECT * FROM rehabilitation_program WHERE program_id=?", (p["program_id"],)).fetchone()
    exs = con.execute("SELECT * FROM exercise WHERE program_id=? AND active=1 ORDER BY sort_order", (prog["program_id"],)).fetchall()
    badges = [r["badge_code"] for r in con.execute("SELECT badge_code FROM patient_badge WHERE patient_id=?", (patient_id,))]
    lvl = level_from_xp(p["xp"])
    return {
        "patient": {"id": p["patient_id"], "code": p["code"], "name": p["name"], "xp": p["xp"],
                    "level": lvl["level"], "xpIntoLevel": lvl["xp_into_level"], "xpForNext": lvl["xp_for_next"],
                    "badges": badges, "streak": streak(con, patient_id, prog)},
        "program": {"id": prog["program_id"], "name": prog["name"], "days": schedule(prog), "maxReps": prog["max_reps"]},
        "phase": phase_info(con, p),
        "exercises": [{
            "id": e["exercise_id"], "code": e["code"], "name": e["name"], "joint": e["joint"],
            "measure": e["measure"], "view": e["view"], "position": e["position"], "side": e["side"],
            "targetAngle": e["target_angle"], "tolerance": e["tolerance"], "safetyMax": e["safety_max"],
            "holdSeconds": e["hold_seconds"], "sets": e["sets"], "restSeconds": e["rest_seconds"],
            "baseReps": e["reps"], "reps": effective_reps(e, prog["max_reps"]), "stage": e["stage"], "cue": e["cue"],
        } for e in exs],
    }


def save_session(con, body):
    """
    Adherence Monitoring Module: Completion Checker -> Status Classifier -> Adherence Record Writer.
    Also applies gamification results (XP, badges) and the Progression Controller.
    """
    pid = int(body["patient_id"])
    p = con.execute("SELECT * FROM patient WHERE patient_id=?", (pid,)).fetchone()
    if not p:
        raise ValueError("Unknown patient_id")
    prog = con.execute("SELECT * FROM rehabilitation_program WHERE program_id=?", (p["program_id"],)).fetchone()
    exs = {e["exercise_id"]: e for e in con.execute("SELECT * FROM exercise WHERE program_id=? AND active=1", (prog["program_id"],))}
    results = body.get("exercises", [])

    # Completion Checker: every prescribed exercise reached its target valid reps
    done_ids = {int(r["exercise_id"]) for r in results if int(r["valid_reps"]) >= int(r["target_reps"])}
    complete = (not body.get("ended_early")) and set(exs.keys()) <= done_ids
    status = "complete" if complete else "incomplete"

    started = body.get("started_at") or datetime.now().isoformat(timespec="seconds")
    ended = body.get("ended_at") or datetime.now().isoformat(timespec="seconds")

    def pain(key):   # 0-10, or None when the check-in was skipped (Unity sends -1)
        v = int(body.get(key, -1))
        return min(10, v) if v >= 0 else None

    sid = con.execute("""INSERT INTO session(patient_id, program_id, started_at, ended_at, input_source, points, stars, status,
                         pain_before, pain_after) VALUES (?,?,?,?,?,?,?,?,?,?)""",
                      (pid, prog["program_id"], started, ended, body.get("input_source", ""),
                       int(body.get("points", 0)), int(body.get("stars", 0)), status,
                       pain("pain_before"), pain("pain_after"))).lastrowid

    notes = []
    for r in results:
        eid = int(r["exercise_id"])
        if eid not in exs:
            continue
        unsafe = int(r.get("unsafe_reps", 0))
        con.execute("""INSERT INTO performance_record(session_id, exercise_id, target_reps, valid_reps, attempts,
                       avg_accuracy, mean_angle_error, unsafe_reps, feedback_log) VALUES (?,?,?,?,?,?,?,?,?)""",
                    (sid, eid, int(r["target_reps"]), int(r["valid_reps"]), int(r["attempts"]),
                     float(r.get("avg_accuracy", 0)), float(r.get("mean_angle_error", 0)), unsafe,
                     json.dumps(r.get("feedback", []))))
        # Progression Controller: >=85% accuracy and no unsafe reps on a completed session
        # -> next stage (+2 reps per set, capped)
        e = exs[eid]
        if complete and unsafe == 0 and float(r.get("avg_accuracy", 0)) >= 85:
            nxt = min(prog["max_reps"], e["reps"] + 2 * e["stage"])
            if nxt > effective_reps(e, prog["max_reps"]):
                con.execute("UPDATE exercise SET stage = stage + 1 WHERE exercise_id=?", (eid,))
                notes.append(f"{e['name']}: next session goes to {nxt} reps")

    session_day = started[:10]
    con.execute("""INSERT INTO adherence_record(patient_id, session_id, session_date, status, completion_flag)
                   VALUES (?,?,?,?,?)""", (pid, sid, session_day, status, 1 if complete else 0))

    # Gamification persistence
    old_level = level_from_xp(p["xp"])["level"]
    new_xp = p["xp"] + int(body.get("points", 0))
    con.execute("UPDATE patient SET xp=? WHERE patient_id=?", (new_xp, pid))
    new_level = level_from_xp(new_xp)["level"]
    if new_level > old_level:
        notes.append(f"Level up! Reached level {new_level}")

    earned = list(body.get("badges_earned", []))
    if complete:
        earned.append("first")
    if streak(con, pid, prog) >= 3:
        earned.append("streak3")
    adh = adherence_rate(day_statuses(con, pid, prog))
    if adh["rate"] >= 80 and adh["scheduled"] >= 4:
        earned.append("adh80")
    if new_level >= 5:
        earned.append("lv5")
    new_badges = []
    for b in earned:
        if b in BADGES:
            cur = con.execute("INSERT OR IGNORE INTO patient_badge(patient_id, badge_code) VALUES (?,?)", (pid, b))
            if cur.rowcount:
                new_badges.append(b)
                notes.append(f"Badge unlocked: {BADGES[b]}")
    con.commit()
    return {"session_id": sid, "status": status, "notes": notes, "new_badges": new_badges,
            "patient": get_program(con, pid)["patient"]}


def update_program(con, patient_id, body):
    p = con.execute("SELECT * FROM patient WHERE patient_id=?", (patient_id,)).fetchone()
    if not p:
        raise ValueError("Unknown patient")
    if "days" in body:
        days = sorted({int(d) for d in body["days"] if 0 <= int(d) <= 6})
        con.execute("UPDATE rehabilitation_program SET schedule_days=? WHERE program_id=?",
                    (",".join(map(str, days)), p["program_id"]))
    if "maxReps" in body:
        con.execute("UPDATE rehabilitation_program SET max_reps=? WHERE program_id=?",
                    (max(1, min(50, int(body["maxReps"]))), p["program_id"]))
    for e in body.get("exercises", []):
        for key, (col, cast, lo, hi) in RX_FIELDS.items():
            if key in e:
                con.execute(f"UPDATE exercise SET {col}=? WHERE exercise_id=? AND program_id=?",
                            (max(lo, min(hi, cast(e[key]))), int(e["id"]), p["program_id"]))
        if "side" in e:
            con.execute("UPDATE exercise SET side=? WHERE exercise_id=? AND program_id=?",
                        (e["side"] if e["side"] in ("L", "R") else "", int(e["id"]), p["program_id"]))
    con.commit()
    return get_program(con, patient_id)


# Prescription fields the therapist can edit: json key -> (column, type, min, max)
RX_FIELDS = {
    "reps": ("reps", int, 1, 50), "targetAngle": ("target_angle", float, 5, 175),
    "tolerance": ("tolerance", float, 1, 30), "safetyMax": ("safety_max", float, 0, 180),
    "holdSeconds": ("hold_seconds", float, 0, 30), "sets": ("sets", int, 1, 10),
    "restSeconds": ("rest_seconds", float, 0, 300),
}


def change_exercises(con, patient_id, method, body, exercise_id=None):
    """Therapist adds an exercise from the library (POST) or removes one (DELETE; its history is kept)."""
    p = con.execute("SELECT * FROM patient WHERE patient_id=?", (patient_id,)).fetchone()
    if not p:
        raise ValueError("Unknown patient")
    if method == "POST":
        add_exercise(con, p["program_id"], body.get("code"))
    else:
        con.execute("UPDATE exercise SET active=0 WHERE exercise_id=? AND program_id=?", (exercise_id, p["program_id"]))
    con.commit()
    return get_program(con, patient_id)


def dashboard(con, patient_id):
    info = get_program(con, patient_id)
    if not info:
        return None
    prog = con.execute("SELECT * FROM rehabilitation_program WHERE program_id=?", (info["program"]["id"],)).fetchone()
    statuses = day_statuses(con, patient_id, prog)
    since = (date.today() - timedelta(days=13)).isoformat()
    sessions = con.execute("""
        SELECT s.session_id, s.started_at, s.input_source, s.points, s.stars, s.status, s.pain_before, s.pain_after,
               COALESCE(SUM(r.valid_reps),0) AS valid, COALESCE(SUM(r.attempts),0) AS attempts,
               CASE WHEN SUM(r.attempts)>0 THEN SUM(r.avg_accuracy*r.attempts)/SUM(r.attempts) ELSE 0 END AS accuracy,
               CASE WHEN SUM(r.attempts)>0 THEN SUM(r.mean_angle_error*r.attempts)/SUM(r.attempts) ELSE 0 END AS mean_err
        FROM session s LEFT JOIN performance_record r ON r.session_id = s.session_id
        WHERE s.patient_id=? GROUP BY s.session_id ORDER BY s.started_at DESC, s.session_id DESC LIMIT 50""", (patient_id,)).fetchall()
    sessions = [dict(r) for r in sessions]
    recent = [s for s in sessions if s["started_at"][:10] >= since]
    att = sum(s["attempts"] for s in recent)
    per_ex = con.execute("""
        SELECT e.name, COUNT(r.record_id) AS n, COALESCE(SUM(r.unsafe_reps),0) AS unsafe,
               CASE WHEN SUM(r.attempts)>0 THEN SUM(r.avg_accuracy*r.attempts)/SUM(r.attempts) ELSE 0 END AS accuracy,
               CASE WHEN SUM(r.attempts)>0 THEN SUM(r.mean_angle_error*r.attempts)/SUM(r.attempts) ELSE 0 END AS mean_err
        FROM exercise e LEFT JOIN performance_record r ON r.exercise_id = e.exercise_id
        WHERE e.program_id=? AND e.active=1 GROUP BY e.exercise_id ORDER BY e.sort_order""", (prog["program_id"],)).fetchall()
    return {
        **info,
        "kpis": {
            "adherence": adherence_rate(statuses),
            "accuracy": round(sum(s["accuracy"] * s["attempts"] for s in recent) / att) if att else 0,
            "attempts": att,
            "validReps": sum(s["valid"] for s in recent),
            "streak": info["patient"]["streak"],
        },
        "calendar": statuses,
        "readiness": readiness(con, con.execute("SELECT * FROM patient WHERE patient_id=?", (patient_id,)).fetchone()),
        "phaseHistory": [dict(r) for r in con.execute("""
            SELECT h.changed_at, h.note, ph.name AS phase, pr.name AS protocol FROM phase_history h
            LEFT JOIN protocol_phase ph ON ph.phase_id = h.phase_id LEFT JOIN protocol pr ON pr.protocol_id = ph.protocol_id
            WHERE h.patient_id=? ORDER BY h.history_id DESC LIMIT 10""", (patient_id,))],
        "sessions": [{**s, "accuracy": round(s["accuracy"], 1), "mean_err": round(s["mean_err"], 1)} for s in sessions],
        "byExercise": [{**dict(r), "accuracy": round(r["accuracy"], 1), "mean_err": round(r["mean_err"], 1)} for r in per_ex],
    }


# ---------------------------------------------------------------- HTTP
class Handler(BaseHTTPRequestHandler):
    server_version = "TheraQuest/1.0"

    def _send(self, code, payload, ctype="application/json"):
        data = payload if isinstance(payload, bytes) else json.dumps(payload).encode("utf-8")
        self.send_response(code)
        self.send_header("Content-Type", ctype)
        self.send_header("Content-Length", str(len(data)))
        self.send_header("Access-Control-Allow-Origin", "*")
        self.send_header("Access-Control-Allow-Headers", "Content-Type")
        self.send_header("Access-Control-Allow-Methods", "GET, POST, PUT, DELETE, OPTIONS")
        self.end_headers()
        self.wfile.write(data)

    def _body(self):
        n = int(self.headers.get("Content-Length") or 0)
        return json.loads(self.rfile.read(n) or b"{}")

    def _route(self, method):
        parts = [p for p in urlparse(self.path).path.split("/") if p]
        try:
            if method == "GET" and parts in ([], ["dashboard"]):
                with open(os.path.join(HERE, "dashboard.html"), "rb") as f:
                    return self._send(200, f.read(), "text/html; charset=utf-8")
            if parts[:1] != ["api"]:
                return self._send(404, {"error": "Not found"})
            con = db()
            try:
                if method == "GET" and parts == ["api", "patients"]:
                    rows = con.execute("SELECT patient_id AS id, code, name, condition, xp FROM patient ORDER BY code").fetchall()
                    return self._send(200, [dict(r) for r in rows])
                if method == "GET" and parts == ["api", "protocols"]:
                    return self._send(200, list_protocols(con))
                if len(parts) == 4 and parts[1] == "patients" and parts[3] == "phase" and method == "POST":
                    return self._send(200, set_phase(con, int(parts[2]), self._body()))
                if method == "GET" and parts == ["api", "library"]:
                    return self._send(200, [{"code": c, "name": v[0], "joint": v[1], "view": v[3], "position": v[4]}
                                            for c, v in LIBRARY.items()])
                if len(parts) == 4 and parts[1] == "patients" and parts[3] == "exercises" and method == "POST":
                    return self._send(201, change_exercises(con, int(parts[2]), method, self._body()))
                if len(parts) == 5 and parts[1] == "patients" and parts[3] == "exercises" and method == "DELETE":
                    return self._send(200, change_exercises(con, int(parts[2]), method, {}, int(parts[4])))
                if len(parts) == 4 and parts[1] == "patients" and parts[3] in ("program", "dashboard"):
                    pid = int(parts[2])
                    if method == "GET":
                        data = get_program(con, pid) if parts[3] == "program" else dashboard(con, pid)
                        return self._send(200, data) if data else self._send(404, {"error": "Patient not found"})
                    if method == "PUT" and parts[3] == "program":
                        return self._send(200, update_program(con, pid, self._body()))
                if method == "POST" and parts == ["api", "sessions"]:
                    return self._send(201, save_session(con, self._body()))
                return self._send(404, {"error": "Not found"})
            finally:
                con.close()
        except (ValueError, KeyError, json.JSONDecodeError) as e:
            return self._send(400, {"error": f"Bad request: {e}"})

    def do_GET(self):
        self._route("GET")

    def do_POST(self):
        self._route("POST")

    def do_PUT(self):
        self._route("PUT")

    def do_DELETE(self):
        self._route("DELETE")

    def do_OPTIONS(self):
        self._send(204, b"")

    def log_message(self, fmt, *args):
        sys.stderr.write("  %s %s\n" % (self.command, self.path))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--port", type=int, default=8000)
    ap.add_argument("--host", default="0.0.0.0")
    ap.add_argument("--reset", action="store_true", help="delete the database first")
    args = ap.parse_args()
    if args.reset and os.path.exists(DB_PATH):
        os.remove(DB_PATH)
    init_db()
    print(f"TheraQuest server running.  Dashboard: http://localhost:{args.port}   (Ctrl+C to stop)")
    try:
        ThreadingHTTPServer((args.host, args.port), Handler).serve_forever()
    except KeyboardInterrupt:
        pass


if __name__ == "__main__":
    main()
