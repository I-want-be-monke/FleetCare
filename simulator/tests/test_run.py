import unittest
from unittest.mock import patch
from urllib.error import HTTPError

from simulator import run


class SimulatorTests(unittest.TestCase):
    def test_deterministic_ids(self):
        vehicle = "00000000-0000-0000-0000-000000000001"
        a = run.event_id(42, "TREND", vehicle, 7)
        self.assertEqual(a, run.event_id(42, "TREND", vehicle, 7))
        self.assertNotEqual(a, run.event_id(43, "TREND", vehicle, 7))
        self.assertNotEqual(a, run.event_id(42, "TREND", vehicle, 8))

    def test_names_are_namespaced(self):
        self.assertEqual(run.vehicle_name(42, "SERVICE"), "FC-DEMO-42-SERVICE")

    def test_repeated_reading_requires_matching_stored_event(self):
        conflict = HTTPError("http://test", 409, "conflict", {}, None)
        with patch.object(run, "request_json", side_effect=[
            conflict,
            [{"eventId": run.event_id(42, "FAULT", "00000000-0000-0000-0000-000000000001", 0),
              "vehicleId": "00000000-0000-0000-0000-000000000001", "odometerKm": 7010, "engineTempC": 83, "faultCode": "P0116"}],
        ]) as request:
            run.send_reading(42, "FAULT", "00000000-0000-0000-0000-000000000001",
                             0, 7010, 83, "P0116")
        self.assertEqual(request.call_count, 2)

    def test_mismatched_replay_rejected(self):
        conflict = HTTPError("http://test", 409, "conflict", {}, None)
        with patch.object(run, "request_json", side_effect=[
            conflict, [{"eventId": run.event_id(42, "FAULT", "00000000-0000-0000-0000-000000000001", 0),
                        "vehicleId": "00000000-0000-0000-0000-000000000001", "odometerKm": 9000, "engineTempC": 83, "faultCode": "P0116"}],
        ]):
            with self.assertRaises(RuntimeError):
                run.send_reading(42, "FAULT", "00000000-0000-0000-0000-000000000001",
                                 0, 7010, 83)

    def test_seed_bounds(self):
        with self.assertRaises(ValueError):
            run.run_scenario(-1)
        with self.assertRaises(ValueError):
            run.run_scenario(10001)


if __name__ == "__main__":
    unittest.main()
