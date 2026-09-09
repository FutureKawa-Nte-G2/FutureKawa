# Périmètre DONNÉE — du broker à la base

> **Le scope :** du message MQTT reçu sur le broker jusqu'à une base prête à être
> requêtée, plus le déploiement de cet ensemble. En amont : le firmware du capteur. En
> aval : l'API métier, le backend siège, le frontend.

> **Moteur unique : PostgreSQL 16 + TimescaleDB** — métier relationnel et relevés IoT
> dans la même base, `measurements` en hypertable. La corrélation lot↔mesures est donc
> une jointure SQL native, sans orchestration entre deux moteurs, et le projet répond à
> l'exigence SQL du cahier sans déviation.

---

## 1. Frontière de responsabilité

Cette frontière **a bougé en cours de projet** et le document le reflète : l'ingestion
était initialement un pont Telegraf sans code, elle est aujourd'hui assurée par les
consumers Python de l'API pays (US #32). Le périmètre data ne fournit donc plus le
*mécanisme* d'ingestion, mais le **contrat** qu'il respecte et la **base** qu'il alimente.

| Je livre | Je ne livre pas |
|---|---|
| Configuration Mosquitto (broker) | Firmware ESP32, simulateur de capteur |
| **Contrat d'ingestion** : topic, payload, sémantique des horodatages (§3) | Le code des consumers MQTT (`Api/app/consumers/`) |
| **Gouvernance du schéma** : ADR-001, revue obligatoire via CODEOWNERS | Les endpoints HTTP de l'API pays |
| Modèle de données et invariants portés par la base | La logique d'évaluation des seuils |
| Charts Helm (pays + siège), Terraform, images, sauvegardes | Backend siège, frontend |

**Ce que « gouvernance du schéma » veut dire, concrètement.** La source de vérité est
`Api/app/models.py` + `Api/alembic/`. Je n'écris pas ce code, mais **toute modification du
schéma passe par ma revue** (`.github/CODEOWNERS`). C'est la décision de l'ADR-001, prise
après qu'un schéma validé collectivement a été remplacé de fait, sans re-validation.

**Frontière alertes.** La base garantit l'**invariant** — une seule alerte active par
épisode, par contrainte et non par vérification applicative. Le code décide **quand**
insérer. Un contrôle applicatif laisserait passer deux consumers concurrents ; une
contrainte, non.

---

## 2. Stack

| Composant | Version | Rôle |
|---|---|---|
| Mosquitto | `eclipse-mosquitto:2` | Pont MQTT, aucune logique |
| PostgreSQL + TimescaleDB | 16 / 2.17.2 | Moteur unique. Local : `timescale/timescaledb:2.17.2-pg16` |
| CloudNativePG | 1.25 | Opérateur Postgres en k8s, image custom CNPG+TimescaleDB |
| Alembic | 1.19 | Migrations. Le Job k8s utilise l'image de l'API, donc le schéma ne peut pas diverger du code qui le lit |

> Les versions sont épinglées, jamais `latest` — y compris le **paquet loader** de
> TimescaleDB, qui fournit `timescaledb.control` : non contraint, il installe un
> `default_version` dont aucun script n'est présent et le cluster ne démarre jamais.

---

## 3. Contrat d'ingestion

Ce que le périmètre data impose à l'amont.

**Topic :** `futurekawa/<code capteur>`

**Payload :**
```json
{ "measuredAt": "2026-09-06T12:00:00Z", "temp": 21.5, "humidity": 54.1 }
```

**Règles :**

- Le **code du capteur fait autorité côté topic**, jamais dans le corps du message. Un
  firmware ne peut pas usurper le topic sur lequel le broker l'a autorisé à publier,
  alors qu'il écrit son corps librement.
- `measuredAt` est l'instant de la **mesure**, pas celui de la réception : un message
  retardé par le broker doit être jugé sur le moment où il a été pris.
- **QoS 1**, pas de `retain`. La redélivrance est attendue et absorbée : la contrainte
  `(sensor_id, meas_date)` rend l'écriture idempotente.
- Les valeurs numériques sont lues **depuis la chaîne d'origine**, jamais via un float :
  `21.5` passé par un float donnerait `21.4999999999999996` dans une colonne
  `numeric(5,2)`.
- Toute clé hors contrat est ignorée ; un message illisible est écarté et journalisé,
  sans arrêter le consumer.

---

## 4. Ce que la base garantit

Le schéma n'est **pas reproduit ici**. Le dupliquer recréerait exactement le problème que
l'ADR-001 corrige : deux descriptions du même schéma qui divergent en silence. La source
est `Api/app/models.py`.

Ce document se limite aux **invariants** portés par la base, qui ne se lisent pas dans un
diagramme :

- **Clé primaire composite `(measurement_id, meas_date)`.** TimescaleDB exige la colonne
  de partitionnement dans toute contrainte d'unicité. Le couple `(sensor_id, meas_date)`
  porte en plus l'idempotence de la redélivrance QoS 1.
- **Une seule alerte active par épisode**, par contrainte. C'est ce qui permet au code
  d'insérer sans lire d'abord, et ce qui tient même à consumers concurrents.
- **`sensor_assignments` borne la fenêtre d'un lot.** Un capteur appartient à une salle et
  publie en continu, y compris à vide. Un relevé n'est conservé que si le capteur suivait
  un lot **au moment de la mesure** — pas au moment de l'écriture.
- **`server_default` sur les identifiants et horodatages.** Les valeurs par défaut côté
  client n'existent que pour les appelants Python ; un `INSERT` depuis `psql` ou un
  consumer non-ORM doit rester valide.
- **Rétention longue.** L'historique *est* le produit : c'est la preuve de traçabilité.
  Compression et policies TimescaleDB sont une évolution, pas un prérequis.

---

## 5. Contrat de lecture

Les endpoints qui exposent ce périmètre, servis par l'API pays :

| Endpoint | Usage |
|---|---|
| `GET /api/measurements?warehouse_ref=…` | Agrégat de la veille, tiré par le siège |
| `GET /api/batches/{batch_id}/measurements` | Courbes d'un lot : points journaliers, seuils, alertes |
| `GET /api/notifications?warehouse_ref=…` | Notifications d'un entrepôt, avec compteur de non-lues |
| `PATCH /api/notifications/{id}/read` | Marque une notification comme lue |

**Convention notable :** `GET /api/measurements` renvoie `200` avec un corps `null`
lorsqu'il n'y a aucun relevé — jamais `204`, jamais un objet aux champs nuls. Les deux
autres formes font lever une `JsonException` au client .NET du siège, ce qui lui coûterait
une erreur journalisée à chaque cycle calme. `null` se désérialise proprement et fait
sauter l'entrepôt sans bruit.

---

## 6. Déploiement

Deux charts, parce que la stack pays se déploie **une fois par pays** et le siège **une
seule fois** :

- `deploy/helm/futurekawa-data` — Postgres/TimescaleDB (CNPG), Mosquitto, API pays,
  consumers, Job Alembic.
- `deploy/helm/futurekawa-siege` — Postgres (CNPG), backend .NET et ses Jobs, frontend,
  Odoo, Ingress.

Cible : k3s sur trois VMs d'un hôte Proxmox, provisionné par `deploy/terraform/`. Procédure
et pièges dans [`../README.md`](../README.md).

---

## 7. Durabilité

Sauvegarde native CloudNativePG (barman → object store), poussée **hors VM**. À activer
via `postgres.backup.enabled=true` avec une destination.

C'est la seule vraie parade pour un métier de traçabilité : la réplication protège de la
panne d'un nœud, **pas** d'une suppression accidentelle. Le serveur physique reste un SPOF
assumé — on accepte de l'indisponibilité, jamais la perte de l'historique.

---

## 8. À ne pas faire

- **Dupliquer le schéma** dans un document ou un diagramme traité comme référence. Les
  vues dérivées sont utiles ; leur donner autorité sur le code ne l'est pas (ADR-001).
- **Modifier le schéma sans revue data.** C'est précisément ce qui a conduit à l'ADR.
- **Faire évaluer les seuils par le pont d'ingestion.** La persistance et l'évaluation
  sont deux abonnés indépendants : un échec de l'une ne doit pas emporter l'autre.
- **Monter les consumers au-delà d'un réplica** sans shared subscriptions (`$share`) :
  deux abonnements sur le même `client_id` écrivent chaque relevé deux fois.
- **Utiliser `latest`** sur une image ou un paquet.
- **Confondre réplication et sauvegarde.**
