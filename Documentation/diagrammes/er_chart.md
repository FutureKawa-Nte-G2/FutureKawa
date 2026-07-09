# Modèle Entité-Relation — FutureKawa

Cible : **PostgreSQL 16** (+ TimescaleDB pour les relevés issus du flux MQTT).

> Les relevés (`MEASUREMENT`) vivent dans une **hypertable Timescale**. Le lien
> mesure ↔ batch passe par `SENSOR_ASSIGNMENT` (fenêtre temporelle), pas par une FK
> directe : un capteur est réassigné à un nouveau batch une fois l'ancien sorti.

## Types ENUM

```sql
CREATE TYPE user_role     AS ENUM ('admin', 'operator', 'driver');
CREATE TYPE batch_status  AS ENUM ('in_stock', 'delivered');
CREATE TYPE quality_grade AS ENUM ('premium', 'mid_range', 'robusta');
-- quality_grade extensible via ALTER TYPE ... ADD VALUE
CREATE TYPE alert_type    AS ENUM ('condition', 'expiration');
CREATE TYPE alert_state   AS ENUM ('active', 'resolved');
```

## Diagramme

```mermaid
erDiagram
    COUNTRY {
        serial id PK
        varchar name "NOT NULL, length 100"
    }
    FARM {
        serial id PK
        varchar name "NOT NULL, length 150"
        varchar address "length 255"
        int country_id FK "NOT NULL"
    }
    SITE {
        serial id PK
        varchar name "NOT NULL, length 150"
        varchar city "length 100"
        varchar zip_code "length 20"
        int country_id FK "NOT NULL"
    }
    USER {
        serial id PK
        varchar name "NOT NULL, length 150"
        user_role role "ENUM, NOT NULL"
        varchar email "NOT NULL, UNIQUE"
        varchar password_hash "NOT NULL"
        int site_id FK "NOT NULL"
    }
    BATCH {
        serial id PK
        timestamptz entered_at "NOT NULL, entree fimo"
        timestamptz exited_at "nullable = still in system"
        int farm_id FK "NOT NULL"
        int user_id FK "NOT NULL"
        quality_grade quality "ENUM, NOT NULL"
        batch_status status "ENUM, NOT NULL"
        boolean is_compliant "NOT NULL, default false"
    }
    ORDERS {
        serial id PK
        int client_id "external ERP reference"
        numeric quantity "NOT NULL, >= 0"
        quality_grade quality "ENUM, NOT NULL"
        int site_id FK "NOT NULL"
    }
    DELIVERY {
        serial id PK
        int order_id FK "NOT NULL"
        int user_id FK "NOT NULL"
        timestamptz delivered_at "NOT NULL, default now()"
    }
    BATCH_DELIVERY {
        serial id PK
        int delivery_id FK "NOT NULL"
        int batch_id FK "NOT NULL, UNIQUE"
    }
    SENSOR {
        serial id PK
        varchar code "NOT NULL, UNIQUE, MQTT topic id"
        varchar model "length 100"
        boolean is_active "NOT NULL, default true"
    }
    SENSOR_ASSIGNMENT {
        serial id PK
        int sensor_id FK "NOT NULL"
        int batch_id FK "NOT NULL"
        timestamptz assigned_at "NOT NULL, batch enters fimo"
        timestamptz released_at "nullable = currently assigned"
    }
    MEASUREMENT {
        timestamptz time "hypertable partition key"
        int sensor_id FK "NOT NULL"
        numeric temperature
        numeric humidity
    }
    ALERT {
        serial id PK
        int batch_id FK "nullable"
        int sensor_id FK "nullable"
        alert_type type "ENUM, NOT NULL"
        alert_state state "ENUM, NOT NULL, default active"
        timestamptz created_at "NOT NULL, default now()"
        timestamptz resolved_at "nullable"
    }

    COUNTRY ||--o{ FARM : "locates"
    COUNTRY ||--o{ SITE : "locates"
    SITE ||--o{ USER : "assigns"
    SITE ||--o{ ORDERS : "ships_to"
    FARM ||--o{ BATCH : "produces"
    USER ||--o{ BATCH : "records"
    USER ||--o{ DELIVERY : "handles"
    ORDERS ||--o{ DELIVERY : "generates"
    BATCH ||--o| BATCH_DELIVERY : "shipped_in"
    DELIVERY ||--o{ BATCH_DELIVERY : "contains"
    BATCH ||--o{ SENSOR_ASSIGNMENT : "monitored_by"
    SENSOR ||--o{ SENSOR_ASSIGNMENT : "assigned_to"
    SENSOR ||--o{ MEASUREMENT : "emits"
    BATCH ||--o{ ALERT : "triggers"
    SENSOR ||--o{ ALERT : "raised_by"
```

## Traçabilité mesure ↔ batch

Un capteur mesure **un seul batch à la fois**, sur la fenêtre passée dans le fimo.
`SENSOR_ASSIGNMENT` matérialise cette fenêtre ; à la sortie du batch on renseigne
`released_at`, ce qui libère le capteur pour un nouveau batch.

Les mesures d'un batch se retrouvent par jointure sur le capteur **et** la fenêtre :

```sql
SELECT m.time, m.temperature, m.humidity
FROM measurement m
JOIN sensor_assignment sa ON sa.sensor_id = m.sensor_id
WHERE sa.batch_id = :batch_id
  AND m.time >= sa.assigned_at
  AND m.time <  COALESCE(sa.released_at, now());
```

Contraintes clés (à poser dans les migrations) :

- `MEASUREMENT` : hypertable, PK composite `(sensor_id, time)` — la colonne de
  partitionnement `time` doit faire partie de tout index unique.
- `SENSOR_ASSIGNMENT` : **pas de chevauchement par capteur** — contrainte
  d'exclusion `EXCLUDE USING gist (sensor_id WITH =, tstzrange(assigned_at, released_at) WITH &&)`
  (extension `btree_gist`). Garantit qu'un capteur n'est jamais sur deux batchs en même temps.
- `BATCH_DELIVERY.batch_id` : `UNIQUE` — un batch (acheté en entier) n'apparaît que sur une ligne.
