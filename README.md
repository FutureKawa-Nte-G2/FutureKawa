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
       │ (interface native Odoo                        │ HTTP API
       │  pour démo jury)                              ▼
       │                                        ┌──────────────────┐
       │                                        │  Frontend Next.js │
       │                                        │  (portal web)     │
       │                                        └──────────────────┘
```

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

## Repository Structure

```
FutureKawa/
├── .github                             # templates (Issues & PR)
│
├── backend/                            # C# .NET 10 application
│   ├── FutureKawaSiege.API/            # Web API (controllers, Program.cs)
│   ├── FutureKawaSiege.Business/       # Business logic (services, validators)
│   ├── FutureKawaSiege.Data/           # Data access (EF Core, repositories)
│   ├── FutureKawaSiege.Commons/        # Shared (DTOs, exceptions)
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
