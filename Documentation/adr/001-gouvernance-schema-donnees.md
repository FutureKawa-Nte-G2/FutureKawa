# ADR-001 — Gouvernance du schéma de données

**Date** : 2026-08-23 · **Statut** : proposé · **Décideurs** : équipe au complet

## Contexte

Le schéma de données validé collectivement en réunion avant l'été a été remplacé de fait,
sans re-validation : d'abord par `mld_warehouse.puml` (arrivé dans la PR #50, dont l'objet
était l'intégration Odoo), puis par son implémentation `Api/app/models.py` + Alembic (PR #54).

Conséquences constatées :
- TimescaleDB (cible actée du cadrage) a disparu sans décision — et le schéma actuel de
  `measurements` rend l'hypertable structurellement impossible (PK sans la colonne temps) ;
- trois vocabulaires `batch_status` coexistent (doc branche db / tuples Python / enum C# siège) ;
- le backlog (#30, #32, #36, #40) contredit le schéma implémenté (`SENSOR_ASSIGNMENT`,
  `is_compliant`, `non_compliant`/`scrapped`, alerte condition par lot vs par entrepôt) ;
- des contraintes d'intégrité validées ont été perdues (dédup alerte expiration, CHECKs
  type↔lot, résolution, dates).

## Décision

1. **Source de vérité unique du schéma** : `Api/app/models.py` (parties déclaratives) +
   `Api/alembic/`. Les diagrammes (`.puml`, ER charts) sont des vues dérivées : en cas de
   divergence, le code fait foi et le diagramme est corrigé.
2. **Ownership** : tout changement de schéma — tables, colonnes, contraintes, index,
   vocabulaires épinglés, migrations — requiert l'approbation du responsable data/DB
   (mécanisé par `.github/CODEOWNERS`). Le code applicatif qui consomme le schéma
   (services, routers, requêtes, mécanique ORM) reste au propriétaire de chaque application.
3. **Contrats de données inter-services** (formats JSON pays↔siège, vocabulaires partagés,
   sémantique des timestamps) relèvent du périmètre data et suivent la même règle.
4. **Dérogation** : tout écart au schéma en vigueur passe par une décision d'équipe tracée
   (ADR ou décision de réunion consignée dans le repo), jamais par une review ordinaire.

## Conséquences

- `er_chart.md` et `er_chart_corrected.md` (modèles obsolètes) sont supprimés ou regénérés
  depuis le schéma en vigueur.
- Les issues #30, #32, #36, #40 sont réécrites pour refléter le schéma en vigueur avant
  toute implémentation des consumers.
- La PR #54 est amendée avant merge (hypertable, server_defaults, contraintes) puis ratifiée
  comme schéma de référence.
