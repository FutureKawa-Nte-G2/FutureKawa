"""Les deux consumers abonnés au broker.

L'US #32 les veut indépendants : un échec de l'un ne doit pas empêcher l'autre
de fonctionner. Ils tournent donc chacun dans leur tâche, avec leur propre
connexion MQTT et leur propre session de base. Le broker délivre le message aux
deux abonnements ; aucun ne dépend du travail de l'autre.

    ./venv/bin/python -m app.consumers.runner

Ce module ne contient aucune règle métier : il décode, appelle un service, et
tient la boucle debout. Ce qui décide de quoi que ce soit vit dans
`app/services/ingestion.py` et `app/services/quality.py`, testables sans broker.
"""

import asyncio
import logging
import os
from collections.abc import Awaitable, Callable

import aiomqtt

from app.consumers.payload import TOPIC_FILTER, MalformedPayloadError, decode
from app.db import get_session_factory
from app.services.ingestion import Reading, persist_reading
from app.services.quality import evaluate_reading

logger = logging.getLogger(__name__)

BROKER_HOST_ENV_VAR = "MQTT_BROKER_HOST"
BROKER_PORT_ENV_VAR = "MQTT_BROKER_PORT"

# Redémarrage après une coupure du broker. Assez court pour qu'une salle ne
# reste pas longtemps sans surveillance, assez long pour ne pas marteler un
# broker qui redémarre.
RECONNECT_DELAY_SECONDS = 5


Handler = Callable[[object, Reading], Awaitable[object]]


async def _consume(name: str, client_id: str, handler: Handler) -> None:
    """Boucle d'un consumer : une connexion, un abonnement, un traitement.

    Toute exception levée en traitant un message est journalisée puis avalée.
    Un relevé malformé, un lot supprimé entre-temps, une contrainte violée : ce
    sont des incidents d'un message, pas du consumer. Les laisser remonter
    arrêterait la surveillance de toute la salle pour une ligne fautive.
    """
    host = os.environ.get(BROKER_HOST_ENV_VAR, "localhost")
    port = int(os.environ.get(BROKER_PORT_ENV_VAR, "1883"))
    sessionmaker = get_session_factory()

    while True:
        try:
            async with aiomqtt.Client(hostname=host, port=port, identifier=client_id) as client:
                await client.subscribe(TOPIC_FILTER)
                logger.info("%s abonné à %s sur %s:%s", name, TOPIC_FILTER, host, port)

                async for message in client.messages:
                    topic = str(message.topic)
                    try:
                        reading = decode(topic, bytes(message.payload))
                    except MalformedPayloadError as exc:
                        logger.warning("%s : message écarté sur %s — %s", name, topic, exc)
                        continue

                    try:
                        async with sessionmaker() as session:
                            await handler(session, reading)
                    except Exception:
                        logger.exception("%s : échec du traitement de %s", name, topic)

        except aiomqtt.MqttError as exc:
            logger.warning(
                "%s : broker injoignable (%s), nouvelle tentative dans %ss",
                name,
                exc,
                RECONNECT_DELAY_SECONDS,
            )
            await asyncio.sleep(RECONNECT_DELAY_SECONDS)


async def main() -> None:
    """Lance les deux consumers en parallèle.

    `gather` sans `return_exceptions` suffit : chaque boucle rattrape déjà ses
    propres pannes et ne rend jamais la main. Si l'une s'arrête malgré tout,
    c'est que quelque chose d'irrattrapable est arrivé, et le processus doit
    tomber pour être relevé plutôt que tourner à moitié.
    """
    logging.basicConfig(
        level=logging.INFO, format="%(asctime)s %(levelname)s %(name)s %(message)s"
    )
    await asyncio.gather(
        _consume("persistance", "futurekawa-ingestion", persist_reading),
        _consume("évaluation", "futurekawa-quality", evaluate_reading),
    )


if __name__ == "__main__":
    asyncio.run(main())
