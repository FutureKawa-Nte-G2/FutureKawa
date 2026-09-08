# syntax=docker/dockerfile:1

# Une image, deux usages :
#   uvicorn app.main:app          -> l'API pays
#   alembic upgrade head          -> migrations, puis sortie
# Contexte de build : ./Api

ARG PYTHON_VERSION=3.12

FROM python:${PYTHON_VERSION}-slim AS runtime
WORKDIR /app

# Le healthcheck compose interroge /health en HTTP, et l'image slim n'embarque
# aucun client. curl plutôt que wget : même invocation que les autres services.
RUN apt-get update \
 && apt-get install -y --no-install-recommends curl \
 && rm -rf /var/lib/apt/lists/*

# requirements.txt seul d'abord : l'install reste en cache tant qu'aucune
# dépendance ne change, même quand tout le code a changé.
COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt

COPY alembic.ini .
COPY alembic/ alembic/
COPY app/ app/

# asyncpg ne lit pas PYTHONPATH depuis le WORKDIR sans ça : `alembic upgrade`
# importe app.models, qui doit être résolvable depuis /app.
ENV PYTHONPATH=/app \
    PYTHONUNBUFFERED=1

# Un utilisateur non root, comme l'image .NET du backend qui tourne en `app`.
RUN useradd --create-home --uid 1000 apiuser
USER apiuser

EXPOSE 8000
CMD ["uvicorn", "app.main:app", "--host", "0.0.0.0", "--port", "8000"]
