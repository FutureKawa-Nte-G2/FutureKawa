from datetime import date
from decimal import Decimal
from typing import Annotated

from pydantic import BaseModel, ConfigDict, PlainSerializer
from pydantic.alias_generators import to_camel

# Their DTO types these as `decimal`, and `System.Text.Json` refuses a quoted
# number for a decimal unless `NumberHandling.AllowReadingFromString` is set —
# which their options do not set. Pydantic serialises a Decimal as a string by
# default, so left alone we would send `"25.30"` and earn a JsonException that
# their catch-all swallows: warehouse skipped, one log line on their side,
# nothing at all on ours. Verified by running their DTO and their options.
#
# The Decimal is kept on the Python side, where the quantisation happens; only
# the wire carries a JSON number.
WireDecimal = Annotated[
    Decimal, PlainSerializer(float, return_type=float, when_used="json")
]


class MeasurementAggregate(BaseModel):
    """One day of readings, aggregated, as head office pulls it.

    Serialised in camelCase because the consumer is the .NET head office, which
    deserialises with `PropertyNameCaseInsensitive`. That option tolerates a
    different case, NOT a different name: `avg_temp` would not bind to
    `AvgTemp` — it would land as `0` without raising. See `Api/CLAUDE.md`.
    """

    model_config = ConfigDict(alias_generator=to_camel, populate_by_name=True)

    avg_temp: WireDecimal
    max_temp: WireDecimal
    min_temp: WireDecimal
    avg_humidity: WireDecimal
    max_humidity: WireDecimal
    min_humidity: WireDecimal
    # `DateOnly` on their side: its converter accepts `yyyy-MM-dd` and nothing
    # else. A timestamp raises a JsonException their client swallows, and the
    # warehouse is skipped with a single log line. A `date` serialises right.
    meas_date: date
