"""Run with:  python test_angles.py   (no camera or MediaPipe needed)"""
import math
import unittest

from angles import measure, angle_at

W, H = 640, 480


def base_pose():
    """A person standing upright, arms hanging, facing the camera (pixel coords)."""
    p = [(320, 240, 0.0)] * 33
    pts = {
        11: (360, 160), 12: (280, 160),   # shoulders (patient's left is on image right)
        13: (360, 240), 14: (280, 240),   # elbows straight down
        15: (360, 310), 16: (280, 310),   # wrists
        23: (360, 300), 24: (280, 300),   # hips
        25: (360, 380), 26: (280, 380),   # knees
        27: (360, 460), 28: (280, 460),   # ankles
    }
    p = list(p)
    for i, (x, y) in pts.items():
        p[i] = (x, y, 0.99)
    return p


def normalise(p):
    return [(x / W, y / H, v) for x, y, v in p]


def rot(origin, length, deg_from_down, direction=1):
    """Point at `length` from origin, rotated `deg_from_down` from straight down."""
    t = math.radians(deg_from_down)
    return (origin[0] + direction * math.sin(t) * length, origin[1] + math.cos(t) * length)


class AngleTests(unittest.TestCase):
    def test_interior(self):
        self.assertAlmostEqual(angle_at((0, 1), (0, 0), (1, 0)), 90.0, places=4)
        self.assertAlmostEqual(angle_at((0, 1), (0, 0), (0, -1)), 180.0, places=4)

    def test_rest_pose_is_zero(self):
        m = measure(normalise(base_pose()), W, H)
        self.assertLess(m["shoulderElev"], 3)
        self.assertLess(m["elbowFlex"], 3)
        self.assertLess(m["hipElev"], 3)
        self.assertLess(m["kneeFlex"], 3)
        self.assertLess(m["trunkLean"], 1)

    def test_shoulder_abduction_90(self):
        p = base_pose()
        sh = p[11][:2]
        el = rot(sh, 80, 90, +1)
        wr = rot(sh, 150, 90, +1)
        p[13] = (*el, 0.99); p[15] = (*wr, 0.99)
        p[14] = (p[14][0], p[14][1], 0.3)   # right arm less visible -> left side chosen
        m = measure(normalise(p), W, H)
        self.assertAlmostEqual(m["shoulderElev"], 90, delta=1.0)

    def test_elbow_flexion_120(self):
        p = base_pose()
        el = p[13][:2]
        wr = rot(el, 70, 120, +1)
        p[15] = (*wr, 0.99)
        p[16] = (p[16][0], p[16][1], 0.2)
        m = measure(normalise(p), W, H)
        self.assertAlmostEqual(m["elbowFlex"], 120, delta=1.0)

    def test_knee_extension_seated(self):
        p = base_pose()
        hip = (300, 300); knee = (380, 300)       # thigh horizontal (seated, side view)
        ankle = rot(knee, 90, 60, +1)             # shin raised 60 deg from hanging
        p[23] = (*hip, 0.99); p[25] = (*knee, 0.99); p[27] = (*ankle, 0.99)
        for i in (24, 26, 28):
            p[i] = (p[i][0], p[i][1], 0.1)
        m = measure(normalise(p), W, H)
        self.assertAlmostEqual(m["kneeExt"], 60, delta=1.0)

    def test_trunk_lean(self):
        p = base_pose()
        for i in (11, 12):
            p[i] = (p[i][0] + 40, p[i][1], 0.99)    # shoulders shifted sideways
        m = measure(normalise(p), W, H)
        self.assertGreater(m["trunkLean"], 12)

    def test_sit_to_stand_uses_knee_extension(self):
        p = base_pose()                            # standing: knee straight
        m = measure(normalise(p), W, H)
        self.assertAlmostEqual(m["kneeExt"], 90, delta=1.0)

    def test_hip_abduction_25(self):
        p = base_pose()
        hip = p[23][:2]
        knee = rot(hip, 80, 25, +1)                # leg out to the side, front view
        p[25] = (*knee, 0.99); p[27] = (*rot(knee, 80, 25, +1), 0.99)
        for i in (24, 26, 28):
            p[i] = (p[i][0], p[i][1], 0.1)
        m = measure(normalise(p), W, H)
        self.assertAlmostEqual(m["hipElev"], 25, delta=1.0)

    def test_straight_leg_raise_45_lying(self):
        p = base_pose()
        sh, hip = (100, 300), (300, 300)          # lying on the back, side view
        knee = (hip[0] + 90 * math.cos(math.radians(45)), hip[1] - 90 * math.sin(math.radians(45)))
        ankle = (hip[0] + 180 * math.cos(math.radians(45)), hip[1] - 180 * math.sin(math.radians(45)))
        p[11] = (*sh, 0.99); p[23] = (*hip, 0.99); p[25] = (*knee, 0.99); p[27] = (*ankle, 0.99)
        for i in (12, 24, 26, 28):
            p[i] = (p[i][0], p[i][1], 0.1)
        m = measure(normalise(p), W, H)
        self.assertAlmostEqual(m["hipElev"], 45, delta=1.0)
        self.assertLess(m["kneeFlex"], 2)          # knee kept straight

    def test_knee_flexion_mini_squat(self):
        p = base_pose()
        knee = p[25][:2]
        ankle = rot(knee, 80, 45, -1)              # shin swings back 45 deg
        p[27] = (*ankle, 0.99)
        for i in (24, 26, 28):
            p[i] = (p[i][0], p[i][1], 0.1)
        m = measure(normalise(p), W, H)
        self.assertAlmostEqual(m["kneeFlex"], 45, delta=1.0)

    def test_both_sides_reported(self):
        p = base_pose()
        sh = p[12][:2]
        p[14] = (*rot(sh, 80, 60, -1), 0.99)       # right arm raised 60, left arm down
        m = measure(normalise(p), W, H)
        self.assertAlmostEqual(m["R"]["shoulderElev"], 60, delta=1.0)
        self.assertLess(m["L"]["shoulderElev"], 3)

    def test_hidden_joint_reports_minus_one(self):
        p = [(x, y, 0.1) for x, y, _ in base_pose()]
        m = measure(normalise(p), W, H)
        self.assertEqual(m["kneeExt"], -1.0)
        self.assertEqual(m["L"]["hipElev"], -1.0)


if __name__ == "__main__":
    unittest.main(verbosity=2)
