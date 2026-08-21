# FutureKawa

EPSI MSPR Project Competency Block 4: Design and develop business and specific application solutions (mobile, embedded and ERP)

## Stack technique

| Layer              | Technology                 |
| ------------------ | -------------------------- |
| Web Frontend       | ReactJS (Next.js) 16       |
| Backend            | C# / .NET 10               |
| ERP                | Odoo 18 Community (Docker) |
| Versioning         | Git / GitHub               |
| Project Management | GitHub Projects            |

## Architecture globale

```
┌─────────────┐     webhook (order created)      ┌──────────────────┐
│   Odoo 18   │ ───────────────────────────────► │  Backend .NET 10 │
│  (Docker)   │                                  │   (API Gateway)  │
│  + PostgreSQL│ ◄─────────────────────────────  │                  │
│  (BDD Odoo) │     JSON-RPC (status=shipped)    │  + PostgreSQL    │
└─────────────┘                                  │  (BDD applicative)│
       │                                        └──────────────────┘
       │                                               │ HTTP API
       │                                               ▼
       │                                        ┌──────────────────┐
       │                                        │  Frontend Next.js │
       │                                        │  (portal web)     │
       │                                        └──────────────────┘
       │
       │     local API (measurements)           ┌──────────────────┐
       └──────────────────────────────────────► │  Entrepôt local  │
                                                 │  capteurs IoT    │
                                                 └──────────────────┘
```

- **Odoo 18** gère les commandes et déclenche un webhook vers le backend à la confirmation.
- **Backend .NET 10** expose l'API métier, reçoit les commandes Odoo, synchronise les mesures des entrepôts et sert le frontend.
- **Frontend Next.js** consomme l'API backend (portail web).
- **Entrepôts locaux** fournissent les mesures agrégées quotidiennes de température et d'humidité.

Voir [Documentation/diagrammes/architecture_odoo_integration.md](Documentation/diagrammes/architecture_odoo_integration.md) pour le détail.

## Démarrage

### 1. Backend .NET

```bash
cd backend
dotnet restore
dotnet ef database update --project FutureKawaSiege.Data --startup-project FutureKawaSiege.API
dotnet run --project FutureKawaSiege.API
```

### 2. ERP Odoo (Docker)

```bash
docker compose up -d
```

Odoo accessible sur `http://localhost:8069`.

Voir [Documentation/demo-script.md](Documentation/demo-script.md) pour la procédure complète.

## Synchronisation des mesures d'entrepôt

Le backend récupère périodiquement les **mesures agrégées quotidiennes** (température et humidité) de chaque entrepôt local via une API locale, et les stocke dans la base applicative PostgreSQL.

### Entités mesurées

Pour chaque entrepôt et chaque journée, les valeurs suivantes sont conservées :

| Valeur            | Description               |
| ----------------- | ------------------------- |
| `AvgMeasTemp`     | Température moyenne (°C)  |
| `MinMeasTemp`     | Température minimale (°C) |
| `MaxMeasTemp`     | Température maximale (°C) |
| `AvgMeasHumidity` | Humidité moyenne (%)      |
| `MinMeasHumidity` | Humidité minimale (%)     |
| `MaxMeasHumidity` | Humidité maximale (%)     |
| `MeasDate`        | Date de la mesure agrégée |

### Fonctionnement

- Un `MeasurementSyncBackgroundService` exécute la synchronisation à intervalle régulier (par défaut toutes les 24 h en dev, configurable via `MeasurementSync:IntervalMinutes`).
- `LocalMeasurementApiService` appelle l'URL configurée dans `MeasurementSync:LocalApiUrl` pour chaque entrepôt.
- En l'absence d'API réelle, le mode `MeasurementSync:UseMockData: true` génère des données fictives autour de 25 °C / 60 % d'humidité (conditions type stockage café).
- Une vérification d'idempotence empêche d'insérer deux mesures pour le même entrepôt et la même date.

### Endpoints API (JWT requis)

| Méthode | Endpoint                          | Description                                        |
| ------- | --------------------------------- | -------------------------------------------------- |
| `GET`   | `/api/measurements`               | Liste toutes les mesures stockées                  |
| `GET`   | `/api/measurements/{warehouseId}` | Liste les mesures d'un entrepôt donné              |
| `POST`  | `/api/measurements/sync`          | Déclenche manuellement un cycle de synchronisation |

### Mock local (dev uniquement)

Le endpoint non sécurisé `GET /api/mock/measurements` est disponible en environnement de développement pour simuler l'API d'un entrepôt local sans matériel IoT.

## Module ERP Odoo (`future_kawa_erp`)

Module personnalisé Odoo 18 pour la gestion des commandes et livraisons de café, situé dans `odoo-addons/future_kawa_erp/`.

### Fonctionnalités

- **Extension de `sale.order`** : champs métier `batch_count`, `batch_ref`, `quality_grade` (A/B/C, déduit du produit), `country`, `integration_status` et `integration_error_message` (synchronisation avec le backend .NET)
- **Extension de `stock.picking`** : champs `delivery_reference` et `carrier_tracking_ref`

### Intégration avec le backend .NET

**Flux Odoo → .NET (webhook)** : lors de la confirmation d'une commande (`action_confirm()`), le module envoie un POST HTTP vers le backend .NET avec le payload de commande (client, date, pays, grade, lots, lignes).

**Flux .NET → Odoo (JSON-RPC)** : le backend .NET appelle l'API JSON-RPC d'Odoo pour marquer une commande comme expédiée via `action_mark_shipped()`.

### Installation du module

1. Démarrer les conteneurs Docker : `docker compose up -d`
2. Créer la base de données Odoo via l'interface web : `http://localhost:8069`

   Les valeurs doivent correspondre à la section `Odoo` du fichier `backend/FutureKawaSiege.API/appsettings.Development.json` (utilisée par le backend pour se connecter à Odoo en JSON-RPC) :

   | Champ du formulaire Odoo | Valeur (dev)      | Clé `appsettings.Development.json` |
   | ------------------------ | ----------------- | ---------------------------------- |
   | Nom de la base           | `futurekawa`      | `Odoo:Db`                          |
   | Email                    | `admin@admin.com` | `Odoo:Username`                    |
   | Mot de passe             | `Not24get`        | `Odoo:Password`                    |

3. Installer les modules de base Odoo : **Sales**, **Inventory**
4. Installer le module FutureKawa :

   ```bash
   docker compose exec odoo odoo -u future_kawa_erp -d futurekawa --stop-after-init
   ```

### Paramétrage

Configurer les paramètres système Odoo (**Paramètres → Technique → Paramètres → Paramètres Système**) :

| Paramètre                   | Description                              |
| --------------------------- | ---------------------------------------- |
| `future_kawa.webhook_url`   | URL du endpoint backend .NET             |
| `future_kawa.webhook_token` | Token partagé (header `X-Webhook-Token`) |

En environnement de développement, le token doit correspondre à `Odoo:WebhookToken` du `appsettings.Development.json` : `futurekawa-webhook-shared-token`.

### Créer une commande de café

1. Dans Odoo, aller dans **Ventes → Nouveau** pour créer un devis
2. Renseigner le **client** (Customer)
3. Ajouter une ligne de commande : choisir un **produit** (café) et une **quantité**
   - Le `quality_grade` (A/B/C) est déduit automatiquement du produit choisi
4. Ouvrir l'onglet **FutureKawa** de la commande et renseigner :
   - **Nombre de lots** (`batch_count`) : nombre de lots de café à générer
   - **Pays de provenance** (`country`) : pays d'origine du café
5. Cliquer sur **Confirmer** : la commande est confirmée, les lots sont générés (`batch_ref`) et le webhook est envoyé vers le backend .NET
6. Vérifier le champ **Statut d'intégration** (`integration_status`) dans l'onglet FutureKawa : il indique si la synchronisation avec le backend a réussi (le message d'erreur éventuel est visible dans `integration_error_message`)

Détails complets : [odoo-addons/future_kawa_erp/README.md](odoo-addons/future_kawa_erp/README.md)

## Configuration backend

Le backend utilise les sections suivantes dans `appsettings.json` / `appsettings.Development.json` :

| Section             | Clé                      | Description                                                        |
| ------------------- | ------------------------ | ------------------------------------------------------------------ |
| `ConnectionStrings` | `DefaultConnection`      | Chaîne de connexion PostgreSQL applicative                         |
| `Jwt`               | `Secret`, `Issuer`, etc. | Paramètres d'authentification JWT                                  |
| `Odoo`              | `Db`, `Username`, etc.   | Connexion JSON-RPC à Odoo et token du webhook                      |
| `MeasurementSync`   | `LocalApiUrl`            | URL de l'API locale d'un entrepôt                                  |
|                     | `IntervalMinutes`        | Intervalle entre deux synchronisations (défaut 60 min)             |
|                     | `UseMockData`            | `true` pour générer des données fictives sans appeler d'API réelle |

### Configuration de développement pour les mesures

```json
"MeasurementSync": {
  "LocalApiUrl": "https://localhost:55648/api/mock/measurements",
  "IntervalMinutes": 1440,
  "UseMockData": true
}
```

- `UseMockData: true` permet de tester le workflow de synchronisation sans API IoT réelle.
- `LocalApiUrl` pointe vers le mock inclus dans le backend (`MockMeasurementsController`) pour la démo en local.
- `IntervalMinutes: 1440` déclenche une synchronisation par jour en dev.

## Repository Structure

```
FutureKawa/
├── .github                             # templates (Issues & PR)
│
├── backend/                            # C# .NET 10 application
│   ├── FutureKawaSiege.API/            # Web API (controllers, Program.cs)
│   │   └── Controllers/
│   │       ├── MeasurementsController.cs        # API mesures (JWT)
│   │       └── MockMeasurementsController.cs    # Mock local API (dev)
│   ├── FutureKawaSiege.Business/       # Business logic (services, validators)
│   │   └── Services/
│   │       ├── LocalMeasurementApiService.cs    # Appel API locale
│   │       ├── MeasurementSyncService.cs        # Orchestration sync
│   │       └── MeasurementSyncBackgroundService.cs  # Sync périodique
│   ├── FutureKawaSiege.Data/           # Data access (EF Core, repositories)
│   │   ├── Entities/
│   │   │   └── Measurement.cs          # Entité mesure
│   │   └── Repositories/
│   │       └── MeasurementRepository.cs
│   ├── FutureKawaSiege.Commons/        # Shared (DTOs, exceptions)
│   │   └── Models/API/Responses/
│   │       └── MeasurementResponseDto.cs
│   └── Tests/                          # Unit & integration tests
│
├── frontend/                           # Next.js 16 application
│   └── ...
│
├── docker-compose.yml                  # Conteneurs Odoo + PostgreSQL
├── odoo.conf                           # Configuration Odoo
├── odoo-addons/                        # Module Odoo personnalisé
│   └── future_kawa_erp/                # Module ERP FutureKawa (Python)
│
├── Documentation/
│   ├── diagrammes/                     # Diagrammes (architecture, séquence, ER)
│   └── demo-script.md                  # Script de démonstration jury
│
└── README.md                           # Global info on the project
```

## Cloner le repository

```bash
git clone git@github.com:FutureKawa-Nte-G2/FutureKawa.git
cd FutureKawa
```

## Branch Strategy

| Branch                                      | Purpose                      |
| ------------------------------------------- | ---------------------------- |
| `main`                                      | Stable production-ready code |
| `develop`                                   | Integration branch           |
| `feature/#[issue-number]-short-description` | New feature linked to a US   |
| `fix/#[issue-number]-short-description`     | Bug fix linked to an issue   |

### Naming Convention Examples

| Issue                 | Branch name                |
| --------------------- | -------------------------- |
| #12 - Login screen UI | `feature/#12-login-screen` |
| #23 - Fix auth token  | `fix/#23-auth-token`       |

### Workflow

1. Pick your assigned issue on the [GitHub Projects board](https://github.com/FETAH-APP/FETAH/projects)
2. Create your branch directly from the issue:
   - Open the issue on GitHub
   - In the right panel → **Development** → **Create a branch**
   - Verify the branch name follows the convention `feature/#[issue-number]-short-description`
   - Select `develop` as the source branch
   - Run the suggested commands locally:

```bash
git fetch origin
git checkout feature/#[issue-number]-short-description
```

3. Work and commit regularly:

```bash
git commit -m "feat(scope): description"
```

4. Push your branch:

```bash
git push origin feature/#[issue-number]-short-description
```

5. Once **all acceptance criteria are met**, open a PR toward `develop`:
   - Title: `feat(scope): #[issue-number] - short description`
   - Description: `Closes #[issue-number]`
6. Wait for review and approval before merging
7. PR merged → issue closed automatically ✅

---

### Visual Summary example

```
Issue #1 assigned to @dev
    ↓
feature/#1-navbar created from develop
    ↓
Development + regular commits
    ↓
All acceptance criteria met ✅
    ↓
PR toward develop (Closes #1) → review → merge
    ↓
Issue #1 closed automatically ✅
```

---

### PR Description Template

```markdown
## Description

Short description of what this PR does.

## Type of change

Feature / Bug fix / Documentation

## Related Issue

Closes #[issue-number]

## Acceptance Criteria

- [ ] Criteria 1
- [ ] Criteria 2

## How to test

## Screenshots

## Checklist

---

## Commit Convention

We follow the [Conventional Commits](https://www.conventionalcommits.org) standard:
```

type(scope): short description

````

| Type | Usage |
|------|-------|
| `feat` | New feature |
| `fix` | Bug fix |
| `style` | UI / formatting only |
| `refactor` | Code change without new feature |
| `docs` | Documentation only |
| `chore` | Config, dependencies |

### Examples
```bash
git commit -m "feat(feed): add swipeable post card component"
git commit -m "fix(auth): handle invalid token response"
git commit -m "docs(readme): update branch strategy section"
````

## Pull Request Rules

- PRs must always target `develop`, **never `main`**
- Link the related GitHub Issue in the PR description using `Closes #[issue-number]`
- At least **1 team member must review** before merging
- Do not merge your own PR without review
- PR title must follow the commit convention: `feat(scope): #[issue-number] - description`

---

## Definition of Done

A User Story is considered **Done** when:

- [ ] The feature works as described in the acceptance criteria
- [ ] The code has been reviewed and approved via Pull Request
- [ ] The branch has been merged into `develop`
- [ ] The related GitHub Issue is closed
- [ ] No known bugs are introduced

---

## Project Management

Tasks and User Stories are tracked on our
[GitHub Projects board](<[Project-Kanban](https://github.com/orgs/FutureKawa-Nte-G2/projects/2)>).
