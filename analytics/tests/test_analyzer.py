from uuid import uuid4

from analytics.analyzer import Sample, analyze, make_risk_id


def samples(*temperatures):
    return [Sample(uuid4(), temp) for temp in temperatures]


def test_insufficient_history():
    assert analyze(samples(80, 81, 80, 79, 80, 90, 92)).status == "insufficient"


def test_normal_temperatures():
    assert analyze(samples(80, 81, 79, 80, 82, 81, 80, 82)).status == "normal"


def test_first_sustained_rise_is_reported_once():
    readings = samples(80, 81, 80, 79, 80, 90, 92, 94, 96)
    first = analyze(readings[:8])
    assert first.status == "risk"
    assert first.score == 0.5
    assert "90/92/94" in first.explanation
    assert analyze(readings).status == "normal"


def test_short_spike_and_decline_are_normal():
    assert analyze(samples(80, 80, 80, 80, 80, 95, 81, 80)).status == "normal"
    assert analyze(samples(80, 80, 80, 80, 80, 94, 92, 90)).status == "normal"


def test_risk_id_is_deterministic():
    vehicle, event = uuid4(), uuid4()
    assert make_risk_id(vehicle, event) == make_risk_id(vehicle, event)
    assert make_risk_id(vehicle, event) != make_risk_id(vehicle, uuid4())
