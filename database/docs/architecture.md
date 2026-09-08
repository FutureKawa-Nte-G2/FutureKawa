# FutureKawa — Diagrammes d'architecture

Diagrammes Mermaid documentant les choix de conception de la solution de suivi des
stocks et des conditions de stockage (MSPR Bloc 4).

> **Moteur de données unique : PostgreSQL 16 + TimescaleDB** — métier relationnel et
> relevés IoT dans la même base, `measurements` en hypertable. Le projet répond donc à
> l'exigence SQL du cahier sans déviation : la corrélation lot↔mesures est une jointure
> native, pas une orchestration entre deux moteurs.
>
> Source de vérité du schéma : `Api/app/models.py` + `Api/alembic/`
> ([ADR-001](../../Documentation/adr/001-gouvernance-schema-donnees.md)). Ces diagrammes
> sont des **vues dérivées** : en cas de divergence, le code fait foi.

---

## 1. Architecture distribuée (pays ↔ siège)

Un backend autonome par pays — base, broker MQTT, API REST — et un siège agrégateur.
Le siège **tire** les données par HTTP ; il n'ouvre jamais de connexion à la base d'un
pays. La démo tourne avec un seul pays, mais rien dans la topologie n'en suppose un seul.

```mermaid
flowchart LR
    subgraph PAYS["Backend pays — ex. Bresil"]
        IOT["Capteur ESP32 + DHT22"]
        MQTT["Mosquitto<br/>(pont pub/sub, aucune logique)"]
        ING["Consumer persistance<br/>app/services/ingestion.py"]
        QUA["Consumer qualite<br/>app/services/quality.py"]
        DB[("PostgreSQL 16 + TimescaleDB<br/>metier + hypertable measurements")]
        API_P["API pays (FastAPI)"]
        IOT -->|"publish futurekawa/&lt;capteur&gt;"| MQTT
        MQTT -->|"subscribe"| ING
        MQTT -->|"subscribe"| QUA
        ING -->|"INSERT measurements"| DB
        QUA -->|"is_compliant + alerte + notification"| DB
        API_P -->|"lit"| DB
    end

    subgraph SIEGE["Siege"]
        AGG["Backend .NET"]
        SDB[("PostgreSQL<br/>agregat journalier + metier siege")]
        FRONT["Frontend Next.js"]
        ODOO["Odoo 18"]
        AGG --> SDB
        FRONT -->|"HTTP (meme origine via l'Ingress)"| AGG
        ODOO -->|"webhook commandes"| AGG
    end

    AGG -->|"PULL periodique<br/>GET /api/measurements (agregat de la veille)"| API_P

    R["Responsable d'exploitation"] --> FRONT
```

**Décisions illustrées :** fan-out MQTT — les deux consumers sont **indépendants**, un
échec de l'évaluation qualité n'empêche pas la persistance et réciproquement · le broker
reste un pont sans logique · un seul moteur de données par pays · communication
pays↔siège uniquement par HTTP.

> **Limite connue.** Le seul flux effectivement branché entre pays et siège est
> l'agrégat journalier des mesures. Les lots et les alertes ne traversent pas encore :
> voir §5.

---

## 2. Modèle de données

Identité hybride : UUID technique (anti-collision en contexte réparti) doublé d'une
référence lisible (`batch_ref`, `warehouse_ref`, `sensor.code`) que l'ERP et le firmware
manipulent.

```mermaid
erDiagram
    COUNTRIES  ||--o{ WAREHOUSES : "possede"
    COUNTRIES  ||--o{ FARMS : "possede"
    WAREHOUSES ||--o{ BATCHES : "stocke"
    FARMS      ||--o{ BATCHES : "produit"
    WAREHOUSES ||--o{ SENSORS : "equipe"
    SENSORS    ||--o{ SENSOR_ASSIGNMENTS : "suit"
    BATCHES    ||--o{ SENSOR_ASSIGNMENTS : "surveille par"
    SENSORS    ||--o{ MEASUREMENTS : "releve"
    WAREHOUSES ||--o{ ALERTS : "condition"
    BATCHES    ||--o{ ALERTS : "peremption"
    WAREHOUSES ||--o{ NOTIFICATIONS : "destinataire"

    COUNTRIES {
        uuid country_id PK
        string country_code "UNIQUE"
        numeric nominal_temp
        numeric tolerance_temp
        numeric nominal_humidity
        numeric tolerance_humidity
    }
    WAREHOUSES {
        uuid warehouse_id PK
        uuid country_id FK
        string warehouse_ref "UNIQUE, reference ERP"
    }
    BATCHES {
        uuid batch_id PK
        uuid warehouse_id FK
        uuid farm_id FK
        string batch_ref "UNIQUE"
        date stored_at
        date shipped_at "NULL = en stock (colonne lue par le FIFO)"
        string quality_grade "nullable, l'ERP ne la connait pas toujours"
        string batch_status
        boolean is_compliant
    }
    SENSORS {
        uuid sensor_id PK
        uuid warehouse_id FK
        string code "UNIQUE, dernier segment du topic MQTT"
        boolean is_active
    }
    SENSOR_ASSIGNMENTS {
        uuid sensor_assignment_id PK
        uuid sensor_id FK
        uuid batch_id FK
        timestamptz assigned_at
        timestamptz released_at "NULL = assignation ouverte"
    }
    MEASUREMENTS {
        uuid measurement_id PK
        timestamptz meas_date PK "colonne de partitionnement"
        uuid sensor_id FK
        numeric meas_temp
        numeric meas_humidity
    }
    ALERTS {
        uuid alert_id PK
        uuid warehouse_id FK
        uuid batch_id FK "NULL pour une alerte de condition"
        string alert_type "condition | expiration"
        string alert_status
        timestamptz resolved_at
    }
    NOTIFICATIONS {
        uuid notification_id PK
        uuid warehouse_id FK
        string notification_type
        uuid batch_id FK "nullable"
        uuid order_id FK "nullable"
        timestamptz read_at
    }
```

**Décisions illustrées :**

- **Seuils portés par le pays**, sous forme de bande `nominal ± tolerance` : un relevé
  sort de la plage aussi bien par le bas que par le haut.
- **`SENSOR_ASSIGNMENTS` est la pièce qui rend le reste possible.** Un capteur appartient
  à une salle, pas à un lot, et publie en continu — y compris quand la salle est vide.
  Sans cette table, « les relevés de ce lot » ne s'exprime pas : on servirait tout
  l'historique de la salle pour chaque lot qu'elle a hébergé.
- **Clé primaire composite sur `MEASUREMENTS`.** TimescaleDB exige la colonne de
  partitionnement dans toute contrainte d'unicité, clé primaire comprise. Le couple
  `(sensor_id, meas_date)` porte en plus l'idempotence : un broker en QoS 1 redélivre, et
  rejouer un message ne doit pas dupliquer une ligne.
- **`ALERTS` et `NOTIFICATIONS` sont distinctes.** Une alerte est le constat technique
  d'un franchissement de seuil ; une notification dit que quelqu'un doit regarder quelque
  chose. La notification porte une clé étrangère et non une phrase rendue : le libellé
  appartient à l'API, et un message stocké vieillirait mal.

> **Point ouvert signalé par l'ADR-001** : `batch_status` est une colonne stockée, et
> trois vocabulaires coexistent encore (documentation, tuples Python, enum C# du siège).
> À unifier avant de s'appuyer dessus pour de l'affichage.

---

## 3. Flux IoT et détection de non-conformité

Le même message est consommé par **deux abonnés indépendants**, chacun avec sa connexion
MQTT et sa session de base. Le broker délivre aux deux ; aucun ne dépend du travail de
l'autre.

```mermaid
sequenceDiagram
    participant C as Capteur
    participant M as Mosquitto
    participant I as Consumer persistance
    participant Q as Consumer qualite
    participant P as PostgreSQL/TimescaleDB

    C->>M: publish futurekawa/CODE-CAPTEUR<br/>{measuredAt, temp, humidity}
    par Persistance
        M->>I: subscribe
        I->>P: capteur actif ? assignation ouverte a la date du releve ?
        alt les deux conditions tenues
            I->>P: INSERT measurements (idempotent)
        else capteur inconnu, inactif, ou au repos
            I->>I: releve ignore — fonctionnement normal, pas une erreur
        end
    and Evaluation qualite
        M->>Q: subscribe
        Q->>P: lit les seuils du pays
        Q->>Q: hors de nominal +/- tolerance ?
        alt hors plage et lot encore conforme
            Q->>P: batch.is_compliant = false<br/>+ alerte + notification
        else deja signale
            Q->>Q: ignore (pas de notification par releve)
        end
    end
```

**Décisions illustrées :** l'écriture est **filtrée par l'assignation** — écrire tout ce
qui arrive remplirait `measurements` de bruit qu'aucune requête ne saurait rattacher à un
lot · toute exception sur un message est journalisée puis avalée : un relevé malformé est
l'incident d'un message, pas du consumer, et le laisser remonter arrêterait la
surveillance de toute la salle pour une ligne fautive · déduplication vérifiée : 24
relevés hors seuil consécutifs produisent **une** notification.

---

## 4. Consultation des courbes d'un lot

La corrélation lot↔mesures est une jointure SQL native, dans le même moteur : un seul
endpoint, une seule requête.

```mermaid
sequenceDiagram
    participant F as Frontend
    participant B as API pays
    participant P as PostgreSQL/TimescaleDB

    F->>B: GET /api/batches/{batch_id}/measurements
    B->>P: jointure batches -> sensor_assignments -> measurements
    Note over P: fenetre bornee par assigned_at / released_at,<br/>agregats journaliers calcules en SQL
    P-->>B: points journaliers + seuils + alertes
    B-->>F: JSON
    F->>F: trace les courbes
```

**Décisions illustrées :** la fenêtre temporelle vient de l'assignation, pas de la date de
stockage du lot · les agrégats sont calculés en base et non côté API · les seuils du pays
sont renvoyés avec la série, pour que le client trace les bandes sans second appel.

---

## 5. Ce qui traverse — et ce qui ne traverse pas encore

```mermaid
flowchart LR
    subgraph P["Pays"]
        PM[("measurements")]
        PB[("batches")]
        PN[("alerts / notifications")]
    end
    subgraph S["Siege"]
        SM[("Measurements")]
        SB[("Batches — vide")]
        SA["GET /api/alerts — absent"]
    end
    FRONT["Frontend"]

    PM -->|"MeasurementSync : agregat de la veille — OK"| SM
    PB -.->|"aucun flux"| SB
    PN -.->|"aucun emetteur cote pays"| SA
    FRONT -->|"appelle GET /api/alerts"| SA
```

**État vérifié sur la stack complète :**

| Flux | État |
|---|---|
| Agrégat journalier des mesures, pays → siège | **fonctionne** (3 entrepôts, 3 persistés, 0 erreur) |
| Lots, pays → siège | **aucun flux** — la table du siège reste vide |
| Alertes, pays → siège | **aucun émetteur** : `quality.py` n'émet aucun appel HTTP |
| `GET /api/alerts` côté siège | **n'existe pas** (405) — seul un `POST` de réception est implémenté |

Le `AlertsController` du siège annonce dans sa documentation qu'il reçoit les alertes de
l'API pays, avec authentification par `X-Api-Key`. Le récepteur existe donc, mais
**personne ne l'appelle**, et **rien ne relit** ce qu'il enregistrerait. La cloche
d'alertes du frontend ne peut par conséquent jamais s'allumer. Voir les issues #33 et #39.

---

## 6. Cycle de vie d'une alerte

Append-only : une ligne par épisode. La résolution pose `resolved_at` sans réécrire
l'historique — c'est la sémantique des systèmes de supervision.

```mermaid
stateDiagram-v2
    [*] --> Active : 1re violation detectee
    Active --> Active : violation persistante (aucune nouvelle ligne)
    Active --> Resolue : retour dans la plage (resolved_at)
    Resolue --> [*]
    note right of Active : une seule alerte active par episode<br/>(contrainte en base, pas en code)
    note right of Resolue : nouvel episode = nouvelle ligne
```

**Décisions illustrées :** l'unicité de l'alerte active est garantie **par la base** et
non par une vérification applicative, qui laisserait passer deux consumers concurrents ·
l'alerte « lot au-delà de 365 jours » n'a pas d'événement MQTT déclencheur : elle relève
d'un balayage périodique, pas de ce flux.

---

## 7. Cible de déploiement — k3s multi-VM sur un hôte Proxmox

Trois VMs d'un même hôte Proxmox forment un cluster k3s multi-nœuds. La haute
disponibilité est atteinte **au niveau nœud/VM** : crash de VM, panne de nœud, upgrade
roulant, failover CloudNativePG. Le **serveur physique reste un SPOF assumé** ; la perte
de données est couverte par des sauvegardes hors VM.

```mermaid
flowchart TB
    subgraph HOST["Hote Proxmox unique — SPOF assume"]
        subgraph K3S["k3s (3 VMs = 3 noeuds)"]
            subgraph CH1["Chart futurekawa-data — un release par pays"]
                D1["Mosquitto, API pays, consumers MQTT"]
                PG1["CNPG + TimescaleDB, instances:2<br/>anti-affinite par noeud"]
            end
            subgraph CH2["Chart futurekawa-siege — un seul release"]
                D2["Backend .NET, frontend, Odoo"]
                PG2["CNPG, instances:2"]
                ING["Ingress : / -> frontend, /api -> backend"]
            end
        end
    end
    PBS[("Sauvegardes hors VM<br/>CNPG barman -> object store")]
    PG1 -.->|"backup planifie"| PBS
    PG2 -.->|"backup planifie"| PBS
```

**Décisions illustrées :**

- **Deux charts et non un seul** : la stack pays se déploie une fois par pays, le siège
  est unique. Les fondre obligerait à redéployer le siège à chaque pays ajouté.
- **Postgres = cluster CNPG `instances:2`** : le failover est piloté par l'opérateur et
  non par un quorum entre instances, deux suffisent donc. Réplication par streaming :
  aucun stockage répliqué n'est requis pour la base.
- **Frontend et API sur la même origine** derrière l'Ingress. Ce n'est pas cosmétique :
  le cookie de refresh est `SameSite=Strict`, et deux origines distinctes l'empêcheraient
  d'être transmis — la reconnexion silencieuse au chargement échouerait.
- **Mosquitto sur un StorageClass réattachable** (Longhorn) en multi-nœuds : `local-path`
  épinglerait le pod à son nœud et l'ingestion ne survivrait pas à la perte de la VM.
- **Durabilité = sauvegardes CNPG hors VM.** La réplication protège de la panne d'un
  nœud, pas d'une suppression accidentelle.

---

## 8. Évolution vers une HA multi-hôte réelle

La HA nœud/VM est atteinte (§7). Pour survivre à la perte de l'hôte physique — le SPOF
assumé — l'évolution est un cluster Proxmox multi-hôtes :

```mermaid
flowchart TB
    subgraph EVO["Cluster Proxmox 3 hotes"]
        H1["Hote 1 — VM(s) k3s"]
        H2["Hote 2 — VM(s) k3s"]
        H3["Hote 3 — VM(s) k3s"]
        CEPH[("Ceph — stockage replique inter-hotes")]
        H1 --- H2
        H2 --- H3
        H3 --- H1
        H1 -.-> CEPH
        H2 -.-> CEPH
        H3 -.-> CEPH
    end
```

**Chemin d'évolution :** k3s réparti sur trois hôtes (HA manager pour le failover de VM)
et **Ceph** pour des PVC répliqués inter-hôtes. CloudNativePG place alors ses instances
sur des hôtes distincts, ce qui donne une vraie tolérance à la perte d'un hôte. Aucun de
ces éléments n'est exigé par le cahier, qui demande une architecture tolérante aux pannes
et une résilience justifiée — ce que la solution livre déjà au niveau nœud/VM.
