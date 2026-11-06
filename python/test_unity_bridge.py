"""Run from repo root: .venv/bin/python -m unittest discover -s python -p 'test_*.py'."""
import json
import socket
import time
import unittest
from types import SimpleNamespace

from unity_bridge import UnityBridge, GAME_SPELLS


class UnityBridgeTests(unittest.TestCase):
    def setUp(self):
        self.game = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        self.game.bind(("127.0.0.1", 0))
        self.game.settimeout(.5)
        self.bridge = UnityBridge(*self.game.getsockname())

    def tearDown(self):
        self.bridge.close()
        self.game.close()

    def acknowledge(self, session=None):
        self.bridge.pump()
        raw, sender = self.game.recvfrom(4096)
        self.assertEqual(json.loads(raw)["type"], "ping")
        self.game.sendto(json.dumps({"type": "ack", "session": session or self.bridge.session}).encode(), sender)
        deadline = time.monotonic() + .15
        while time.monotonic() < deadline and not self.bridge.was_connected:
            self.bridge.pump()
            time.sleep(.005)

    def cast(self, spell="fire", confidence=.9, accepted=True):
        return SimpleNamespace(spell=spell, confidence=confidence, accepted=accepted,
                               label="vertical_swipe")

    def test_no_backlog_when_game_is_disconnected(self):
        self.assertFalse(self.bridge.cast(self.cast(), .55))
        self.assertEqual(self.bridge.sequence, 0)

    def test_acknowledgement_must_match_session(self):
        self.acknowledge("different-run")
        self.assertFalse(self.bridge.was_connected)

    def test_all_four_spells_are_mapped_and_sequenced(self):
        self.acknowledge()
        self.assertTrue(self.bridge.was_connected)
        for sequence, (spell, effect) in enumerate(GAME_SPELLS.items(), 1):
            self.assertTrue(self.bridge.cast(self.cast(spell), .55))
            raw, _ = self.game.recvfrom(4096)
            packet = json.loads(raw)
            self.assertEqual((packet["id"], packet["spell"]), (sequence, effect))
            self.assertEqual(packet["session"], self.bridge.session)

    def test_idle_low_confidence_and_rejected_segments_do_not_send(self):
        self.acknowledge()
        for cast in [self.cast("none"), self.cast(confidence=.54), self.cast(accepted=False),
                     self.cast(confidence=float("nan"))]:
            self.assertFalse(self.bridge.cast(cast, .55))
        self.assertEqual(self.bridge.sequence, 0)

    def test_expired_connection_stops_sending(self):
        self.acknowledge()
        self.bridge.last_ack -= 5
        self.bridge.pump()
        self.assertFalse(self.bridge.cast(self.cast(), .55))

    def test_pose_is_continuous_separate_from_cast_and_rate_limited(self):
        self.acknowledge()
        self.assertTrue(self.bridge.pose([1., 0., 0., 0.], origin=2))
        raw, _ = self.game.recvfrom(4096)
        packet = json.loads(raw)
        self.assertEqual((packet["type"], packet["origin"], packet["pose_id"]), ("pose", 2, 1))
        self.assertNotIn("spell", packet)
        self.assertFalse(self.bridge.pose([1., 0., 0., 0.]))
        self.assertEqual(self.bridge.sequence, 0)


if __name__ == "__main__":
    unittest.main()
