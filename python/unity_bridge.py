"""Small UDP link for laptop predictions -> Quest (no raw IMU streaming)."""
from __future__ import annotations

import json
import math
import socket
import time
import uuid

# Keep dataset labels unchanged; translate only at the game boundary.
GAME_SPELLS = {"fire": "fire", "wind": "wind", "lightning": "lightning",
               "water": "water"}


class UnityBridge:
    def __init__(self, host: str, port: int = 5055):
        self.address = (socket.gethostbyname(host), port)
        self.socket = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        self.socket.setblocking(False)
        self.session = uuid.uuid4().hex
        self.sequence = 0
        self.last_ping = -10.0
        self.last_ack = 0.0
        self.was_connected = False
        self.game_status = None
        self.last_pose = 0.
        self.pose_sequence = 0
        self.pose_count = 0
        self.pose_angle = 0.

    def send(self, packet: dict) -> None:
        packet = dict(packet, session=self.session)
        self.socket.sendto(json.dumps(packet, allow_nan=False).encode(), self.address)

    def pump(self) -> None:
        now = time.monotonic()
        if now - self.last_ping >= 1.0:
            self.last_ping = now
            try:
                self.send({"type": "ping"})
            except OSError as exc:
                print(f"Quest link: {exc}", flush=True)
        while True:
            try:
                raw, address = self.socket.recvfrom(4096)
            except BlockingIOError:
                break
            except OSError:
                break
            if address != self.address:
                continue
            try:
                packet = json.loads(raw)
            except (ValueError, UnicodeError):
                continue
            if not isinstance(packet, dict) or packet.get("session") != self.session:
                continue
            if packet.get("type") == "ack":
                self.last_ack = now
                self.pose_count = packet.get("pose_count", 0)
                self.pose_angle = packet.get("pose_angle", 0.)
                if packet.get("id", 0):
                    print(f"Quest cast #{packet['id']}: {packet.get('status')}", flush=True)
                elif packet.get("status") != self.game_status:
                    self.game_status = packet.get("status")
                    print(f"Quest game: {self.game_status}", flush=True)
        connected = self.last_ack > 0 and now - self.last_ack < 4.0
        if connected != self.was_connected:
            print("Quest link: CONNECTED" if connected else
                  "Quest link: no reply (check app, headset sleep and Wi-Fi)", flush=True)
            self.was_connected = connected

    def pose(self, quaternion, origin=0):
        now = time.monotonic()
        if not self.was_connected or now - self.last_pose < .02:
            return False
        self.last_pose = now
        self.pose_sequence += 1
        try:
            self.send({"type": "pose", "pose_id": self.pose_sequence,
                       "origin": origin, "q": quaternion})
        except OSError:
            return False
        return True

    def cast(self, cast, min_confidence: float) -> bool:
        if (not cast.accepted or not math.isfinite(cast.confidence)
                or cast.confidence < min_confidence
                or cast.spell not in GAME_SPELLS):
            return False
        # Never queue/replay gestures made while the headset is disconnected.
        if not self.was_connected:
            print("Gesture not sent: Quest has not acknowledged the link", flush=True)
            return False
        self.sequence += 1
        try:
            self.send({"type": "cast", "id": self.sequence,
                       "spell": GAME_SPELLS[cast.spell], "gesture": cast.label,
                       "conf": float(cast.confidence)})
        except OSError as exc:
            print(f"Quest send failed: {exc}", flush=True)
            return False
        print(f"Sent #{self.sequence}: {cast.label} -> {GAME_SPELLS[cast.spell]} "
              f"({cast.confidence:.0%})", flush=True)
        return True

    def close(self) -> None:
        self.socket.close()
