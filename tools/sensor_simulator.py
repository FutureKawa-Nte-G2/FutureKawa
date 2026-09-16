#!/usr/bin/env python3
"""Sensor simulator: generates readings following the project's MQTT contract.

Stands in for the firmware, which does not publish to the broker yet, so the
whole chain can be exercised without hardware.

It does not publish itself: it writes one JSON payload per line for
`mosquitto_pub -l`, so no Python dependency is needed and a file replays identically.

    # one sensor, 48 h of readings every 30 min, into /tmp
    python3 tools/sensor_simulator.py --out /tmp

    # publish (from the repo root, stack running)
    docker compose exec -T mosquitto \
      mosquitto_pub -h localhost -q 1 -t futurekawa/SENSOR-BR-01 -l \
      < /tmp/SENSOR-BR-01.jsonl

Contract (see Api/app/consumers/payload.py):

    topic   : futurekawa/<sensor code>
    payload : {"measuredAt": "...Z", "temp": 21.5, "humidity": 54.1}
"""

import argparse
import datetime as dt
import json
import pathlib
import random
import sys

# Matches the Brazilian band required by the brief (29 °C ± 3, 55 % ± 2), the
# same one the fixture loads: a nominal sensor stays inside, a drifting one
# clearly leaves it.
NOMINAL_TEMP = 29.0
NOMINAL_HUMIDITY = 55.0


def readings(steps, step_minutes, drift_from, seed):
    """`drift_from` beyond `steps` gives a fully nominal sensor."""
    rng = random.Random(seed)
    now = dt.datetime.now(dt.UTC).replace(second=0, microsecond=0)

    for i in range(steps):
        measured_at = now - dt.timedelta(minutes=step_minutes * (steps - 1 - i))

        if i < drift_from:
            temp = NOMINAL_TEMP + rng.uniform(-1, 1)
            humidity = NOMINAL_HUMIDITY + rng.uniform(-1, 1)
        else:
            # Steady rise: once out of band it must raise exactly one
            # notification, not one per reading.
            over = i - drift_from
            temp = NOMINAL_TEMP + over * 0.35 + rng.uniform(-0.3, 0.3)
            humidity = NOMINAL_HUMIDITY + over * 0.9 + rng.uniform(-1, 1)

        yield {
            "measuredAt": measured_at.strftime("%Y-%m-%dT%H:%M:%SZ"),
            # One decimal: the column is numeric(5,2).
            "temp": round(temp, 1),
            "humidity": round(humidity, 1),
        }


def main(argv=None):
    parser = argparse.ArgumentParser(
        description="Generates sensor readings following the project's MQTT contract.",
    )
    parser.add_argument(
        "--sensors", default="SENSOR-BR-01",
        help="Comma-separated sensor codes (default: SENSOR-BR-01)",
    )
    parser.add_argument(
        "--steps", type=int, default=96,
        help="Readings per sensor (default: 96)",
    )
    parser.add_argument(
        "--interval", type=int, default=30,
        help="Minutes between readings (default: 30, i.e. 48 h over 96 steps)",
    )
    parser.add_argument(
        "--drift", default="",
        help="Comma-separated sensors that drift. "
             "The others stay within the band.",
    )
    parser.add_argument(
        "--drift-from", type=int, default=72,
        help="Reading index where the drift starts (default: 72)",
    )
    parser.add_argument(
        "--seed", type=int, default=42,
        help="Random seed: same seed, same series (default: 42)",
    )
    parser.add_argument(
        "--out", default="-",
        help="Output directory, one .jsonl per sensor. '-' writes to "
             "stdout (single sensor only).",
    )
    args = parser.parse_args(argv)

    sensors = [s.strip() for s in args.sensors.split(",") if s.strip()]
    drifting = {s.strip() for s in args.drift.split(",") if s.strip()}

    unknown = drifting - set(sensors)
    if unknown:
        parser.error(f"--drift names sensors missing from --sensors: {', '.join(sorted(unknown))}")

    if args.out == "-" and len(sensors) > 1:
        parser.error("stdout can only serve one sensor; use --out <directory>")

    for index, code in enumerate(sensors):
        drift_from = args.drift_from if code in drifting else args.steps + 1
        # Per-sensor seed: identical series would be a visible artefact in a demo.
        lines = [
            json.dumps(r, ensure_ascii=False)
            for r in readings(args.steps, args.interval, drift_from, args.seed + index)
        ]

        if args.out == "-":
            print("\n".join(lines))
        else:
            path = pathlib.Path(args.out) / f"{code}.jsonl"
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text("\n".join(lines) + "\n", encoding="utf-8")
            state = "drifting" if code in drifting else "nominal"
            print(f"{path} · {len(lines)} readings · {state}", file=sys.stderr)

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
