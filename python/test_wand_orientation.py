import unittest
from types import SimpleNamespace
import numpy as np

from wand_orientation import WandOrientation


class OrientationTests(unittest.TestCase):
    def sample(self, ms, gyro=(0, 0, 0), acc=(0, 0, 0)):
        return SimpleNamespace(t_ms=ms, gyro=np.array(gyro, dtype=float), acc=np.array(acc, dtype=float))

    def test_90_degree_horizontal_swing_and_return(self):
        o = WandOrientation()
        o.update(self.sample(0))
        for i in range(1, 101):
            o.update(self.sample(i * 10, (90, 0, 0)))
        np.testing.assert_allclose(o.unity_quaternion(), [2**-.5, 0, 2**-.5, 0], atol=1e-6)
        for i in range(101, 201):
            o.update(self.sample(i * 10, (-90, 0, 0)))
        np.testing.assert_allclose(o.unity_quaternion(), [1, 0, 0, 0], atol=1e-6)

    def test_gravity_limits_stationary_tilt_drift(self):
        o = WandOrientation()
        o.update(self.sample(0, acc=(0, 0, 1)))
        o.q = np.array([np.cos(np.deg2rad(5)), np.sin(np.deg2rad(5)), 0., 0.])
        for i in range(1, 1001):
            o.update(self.sample(i * 10, acc=(0, 0, 1)))
        self.assertLess(np.rad2deg(2 * np.arccos(o.q[0])), 1.)

    def test_gaps_invalid_samples_and_reset_do_not_jump(self):
        o = WandOrientation()
        o.update(self.sample(0))
        o.update(self.sample(2000, (1000, 0, 0)))
        o.update(self.sample(2010, (float("nan"), 0, 0)))
        np.testing.assert_allclose(o.unity_quaternion(), [1, 0, 0, 0])
        origin = o.origin
        o.reset()
        self.assertEqual(o.origin, origin + 1)

    def test_axis_mapping_is_configurable(self):
        o = WandOrientation("x,y,z")
        o.update(self.sample(0))
        o.update(self.sample(10, (90, 0, 0)))
        self.assertGreater(o.unity_quaternion()[1], 0)
        with self.assertRaises(ValueError):
            WandOrientation("x,x,z")
