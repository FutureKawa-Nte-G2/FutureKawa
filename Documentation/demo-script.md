# Script de démonstration — Intégration ERP Odoo ↔ Backend .NET

## Prérequis

1. Docker Desktop démarré
2. PostgreSQL .NET en cours d'exécution (port 5432)
3. Backend .NET démarré (port 55648)
4. Odoo démarré via `docker compose up -d` (port 8069)

## Préparation

### 1. Démarrer Odoo

```bash
docker compose up -d
```

Vérifier : `http://localhost:8069` affiche l'assistant de création de BDD Odoo.

### 2. Créer la base de données Odoo

- Aller sur `http://localhost:8069`
- Créer une BDD nommée `futurekawa`
- Mot de passe admin : `admin`
- Cocher "Load demo data"

### 3. Installer les modules de base

Dans Odoo :

- **Apps** → installer **Sales** et **Inventory**

### 4. Installer le module FutureKawa

```bash
docker compose exec odoo odoo -u future_kawa_erp -d futurekawa --stop-after-init
```

Ou via l'interface : **Apps** → rechercher "FutureKawa" → **Install**

### 5. Configurer les paramètres système

Dans Odoo : **Paramètres → Technique → Paramètres → Paramètres Système**

Vérifier :

- `future_kawa.webhook_url` = `https://host.docker.internal:55648/api/integration/odoo/orders`
- `future_kawa.webhook_token` = `futurekawa-webhook-shared-token`

### 6. Appliquer la migration .NET

```bash
cd backend
dotnet ef database update --project FutureKawaSiege.Data --startup-project FutureKawaSiege.API
```

---

## Démonstration

### Scénario 1 : Création d'une commande dans Odoo → webhook vers .NET

**Étapes :**

1. Dans Odoo, aller dans **Ventes → Commandes → Créer**
2. Sélectionner le client "Distributeur Café Europe SARL" (données de démo)
3. Ajouter une ligne : "Café Arabica - Grade A", quantité 100
4. Aller dans l'onglet **FutureKawa** :
   - Référence Lot : `LOT-001`
   - Grade Qualité : `Grade A`
   - Pays d'Origine : `Colombie`
5. Cliquer **Confirmer**

**Points à expliquer au jury :**

- **Code Python** (`sale_order.py`, ligne ~70) : La méthode `action_confirm()` surcharge la méthode standard d'Odoo. Elle appelle `super().action_confirm()` pour exécuter la logique native, puis déclenche `_notify_dotnet_backend()`.

- **Code Python** (`sale_order.py`, ligne ~90) : La méthode `_notify_dotnet_backend()` construit le payload JSON et envoie un POST HTTP vers le backend .NET. Le token d'authentification est récupéré depuis les paramètres système d'Odoo.

- **Code C#** (`IntegrationController.cs`) : Le endpoint `POST /api/integration/odoo/orders` reçoit le webhook, valide le token partagé, puis valide le payload avec FluentValidation.

- **Code C#** (`OrderService.cs`) : La méthode `ReceiveOrderFromOdooAsync()` vérifie l'idempotence (la commande existe-t-elle déjà ?), crée l'entité `Order` avec ses `OrderLine`, et l'enregistre en base.

**Vérification :**

- Dans Odoo : le champ "Statut Intégration" passe à "Synchronisé"
- Dans les logs .NET : `Order received from Odoo: ODOO-42 (OdooOrderId=42)`
- Appeler `GET https://localhost:55648/api/orders` (avec JWT) → la commande apparaît

### Scénario 2 : Marquer une commande comme expédiée → notification Odoo

**Étapes :**

1. Récupérer un token JWT :

   ```
   POST /api/auth/login
   { "email": "test@futurekawa.com", "password": "TestPass123" }
   ```

2. Lister les commandes :

   ```
   GET /api/orders
   Authorization: Bearer <token>
   ```

3. Marquer la commande comme expédiée :
   ```
   PATCH /api/orders/{id}/status
   Authorization: Bearer <token>
   { "status": "Shipped" }
   ```

**Points à expliquer au jury :**

- **Code C#** (`OrderService.cs`, méthode `UpdateOrderStatusAsync`) : Quand le statut passe à `Shipped` et que la commande a un `OdooOrderId`, le service appelle `IOdooIntegrationService.NotifyOrderShippedAsync()`.

- **Code C#** (`OdooIntegrationService.cs`, méthode `NotifyOrderShippedAsync`) : Le service authentifie auprès d'Odoo via JSON-RPC (service "common", méthode "login"), puis appelle `execute_kw` pour déclencher `action_mark_shipped` sur le modèle `sale.order`.

- **Code Python** (`sale_order.py`, méthode `action_mark_shipped`) : Cette méthode met à jour le `integration_status` à "shipped" dans Odoo. Elle est exposée via l'API web service d'Odoo et accessible avec les credentials Odoo.

**Vérification :**

- Dans les logs .NET : `Odoo notified: order 42 marked as shipped`
- Dans Odoo : le champ "Statut Intégration" passe à "Expédié"

---

## Résumé des compétences démontrées

| Compétence du cahier des charges                             | Où ?                                                                      |
| ------------------------------------------------------------ | ------------------------------------------------------------------------- |
| Paramétrer un module spécifique d'un progiciel intégré       | Configuration des paramètres système Odoo (webhook URL, token)            |
| Développer une partie d'un module avec le langage spécifique | Module Python `future_kawa_erp` (surcharge `sale.order`, `stock.picking`) |
| Concevoir et détailler une logique de développement          | Diagrammes d'architecture et de séquence, documentation du module         |
| Développer un composant applicatif métier spécifique intégré | Services .NET (`OrderService`, `OdooIntegrationService`), contrôleurs API |
| Démonstration technique (interprétation de code)             | Points d'explication dans chaque scénario                                 |
