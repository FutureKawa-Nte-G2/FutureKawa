"""SQLAlchemy models of the country database.

Mirrors `Documentation/diagrammes/mld_warehouse.puml` on `develop`: plural table
names, `<entity>_id` primary keys, uuid throughout, `varchar` lengths as
specified. Where a value belongs to an enum owned by head office
(`FutureKawaSiege.Data/Entities`), the constant lives here so the two sides
cannot drift apart silently.

No relationships are declared: every service so far joins explicitly, and an
unused relationship is one more thing to keep in sync. Add them when a caller
needs one.
"""

import uuid
from datetime import date, datetime
from decimal import Decimal

from sqlalchemy import (
    Boolean,
    CheckConstraint,
    Date,
    DateTime,
    ForeignKey,
    Index,
    Integer,
    Numeric,
    String,
    Text,
    UniqueConstraint,
    text,
)
from sqlalchemy.orm import DeclarativeBase, Mapped, mapped_column


class Base(DeclarativeBase):
    pass


# --- vocabulary shared with head office ------------------------------------

# `BatchQualityGrade { A, B, C }` — hence varchar(8) on the column.
QUALITY_GRADES = ("A", "B", "C")

# `BatchStatus { Stored, Shipped, Delivered, Expired }`, lowercased on the wire.
BATCH_STATUSES = ("stored", "shipped", "delivered", "expired")

ALERT_TYPES = ("condition", "expiration")
ALERT_STATUSES = ("active", "resolved")

# What a notification tells the warehouse about. `batch_non_compliant` is
# written by the quality service alongside a `condition` alert (US #30);
# `order_received` by the order service. Same table, same read API, two
# producers — hence a type rather than two tables.
NOTIFICATION_TYPES = ("batch_non_compliant", "order_received")


# --- reference data --------------------------------------------------------


class Country(Base):
    __tablename__ = "countries"

    country_id: Mapped[uuid.UUID] = mapped_column(primary_key=True, default=uuid.uuid4)
    country_name: Mapped[str] = mapped_column(String(128))
    country_code: Mapped[str] = mapped_column(String(8), unique=True)
    # A band, not a ceiling: a reading is out of range when it leaves
    # nominal ± tolerance, too low as well as too high.
    nominal_temp: Mapped[Decimal] = mapped_column(Numeric(5, 2))
    tolerance_temp: Mapped[Decimal] = mapped_column(Numeric(5, 2))
    nominal_humidity: Mapped[Decimal] = mapped_column(Numeric(5, 2))
    tolerance_humidity: Mapped[Decimal] = mapped_column(Numeric(5, 2))


class Farm(Base):
    __tablename__ = "farms"

    farm_id: Mapped[uuid.UUID] = mapped_column(primary_key=True, default=uuid.uuid4)
    country_id: Mapped[uuid.UUID] = mapped_column(ForeignKey("countries.country_id"))
    farm_name: Mapped[str] = mapped_column(String(128))
    # The reference the ERP uses to designate this farm. It never learns our
    # internal id, so this is the only key the two systems share.
    farm_ref: Mapped[str] = mapped_column(String(64), unique=True)


class Warehouse(Base):
    __tablename__ = "warehouses"

    warehouse_id: Mapped[uuid.UUID] = mapped_column(
        primary_key=True, default=uuid.uuid4
    )
    country_id: Mapped[uuid.UUID] = mapped_column(ForeignKey("countries.country_id"))
    warehouse_name: Mapped[str] = mapped_column(String(128))
    warehouse_ref: Mapped[str] = mapped_column(String(64), unique=True)


class User(Base):
    __tablename__ = "users"

    user_id: Mapped[uuid.UUID] = mapped_column(primary_key=True, default=uuid.uuid4)
    email: Mapped[str] = mapped_column(String(256), unique=True)
    password_hash: Mapped[str] = mapped_column(Text)
    role: Mapped[str] = mapped_column(String(32))
    # NULL for head office roles, which belong to no single warehouse.
    warehouse_id: Mapped[uuid.UUID | None] = mapped_column(
        ForeignKey("warehouses.warehouse_id"), nullable=True
    )


# --- stock -----------------------------------------------------------------


class Batch(Base):
    __tablename__ = "batches"

    batch_id: Mapped[uuid.UUID] = mapped_column(primary_key=True, default=uuid.uuid4)
    warehouse_id: Mapped[uuid.UUID] = mapped_column(
        ForeignKey("warehouses.warehouse_id")
    )
    farm_id: Mapped[uuid.UUID] = mapped_column(ForeignKey("farms.farm_id"))
    # UNIQUE is not decoration: it is what stops a reception file replayed by
    # the ERP from creating the same batch twice.
    batch_ref: Mapped[str] = mapped_column(String(64), unique=True)
    stored_at: Mapped[date] = mapped_column(Date)
    # NULL means still in stock: this is the column the FIFO query reads.
    shipped_at: Mapped[date | None] = mapped_column(Date, nullable=True)
    # Nullable because the ERP does not always know the grade: Odoo computes it
    # from the product code and sends `null` for anything that is not
    # COFFEE-A/B/C. A batch exists physically whether or not its grade is
    # known, and refusing it would cost the traceability of the whole batch for
    # a secondary attribute.
    quality_grade: Mapped[str | None] = mapped_column(String(8), nullable=True)
    batch_status: Mapped[str] = mapped_column(String(16))
    # Flipped to false by the quality consumer the first time a reading leaves
    # the country's band, and never flipped back automatically: a batch that
    # spent a night out of range stays suspect until someone says otherwise.
    #
    # A column of its own rather than a `non_compliant` value in `batch_status`:
    # that vocabulary is head office's `BatchStatus` enum, and adding a value on
    # one side only would deserialise as 0 — Stored — on theirs, silently. The
    # question of whether they want the value belongs to them.
    is_compliant: Mapped[bool] = mapped_column(
        Boolean, default=True, server_default=text("true")
    )

    __table_args__ = (
        # The FIFO listing is "what is still in stock, oldest first". Without
        # this the page scans the whole table as the country accumulates years
        # of shipped batches.
        Index("ix_batches_fifo", "warehouse_id", "stored_at"),
    )


# --- IoT -------------------------------------------------------------------


class Sensor(Base):
    __tablename__ = "sensors"

    sensor_id: Mapped[uuid.UUID] = mapped_column(primary_key=True, default=uuid.uuid4)
    warehouse_id: Mapped[uuid.UUID] = mapped_column(
        ForeignKey("warehouses.warehouse_id")
    )
    # The MQTT topic the device publishes on.
    code: Mapped[str] = mapped_column(String(64), unique=True)
    is_active: Mapped[bool] = mapped_column(
        Boolean, default=True, server_default=text("true")
    )


class Measurement(Base):
    __tablename__ = "measurements"

    # Composite PK: TimescaleDB requires the partitioning column (meas_date)
    # in every unique constraint, PK included. The server_default lets non-ORM
    # writers (ingestion consumer, psql) omit the id — client defaults only
    # exist for Python callers.
    measurement_id: Mapped[uuid.UUID] = mapped_column(
        primary_key=True,
        default=uuid.uuid4,
        server_default=text("gen_random_uuid()"),
    )
    sensor_id: Mapped[uuid.UUID] = mapped_column(ForeignKey("sensors.sensor_id"))
    meas_date: Mapped[datetime] = mapped_column(
        DateTime(timezone=True), primary_key=True
    )
    meas_humidity: Mapped[Decimal] = mapped_column(Numeric(5, 2))
    meas_temp: Mapped[Decimal] = mapped_column(Numeric(5, 2))

    __table_args__ = (
        # `mld_warehouse.puml` marks meas_date alone as UNIQUE. Taken
        # literally, two sensors in the same warehouse could not report at the
        # same instant — which is exactly what several devices on one broker
        # do. Read as (sensor_id, meas_date), which is also what makes an MQTT
        # message replay idempotent. To confirm with whoever wrote the MLD.
        UniqueConstraint("sensor_id", "meas_date", name="uq_measurement_per_sensor"),
        # Every read of this table is "a window for these sensors": the daily
        # aggregate head office pulls, and the curves the frontend asks for.
        Index("ix_measurements_sensor_date", "sensor_id", "meas_date"),
    )


class SensorAssignment(Base):
    """Which batch a sensor was measuring, and over which window.

    Neither the MCD nor the MLD carries this link: a sensor belongs to a
    warehouse and a measurement to a sensor, so nothing says which batch a
    reading concerns. Without it, "the readings of this batch" cannot be
    expressed — the room's whole history would be served for every batch it
    ever held.

    A row is open while `released_at` is NULL: the sensor is still on that
    batch. Released rows are kept rather than deleted — the curves of a batch
    that has already shipped are exactly what the quality page has to show.
    """

    __tablename__ = "sensor_assignments"

    sensor_assignment_id: Mapped[uuid.UUID] = mapped_column(
        primary_key=True, default=uuid.uuid4
    )
    sensor_id: Mapped[uuid.UUID] = mapped_column(ForeignKey("sensors.sensor_id"))
    batch_id: Mapped[uuid.UUID] = mapped_column(ForeignKey("batches.batch_id"))
    assigned_at: Mapped[datetime] = mapped_column(
        DateTime(timezone=True), server_default=text("CURRENT_TIMESTAMP")
    )
    # NULL while the sensor is still on the batch. The measurement window is
    # `assigned_at` to `released_at ?? now()`.
    released_at: Mapped[datetime | None] = mapped_column(
        DateTime(timezone=True), nullable=True
    )

    __table_args__ = (
        # One physical sensor cannot be on two batches at once. Partial, so the
        # same sensor is freely reassigned after release, and a batch may still
        # carry several sensors at the same time.
        Index(
            "uq_sensor_assignment_open_per_sensor",
            "sensor_id",
            unique=True,
            postgresql_where=text("released_at IS NULL"),
            sqlite_where=text("released_at IS NULL"),
        ),
        # The history query reads "the windows of this batch, oldest first".
        Index("ix_sensor_assignments_batch", "batch_id", "assigned_at"),
    )


class Notification(Base):
    """What a warehouse is told, as the bell reads it.

    Distinct from `Alert`, which is the technical record of a breach. An alert
    says a threshold was crossed; a notification says someone has to look at
    something. US #30 has the quality service write both in one operation, and
    the order service write notifications of its own — two producers, one table,
    one read API.

    The subject is carried by a foreign key rather than a rendered sentence:
    the wording belongs to the API, and a stored message would go stale the day
    a batch reference changes. `notification_type` says which key is filled.
    """

    __tablename__ = "notifications"

    notification_id: Mapped[uuid.UUID] = mapped_column(
        primary_key=True, default=uuid.uuid4
    )
    warehouse_id: Mapped[uuid.UUID] = mapped_column(
        ForeignKey("warehouses.warehouse_id")
    )
    notification_type: Mapped[str] = mapped_column(String(32))
    # Exactly one of the two is filled, enforced below. Nullable because a
    # notification concerns either a batch or an order, never both.
    batch_id: Mapped[uuid.UUID | None] = mapped_column(
        ForeignKey("batches.batch_id"), nullable=True
    )
    order_id: Mapped[uuid.UUID | None] = mapped_column(
        ForeignKey("orders.id"), nullable=True
    )
    created_at: Mapped[datetime] = mapped_column(
        DateTime(timezone=True), server_default=text("CURRENT_TIMESTAMP")
    )
    # NULL while unread. A timestamp rather than a boolean: "when did someone
    # see this" is the question a quality review asks afterwards.
    read_at: Mapped[datetime | None] = mapped_column(
        DateTime(timezone=True), nullable=True
    )

    __table_args__ = (
        # A data rule, not a code rule: a `batch_non_compliant` with no batch is
        # a notification the frontend cannot route anywhere, and no amount of
        # care in one service stops another from writing one.
        CheckConstraint(
            "(notification_type = 'batch_non_compliant' AND batch_id IS NOT NULL "
            "AND order_id IS NULL) OR "
            "(notification_type = 'order_received' AND order_id IS NOT NULL "
            "AND batch_id IS NULL)",
            name="ck_notification_subject_matches_type",
        ),
        # Every read of this table is "this warehouse's notifications, newest
        # first", with the unread ones asked for far more often than the rest.
        Index("ix_notifications_warehouse_created", "warehouse_id", "created_at"),
    )


class Alert(Base):
    __tablename__ = "alerts"

    alert_id: Mapped[uuid.UUID] = mapped_column(primary_key=True, default=uuid.uuid4)
    warehouse_id: Mapped[uuid.UUID] = mapped_column(
        ForeignKey("warehouses.warehouse_id")
    )
    # Filled only by an `expiration` alert; a `condition` alert concerns the
    # room, not one batch.
    batch_id: Mapped[uuid.UUID | None] = mapped_column(
        ForeignKey("batches.batch_id"), nullable=True
    )
    alert_type: Mapped[str] = mapped_column(String(32))
    alert_status: Mapped[str] = mapped_column(String(32), server_default="active")
    created_at: Mapped[datetime] = mapped_column(
        DateTime(timezone=True), server_default=text("CURRENT_TIMESTAMP")
    )
    resolved_at: Mapped[datetime | None] = mapped_column(
        DateTime(timezone=True), nullable=True
    )

    __table_args__ = (
        # "One active condition alert per warehouse" is a data rule, not a code
        # rule: a service reads the alerts then writes one, and two readings
        # evaluated at once would both pass that read. Partial, so resolved
        # alerts accumulate freely and expiration alerts never collide.
        Index(
            "uq_alert_active_condition_per_warehouse",
            "warehouse_id",
            unique=True,
            postgresql_where=text("alert_status = 'active' AND alert_type = 'condition'"),
            sqlite_where=text("alert_status = 'active' AND alert_type = 'condition'"),
        ),
    )


# --- orders (mirrored from Odoo through head office) -----------------------


class Order(Base):
    __tablename__ = "orders"

    id: Mapped[uuid.UUID] = mapped_column(primary_key=True, default=uuid.uuid4)
    # The id this order carries in Odoo, kept to reconcile the two sides.
    odoo_order_id: Mapped[int | None] = mapped_column(Integer, index=True, nullable=True)
    order_reference: Mapped[str] = mapped_column(String(64))
    order_date: Mapped[datetime] = mapped_column(DateTime(timezone=True))
    client_name: Mapped[str] = mapped_column(String(256))
    country_id: Mapped[uuid.UUID | None] = mapped_column(
        ForeignKey("countries.country_id"), nullable=True
    )
    status: Mapped[str] = mapped_column(String(32))
    integration_error_message: Mapped[str | None] = mapped_column(
        String(1024), nullable=True
    )
    created_at: Mapped[datetime] = mapped_column(
        DateTime(timezone=True), server_default=text("CURRENT_TIMESTAMP")
    )
    updated_at: Mapped[datetime | None] = mapped_column(
        DateTime(timezone=True), nullable=True
    )


class OrderLine(Base):
    __tablename__ = "order_lines"

    id: Mapped[uuid.UUID] = mapped_column(primary_key=True, default=uuid.uuid4)
    order_id: Mapped[uuid.UUID] = mapped_column(ForeignKey("orders.id"))
    product_name: Mapped[str] = mapped_column(String(256))
    quantity: Mapped[Decimal] = mapped_column(Numeric(12, 2))


class OrderBatch(Base):
    """Which batches were shipped against which order."""

    __tablename__ = "order_batches"

    batch_id: Mapped[uuid.UUID] = mapped_column(
        ForeignKey("batches.batch_id"), primary_key=True
    )
    order_id: Mapped[uuid.UUID] = mapped_column(
        ForeignKey("orders.id"), primary_key=True
    )
