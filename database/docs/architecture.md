# FutureKawa — Diagrammes d'architecture

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

Diagrammes Mermaid documentant les choix de conception de la solution de suivi
des stocks et conditions de stockage (MSPR Bloc 4).

> **Moteur de données unique : PostgreSQL 16 + TimescaleDB** (métier relationnel +
> relevés IoT en hypertable). Le projet est **conforme à l'exigence SQL** du cahier.
> Rendu : GitHub/GitLab affichent ces blocs nativement, ou https://mermaid.live.

---

## 1. Architecture distribuée (pays ↔ siège)

Topologie répartie : un backend autonome par pays (PostgreSQL/TimescaleDB + broker
MQTT + API REST), un siège agrégateur. Le siège **pull** le relationnel léger et
**proxy** les courbes à la demande — communication **uniquement via l'API**, jamais
en base. Démo mono-backend, mais architecture conçue pour N pays.

```mermaid
flowchart LR
    subgraph PAYS["Backend Pays — ex. Bresil (conteneurise)"]
        IOT["Capteur IoT<br/>ESP32 + DHT22"]
        MQTT["Broker MQTT<br/>Mosquitto (pont, pub/sub)"]
        BRIDGE["Telegraf<br/>mqtt_consumer -> postgresql"]
        ALERTER["Consumer d'alerte<br/>(code, abonne MQTT)"]
        DB[("PostgreSQL + TimescaleDB<br/>metier + hypertable conditions")]
        API_P["API REST Pays<br/>lecture (service)"]
        CRON["Job quotidien<br/>peremption lots"]
        IOT -->|"publish"| MQTT
        MQTT -->|"subscribe (persistance)"| BRIDGE
        MQTT -->|"subscribe (alerte)"| ALERTER
        BRIDGE -->|"INSERT conditions (role telegraf)"| DB
        ALERTER -->|"seuils + dedup + alerte"| DB
        CRON -->|"lit date_stockage"| DB
        API_P -->|"lit vues (role apiread)"| DB
    end

    subgraph SIEGE["Siege (conteneurise)"]
        AGG["Backend central<br/>agregateur"]
        CACHE[("Cache relationnel<br/>read-model C-lite")]
        FRONT["Frontend Web"]
        AGG --> CACHE
        FRONT -->|"HTTP + refresh periodique"| AGG
    end

    R["Responsable<br/>d'exploitation"]

    AGG -->|"PULL periodique<br/>stocks - alertes"| API_P
    AGG -.->|"PROXY a la demande<br/>courbes d'un lot"| API_P
    ALERTER -->|"email sur alerte"| R
```

**Décisions illustrées :** **fan-out MQTT** — deux abonnés indépendants (Telegraf → DB
pour la persistance, consumer d'alerte → DB + email en temps-réel) · le broker reste
un pont pub/sub · **un seul moteur de données par pays** · API en lecture seule via un
rôle dédié · siège agrégateur cache C-lite · pull relationnel + proxy time-series · API-only.

---

## 2. Modèle de données (PostgreSQL/TimescaleDB)

Identité **hybride** : `id` (UUID technique, anti-collision en contexte réparti) +
code lisible (`code_metier`, `code_mqtt`). Les relevés vivent dans la **hypertable
`conditions`** (même moteur). Le **statut de lot n'est pas une colonne** : dérivé au read (diagramme 6).

```mermaid
erDiagram
    EXPLOITATION ||--o{ ENTREPOT : possede
    ENTREPOT    ||--o{ LOT : stocke
    ENTREPOT    ||--o{ ALERTE : "condition"
    LOT         ||--o{ ALERTE : "peremption"
    ENTREPOT    ||--o{ CONDITIONS : "code_mqtt (jointure logique)"

    EXPLOITATION {
        uuid id PK
        string pays
        string nom
        numeric temp_ideale
        numeric hum_ideale
        numeric tol_temp
        numeric tol_hum
    }
    ENTREPOT {
        uuid id PK
        uuid exploitation_id FK
        string nom
        string code_mqtt "UNIQUE, ex BR-ENT-01"
    }
    LOT {
        uuid id PK
        string code_metier "BR-2026-00042"
        uuid entrepot_id FK
        timestamptz date_stockage
        timestamptz date_sortie "nullable = en stock"
    }
    ALERTE {
        uuid id PK
        uuid entrepot_id FK
        uuid lot_id FK "nullable (peremption)"
        enum type "condition | peremption"
        enum etat "active | resolue"
        timestamptz created_at
        timestamptz resolved_at "nullable"
    }
    CONDITIONS {
        timestamptz time "hypertable Timescale"
        string pays
        string entrepot_id "= code_mqtt"
        float temperature
        float humidite
    }
```

**Décisions illustrées :** seuils/tolérances au niveau pays · UUID + code métier ·
`entrepot.code_mqtt` (clé de rattachement des relevés) · `lot.date_sortie` (fenêtre) ·
relevés en hypertable jointe par `code_mqtt` · alertes append-only · pas de colonne `statut`.

---

## 3. Flux IoT et levée d'alerte (fan-out événementiel)

Le même message MQTT est consommé par **deux abonnés indépendants** : Telegraf le
persiste dans la hypertable `conditions` ; un **consumer d'alerte** (en code) évalue les
seuils en temps-réel, gère la déduplication et envoie l'email. Le chemin de persistance
reste intact, l'alerting n'en dépend pas.

```mermaid
sequenceDiagram
    participant C as Capteur IoT
    participant M as Broker MQTT
    participant T as Telegraf
    participant P as PostgreSQL/TimescaleDB
    participant A as Consumer d'alerte
    participant R as Responsable expl.

    C->>M: publish {temp, hum}
    par Persistance
        M->>T: subscribe
        T->>P: INSERT conditions (role telegraf)
    and Alerte (temps-reel)
        M->>A: subscribe
        A->>P: lit seuils pays + alertes actives
        A->>A: evalue seuils +/- tolerance
        alt hors plage ET aucune alerte active
            A->>P: cree alerte (active) - dedup garantie par index
            A->>R: email
        else alerte active deja existante
            A->>A: ignore (anti-spam)
        else retour dans la plage
            A->>P: passe alerte a resolue
        end
    end
```

**Décisions illustrées :** fan-out pub/sub · alerting en code (testable), indépendant
de la persistance · déduplication par épisode garantie par la base · email local au pays.
(L'alerte « lot > 365 j » n'a pas d'événement MQTT → **job quotidien** lisant PostgreSQL.)

---

## 4. Consultation des courbes d'un lot (smart endpoint 100 % SQL)

La corrélation lot↔mesures est une **jointure SQL native** (même moteur) : un seul
endpoint, une seule requête. Le siège ne fait que **proxy**, le frontend affiche.

```mermaid
sequenceDiagram
    participant F as Frontend (siege)
    participant S as Backend Siege (proxy)
    participant B as Backend Pays (API)
    participant P as PostgreSQL/TimescaleDB (pays)

    F->>S: GET /lots/{id}/mesures
    S->>B: proxy GET /lots/{id}/mesures
    B->>P: SELECT * FROM v_lot_mesures WHERE lot_id=?
    Note over P: jointure lot->entrepot->conditions<br/>sur code_mqtt + fenetre [date_stockage, date_sortie|now)
    P-->>B: serie temp/hum bornee
    B-->>S: serie JSON
    S-->>F: serie JSON
    F->>F: affiche les courbes
```

**Décisions illustrées :** corrélation = **une requête SQL** (plus d'orchestration
bi-moteur) · jointure sur `code_mqtt` + fenêtre temporelle · le time-series ne transite
jamais en masse vers le siège (proxy à la demande).

---

## 5. Cycle de vie d'une alerte

Append-only : une ligne par occurrence. La résolution pose `resolved_at` sans
réécrire l'historique. C'est la sémantique des systèmes de supervision.

```mermaid
stateDiagram-v2
    [*] --> Active : 1re violation detectee (cree ligne + email)
    Active --> Active : violation persistante (anti-spam)
    Active --> Resolue : retour dans la plage (resolved_at)
    Resolue --> [*]
    note right of Active : une seule alerte active par episode (index partiel unique)
    note right of Resolue : nouvel episode = nouvelle ligne
```

**Décisions illustrées :** alertes append-only · état `active`/`résolue` (CHECK sur
`resolved_at`) · traçabilité par `created_at`/`resolved_at`.

---

## 6. Statut de lot — dérivé au read

Le statut n'est jamais stocké : il se calcule à l'affichage. Zéro incohérence.

```mermaid
flowchart TD
    START["Affichage d'un lot"] --> Q0{"date_sortie<br/>renseignee ?"}
    Q0 -->|oui| SORTI["statut = sorti"]
    Q0 -->|non| Q1{"now - date_stockage<br/>> 365 jours ?"}
    Q1 -->|oui| PERIME["statut = perime"]
    Q1 -->|non| Q2{"alerte ACTIVE<br/>sur l'entrepot ?"}
    Q2 -->|oui| EN_ALERTE["statut = en alerte"]
    Q2 -->|non| CONFORME["statut = conforme"]
```

**Décisions illustrées :** une seule source de vérité par fait · statut = projection ·
branche `sorti` (via `date_sortie`) · péremption calendaire (café vert, seuil unique 365 j).

---

## 7. Cible de déploiement — k3s multi-VM sur un hôte Proxmox

Déploiement réel sur **3 VMs d'un même hôte Proxmox** → cluster k3s multi-nœuds
(quorum etcd). **HA au niveau nœud/VM** (crash VM, panne de nœud, upgrade roulant,
failover CNPG). Le **serveur physique reste un SPOF assumé** ; la perte de **données**
est couverte par des **sauvegardes hors-VM**. Livrable de démo : `docker compose up`.

```mermaid
flowchart TB
    subgraph HOST["Hote Proxmox unique — SPOF assume"]
        subgraph K3S["k3s HA (3 VMs = 3 noeuds serveurs, quorum etcd)"]
            subgraph SL["Stateless : self-healing"]
                DEP["Deployments : Mosquitto, Telegraf<br/>(+ API, siege, frontend hors scope)"]
            end
            subgraph ST["Stateful"]
                PG["Cluster CloudNativePG + TimescaleDB<br/>instances:2 (primary + hot standby)<br/>anti-affinite par noeud, streaming"]
                MOS[("Mosquitto PVC<br/>StorageClass Longhorn (reattachable)")]
            end
        end
    end
    PBS[("Sauvegardes hors-VM<br/>CNPG barman -> object store")]
    PG -.->|"backup planifie"| PBS

    R["Responsable"] -->|"kubectl / helm"| K3S
```

**Décisions illustrées :** stateless = `Deployment` (self-healing) · Postgres = **cluster
CNPG `instances:2`** (failover opérateur, pas de quorum Postgres) · réplication par
streaming (pas de stockage répliqué requis pour PG) · Mosquitto/Longhorn réattachable ·
**durabilité = backups CNPG hors-VM** · SPOF hôte assumé (HA multi-hôte → diagramme 8).

---

## 8. Évolution vers une HA multi-hôte réelle

La HA **nœud/VM** est atteinte (diagramme 7). Pour survivre à la **perte de l'hôte
physique** (le SPOF assumé), l'évolution est un **cluster Proxmox multi-hôtes** :

```mermaid
flowchart TB
    subgraph EVO["Evolution : cluster Proxmox 3 hotes"]
        H1["Hote 1<br/>VM(s) k3s"]
        H2["Hote 2<br/>VM(s) k3s"]
        H3["Hote 3<br/>VM(s) k3s"]
        CEPH[("Ceph<br/>stockage repliss inter-hotes")]
        H1 --- H2
        H2 --- H3
        H3 --- H1
        H1 -.-> CEPH
        H2 -.-> CEPH
        H3 -.-> CEPH
    end
```

**Chemin d'évolution :** k3s réparti sur **3 hôtes Proxmox** (HA manager pour le
failover de VM) + **Ceph** (PVC répliqués inter-hôtes) → CloudNativePG place alors ses
instances sur des hôtes distincts (vraie tolérance à la perte d'un hôte). Aucun de ces
éléments n'est requis par le cahier (qui demande une architecture *tolérante aux pannes*
et une *résilience justifiée*) — ce que la solution livre déjà au niveau nœud/VM.

---

Le cahier des charges impose une persistance **SQL** : le choix **PostgreSQL + TimescaleDB**
(hypertable pour les relevés IoT) y répond **sans déviation** — un seul moteur, jointures
natives, une seule stratégie de HA (CloudNativePG) et de sauvegarde (barman hors-VM).
