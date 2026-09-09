#!/usr/bin/env python3
"""Simulateur de capteurs — génère des relevés au contrat MQTT du projet.

Le firmware embarqué ne publie pas encore sur le broker : il lit le DHT22 et
écrit dans le moniteur série. Ce simulateur tient sa place pour exercer la
chaîne complète — broker, consumers, hypertable, évaluation des seuils,
notifications — sans matériel.

Il **n'émet pas lui-même** : il écrit des payloads JSON, une par ligne, que
`mosquitto_pub -l` publie. Aucune dépendance Python à installer, et le même
fichier peut être rejoué à l'identique, ce qui rend le test reproductible.

    # un capteur, 48 h de relevés au pas de 30 min, dans /tmp
    python3 tools/sensor_simulator.py --out /tmp

    # publication (depuis la racine, stack démarrée)
    docker compose exec -T mosquitto \
      mosquitto_pub -h localhost -q 1 -t futurekawa/SENSOR-BR-01 -l \
      < /tmp/SENSOR-BR-01.jsonl

Contrat respecté (cf. Api/app/consumers/payload.py) :

    topic   : futurekawa/<code capteur>
    payload : {"measuredAt": "...Z", "temp": 21.5, "humidity": 54.1}
"""

import argparse
import datetime as dt
import json
import pathlib
import random
import sys

# Valeurs par défaut alignées sur les seuils du seed brésilien : 20 °C ± 3 et
# 55 % ± 10. Un capteur « nominal » reste donc dans la bande, un capteur en
# dérive en sort franchement.
NOMINAL_TEMP = 20.0
NOMINAL_HUMIDITY = 55.0


def readings(steps, step_minutes, drift_from, seed):
    """Produit une série de relevés, éventuellement en dérive.

    `drift_from` est l'indice à partir duquel la température et l'humidité
    montent régulièrement. Passer un indice supérieur au nombre de pas donne un
    capteur entièrement nominal.
    """
    rng = random.Random(seed)
    now = dt.datetime.now(dt.UTC).replace(second=0, microsecond=0)

    for i in range(steps):
        measured_at = now - dt.timedelta(minutes=step_minutes * (steps - 1 - i))

        if i < drift_from:
            temp = NOMINAL_TEMP + rng.uniform(-1, 1)
            humidity = NOMINAL_HUMIDITY + rng.uniform(-3, 3)
        else:
            # Montée continue : au bout de quelques heures le relevé sort de la
            # bande de tolérance, ce qui doit déclencher exactement une
            # notification — pas une par relevé.
            over = i - drift_from
            temp = NOMINAL_TEMP + over * 0.35 + rng.uniform(-0.3, 0.3)
            humidity = NOMINAL_HUMIDITY + over * 0.9 + rng.uniform(-1, 1)

        yield {
            "measuredAt": measured_at.strftime("%Y-%m-%dT%H:%M:%SZ"),
            # Arrondi à la décimale : la colonne est un numeric(5,2) et le
            # consumer lit la valeur depuis la chaîne, jamais via un float.
            "temp": round(temp, 1),
            "humidity": round(humidity, 1),
        }


def main(argv=None):
    parser = argparse.ArgumentParser(
        description="Génère des relevés de capteurs au contrat MQTT du projet.",
    )
    parser.add_argument(
        "--sensors", default="SENSOR-BR-01",
        help="Codes de capteurs séparés par des virgules (défaut : SENSOR-BR-01)",
    )
    parser.add_argument(
        "--steps", type=int, default=96,
        help="Nombre de relevés par capteur (défaut : 96)",
    )
    parser.add_argument(
        "--interval", type=int, default=30,
        help="Minutes entre deux relevés (défaut : 30, soit 48 h sur 96 pas)",
    )
    parser.add_argument(
        "--drift", default="",
        help="Capteurs partant en dérive, séparés par des virgules. "
             "Les autres restent dans la bande de tolérance.",
    )
    parser.add_argument(
        "--drift-from", type=int, default=72,
        help="Indice du relevé à partir duquel la dérive commence (défaut : 72)",
    )
    parser.add_argument(
        "--seed", type=int, default=42,
        help="Graine aléatoire : à graine égale, série identique (défaut : 42)",
    )
    parser.add_argument(
        "--out", default="-",
        help="Répertoire de sortie, un .jsonl par capteur. '-' écrit sur "
             "la sortie standard (un seul capteur attendu).",
    )
    args = parser.parse_args(argv)

    sensors = [s.strip() for s in args.sensors.split(",") if s.strip()]
    drifting = {s.strip() for s in args.drift.split(",") if s.strip()}

    unknown = drifting - set(sensors)
    if unknown:
        parser.error(f"--drift désigne des capteurs absents de --sensors : {', '.join(sorted(unknown))}")

    if args.out == "-" and len(sensors) > 1:
        parser.error("la sortie standard ne peut servir qu'un seul capteur ; utiliser --out <répertoire>")

    for index, code in enumerate(sensors):
        drift_from = args.drift_from if code in drifting else args.steps + 1
        # Graine dérivée du rang : deux capteurs nominaux ne produisent pas la
        # même série au relevé près, ce qui serait un artefact visible en démo.
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
            state = "en dérive" if code in drifting else "nominal"
            print(f"{path} · {len(lines)} relevés · {state}", file=sys.stderr)

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
