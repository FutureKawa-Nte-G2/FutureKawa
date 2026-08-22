# API pays — FutureKawa

API locale d'un pays. Elle lit la base de ce pays (stock, capteurs, alertes) et
le siège vient l'interroger en HTTP. Une instance = un pays.

Le contrat imposé par le siège est dans [CLAUDE.md](./CLAUDE.md).

## Démarrer

```bash
# 1. La base du pays
docker compose up -d warehouse-db          # depuis la racine du dépôt

# 2. L'environnement Python
cd Api
python3 -m venv venv
./venv/bin/pip install -r requirements-dev.txt

# 3. La configuration
cp .env.example .env                       # puis ajuster si besoin
export $(grep -v '^#' .env | xargs)

# 4. Le schéma
./venv/bin/alembic upgrade head

# 5. L'API
./venv/bin/uvicorn app.main:app --reload
```

`http://127.0.0.1:8000/health` répond `{"status":"ok"}`, et la documentation
interactive est sur `/docs`.

## Tests

```bash
./venv/bin/python -m pytest tests -q
```

Ils tournent sur SQLite en mémoire : aucune base à démarrer, et chaque test a la
sienne. Ce que SQLite ne reproduit pas est listé en tête de
[tests/conftest.py](./tests/conftest.py).

## Migrations

Le schéma vit dans `app/models.py` et suit
`Documentation/diagrammes/mld_warehouse.puml`. Alembic lit `DATABASE_URL`, la
même variable que l'application — rien à configurer dans `alembic.ini`.

```bash
# après avoir modifié app/models.py
./venv/bin/alembic revision --autogenerate -m "ce que ça change"
./venv/bin/alembic upgrade head

# revenir en arrière d'une révision
./venv/bin/alembic downgrade -1

# voir le SQL sans l'exécuter, pour relire avant d'appliquer
./venv/bin/alembic upgrade head --sql
```

**Relire ce qu'autogenerate produit avant de commiter.** Il compare les modèles
à la base et devine ; il ne devine pas toujours juste, en particulier sur les
renommages, qu'il traduit en `drop_column` suivi d'un `add_column` — donc en
perte de données.

## Variables d'environnement

| Variable | Rôle |
|---|---|
| `DATABASE_URL` | base du pays, en `postgresql+asyncpg://` |
| `LOCAL_API_KEY` | secret partagé attendu en `X-API-Key` sur les routes que le siège consomme |

Les deux échouent à l'usage plutôt que de se replier sur une valeur par défaut :
une variable oubliée est une erreur de déploiement, elle doit se voir.
