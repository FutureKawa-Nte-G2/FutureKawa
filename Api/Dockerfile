# syntax=docker/dockerfile:1

# One image, two uses:
#   uvicorn app.main:app          -> the country API
#   alembic upgrade head          -> migrations, then exit
# Build context: ./Api

# Same version as the Python API CI job, so local runs match what CI validates.
ARG PYTHON_VERSION=3.13

FROM python:${PYTHON_VERSION}-slim AS runtime
WORKDIR /app

# The compose healthcheck needs an HTTP client, which slim does not ship.
RUN apt-get update \
 && apt-get install -y --no-install-recommends curl \
 && rm -rf /var/lib/apt/lists/*

# Copied alone first so the install stays cached until a dependency changes.
COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt

COPY alembic.ini .
COPY alembic/ alembic/
COPY app/ app/

# `alembic upgrade` imports app.models, which must resolve from /app.
ENV PYTHONPATH=/app \
    PYTHONUNBUFFERED=1

# Non-root, like the backend .NET image.
RUN useradd --create-home --uid 1000 apiuser
USER apiuser

EXPOSE 8000
CMD ["uvicorn", "app.main:app", "--host", "0.0.0.0", "--port", "8000"]
