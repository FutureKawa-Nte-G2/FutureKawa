"""Décodage d'un message MQTT et règle de bande (US #32).

Deux morceaux purs du chemin capteur : transformer un message en relevé, et
décider si un relevé tient dans la plage du pays. Ni broker ni base — c'est
justement pour cela qu'ils sont isolés du transport et de la persistance.
"""

from decimal import Decimal

import pytest

from app.consumers.payload import (
    MalformedPayloadError,
    decode,
    sensor_code_from_topic,
)
from app.services.quality import is_within_band
from tests.conftest import NOMINAL_HUMIDITY, NOMINAL_TEMP, reference_country


# --- décodage du message ----------------------------------------------------


def test_should_read_the_sensor_code_from_the_topic():
    """Le topic fait autorité : un firmware écrit son corps, pas son topic."""
    assert sensor_code_from_topic("futurekawa/BR-SEN-01") == "BR-SEN-01"


def test_should_reject_a_topic_outside_the_contract():
    for topic in ("BR-SEN-01", "autre/BR-SEN-01", "futurekawa/"):
        with pytest.raises(MalformedPayloadError):
            sensor_code_from_topic(topic)


def test_should_decode_a_well_formed_message():
    reading = decode(
        "futurekawa/BR-SEN-01",
        b'{"measuredAt":"2026-09-06T12:00:00Z","temp":21.5,"humidity":54.1}',
    )

    assert reading.sensor_code == "BR-SEN-01"
    # Decimal depuis la chaîne : un float donnerait 21.4999999999999996.
    assert reading.temperature == Decimal("21.5")
    assert reading.humidity == Decimal("54.1")


def test_should_keep_the_exact_decimal_the_firmware_sent():
    reading = decode("futurekawa/S", b'{"temp":21.55,"humidity":54.15}')

    assert str(reading.temperature) == "21.55"
    assert str(reading.humidity) == "54.15"


def test_should_fall_back_to_now_when_the_firmware_sends_no_date():
    """Un firmware sans horloge ne doit pas voir sa mesure jetée."""
    reading = decode("futurekawa/S", b'{"temp":20,"humidity":55}')

    assert reading.measured_at is not None


def test_should_reject_a_message_that_is_not_a_reading():
    for payload in (b"pas du json", b"[]", b'{"temp":20}', b'{"temp":"abc","humidity":55}'):
        with pytest.raises(MalformedPayloadError):
            decode("futurekawa/S", payload)



# --- la bande du pays ---


def test_should_treat_the_threshold_as_a_band_not_a_ceiling():
    """Trop froid abîme le café autant que trop chaud."""
    country = reference_country()[0]

    assert is_within_band(NOMINAL_TEMP, NOMINAL_HUMIDITY, country) is True
    # 20 ± 5 : 25 et 15 sont aux bords, dedans.
    assert is_within_band(Decimal("25.00"), NOMINAL_HUMIDITY, country) is True
    assert is_within_band(Decimal("15.00"), NOMINAL_HUMIDITY, country) is True
    # Au-delà, dehors — des deux côtés.
    assert is_within_band(Decimal("25.01"), NOMINAL_HUMIDITY, country) is False
    assert is_within_band(Decimal("14.99"), NOMINAL_HUMIDITY, country) is False


def test_should_check_humidity_as_well_as_temperature():
    country = reference_country()[0]

    assert is_within_band(NOMINAL_TEMP, Decimal("70.00"), country) is False


