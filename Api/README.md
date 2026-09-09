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

## Alertes

Deux routes, sur la table qu'écrit `app/services/quality.py` :

```
GET   /api/alerts?warehouseRef=BR-ENT-01&status=active&limit=50
PATCH /api/alerts/{alertId}/resolve?warehouseRef=BR-ENT-01
```

`warehouseRef` est obligatoire et vient du siège, qui détient la session : cette
API n'a pas de login à elle. Une référence inconnue répond `404` et non une
liste vide — « cet entrepôt n'a rien d'ouvert » et « cet entrepôt n'existe pas »
ne se disent pas de la même façon.

`status` vaut `active` (défaut), `resolved` ou `all`. La lecture est ouverte
comme `/api/measurements` ; la résolution écrit, donc elle exige `X-API-Key`.

**Résoudre n'est pas du rangement.** `alerts` porte un index unique partiel
n'autorisant qu'une alerte `condition` active par entrepôt : tant que la
première n'est pas refermée, une nouvelle dérive de la salle ne peut plus rien
ouvrir — elle tombe dans le rattrapage d'`IntegrityError` de `evaluate_reading`
et se réduit à un marquage de lot. La résolution est ce qui réarme la détection.

Elle ne touche pas à `batches.is_compliant` : réparer une salle ne blanchit pas
le café qui a passé la nuit hors plage. Lever ce drapeau est un jugement porté
sur le lot, pas un effet de bord de l'accusé de réception de la salle — et cet
endpoint-là reste à écrire.

Les alertes `expiration` sont lues et affichées, mais **personne ne les crée
encore** : il faut une tâche périodique, là où les deux consumers actuels sont
réactifs. À faire.

## Consumers MQTT

Deux services abonnés au broker, indépendants l'un de l'autre : l'un écrit les
relevés, l'autre évalue les seuils. Ils tournent hors de l'API — celle-ci sert
des requêtes HTTP, eux consomment un flux.

```bash
./venv/bin/python -m app.consumers.runner
```

Ils ont besoin d'un broker joignable (`MQTT_BROKER_HOST`, `MQTT_BROKER_PORT`).
Sans broker, ils journalisent une tentative de reconnexion toutes les 5
secondes plutôt que de s'arrêter — Mosquitto n'est pas encore configuré (#31).

Le contrat du message, côté firmware :

```
topic   : futurekawa/<code du capteur>
payload : {"measuredAt": "2026-09-06T12:00:00Z", "temp": 21.5, "humidity": 54.1}
```

`measuredAt` est facultatif : un firmware sans horloge synchronisée n'en envoie
pas, et la date de réception sert alors d'approximation.

La logique métier vit dans `app/services/ingestion.py` et
`app/services/quality.py`, qui reçoivent un relevé déjà décodé. `app/consumers/`
ne fait que décoder et tenir la boucle debout — c'est ce qui rend les règles
vérifiables sans monter de broker.

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
