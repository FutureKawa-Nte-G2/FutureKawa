# FutureKawa ERP Integration — Module Odoo

## Description

Module personnalisé Odoo 18 pour la gestion des commandes et livraisons de café
dans le cadre du projet FutureKawaSiege (EPSI MSPR).

## Fonctionnalités

### Extension du modèle `sale.order`

Champs métier ajoutés :

| Champ                       | Type              | Description                                    |
| --------------------------- | ----------------- | ---------------------------------------------- |
| `batch_count`               | Integer           | Nombre de lots de café à générer               |
| `batch_ref`                 | Char              | Références des lots générées (lecture seule)   |
| `quality_grade`             | Selection (A/B/C) | Grade de qualité du café                       |
| `country`                   | Char              | Pays d'origine                                 |
| `integration_status`        | Selection         | Statut de synchronisation avec le backend .NET |
| `integration_error_message` | Text              | Message d'erreur d'intégration                 |

### Extension du modèle `stock.picking`

| Champ                  | Type | Description                       |
| ---------------------- | ---- | --------------------------------- |
| `delivery_reference`   | Char | Référence de livraison FutureKawa |
| `carrier_tracking_ref` | Char | Référence de suivi transporteur   |

### Intégration avec le backend .NET

**Flux Odoo → .NET (webhook) :**

Lors de la confirmation d'une commande (`action_confirm()`), le module envoie
un POST HTTP vers le backend .NET avec le payload suivant :

```json
{
  "orderId": 42,
  "client": "Distributeur Café Europe SARL",
  "orderDate": "2026-08-03T10:30:00",
  "country": "CO",
  "qualityGrade": "a",
  "batchReferences": ["LOT-008", "LOT-009"],
  "lines": [{ "product": "Café Arabica - Grade A", "quantity": 100.0 }]
}
```

L'URL et le token d'authentification sont configurés via les paramètres système
Odoo :

- `future_kawa.webhook_url` : URL du endpoint .NET
- `future_kawa.webhook_token` : token partagé (header `X-Webhook-Token`)

**Flux .NET → Odoo (JSON-RPC) :**

Le backend .NET appelle l'API JSON-RPC d'Odoo pour marquer une commande comme
expédiée via la méthode `action_mark_shipped()`.

## Installation

1. Démarrer les conteneurs Docker :

   ```bash
   docker compose up -d
   ```

2. Créer la base de données Odoo via l'interface web : `http://localhost:8069`

3. Installer les modules de base : Sales, Inventory

4. Installer le module FutureKawa :
   ```bash
   docker compose exec odoo odoo -u future_kawa_erp -d <dbname> --stop-after-init
   ```

## Paramétrage

Après installation, configurer les paramètres système :

1. Aller dans **Paramètres → Technique → Paramètres → Paramètres Système**
2. Vérifier/modifier :
   - `future_kawa.webhook_url` : URL du backend .NET
   - `future_kawa.webhook_token` : token d'authentification partagé

## Logique de développement

Ce module illustre le pattern d'extension d'un progiciel intégré (ERP) :

1. **Paramétrage** : utilisation des paramètres système d'Odoo pour externaliser
   la configuration (URL webhook, token) sans modifier le code

2. **Développement spécifique** : surcharge de la méthode `action_confirm()`
   du modèle `sale.order` pour ajouter un comportement métier (notification
   du backend .NET) tout en préservant la logique standard d'Odoo

3. **Intégration** : communication bidirectionnelle entre l'ERP et le backend
   applicatif via des protocoles standards (HTTP webhook + JSON-RPC)
