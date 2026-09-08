# Périmètre DONNÉE — du broker à la base (contrat de lecture)

> [!IMPORTANT]
> **Document partiellement obsolète.** L'ingestion décrite ici (pont **Telegraf** vers une
> hypertable `conditions`, migrations **Flyway**) a été remplacée par les **consumers Python**
> (`Api/app/consumers/`) écrivant dans `measurements`, et par les migrations **Alembic**.
> Le contrat de topic a changé lui aussi : `futurekawa/<code capteur>` avec les champs
> `temp`/`humidity`, et non `futurekawa/{pays}/{code_mqtt}/conditions` avec
> `temperature`/`humidite`.
>
> Source de vérité du schéma : `Api/app/models.py` + `Api/alembic/` (ADR-001).
> Déploiement à jour : [`database/README.md`](../README.md).
> Réécriture de ce document à planifier.

> Mon scope dans l'équipe : **du message MQTT reçu sur le broker jusqu'à une base
> prête à être requêtée**, avec un contrat de lecture exécutable pour le dev API.
> Hors scope : firmware/capteur (en amont), API métier, logique d'alerte, frontend
> (en aval).

> **Choix moteur (assumé) :** un **seul moteur de données, PostgreSQL 16 + TimescaleDB**
> (métier relationnel **et** relevés IoT en hypertable). Le projet est donc **conforme à
> l'exigence SQL** du cahier des charges — la corrélation lot↔mesures est une jointure SQL
> native, sans orchestration inter-moteurs.

---

## 1. Frontière de responsabilité

**Entrée :** le message arrive sur Mosquitto. Le capteur et le firmware ESP32 sont
**avant** moi ; je leur **impose un contrat** (topic + payload, §3) mais je ne les code
pas.

**Sortie :** une base modélisée + indexée + peuplée + des **vues/requêtes
documentées** (§6). Le dev API consomme ce contrat ; je n'écris pas l'API.

| Je livre | Je ne livre PAS (autre membre) |
|---|---|
| Config Mosquitto + Telegraf (ingestion MQTT → PostgreSQL) | Firmware ESP32 / simulateur capteur |
| Schéma PostgreSQL + hypertable TimescaleDB (relevés) | Endpoints HTTP de l'API |
| Schéma métier (tables, enums, index, contraintes, invariants) | **Logique** d'évaluation d'alerte (le consumer décide « hors plage → crée ») |
| Vues de lecture + smart endpoint (100 % SQL) | Orchestration finale dans le code API |
| Rôles moindre-privilège (`telegraf`, `apiread`) | Envoi d'email |
| Seed (3 pays + seuils), migrations Flyway | Frontend / Chart.js |
| Chart Helm (CNPG), Terraform (VMs + k3s), backups | |

**Frontière alertes (à dire clairement en soutenance) :** je fournis le **modèle**
(table `alerte`, seuils par pays, contraintes de déduplication, requêtes). Le dev
fournit l'**évaluation** (le consumer MQTT qui applique la règle). La DB *garantit
l'invariant* (une seule alerte active par épisode) ; le code *décide quand insérer*.

---

## 2. Stack de mon périmètre + versions

- **Mosquitto** (`eclipse-mosquitto:2`) — pont MQTT.
- **Telegraf** (`telegraf:1.30`) — `mqtt_consumer` → output **`postgresql`**. Pont d'ingestion, **zéro code**.
- **PostgreSQL 16 + TimescaleDB** — moteur unique. Local : image `timescale/timescaledb:2.17.2-pg16`.
  k8s : cluster **CloudNativePG** avec image custom CNPG+TimescaleDB.
- **Flyway** — migrations SQL versionnées (mécanisme identique local et k8s).

---

## 3. Contrat d'ingestion (ce que j'impose à l'amont)

**Topic MQTT :** `futurekawa/{pays}/{code_mqtt}/conditions`
- `pays` : `BR` | `EC` | `CO`
- `code_mqtt` : le code stable de l'entrepôt (ex. `BR-ENT-01`), **pas** un UUID.

**Payload JSON :**
```json
{ "temperature": 29.4, "humidite": 56.1 }
```
- QoS 1 recommandé. Pas de `retain`. Telegraf horodate à la réception.
- Fréquence de publication : à fixer avec l'amont (ex. toutes les 30–60 s).
- ⚠️ Toute clé JSON en dehors de `temperature`/`humidite` est **ignorée** (schéma figé).

---

## 4. Ingestion — Telegraf (pont MQTT → PostgreSQL)

`infra/telegraf/telegraf.conf` (extrait) :
```toml
[agent]
  omit_hostname = true                 # pas de colonne 'host'

[[inputs.mqtt_consumer]]
  servers   = ["tcp://mosquitto:1883"]
  topics    = ["futurekawa/+/+/conditions"]
  qos       = 1
  client_id = "telegraf-local"          # session persistante -> QoS 1 tenu
  persistent_session = true
  topic_tag = ""                        # pas de colonne 'topic'
  data_format  = "json"
  fieldinclude = ["temperature", "humidite"]   # colonnes de mesure figées
  name_override = "conditions"
  [[inputs.mqtt_consumer.topic_parsing]]
    topic = "futurekawa/+/+/conditions"
    tags  = "_/pays/entrepot_id/_"

[[outputs.postgresql]]
  connection = "host=postgres ... dbname=futurekawa sslmode=disable"
  add_column_templates = []             # interdit toute modif de schéma hors Flyway
```
> Telegraf écrit dans la table **`conditions`** (hypertable). `pays` et `entrepot_id`
> (= `code_mqtt`) sont des colonnes-tags ; `temperature`/`humidite` des mesures.
> C'est le seul chemin d'écriture des relevés. En k8s, Telegraf se connecte via le
> rôle **`telegraf`** (INSERT sur `conditions` uniquement).

---

## 5. Schémas

### 5.1 Relevés — table `conditions` (TimescaleDB)

```sql
CREATE TABLE conditions (
  time        TIMESTAMPTZ NOT NULL,
  pays        TEXT,
  entrepot_id TEXT,                 -- = code_mqtt (tag Telegraf)
  temperature DOUBLE PRECISION,
  humidite    DOUBLE PRECISION
);
SELECT create_hypertable('conditions', 'time');
CREATE INDEX idx_conditions_entrepot_time ON conditions (entrepot_id, "time" DESC);
```
- **Rétention : longue / infinie** au proto (l'historique EST le produit, preuve de
  traçabilité). Compression/rétention TimescaleDB = évolution (policies), pas au proto.
- L'extension `timescaledb` est créée par la **plateforme** (image locale / CNPG), pas par Flyway.

### 5.2 Métier (PostgreSQL)

```sql
CREATE TABLE exploitation (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  pays TEXT NOT NULL, nom TEXT NOT NULL,
  temp_ideale NUMERIC(4,1) NOT NULL, hum_ideale NUMERIC(4,1) NOT NULL,
  tol_temp NUMERIC(3,1) NOT NULL DEFAULT 3.0, tol_hum NUMERIC(3,1) NOT NULL DEFAULT 2.0
);
CREATE TABLE entrepot (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  exploitation_id UUID NOT NULL REFERENCES exploitation(id),
  nom TEXT NOT NULL,
  code_mqtt TEXT NOT NULL UNIQUE           -- identité externe stable (topic + tag)
);
CREATE TABLE lot (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  code_metier TEXT NOT NULL UNIQUE,
  entrepot_id UUID NOT NULL REFERENCES entrepot(id),
  date_stockage TIMESTAMPTZ NOT NULL DEFAULT now(),
  date_sortie   TIMESTAMPTZ,                -- NULL = en stock ; borne haute de la fenêtre
  CONSTRAINT chk_lot_dates CHECK (date_sortie IS NULL OR date_sortie >= date_stockage)
);
CREATE INDEX idx_lot_date_stockage ON lot (date_stockage);
CREATE INDEX idx_lot_en_stock ON lot (entrepot_id) WHERE date_sortie IS NULL;

CREATE TYPE alerte_type AS ENUM ('condition', 'peremption');
CREATE TYPE alerte_etat AS ENUM ('active', 'resolue');
CREATE TABLE alerte (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  entrepot_id UUID NOT NULL REFERENCES entrepot(id),
  lot_id UUID REFERENCES lot(id),          -- NULL pour 'condition', rempli pour 'peremption'
  type alerte_type NOT NULL, etat alerte_etat NOT NULL DEFAULT 'active',
  created_at TIMESTAMPTZ NOT NULL DEFAULT now(), resolved_at TIMESTAMPTZ,
  CONSTRAINT chk_alerte_lot CHECK (
    (type='peremption' AND lot_id IS NOT NULL) OR (type='condition' AND lot_id IS NULL)),
  CONSTRAINT chk_alerte_resolution CHECK (
    (etat='active' AND resolved_at IS NULL) OR (etat='resolue' AND resolved_at IS NOT NULL))
);
-- invariants de déduplication garantis par la base :
CREATE UNIQUE INDEX uq_alerte_condition_active ON alerte (entrepot_id)
  WHERE etat='active' AND type='condition';
CREATE UNIQUE INDEX uq_alerte_peremption_active ON alerte (lot_id)
  WHERE etat='active' AND type='peremption';
-- cohérence inter-tables (l'alerte péremption vise un lot du même entrepôt) via FK composite :
ALTER TABLE lot ADD CONSTRAINT uq_lot_id_entrepot UNIQUE (id, entrepot_id);
ALTER TABLE alerte ADD CONSTRAINT fk_alerte_lot_entrepot
  FOREIGN KEY (lot_id, entrepot_id) REFERENCES lot (id, entrepot_id);
```
> **Pas de colonne `statut` sur `lot`** : le statut est dérivé au read (vue §6.2).
> Les index partiels font que la base **refuse** une 2ᵉ alerte active du même épisode.

### 5.3 Seed
3 exploitations (BR/EC/CO) + un entrepôt chacune (`code_mqtt` = `XX-ENT-01`) + lots de
démo couvrant `conforme`/`perime`/`sorti`. Fixtures de démonstration (multi-pays), pas
de la donnée de production.

---

## 6. Contrat de lecture livré au dev API (100 % SQL)

### 6.1 Smart endpoint `GET /lots/{id}/mesures` — une seule requête
```sql
SELECT "time", temperature, humidite
FROM v_lot_mesures WHERE lot_id = :id ORDER BY "time";
```
`v_lot_mesures` joint `lot → entrepot → conditions` sur `code_mqtt` et borne sur
`[date_stockage, date_sortie|now)`. Plus de jointure inter-moteurs.

### 6.2 Statut de lot dérivé
```sql
SELECT * FROM v_lot_statut;   -- 'sorti' | 'perime' (>365j) | 'en_alerte' | 'conforme'
```

### 6.3 FIFO + autres lectures
```sql
SELECT * FROM v_lot_statut WHERE date_sortie IS NULL ORDER BY date_stockage ASC;  -- FIFO en stock
SELECT a.* FROM alerte a
JOIN entrepot e ON e.id=a.entrepot_id JOIN exploitation x ON x.id=e.exploitation_id
WHERE x.pays=:pays AND a.etat='active';
```

### 6.4 Aides à l'écriture des alertes (le dev appelle, je garantis l'invariant)
```sql
SELECT x.temp_ideale, x.hum_ideale, x.tol_temp, x.tol_hum
FROM entrepot e JOIN exploitation x ON x.id=e.exploitation_id WHERE e.id=:entrepot_id;
INSERT INTO alerte (entrepot_id, type) VALUES (:entrepot_id, 'condition');   -- échoue si doublon
UPDATE alerte SET etat='resolue', resolved_at=now()
WHERE entrepot_id=:entrepot_id AND type='condition' AND etat='active';
```

---

## 7. Conteneurs & déploiement

- **Démo :** `docker compose up` (Mosquitto, TimescaleDB, Telegraf, Flyway). Cf. `../README.md`.
- **Cible :** chart Helm `deploy/helm/futurekawa-data` sur **k3s (3 VMs sur 1 hôte Proxmox)**
  provisionné par `deploy/terraform/`. Postgres = **cluster CloudNativePG + TimescaleDB**
  `instances:2` (1 primary + 1 hot standby, failover opérateur), Mosquitto/Telegraf en
  Deployments, migrations en Job (hook post-install). Un release par pays.

---

## 8. Durabilité (volet infra de mon scope)

- **PostgreSQL** : sauvegarde native **CloudNativePG** (barman → object store), poussée
  **hors-VM**. `postgres.backup.enabled=true` + destination à brancher.
- C'est la vraie parade pour un métier de **traçabilité** : le serveur physique est un
  **SPOF assumé** (mono-hôte) ; on accepte du downtime, jamais la perte de l'historique.

---

## 9. À NE PAS FAIRE (dans mon périmètre)

- ❌ Laisser Telegraf modifier le schéma (colonnes `host`/`topic`, auto-`ALTER`) → config figée (§4).
- ❌ Colonne `statut` stockée → vue `v_lot_statut`.
- ❌ Rétention courte → l'historique est le produit.
- ❌ Évaluer les alertes dans Telegraf → Telegraf ne fait QUE persister ; l'évaluation est au dev.
- ❌ `latest` sur les images → versions épinglées.
- ❌ Un seul rôle DB tout-puissant → `telegraf` (INSERT) et `apiread` (SELECT) séparés.
- ❌ Fausse redondance (réplica sur un disque unique) → HA nœud via CNPG + k3s ; HA hôte = évolution.

---

## 10. Ordre de livraison conseillé

1. `docker-compose` datastores + ingestion (Mosquitto, TimescaleDB, Telegraf).
2. Migrations Flyway (§5) + seed.
3. Config Telegraf + test bout-en-bout : publier un message MQTT → vérifier `conditions`.
4. Vues de lecture (§6) + jeu de requêtes documentées.
5. Chart Helm + Terraform (socle k3s) + rôles moindre-privilège.
6. Backups CNPG hors-VM.
