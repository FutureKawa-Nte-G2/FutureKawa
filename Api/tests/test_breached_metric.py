"""Which metric crossed its limit first (#80).

Cases where both metrics are out pick values on which interpolation and
relative deviation disagree, so a passing test proves which rule decided.

Reference country: temperature 20 ± 5, humidity 55 ± 5.
"""

from datetime import datetime, timedelta
from decimal import Decimal

import pytest

from app.models import Country
from app.services.quality import (
    MAX_INTERPOLATION_GAP,
    Band,
    Sample,
    select_breached_metric,
)
from tests.conftest import reference_country

NOW = datetime(2026, 8, 10, 12, 0, 0)
FIVE_MINUTES_AGO = NOW - timedelta(minutes=5)


@pytest.fixture
def country() -> Country:
    return next(entity for entity in reference_country() if isinstance(entity, Country))


def _sample(temp: str, humidity: str, at: datetime = NOW) -> Sample:
    return Sample(measured_at=at, temperature=Decimal(temp), humidity=Decimal(humidity))


# --- one metric out --------------------------------------------------------


def test_should_return_nothing_when_the_reading_is_in_band(country):
    assert select_breached_metric(_sample("20", "55"), None, country) is None


def test_should_return_temperature_when_only_temperature_is_out(country):
    assert select_breached_metric(_sample("26", "55"), None, country) == "temperature"


def test_should_return_humidity_when_only_humidity_is_out(country):
    assert select_breached_metric(_sample("20", "61"), None, country) == "humidity"


# --- both out, nothing to break the tie ------------------------------------


def test_should_pick_the_largest_relative_deviation_without_a_previous_reading(country):
    assert select_breached_metric(_sample("26", "65"), None, country) == "humidity"
    assert select_breached_metric(_sample("30", "61"), None, country) == "temperature"


def test_should_ignore_a_previous_reading_older_than_the_gap(country):
    too_old = NOW - MAX_INTERPOLATION_GAP - timedelta(seconds=1)
    previous = _sample("24.8", "52", at=too_old)

    assert select_breached_metric(_sample("26", "65"), previous, country) == "humidity"


def test_should_still_interpolate_at_exactly_the_gap(country):
    previous = _sample("24.8", "52", at=NOW - MAX_INTERPOLATION_GAP)

    assert select_breached_metric(_sample("26", "65"), previous, country) == "temperature"


def test_should_pick_temperature_on_an_exact_deviation_tie(country):
    assert select_breached_metric(_sample("26", "61"), None, country) == "temperature"


# --- the previous reading breaks the tie -----------------------------------


def test_should_pick_the_metric_already_out_on_the_previous_reading(country):
    previous = _sample("24", "62", at=FIVE_MINUTES_AGO)

    assert select_breached_metric(_sample("30", "61"), previous, country) == "humidity"


def test_should_fall_back_to_deviation_when_both_were_already_out(country):
    previous = _sample("27", "63", at=FIVE_MINUTES_AGO)

    assert select_breached_metric(_sample("26", "65"), previous, country) == "humidity"


def test_should_interpolate_temperature_crossing_first(country):
    # Temperature crosses at 0.17 of the interval, humidity at 0.62.
    previous = _sample("24.8", "52", at=FIVE_MINUTES_AGO)

    assert select_breached_metric(_sample("26", "65"), previous, country) == "temperature"


def test_should_interpolate_humidity_crossing_first(country):
    # Temperature crosses at 0.64 of the interval, humidity at 0.17.
    previous = _sample("16", "59.8", at=FIVE_MINUTES_AGO)

    assert select_breached_metric(_sample("30", "61"), previous, country) == "humidity"


def test_should_interpolate_a_crossing_through_the_lower_limit(country):
    previous = _sample("15.2", "58", at=FIVE_MINUTES_AGO)

    assert select_breached_metric(_sample("14", "40"), previous, country) == "temperature"


def test_should_fall_back_to_deviation_when_both_cross_at_the_same_instant(country):
    previous = _sample("24", "58", at=FIVE_MINUTES_AGO)

    assert select_breached_metric(_sample("26", "62"), previous, country) == "humidity"


# --- band ------------------------------------------------------------------


def test_should_express_a_deviation_in_tolerances():
    band = Band(nominal=Decimal(20), tolerance=Decimal(5))

    assert band.deviation_ratio(Decimal(26)) == Decimal("1.2")
    assert band.deviation_ratio(Decimal(10)) == 2


def test_should_not_divide_by_a_zero_tolerance():
    band = Band(nominal=Decimal(20), tolerance=Decimal(0))

    assert band.deviation_ratio(Decimal(20)) == 0
    assert band.deviation_ratio(Decimal(21)) == Decimal("Infinity")
