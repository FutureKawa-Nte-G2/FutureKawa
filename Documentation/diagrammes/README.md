# Diagrammes

Ces fichiers sont des **vues dérivées** du schéma, pas sa définition. En cas de
divergence, le code fait foi et le diagramme est corrigé
([ADR-001](../adr/001-gouvernance-schema-donnees.md)).

**Source de vérité du schéma :** `Api/app/models.py` + `Api/alembic/`.

`er_chart.md` et `er_chart_corrected.md` ont été supprimés : ils décrivaient deux
modèles concurrents, tous deux antérieurs au schéma en vigueur — identifiants `serial`
et types `ENUM` là où l'implémentation utilise des UUID et des colonnes `String(n)`.
Les garder revenait à entretenir trois descriptions divergentes du même schéma, ce que
l'ADR-001 corrige.

Le modèle à jour est le diagramme entité-relation de
[`database/docs/architecture.md`](../../database/docs/architecture.md) (§2), tenu avec
les autres diagrammes d'architecture.
