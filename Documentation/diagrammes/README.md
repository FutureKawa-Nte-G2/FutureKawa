# Diagrammes

Ces fichiers sont des **vues dérivées** du schéma, pas sa définition. En cas de
divergence, le code fait foi et le diagramme est corrigé
([ADR-001](../adr/001-gouvernance-schema-donnees.md)).

**Source de vérité du schéma :** `Api/app/models.py` + `Api/alembic/`.

## `er_chart.md` — à supprimer

Deux versions concurrentes de ce fichier ont existé, ajoutées indépendamment sur deux
branches. Celle portée par la branche `db` est supprimée ici.

**Celle de `develop` subsiste et doit l'être aussi** : elle décrit `PAYS`, `SITE`,
`EXPLOITATION` avec des identifiants `int` — des entités qui n'existent plus. Elle ne
peut pas être supprimée depuis une autre branche, puisque la base de fusion ne la
contient pas ; la suppression doit être faite sur `develop`.

`er_chart_corrected.md` est supprimé pour la même raison : types `ENUM` et `serial` là
où l'implémentation utilise des colonnes `String(n)` et des UUID.

Le modèle à jour est le diagramme entité-relation de
[`database/docs/architecture.md`](../../database/docs/architecture.md) (§2), tenu avec
les autres diagrammes d'architecture.
