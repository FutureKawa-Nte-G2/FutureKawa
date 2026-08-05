# Architecture — Intégration ERP Odoo

## Vue d'ensemble

```mermaid
graph TB
    subgraph "Conteneurs Docker"
        ODOO[Odoo 18 Community]
        ODOO_DB[(PostgreSQL<br/>BDD Odoo)]
    end

    subgraph "Backend .NET 10"
        API[API Gateway<br/>FutureKawaSiege.API]
        BIZ[Business Layer<br/>Services]
        DATA[Data Layer<br/>EF Core]
        APP_DB[(PostgreSQL<br/>BDD Applicative)]
    end

    subgraph "Frontend Next.js"
        WEB[Portal Web<br/>Next.js 16]
    end

    %% Odoo → .NET : webhook
    ODOO -- "webhook HTTP POST<br/>(order created)" --> API

    %% .NET → Odoo : JSON-RPC
    API -- "JSON-RPC<br/>(status = shipped)" --> ODOO

    %% Connexions BDD
    ODOO --- ODOO_DB
    DATA --- APP_DB

    %% Frontend → API
    WEB -- "HTTP API (JWT)" --> API

    %% Liens internes .NET
    API --> BIZ
    BIZ --> DATA
```

## Composants

### 1. Odoo 18 Community (Conteneur Docker)

- **Image** : `odoo:18.0`
- **Port** : 8069
- **BDD** : PostgreSQL 16 dédié (conteneur séparé)
- **Module personnalisé** : `future_kawa_erp` (Python, framework ORM Odoo)

### 2. Backend .NET 10 (API Gateway)

- **Rôle** : Point d'entrée unique pour le frontend, gateway vers Odoo
- **Endpoints** :
  - `POST /api/integration/odoo/orders` — reçoit le webhook Odoo (token partagé)
  - `GET /api/orders` — liste des commandes (JWT)
  - `GET /api/orders/{id}` — détail d'une commande (JWT)
  - `PATCH /api/orders/{id}/status` — mise à jour du statut (JWT, notifie Odoo si shipped)
- **BDD** : PostgreSQL dédié (entités User, RefreshToken, Order, OrderLine)

### 3. Frontend Next.js 16

- **Rôle** : Portal web (non inclus dans ce périmètre)
- Communique uniquement avec le backend .NET

## Flux de données

### Flux 1 : Création de commande (Odoo → .NET)

```mermaid
sequenceDiagram
    participant U as Utilisateur Odoo
    participant O as Odoo (Module FK)
    participant API as Backend .NET
    participant DB as BDD Applicative

    U->>O: Confirme commande (action_confirm)
    O->>O: super().action_confirm()
    O->>O: _notify_dotnet_backend()
    O->>API: POST /api/integration/odoo/orders<br/>{order_id, client, lines, ...}
    API->>API: Valide token X-Webhook-Token
    API->>API: Valide payload (FluentValidation)
    API->>DB: Enregistre Order + OrderLines
    API-->>O: 200 OK {success: true}
    O->>O: integration_status = "synced"
```

### Flux 2 : Expédition de lot (.NET → Odoo)

```mermaid
sequenceDiagram
    participant API as Backend .NET
    participant OS as OrderService
    participant OIS as OdooIntegrationService
    participant O as Odoo
    participant DB as BDD Applicative

    API->>OS: UpdateOrderStatusAsync(id, {status: "Shipped"})
    OS->>DB: Récupère Order (avec OdooOrderId)
    OS->>OS: Status = OrderStatus.Shipped
    OS->>OIS: NotifyOrderShippedAsync(odooOrderId)
    OIS->>OIS: AuthenticateAsync() → uid
    OIS->>O: JSON-RPC execute_kw<br/>sale.order.action_mark_shipped
    O->>O: integration_status = "shipped"
    O-->>OIS: true
    OIS-->>OS: true
    OS->>DB: UpdateAsync(order)
    OS-->>API: OrderResponseDto
```

## Sécurité

| Aspect              | Mécanisme                                           |
| ------------------- | --------------------------------------------------- |
| Webhook Odoo → .NET | Token partagé dans header `X-Webhook-Token`         |
| API .NET → Frontend | JWT Bearer (HMAC-SHA256)                            |
| .NET → Odoo         | Credentials Odoo (db, login, password) via JSON-RPC |
| Rate limiting       | Webhook : 30 req/min, Login : 5 req/min             |

## Configuration

### Paramètres système Odoo

| Paramètre                   | Valeur (dev)                                                     |
| --------------------------- | ---------------------------------------------------------------- |
| `future_kawa.webhook_url`   | `https://host.docker.internal:55648/api/integration/odoo/orders` |
| `future_kawa.webhook_token` | `futurekawa-webhook-shared-token-change-me`                      |

### appsettings.Development.json (.NET)

```json
{
  "Odoo": {
    "Url": "http://localhost:8069",
    "Db": "futurekawa",
    "Username": "admin",
    "Password": "admin",
    "WebhookToken": "futurekawa-webhook-shared-token-change-me"
  }
}
```
