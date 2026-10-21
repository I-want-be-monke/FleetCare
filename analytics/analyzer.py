"""Explainable temperature trend detection for one vehicle."""

from dataclasses import dataclass
from statistics import median
from uuid import UUID, uuid5

RISK_KIND = "EngineTemperatureTrend"
RISK_NAMESPACE = UUID("1a47d070-2865-4ce7-928c-b9183ae61d67")
BASELINE_COUNT = 5
TREND_COUNT = 3
WINDOW_SIZE = BASELINE_COUNT + TREND_COUNT
MIN_DEVIATION_C = 8.0


@dataclass(frozen=True)
class Sample:
    event_id: UUID
    temp_c: float


@dataclass(frozen=True)
class AnalysisResult:
    status: str  # insufficient, normal, risk
    score: float | None = None
    explanation: str | None = None


def _evaluate_window(samples: list[Sample]) -> AnalysisResult:
    if len(samples) < WINDOW_SIZE:
        return AnalysisResult("insufficient")

    window = samples[-WINDOW_SIZE:]
    baseline = median(item.temp_c for item in window[:BASELINE_COUNT])
    trend = [item.temp_c for item in window[BASELINE_COUNT:]]
    deviation = min(trend) - baseline
    if deviation < MIN_DEVIATION_C or trend != sorted(trend):
        return AnalysisResult("normal")

    score = min(1.0, deviation / 20.0)
    values = "/".join(f"{value:g}" for value in trend)
    return AnalysisResult(
        "risk", score,
        f"Медиана {baseline:g} °C; три показания {values} °C; "
        f"минимальное отклонение {deviation:g} °C.",
    )


def analyze(samples: list[Sample]) -> AnalysisResult:
    """Emit only on the first window of a sustained anomaly."""
    current = _evaluate_window(samples)
    if current.status != "risk":
        return current
    if len(samples) > WINDOW_SIZE and _evaluate_window(samples[:-1]).status == "risk":
        return AnalysisResult("normal")
    return current


def make_risk_id(vehicle_id: UUID, source_event_id: UUID) -> UUID:
    return uuid5(RISK_NAMESPACE, f"{vehicle_id}:{RISK_KIND}:{source_event_id}")
