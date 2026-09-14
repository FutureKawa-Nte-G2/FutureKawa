"""The two MQTT consumers.

Each runs with its own connection and session so that one failing never stops
the other (US #32). No business rule lives here, so services test without a broker.

    ./venv/bin/python -m app.consumers.runner
"""

import asyncio
import logging
import os
from collections.abc import Awaitable, Callable

import aiomqtt
import httpx

from app.consumers.payload import TOPIC_FILTER, MalformedPayloadError, decode
from app.db import get_session_factory
from app.services.head_office import push_alert
from app.services.ingestion import Reading, persist_reading
from app.services.quality import Evaluation, evaluate_reading

logger = logging.getLogger(__name__)

BROKER_HOST_ENV_VAR = "MQTT_BROKER_HOST"
BROKER_PORT_ENV_VAR = "MQTT_BROKER_PORT"

# Short enough not to leave rooms unmonitored, long enough not to hammer a restarting broker.
RECONNECT_DELAY_SECONDS = 5


Handler = Callable[[object, Reading], Awaitable[object]]


def _evaluate_and_push(client: httpx.AsyncClient, pending: set[asyncio.Task]) -> Handler:
    """Pushes in a separate task: messages are handled one at a time, so
    awaiting a failing head office would stall monitoring for every room.
    """

    async def handler(session, reading: Reading) -> Evaluation:
        evaluation = await evaluate_reading(session, reading)
        if evaluation.alert_push is not None:
            task = asyncio.create_task(push_alert(client, evaluation.alert_push))
            # asyncio only keeps a weak reference: an unreferenced task may be
            # garbage collected before it finishes.
            pending.add(task)
            task.add_done_callback(pending.discard)
            task.add_done_callback(_log_unexpected_push_failure)
        return evaluation

    return handler


def _log_unexpected_push_failure(task: asyncio.Task) -> None:
    """Otherwise an unexpected exception would surface only at garbage
    collection, if ever: an alert lost silently.
    """
    if task.cancelled():
        return
    exc = task.exception()
    if exc is not None:
        logger.error("Unexpected failure while pushing an alert to head office", exc_info=exc)


async def _consume(name: str, client_id: str, handler: Handler) -> None:
    """Exceptions from one message are logged and swallowed: letting them
    propagate would stop monitoring every room over a single bad reading.
    """
    host = os.environ.get(BROKER_HOST_ENV_VAR, "localhost")
    port = int(os.environ.get(BROKER_PORT_ENV_VAR, "1883"))
    sessionmaker = get_session_factory()

    while True:
        try:
            async with aiomqtt.Client(hostname=host, port=port, identifier=client_id) as client:
                await client.subscribe(TOPIC_FILTER)
                logger.info("%s subscribed to %s on %s:%s", name, TOPIC_FILTER, host, port)

                async for message in client.messages:
                    topic = str(message.topic)
                    try:
                        reading = decode(topic, bytes(message.payload))
                    except MalformedPayloadError as exc:
                        logger.warning("%s: message dropped on %s: %s", name, topic, exc)
                        continue

                    try:
                        async with sessionmaker() as session:
                            await handler(session, reading)
                    except Exception:
                        logger.exception("%s: failed to process %s", name, topic)

        except aiomqtt.MqttError as exc:
            logger.warning(
                "%s: broker unreachable (%s), retrying in %ss",
                name,
                exc,
                RECONNECT_DELAY_SECONDS,
            )
            await asyncio.sleep(RECONNECT_DELAY_SECONDS)


async def main() -> None:
    """No `return_exceptions`: each loop already survives its own failures, so
    one ending means something unrecoverable, and the process should restart
    rather than run half-way.
    """
    logging.basicConfig(
        level=logging.INFO, format="%(asctime)s %(levelname)s %(name)s %(message)s"
    )
    pending_pushes: set[asyncio.Task] = set()
    # Shared so connections are reused from one alert to the next.
    async with httpx.AsyncClient() as client:
        await asyncio.gather(
            _consume("ingestion", "futurekawa-ingestion", persist_reading),
            _consume(
                "evaluation",
                "futurekawa-quality",
                _evaluate_and_push(client, pending_pushes),
            ),
        )


if __name__ == "__main__":
    asyncio.run(main())
