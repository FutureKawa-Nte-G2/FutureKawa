# API pays — FutureKawa

API locale d'un pays. Elle lit la base de ce pays (stock, capteurs, alertes), le
siège vient l'interroger en HTTP, et elle pousse au siège chaque alerte qu'elle
ouvre. Une instance = un pays.

Les contrats échangés avec le siège sont dans
[Documentation/api-pays-contrats.md](../Documentation/api-pays-contrats.md).

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
GET   /api/alerts?warehouse_ref=WH-BR-SANTOS&status=active&limit=50
PATCH /api/alerts/{alertId}/resolve?warehouse_ref=WH-BR-SANTOS
```

`warehouse_ref` est obligatoire et vient du siège, qui détient la session : cette
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

Les alertes `expiration` sont lues et affichées, mais **aucune n'est créée** :
ce n'est pas prévu. Seules les alertes `condition` sont produites, et poussées
au siège.

## Envoi des alertes au siège

Chaque alerte `condition` commitée par `evaluate_reading` est poussée au siège
sur `POST /api/alerts`, sans attendre que celui-ci vienne la chercher. Le
contrat détaillé est dans
[Documentation/api-pays-contrats.md](../Documentation/api-pays-contrats.md#contrat-imposé--post-apialerts-envoi-au-siège).

```
evaluate_reading (commit) → Evaluation.alert_push
                          → runner : tâche asyncio → head_office.push_alert → siège
```

**Le consumer ne se bloque pas.** L'envoi part dans sa propre tâche : les
messages MQTT sont traités un par un, et un siège en panne suspendrait sinon la
surveillance de toutes les salles. `quality.py` ne fait aucun appel réseau ; il
décrit l'alerte à envoyer, et `app/consumers/runner.py` lance l'envoi.

**`type` : `temperature` ou `humidity`, jamais `condition`.** Le siège et le
frontend veulent une grandeur, une alerte `condition` porte sur la salle. On
envoie celle qui a franchi son seuil **en premier** :

| Relevé déclencheur | Grandeur envoyée |
|---|---|
| une seule grandeur hors bande | celle-là |
| les deux, et le relevé précédent du capteur en avait déjà une hors bande | celle-là |
| les deux, relevé précédent dans la bande et vieux de 15 min au plus | la plus précoce, par interpolation linéaire de l'instant de franchissement |
| les deux, sans relevé précédent utilisable, ou instants égaux | le plus gros écart relatif, `\|valeur − nominal\| / tolérance` |

Le relevé précédent est lu dans `measurements`, seulement quand les deux
grandeurs débordent. La règle elle-même, `select_breached_metric`, est une
fonction pure.

**`measuredAt` est l'horodatage du relevé, pas celui de l'alerte.**
`alerts.measured_at` reprend `reading.measured_at` ; `created_at` reste le
moment où le consumer a traité le message. Les deux divergent quand un capteur
bufferise hors ligne ou qu'un consumer rattrape une coupure du broker.

**Siège injoignable.** Tentatives en mémoire, dans `app/services/head_office.py` :

| Réponse | Comportement |
|---|---|
| `2xx` | reçue |
| erreur réseau, `5xx` | nouvelle tentative : 5 essais, délais de 2, 4, 8, 16 s |
| `429` | nouvelle tentative, `Retry-After` respecté (60 s au plus) |
| `400`, `401`, `404` | abandon immédiat : la prochaine réponse serait la même |

Un envoi encore en cours quand le consumer s'arrête est perdu, mais jamais en
silence : tout abandon finit sur un log d'erreur qui nomme l'`alert_id`. La clé
n'apparaît dans aucun log.

**La résolution ne part pas d'ici.** Elle est décidée au siège, qui la renvoie
sur `PATCH /api/alerts/{id}/resolve` grâce au `sourceAlertId` reçu.

## Consumers MQTT

Deux services abonnés au broker, indépendants l'un de l'autre : l'un écrit les
relevés, l'autre évalue les seuils. Ils tournent hors de l'API — celle-ci sert
des requêtes HTTP, eux consomment un flux.

```bash
./venv/bin/python -m app.consumers.runner
```

Ils ont besoin d'un broker joignable (`MQTT_BROKER_HOST`, `MQTT_BROKER_PORT`) :
`docker compose up -d mosquitto` en local, le service `country-consumers` du
compose sinon. Sans broker, ils journalisent une tentative de reconnexion toutes
les 5 secondes plutôt que de s'arrêter.

Le consumer d'évaluation a aussi besoin de `HEAD_OFFICE_ALERTS_URL` pour pousser
ses alertes. Sans elle, il continue de surveiller, mais chaque alerte ouverte
produit un log d'erreur au lieu d'un envoi.

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
| `LOCAL_API_KEY` | clé du pays en `X-API-Key`, dans les deux sens : attendue sur les routes qui écrivent, présentée au siège lors de l'envoi des alertes. Doit valoir `LocalApi:Countries:{code}:ApiKey` côté siège |
| `HEAD_OFFICE_ALERTS_URL` | URL complète de `POST /api/alerts` du siège (consumers seulement), par exemple `http://localhost:55648/api/alerts` |
| `MQTT_BROKER_HOST`, `MQTT_BROKER_PORT` | broker des consumers (défaut `localhost:1883`) |

`DATABASE_URL` et `LOCAL_API_KEY` échouent à l'usage plutôt que de se replier sur
une valeur par défaut : une variable oubliée est une erreur de déploiement, elle
doit se voir. `HEAD_OFFICE_ALERTS_URL` absente ne fait pas tomber le consumer —
couper la surveillance pour une configuration d'envoi serait pire — mais chaque
alerte non envoyée est journalisée en erreur.
