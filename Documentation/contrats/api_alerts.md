# Contrat d'API — `GET /api/alerts` (API locale → Head Office)

> Pendant du contrat `GET /api/measurements`, dans l'autre sens : ce document
> décrit ce que **l'API locale expose**, pour que le Head Office puisse
> l'intégrer sans aller-retour. Les remarques et demandes de changement sont
> les bienvenues avant implémentation côté siège.

---

## 1. Endpoint

| Élément | Valeur |
|---|---|
| **Méthode** | `GET` |
| **Chemin** | `/api/alerts` |
| **Authentification** | `X-API-Key`, secret partagé (voir §5) |
| **Content-Type** | `application/json` |

Une instance d'API locale = **un pays**. L'endpoint renvoie les alertes de tous
les entrepôts du pays en un seul appel : pas besoin d'itérer entrepôt par
entrepôt, chaque alerte porte la référence de son entrepôt.

---

## 2. Format de la réponse

### 2.1. Structure

Un **tableau JSON**, trié par `createdAt` décroissant (plus récente en premier) :

```json
[
  {
    "id": 2,
    "type": "expiration",
    "state": "active",
    "warehouseRef": "BR-ENT-01",
    "batchRef": "BR-2026-00042",
    "value": 400.00,
    "createdAt": "2026-07-30T08:00:00Z",
    "resolvedAt": null
  },
  {
    "id": 1,
    "type": "condition",
    "state": "active",
    "warehouseRef": "BR-ENT-01",
    "batchRef": null,
    "value": 27.50,
    "createdAt": "2026-07-28T09:00:00Z",
    "resolvedAt": null
  }
]
```

Un tableau vide (`[]`) est renvoyé quand rien ne correspond — jamais `204`, pour
que le siège n'ait qu'un seul cas à traiter.

### 2.2. Champs

| Champ | Type | Nullable | Description |
|---|---|---|---|
| `id` | `int` | non | Identifiant de l'alerte **dans la base du pays**. Unique par pays, pas globalement (voir §6). |
| `type` | `string` | non | `condition` (dérive de température/humidité) ou `expiration` (lot trop ancien). |
| `state` | `string` | non | `active` ou `resolved`. |
| `warehouseRef` | `string` | **non** | Référence ERP de l'entrepôt concerné. Toujours renseignée, quel que soit le `type`. |
| `batchRef` | `string` | oui | Référence ERP du lot. Renseignée uniquement si `type = expiration`. |
| `value` | `decimal` | oui | Valeur relevée (°C ou %) pour une `condition`, âge en jours pour une `expiration`. |
| `createdAt` | `string` | non | ISO 8601 UTC, instant de levée. |
| `resolvedAt` | `string` | oui | ISO 8601 UTC. Renseignée uniquement si `state = resolved`. |

Les noms sont en **camelCase**, comme dans le contrat `measurements`. Rappel du
piège que ton document signalait : `PropertyNameCaseInsensitive` tolère une
casse différente, pas un nom différent.

### 2.3. Pourquoi des références et pas des ids

`warehouseRef` et `batchRef` sont les références ERP (`external_ref`,
`batch_ref`), pas nos ids internes. Le siège n'a aucun moyen de mapper nos
`serial` ; la référence ERP est la seule clé commune aux deux systèmes. C'est le
même raisonnement que pour le `warehouseId` de `measurements`.

---

## 3. Paramètres de requête

| Paramètre | Type | Défaut | Effet |
|---|---|---|---|
| `since` | ISO 8601 | absent | Ne renvoie que les alertes dont `createdAt` est **strictement supérieur**. |
| `state` | `active` \| `resolved` | absent | Filtre sur l'état. |

`since` est **exclusif** : le siège peut renvoyer le `createdAt` de la dernière
alerte qu'il a stockée et ne la recevra pas une seconde fois.

Une valeur de `state` inconnue renvoie `422` plutôt qu'un résultat vide : une
faute de frappe doit se voir, pas ressembler à « aucune alerte ».

---

## 4. Codes HTTP

| Code | Cas |
|---|---|
| `200` | Tableau d'alertes, éventuellement vide |
| `401` | `X-API-Key` absente ou invalide |
| `422` | `since` malformée ou `state` inconnu |
| `500` | Erreur locale |

Le body d'erreur a la forme `{"detail": {"code": "...", "message": "..."}}` — la
raison se lit dans `detail.code`.

---

## 5. Authentification

Header `X-API-Key`, secret partagé, à échanger hors bande entre les deux
équipes. Côté API locale il est lu dans la variable d'environnement
`LOCAL_API_KEY`.

Si la variable n'est pas configurée, l'API **refuse de servir** (`500`) plutôt
que de servir en accès libre : une variable oubliée doit ressembler à une erreur
de déploiement, pas passer inaperçue.

---

## 6. Idempotence côté siège

Contrairement à `measurements`, une alerte est un **événement**, pas un agrégat
recalculable : elle est créée une fois et ne change plus, sauf pour passer à
`resolved`.

Deux conséquences pour le stockage côté siège :

1. **La clé d'unicité ne peut pas être `id` seul.** Il est unique dans la base
   d'un pays, pas entre pays : le Brésil et la Colombie auront tous les deux une
   alerte `id = 1`. La clé doit être `(pays, id)` — le pays étant déjà connu du
   siège, puisque c'est lui qui choisit quelle API locale il interroge.
2. **Une alerte déjà stockée peut revenir modifiée** si elle est passée à
   `resolved` entre deux pulls. Le siège doit donc faire un *upsert* sur
   `(pays, id)`, pas un insert.

Avec `since`, le second cas ne se produit que si le siège re-pull une fenêtre
déjà vue. Si tu veux récupérer les résolutions au fil de l'eau, appelle
`?state=resolved` sans `since` de temps en temps, ou garde une fenêtre de
recouvrement.

---

## 7. Exemples

```bash
# Tout ce que le pays a en base
curl -H "X-API-Key: <secret>" https://warehouse-api.brazil.com/api/alerts

# Pull incrémental : seulement ce qui est arrivé depuis le dernier passage
curl -H "X-API-Key: <secret>" \
  "https://warehouse-api.brazil.com/api/alerts?since=2026-07-30T08:00:00Z"

# Seulement ce qui est encore ouvert
curl -H "X-API-Key: <secret>" \
  "https://warehouse-api.brazil.com/api/alerts?state=active"
```

---

## 8. Points à confirmer

1. **Ce format te convient-il ?** Si le siège préfère un objet enveloppe
   (`{"alerts": [...]}`) ou d'autres noms de champs, dis-le maintenant : c'est
   trivial à changer avant, coûteux après.
2. **`value` te sert-il ?** Il mélange deux unités selon le `type` (°C/% ou
   jours). On peut le scinder en deux champs si c'est gênant à typer côté .NET.
3. **Fréquence de pull ?** Les alertes sont des événements temps réel ; un pull
   quotidien comme pour `measurements` ferait perdre l'intérêt de l'alerte.
4. **Faut-il une pagination ?** Aujourd'hui la réponse n'est pas paginée. Avec
   `since` le volume reste faible, mais un premier pull sur une base ancienne
   peut être gros.
5. **Ce contrat remplace les notifications.** Le modèle de données corrigé n'a
   pas de table `NOTIFICATION` : l'alerte porte l'information affichable. Les
   endpoints `/api/notifications` évoqués précédemment n'existeront pas.
