"""The measurement history of one batch, as the quality page reads it.

Two granularities behind one route. `daily` is what a control chart over a
storage period needs: a batch may sit for a year, and the firmware already
averages to one point per 5 minutes, so raw would be ~105 000 points for a
single curve. `raw` exists for zooming into a short window — an incident, a
suspect night — and is capped for that reason.

camelCase on the wire, like `/api/measurements` and `/api/batches`: the reader
is head office's .NET relay, whose `PropertyNameCaseInsensitive` tolerates a
different case but not a different name.
"""

from datetime import date, datetime
from decimal import Decimal
from enum import Enum
from typing import Annotated

from pydantic import BaseModel, ConfigDict, PlainSerializer
from pydantic.alias_generators import to_camel

# Same reason as in `schemas/measurement.py`: a quoted number would not bind to
# a `decimal` on the .NET side, and the failure is silent.
WireDecimal = Annotated[
    Decimal, PlainSerializer(float, return_type=float, when_used="json")
]


class Granularity(str, Enum):
    DAILY = "daily"
    RAW = "raw"


class CamelModel(BaseModel):
    model_config = ConfigDict(alias_generator=to_camel, populate_by_name=True)


class Thresholds(CamelModel):
    """The country's band, so the frontend draws its lines without a second call.

    A band, not a ceiling: a reading is out of range when it leaves
    `nominal ± tolerance`, too low as well as too high. `mcd_warehouse.puml`,
    `mld_warehouse.puml` and head office's `Country.cs` all agree on these four
    names; the `max_temp`/`max_humidity` of issue #36 exist nowhere in the model.
    """

    nominal_temp: WireDecimal
    tolerance_temp: WireDecimal
    nominal_humidity: WireDecimal
    tolerance_humidity: WireDecimal


class DailyPoint(CamelModel):
    """One calendar day, aggregated. Min and max carry the excursions an
    average would flatten away — which is the whole point of a control chart."""

    day: date
    avg_temp: WireDecimal
    min_temp: WireDecimal
    max_temp: WireDecimal
    avg_humidity: WireDecimal
    min_humidity: WireDecimal
    max_humidity: WireDecimal


class RawPoint(CamelModel):
    """One reading, as the firmware sent it. No recomputation here."""

    meas_date: datetime
    temp: WireDecimal
    humidity: WireDecimal


class AlertRead(CamelModel):
    """An alert overlapping the window, so the frontend can shade the period.

    Both kinds are returned. An `expiration` alert carries the batch id; a
    `condition` alert belongs to the room and carries none — yet it is exactly
    the one that marks an out-of-band period. Returning only the batch's own
    alerts would leave the shading empty, which is the feature the issue asks
    for.
    """

    alert_id: str
    alert_type: str
    alert_status: str
    created_at: datetime
    resolved_at: datetime | None
    # False when the alert belongs to the warehouse rather than to this batch.
    concerns_batch: bool


class BatchMeasurementHistory(CamelModel):
    """Everything the curve needs, in one response."""

    batch_id: str
    batch_ref: str
    granularity: Granularity
    # The window actually served, which is not always the one asked for: it is
    # clipped to the periods a sensor was really on this batch. Both are null
    # when no sensor has ever been assigned — a batch received but not yet
    # equipped, which is a legitimate state and not an error.
    window_from: datetime | None
    window_to: datetime | None
    thresholds: Thresholds
    daily_points: list[DailyPoint]
    raw_points: list[RawPoint]
    alerts: list[AlertRead]
