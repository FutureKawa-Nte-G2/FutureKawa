"""The measurement history of one batch.

Read-only: this module never writes to `measurements` nor to `alerts`. It joins
what the ingestion consumer and the firmware already produced.

The join that matters is the sensor window. A sensor belongs to a warehouse,
not to a batch, so its readings only concern this batch while it was assigned
to it — `sensor_assignments.assigned_at` to `released_at ?? now()`. Without
that clip, every batch a room ever held would be served the room's whole
history, which is the same curve for all of them.
"""

from collections import defaultdict
from datetime import UTC, date, datetime, timedelta
from decimal import ROUND_HALF_UP, Decimal

from sqlalchemy import and_, or_, select
from sqlalchemy.ext.asyncio import AsyncSession

from app.models import Alert, Batch, Country, Measurement, SensorAssignment, Warehouse
from app.schemas.batch_history import (
    AlertRead,
    BatchMeasurementHistory,
    DailyPoint,
    Granularity,
    RawPoint,
    Thresholds,
)

# A year of 5-minute readings is ~105 000 points. `raw` is for zooming into an
# incident, not for drawing a storage period, so the window is bounded rather
# than paginated: a caller that wants the whole period wants `daily`.
RAW_MAX_WINDOW = timedelta(days=7)


class BatchNotFoundError(Exception):
    code = "batch_not_found"

    def __init__(self, batch_id: str) -> None:
        self.message = f"No batch found with id {batch_id}."
        super().__init__(self.message)


class WindowTooWideError(Exception):
    code = "window_too_wide"

    def __init__(self) -> None:
        self.message = (
            f"A raw window cannot exceed {RAW_MAX_WINDOW.days} days. "
            "Use granularity=daily for a longer period."
        )
        super().__init__(self.message)


def _two_decimals(value: Decimal | float) -> Decimal:
    """Bring an aggregate back to the 2 decimals of the contract.

    Same reason as in `services/measurements.py`: `avg()` widens the scale on
    PostgreSQL and hands back a float on SQLite.
    """
    return Decimal(str(value)).quantize(Decimal("0.01"), rounding=ROUND_HALF_UP)


def _as_utc(value: datetime) -> datetime:
    """Stamp UTC on a value going out on the wire.

    SQLite hands back a `timestamptz` naive while PostgreSQL hands it aware, so
    left alone the same field would serialise as `...T00:00:00` on one engine
    and `...T00:00:00Z` on the other. A browser reads the first as local time:
    the chart axis would shift by the viewer's offset. Everything stored is UTC,
    so saying so costs nothing and removes the ambiguity.
    """
    if value.tzinfo is None:
        return value.replace(tzinfo=UTC)
    return value.astimezone(UTC)


def _as_naive_utc(value: datetime) -> datetime:
    """Drop the offset after normalising to UTC.

    SQLite gives back a `timestamptz` naive while PostgreSQL gives it aware, so
    the two sides of a comparison would raise TypeError depending on the engine.
    Everything is compared in UTC here, so the offset carries no information
    once normalised.
    """
    if value.tzinfo is None:
        return value
    return value.astimezone(UTC).replace(tzinfo=None)


async def batch_history(
    session: AsyncSession,
    batch_id: str,
    granularity: Granularity = Granularity.DAILY,
    window_from: datetime | None = None,
    window_to: datetime | None = None,
    now: datetime | None = None,
) -> BatchMeasurementHistory:
    """Readings, alerts and country band for one batch.

    Serves a scrapped or shipped batch like any other: its curve is precisely
    what a quality review looks at afterwards, so nothing filters on status.
    """
    row = (
        await session.execute(
            select(Batch, Country)
            .join(Warehouse, Warehouse.warehouse_id == Batch.warehouse_id)
            .join(Country, Country.country_id == Warehouse.country_id)
            .where(Batch.batch_id == batch_id)
        )
    ).first()

    if row is None:
        raise BatchNotFoundError(str(batch_id))

    batch, country = row

    thresholds = Thresholds(
        nominal_temp=country.nominal_temp,
        tolerance_temp=country.tolerance_temp,
        nominal_humidity=country.nominal_humidity,
        tolerance_humidity=country.tolerance_humidity,
    )

    empty = BatchMeasurementHistory(
        batch_id=str(batch.batch_id),
        batch_ref=batch.batch_ref,
        granularity=granularity,
        window_from=None,
        window_to=None,
        thresholds=thresholds,
        daily_points=[],
        raw_points=[],
        alerts=[],
    )

    # --- the windows a sensor was actually on this batch --------------------

    reference_now = _as_naive_utc(now or datetime.now(UTC))

    assignments = (
        await session.execute(
            select(SensorAssignment)
            .where(SensorAssignment.batch_id == batch_id)
            .order_by(SensorAssignment.assigned_at)
        )
    ).scalars().all()

    if not assignments:
        # Received but never equipped. An empty curve, not an error.
        return empty

    windows = [
        (
            assignment.sensor_id,
            _as_naive_utc(assignment.assigned_at),
            _as_naive_utc(assignment.released_at) if assignment.released_at else reference_now,
        )
        for assignment in assignments
    ]

    served_from = min(start for _, start, _ in windows)
    served_to = max(end for _, _, end in windows)

    # --- clip to what the caller asked for ----------------------------------

    if window_from is not None:
        served_from = max(served_from, _as_naive_utc(window_from))
    if window_to is not None:
        served_to = min(served_to, _as_naive_utc(window_to))

    if granularity is Granularity.RAW:
        # Default to the tail of the period rather than refusing a call with no
        # window: the last days are what someone opening a curve looks at first.
        if window_from is None and served_to - served_from > RAW_MAX_WINDOW:
            served_from = served_to - RAW_MAX_WINDOW
        if served_to - served_from > RAW_MAX_WINDOW:
            raise WindowTooWideError()

    if served_from >= served_to:
        # The requested window falls outside every assignment.
        return empty.model_copy(
            update={"window_from": _as_utc(served_from), "window_to": _as_utc(served_from)}
        )

    # A reading counts when it falls inside one window AND comes from that
    # window's sensor. Expressed as one OR of ranges rather than a join, so a
    # sensor reassigned to another batch mid-period cannot leak in.
    in_a_window = or_(
        *[
            and_(
                Measurement.sensor_id == sensor_id,
                Measurement.meas_date >= max(start, served_from),
                Measurement.meas_date < min(end, served_to),
            )
            for sensor_id, start, end in windows
            if max(start, served_from) < min(end, served_to)
        ]
    )

    measurements = (
        await session.execute(
            select(Measurement).where(in_a_window).order_by(Measurement.meas_date)
        )
    ).scalars().all()

    daily_points: list[DailyPoint] = []
    raw_points: list[RawPoint] = []

    if granularity is Granularity.RAW:
        raw_points = [
            RawPoint(
                meas_date=_as_utc(measurement.meas_date),
                temp=measurement.meas_temp,
                humidity=measurement.meas_humidity,
            )
            for measurement in measurements
        ]
    else:
        daily_points = _aggregate_by_day(measurements)

    return BatchMeasurementHistory(
        batch_id=str(batch.batch_id),
        batch_ref=batch.batch_ref,
        granularity=granularity,
        window_from=_as_utc(served_from),
        window_to=_as_utc(served_to),
        thresholds=thresholds,
        daily_points=daily_points,
        raw_points=raw_points,
        alerts=await _alerts_overlapping(
            session, batch, served_from, served_to, reference_now
        ),
    )


def _aggregate_by_day(measurements: list[Measurement]) -> list[DailyPoint]:
    """Group readings by UTC calendar day.

    Aggregated in Python rather than in SQL: `date(timestamptz)` resolves
    against the server's TimeZone setting on PostgreSQL and returns a string on
    SQLite, so the same query would group differently on the two engines the
    project runs. The UTC cut is the same simplification already documented in
    `services/measurements.py` — a Brazilian day (UTC-3) splits its night
    across two UTC days.
    """
    buckets: dict[date, list[Measurement]] = defaultdict(list)
    for measurement in measurements:
        buckets[_as_naive_utc(measurement.meas_date).date()].append(measurement)

    points = []
    for day in sorted(buckets):
        temps = [m.meas_temp for m in buckets[day]]
        humidities = [m.meas_humidity for m in buckets[day]]
        points.append(
            DailyPoint(
                day=day,
                avg_temp=_two_decimals(sum(temps) / len(temps)),
                min_temp=_two_decimals(min(temps)),
                max_temp=_two_decimals(max(temps)),
                avg_humidity=_two_decimals(sum(humidities) / len(humidities)),
                min_humidity=_two_decimals(min(humidities)),
                max_humidity=_two_decimals(max(humidities)),
            )
        )
    return points


async def _alerts_overlapping(
    session: AsyncSession,
    batch: Batch,
    served_from: datetime,
    served_to: datetime,
    reference_now: datetime,
) -> list[AlertRead]:
    """Alerts the frontend needs to shade the out-of-band periods.

    Two kinds, deliberately. An `expiration` alert carries `batch_id`. A
    `condition` alert carries none — `models.py` reserves it for the room — yet
    it is the one that marks a period outside the band, which is exactly what
    the issue asks the frontend to highlight. Returning only the batch's own
    alerts would leave that shading permanently empty.
    """
    rows = (
        await session.execute(
            select(Alert)
            .where(
                or_(
                    Alert.batch_id == batch.batch_id,
                    and_(
                        Alert.warehouse_id == batch.warehouse_id,
                        Alert.alert_type == "condition",
                    ),
                )
            )
            .order_by(Alert.created_at)
        )
    ).scalars().all()

    overlapping = []
    for alert in rows:
        created = _as_naive_utc(alert.created_at)
        resolved = _as_naive_utc(alert.resolved_at) if alert.resolved_at else reference_now
        # An open alert runs to now, so it overlaps any window that has not ended.
        if created < served_to and resolved > served_from:
            overlapping.append(
                AlertRead(
                    alert_id=str(alert.alert_id),
                    alert_type=alert.alert_type,
                    alert_status=alert.alert_status,
                    created_at=_as_utc(alert.created_at),
                    resolved_at=_as_utc(alert.resolved_at) if alert.resolved_at else None,
                    concerns_batch=alert.batch_id == batch.batch_id,
                )
            )
    return overlapping
