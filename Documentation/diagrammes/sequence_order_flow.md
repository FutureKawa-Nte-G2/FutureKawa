# Diagramme de séquence — Flux commande (Odoo ↔ .NET)

## Scénario complet : de la création à l'expédition

```mermaid
sequenceDiagram
    actor U as Utilisateur
    participant O as Odoo 18
    participant M as Module FK<br/>(future_kawa_erp)
    participant API as Backend .NET
    participant SVC as OrderService
    participant ODOO as OdooIntegrationService
    participant DB as BDD .NET

    %% ── Étape 1 : Création de commande dans Odoo ──
    U->>O: Crée une commande (sale.order)
    U->>O: Renseigne champs FK<br/>(batch_ref, quality_grade, origin_country)
    U->>O: Clique "Confirmer"

    %% ── Étape 2 : Webhook Odoo → .NET ──
    O->>M: action_confirm()
    M->>M: super().action_confirm()<br/>(logique standard Odoo)
    M->>M: _notify_dotnet_backend()
    Note over M: Construit le payload JSON<br/>{order_id, client, date,<br/>batch_ref, quality_grade, lines}
    M->>API: POST /api/integration/odoo/orders<br/>Header: X-Webhook-Token
    API->>API: Valide le token
    API->>API: Valide le payload (FluentValidation)
    API->>SVC: ReceiveOrderFromOdooAsync(dto)
    SVC->>SVC: Vérifie idempotence<br/>(OdooOrderId déjà existant ?)
    SVC->>DB: INSERT Order + OrderLines
    SVC-->>API: OrderResponseDto
    API-->>M: 200 OK {success: true, data: {...}}
    M->>M: integration_status = "synced"

    %% ── Étape 3 : Consultation des commandes ──
    U->>API: GET /api/orders<br/>Authorization: Bearer JWT
    API->>SVC: GetOrdersAsync()
    SVC->>DB: SELECT Orders + OrderLines
    SVC-->>API: List<OrderResponseDto>
    API-->>U: 200 OK {success: true, data: [...]}

    %% ── Étape 4 : Marquer comme expédiée ──
    U->>API: PATCH /api/orders/{id}/status<br/>{status: "Shipped"}
    API->>SVC: UpdateOrderStatusAsync(id, dto)
    SVC->>DB: Récupère Order (avec OdooOrderId)
    SVC->>SVC: Status = Shipped
    SVC->>ODOO: NotifyOrderShippedAsync(odooOrderId)

    %% ── Étape 5 : JSON-RPC .NET → Odoo ──
    ODOO->>ODOO: AuthenticateAsync()<br/>JSON-RPC /jsonrpc<br/>{service: "common", method: "login"}
    ODOO-->>ODOO: uid
    ODOO->>O: JSON-RPC /jsonrpc<br/>{service: "object", method: "execute_kw",<br/>model: "sale.order", method: "action_mark_shipped"}
    O->>O: integration_status = "shipped"
    O-->>ODOO: true
    ODOO-->>SVC: true

    %% ── Étape 6 : Finalisation ──
    SVC->>DB: UPDATE Order (status, updatedAt)
    SVC-->>API: OrderResponseDto
    API-->>U: 200 OK {success: true, data: {...}}
```

## Points clés pour la démonstration

1. **Webhook (Odoo → .NET)** : Le module Python surcharge `action_confirm()` et envoie un POST HTTP. Interpréter les lignes de `_notify_dotnet_backend()` dans `sale_order.py`.

2. **Validation** : Le backend .NET valide le token partagé puis le payload avec FluentValidation. Interpréter `OdooOrderWebhookValidator.cs`.

3. **Idempotence** : Le `OrderService` vérifie si la commande existe déjà avant de l'insérer. Interpréter `ReceiveOrderFromOdooAsync()` dans `OrderService.cs`.

4. **JSON-RPC (.NET → Odoo)** : Le `OdooIntegrationService` authentifie puis appelle `execute_kw` pour déclencher `action_mark_shipped`. Interpréter `NotifyOrderShippedAsync()` dans `OdooIntegrationService.cs`.

5. **Pattern d'extension Odoo** : Le module surcharge `action_confirm()` en appelant `super()` puis ajoute un comportement. C'est le pattern standard d'extension d'un progiciel intégré.
