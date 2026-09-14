"""Quelle grandeur a franchi son seuil en premier (#80).

Le siège attend `temperature` ou `humidity`, une seule. Tout se joue quand les
deux débordent sur le même relevé : les cas ci-dessous choisissent exprès des
valeurs où l'interpolation et l'écart relatif donnent des réponses opposées,
pour qu'un test vert prouve quelle branche a tranché.

Fonction pure, sans base. Seuils du pays de référence : température 20 ± 5
(15 à 25), humidité 55 ± 5 (50 à 60).
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


# --- une seule grandeur hors bande -----------------------------------------


def test_should_return_nothing_when_the_reading_is_in_band(country):
    assert select_breached_metric(_sample("20", "55"), None, country) is None


def test_should_return_temperature_when_only_temperature_is_out(country):
    assert select_breached_metric(_sample("26", "55"), None, country) == "temperature"


def test_should_return_humidity_when_only_humidity_is_out(country):
    assert select_breached_metric(_sample("20", "61"), None, country) == "humidity"


# --- les deux hors bande, rien pour départager -----------------------------


def test_should_pick_the_largest_relative_deviation_without_a_previous_reading(country):
    # Température 6/5 = 1,2 tolérance, humidité 10/5 = 2.
    assert select_breached_metric(_sample("26", "65"), None, country) == "humidity"
    # Température 10/5 = 2, humidité 6/5 = 1,2.
    assert select_breached_metric(_sample("30", "61"), None, country) == "temperature"


def test_should_ignore_a_previous_reading_older_than_the_gap(country):
    # Interpolé, la température passerait en premier ; trop ancien, on compare
    # les écarts, et l'humidité (2 tolérances) l'emporte.
    too_old = NOW - MAX_INTERPOLATION_GAP - timedelta(seconds=1)
    previous = _sample("24.8", "52", at=too_old)

    assert select_breached_metric(_sample("26", "65"), previous, country) == "humidity"


def test_should_still_interpolate_at_exactly_the_gap(country):
    previous = _sample("24.8", "52", at=NOW - MAX_INTERPOLATION_GAP)

    assert select_breached_metric(_sample("26", "65"), previous, country) == "temperature"


def test_should_pick_temperature_on_an_exact_deviation_tie(country):
    """Égalité parfaite : un résultat déterministe, pas une règle métier."""
    assert select_breached_metric(_sample("26", "61"), None, country) == "temperature"


# --- le relevé précédent départage -----------------------------------------


def test_should_pick_the_metric_already_out_on_the_previous_reading(country):
    # L'écart relatif désignerait la température (2 contre 1,2) ; mais
    # l'humidité débordait déjà, seule, au relevé d'avant.
    previous = _sample("24", "62", at=FIVE_MINUTES_AGO)

    assert select_breached_metric(_sample("30", "61"), previous, country) == "humidity"


def test_should_fall_back_to_deviation_when_both_were_already_out(country):
    previous = _sample("27", "63", at=FIVE_MINUTES_AGO)

    assert select_breached_metric(_sample("26", "65"), previous, country) == "humidity"


def test_should_interpolate_temperature_crossing_first(country):
    # Température : 24,8 → 26, borne 25, fraction 0,2 / 1,2 ≈ 0,17.
    # Humidité    : 52 → 65,   borne 60, fraction 8 / 13 ≈ 0,62.
    # L'écart relatif aurait dit humidité (2 contre 1,2).
    previous = _sample("24.8", "52", at=FIVE_MINUTES_AGO)

    assert select_breached_metric(_sample("26", "65"), previous, country) == "temperature"


def test_should_interpolate_humidity_crossing_first(country):
    # Température : 16 → 30,     borne 25, fraction 9 / 14 ≈ 0,64.
    # Humidité    : 59,8 → 61,   borne 60, fraction 0,2 / 1,2 ≈ 0,17.
    # L'écart relatif aurait dit température (2 contre 1,2).
    previous = _sample("16", "59.8", at=FIVE_MINUTES_AGO)

    assert select_breached_metric(_sample("30", "61"), previous, country) == "humidity"


def test_should_interpolate_a_crossing_through_the_lower_limit(country):
    # Température : 15,2 → 14, borne basse 15, fraction 0,2 / 1,2 ≈ 0,17.
    # Humidité    : 58 → 40,   borne basse 50, fraction 8 / 18 ≈ 0,44.
    # L'écart relatif aurait dit humidité (3 contre 1,2).
    previous = _sample("15.2", "58", at=FIVE_MINUTES_AGO)

    assert select_breached_metric(_sample("14", "40"), previous, country) == "temperature"


def test_should_fall_back_to_deviation_when_both_cross_at_the_same_instant(country):
    # Température : 24 → 26, fraction 1 / 2 = 0,5.
    # Humidité    : 58 → 62, fraction 2 / 4 = 0,5.
    # Égalité : l'humidité, 1,4 tolérance contre 1,2, l'emporte.
    previous = _sample("24", "58", at=FIVE_MINUTES_AGO)

    assert select_breached_metric(_sample("26", "62"), previous, country) == "humidity"


# --- la bande --------------------------------------------------------------


def test_should_express_a_deviation_in_tolerances():
    band = Band(nominal=Decimal(20), tolerance=Decimal(5))

    assert band.deviation_ratio(Decimal(26)) == Decimal("1.2")
    assert band.deviation_ratio(Decimal(10)) == 2


def test_should_not_divide_by_a_zero_tolerance():
    band = Band(nominal=Decimal(20), tolerance=Decimal(0))

    assert band.deviation_ratio(Decimal(20)) == 0
    assert band.deviation_ratio(Decimal(21)) == Decimal("Infinity")
