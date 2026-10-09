"""
TheraQuest - Joint Angle Calculator (Motion Tracking Module, Section 3.6.3).

Pure math, no camera or MediaPipe needed, so it can be unit-tested on its own.

Every angle is a "range of motion" (ROM) value that starts at 0 degrees in the
resting position and increases as the patient moves toward the therapist's
target. Unity compares it to the target angle. One measured angle serves
several exercises; the camera view decides which plane of motion it sees.

  shoulderElev  angle between trunk (shoulder->hip) and upper arm (shoulder->elbow)
                0 = arm at the side. Abduction / scaption (front view),
                flexion / wall slide (side view)
  elbowFlex     180 - interior elbow angle (shoulder-elbow-wrist)
                0 = arm straight, ~140 = fully bent
  hipElev       180 - interior hip angle (shoulder-hip-knee)
                0 = leg in line with the trunk. Hip abduction (front view),
                straight leg raise / marching (side view)
  kneeFlex      180 - interior knee angle (hip-knee-ankle)
                0 = leg straight. Heel slides, mini squat (side view)
  kneeExt       interior knee angle - 90
                0 = knee bent to 90, 90 = leg straight. Seated knee extension,
                sit-to-stand (side view)

MediaPipe Pose landmark indices used below:
  11/12 shoulders, 13/14 elbows, 15/16 wrists, 23/24 hips, 25/26 knees, 27/28 ankles
  (odd = patient's LEFT side, even = patient's RIGHT side)
"""
import math

L_SHOULDER, R_SHOULDER = 11, 12
L_ELBOW, R_ELBOW = 13, 14
L_WRIST, R_WRIST = 15, 16
L_HIP, R_HIP = 23, 24
L_KNEE, R_KNEE = 25, 26
L_ANKLE, R_ANKLE = 27, 28

# Which three landmarks (a, b, c) form the measured angle at joint b, per side.
JOINTS = {
    "shoulderElev": {"L": (L_HIP, L_SHOULDER, L_ELBOW), "R": (R_HIP, R_SHOULDER, R_ELBOW)},
    "elbowFlex":    {"L": (L_SHOULDER, L_ELBOW, L_WRIST), "R": (R_SHOULDER, R_ELBOW, R_WRIST)},
    "hipElev":      {"L": (L_SHOULDER, L_HIP, L_KNEE), "R": (R_SHOULDER, R_HIP, R_KNEE)},
    "kneeFlex":     {"L": (L_HIP, L_KNEE, L_ANKLE), "R": (R_HIP, R_KNEE, R_ANKLE)},
    "kneeExt":      {"L": (L_HIP, L_KNEE, L_ANKLE), "R": (R_HIP, R_KNEE, R_ANKLE)},
}

MIN_VISIBILITY = 0.5


def angle_at(a, b, c):
    """Interior angle ABC in degrees, using 2D image coordinates (x, y)."""
    v1 = (a[0] - b[0], a[1] - b[1])
    v2 = (c[0] - b[0], c[1] - b[1])
    n1 = math.hypot(*v1)
    n2 = math.hypot(*v2)
    if n1 == 0 or n2 == 0:
        return 0.0
    cos = (v1[0] * v2[0] + v1[1] * v2[1]) / (n1 * n2)
    cos = max(-1.0, min(1.0, cos))
    return math.degrees(math.acos(cos))


def to_rom(name, interior):
    if name == "shoulderElev":
        return interior
    if name in ("elbowFlex", "hipElev", "kneeFlex"):
        return 180.0 - interior
    if name == "kneeExt":
        return interior - 90.0
    raise ValueError(name)


def _visibility(points, idxs):
    return sum(points[i][2] for i in idxs) / len(idxs)


def best_side(points, name):
    """Pick the side whose three landmarks the camera sees most clearly."""
    vl = _visibility(points, JOINTS[name]["L"])
    vr = _visibility(points, JOINTS[name]["R"])
    return ("L", vl) if vl >= vr else ("R", vr)


def side_angles(px, side):
    """All angles for one side of the body; -1 where the camera can't see the joint well."""
    out = {}
    for name in JOINTS:
        idxs = JOINTS[name][side]
        if _visibility(px, idxs) < MIN_VISIBILITY:
            out[name] = -1.0
            continue
        a, b, c = (px[i] for i in idxs)
        out[name] = round(max(0.0, min(180.0, to_rom(name, angle_at(a, b, c)))), 1)
    return out


def empty_packet():
    """Packet sent when nobody is in view."""
    blank = {name: -1.0 for name in JOINTS}
    return {"visible": False, "side": "", **blank, "trunkLean": 0, "elbowDrift": 0,
            "L": dict(blank), "R": dict(blank)}


def measure(points, width, height):
    """
    points: list of 33 (x, y, visibility) tuples with x, y normalised 0..1
            (exactly what MediaPipe returns).
    Returns the packet dict that is sent to Unity. Angles that can't be
    measured reliably are -1.
      top-level angles  from whichever side the camera sees best (per angle)
      "L" / "R"         every angle for that side, used when the therapist
                        sets the operated side
    """
    px = [(p[0] * width, p[1] * height, p[2]) for p in points]
    per_side = {"L": side_angles(px, "L"), "R": side_angles(px, "R")}
    out = {"visible": True}
    sides = {}
    for name in JOINTS:
        side, _ = best_side(px, name)
        sides[name] = side
        out[name] = per_side[side][name]
    out["side"] = sides["shoulderElev"]
    out["L"], out["R"] = per_side["L"], per_side["R"]

    # Error Detector inputs (posture checks) ---------------------------------
    # Trunk lean: angle of the shoulder-midpoint -> hip-midpoint line from vertical.
    sm = ((px[L_SHOULDER][0] + px[R_SHOULDER][0]) / 2, (px[L_SHOULDER][1] + px[R_SHOULDER][1]) / 2)
    hm = ((px[L_HIP][0] + px[R_HIP][0]) / 2, (px[L_HIP][1] + px[R_HIP][1]) / 2)
    out["trunkLean"] = round(abs(math.degrees(math.atan2(sm[0] - hm[0], hm[1] - sm[1]))), 1)

    # Elbow drift during elbow flexion: how far the upper arm swings away from the trunk.
    s = sides["elbowFlex"]
    hip, sh, el = (px[i] for i in JOINTS["shoulderElev"][s])
    out["elbowDrift"] = round(angle_at(hip, sh, el), 1)
    return out
