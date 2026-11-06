"""Relative 6-axis IMU orientation; never integrates acceleration into position.

Gyro integration follows fast motion. A gentle gravity correction limits tilt
drift while acceleration is close to 1 g. Heading still drifts without a compass.
Quaternions are scalar-first (w,x,y,z).
"""
import numpy as np


def multiply(a, b):
    w, v = a[0], a[1:]
    return np.r_[w * b[0] - np.dot(v, b[1:]),
                 w * b[1:] + b[0] * v + np.cross(v, b[1:])]


def rotate(q, v):
    return v + 2 * np.cross(q[1:], np.cross(q[1:], v) + q[0] * v)


class WandOrientation:
    def __init__(self, axes="y,x,-z"):
        self.axes = []
        for axis in axes.split(","):
            name = axis.lstrip("+-")
            if name not in ("x", "y", "z"):
                raise ValueError("wand axes must be a signed permutation, e.g. y,x,-z")
            self.axes.append((("x", "y", "z").index(name), -1 if axis.startswith("-") else 1))
        if len(self.axes) != 3 or len({a[0] for a in self.axes}) != 3:
            raise ValueError("wand axes must use x, y and z exactly once")
        self.origin = -1
        self.reset()

    def reset(self):
        self.q = np.array([1., 0., 0., 0.])
        self.gravity = None
        self.last_ms = None
        self.origin += 1

    def update(self, sample):
        if not np.isfinite(sample.gyro).all() or not np.isfinite(sample.acc).all():
            return
        if self.last_ms is None:
            self.last_ms = sample.t_ms
            norm = np.linalg.norm(sample.acc)
            if norm > .1:
                self.gravity = sample.acc / norm
            return
        dt = (sample.t_ms - self.last_ms) / 1000.
        self.last_ms = sample.t_ms
        # Do not fabricate rotations across missing USB data or time resets.
        if not 0 < dt <= .1:
            return
        rate = np.deg2rad(sample.gyro)
        norm = np.linalg.norm(sample.acc)
        if self.gravity is not None and .9 < norm < 1.1 and np.linalg.norm(rate) < 1.5:
            inverse = self.q * np.array([1., -1., -1., -1.])
            predicted = rotate(inverse, self.gravity)
            rate += .5 * np.cross(sample.acc / norm, predicted)
        angle = np.linalg.norm(rate) * dt
        if angle > 1e-12:
            delta = np.r_[np.cos(angle / 2), rate / np.linalg.norm(rate) * np.sin(angle / 2)]
            self.q = multiply(self.q, delta)
            self.q /= np.linalg.norm(self.q)

    def unity_quaternion(self):
        # Matches the dataset tracer: gx moves horizontally, gy vertically.
        # Signed axes are configurable because the IMU can be mounted differently.
        return [float(self.q[0])] + [float(self.q[i + 1] * sign) for i, sign in self.axes]
