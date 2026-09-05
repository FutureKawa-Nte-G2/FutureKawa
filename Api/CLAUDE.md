# CLAUDE.md — API locale (pays / warehouse)

## Contrat imposé : `GET /api/measurements`

Le backend Head Office (.NET) consomme cet endpoint. Le contrat est imposé par
le siège : toute déviation casse l'intégration, et elle la casse **en silence**.

> **Ce document décrit le code, pas le document de spécification reçu.** Les
> deux divergent sur plusieurs points ; **le code fait foi**. Source de vérité :
> `backend/FutureKawaSiege.Business/Services/LocalMeasurementApiService.cs` et
> `backend/FutureKawaSiege.Commons/Models/API/Responses/LocalMeasurementDto.cs`,
> mergés sur `develop` par #48. Les comportements ci-dessous ont été vérifiés en
> exécutant le désérialiseur .NET avec le DTO et les options réels, pas déduits.

### Réponse `200 OK`

```json
{
  "avgTemp": 25.30,
  "maxTemp": 27.80,
  "minTemp": 22.50,
  "avgHumidity": 60.50,
  "maxHumidity": 64.00,
  "minHumidity": 56.20,
  "measDate": "2026-08-11"
}
```

| Champ | Type .NET | Contrainte |
|---|---|---|
| `avgTemp` / `maxTemp` / `minTemp` | `decimal` | °C, 2 décimales |
| `avgHumidity` / `maxHumidity` / `minHumidity` | `decimal` | %, 2 décimales |
| `measDate` | **`DateOnly`** | **`yyyy-MM-dd` uniquement** |

### `measDate` : `DateOnly`, décidé après le document de spécification

Le passage de `datetime` à `DateOnly` est une **décision de conception**, prise
avec Laurent : le siège ne récupère que des agrégats **quotidiens**, l'heure n'y
porte aucune information. Le document de spécification, rédigé avant cette
décision, annonce encore `"measDate": "2026-08-11T00:00:00Z"` et « ISO 8601,
UTC » — il est périmé sur ce point, il n'y a pas de désaccord à arbitrer. Le DTO
fait foi, et son convertisseur n'accepte que `yyyy-MM-dd`. Vérifié :

```
"measDate":"2026-08-11T00:00:00Z"   ->  JsonException
                                        "could not be converted to System.DateOnly"
"measDate":"2026-08-11"             ->  OK
```

Et l'appel est enveloppé dans `try { ... } catch (Exception) { return null; }` :
l'exception est avalée, l'entrepôt est sauté, la seule trace est une ligne de
log. Envoyer le format du document plutôt que celui du DTO échoue donc sans
aucun signal de notre côté — d'où ce paragraphe.

**Renvoyer `"2026-08-11"`.**

### Casse des noms

`System.Text.Json` avec `PropertyNameCaseInsensitive = true` : la casse est
tolérée, **le nom lui-même ne l'est pas**. Un champ mal nommé n'est pas une
erreur — il arrive à `0` côté siège.

### Nombres : jamais entre guillemets

Les six agrégats sont des `decimal`. Leurs `JsonSerializerOptions` ne posent
**pas** `NumberHandling.AllowReadingFromString` : un nombre entre guillemets est
refusé. Vérifié en exécutant leur DTO avec leurs options :

```
{"avgTemp":"25.30", …}   ->  JsonException
                             "could not be converted to System.Decimal"
{"avgTemp":25.30,   …}   ->  OK
```

Le piège est côté Python : **Pydantic sérialise un `Decimal` en chaîne par
défaut**. Sans traitement, on envoie `"25.30"`, leur client lève, avale
l'exception et saute l'entrepôt. `app/schemas/measurement.py` force donc la
sortie en nombre JSON (`WireDecimal`), et un test l'affirme sur le JSON brut.

Le zéro final se perd au passage (`28.10` part en `28.1`) : sans importance,
c'est le même nombre et leur `decimal` le lit sans broncher.

### Absence de mesures

| Réponse | Ce qui se passe réellement |
|---|---|
| `200` + corps `null` | désérialise en `null`, entrepôt sauté proprement — **à privilégier** |
| `204` corps vide | `JsonException` attrapée, **log d'erreur**, puis saut |
| `200` + objet à `measDate: null` | `JsonException` : `DateOnly` n'est pas nullable |

Le document propose les deux derniers. Le premier est le seul qui ne pollue pas
leurs logs à chaque cycle sans données.

### Erreurs

`EnsureSuccessStatusCode()` lève sur tout code ≥ 400, l'exception est attrapée,
l'entrepôt est sauté. Le corps d'erreur n'est jamais lu : son format est libre.
Le siège ne plante jamais — une erreur coûte un cycle de synchronisation.

### URL

`MeasurementSync:LocalApiUrl` est utilisée **telle quelle** (`TrimEnd('/')`,
aucun chemin ajouté). Elle doit donc contenir le chemin complet, par exemple
`http://localhost:8000/api/measurements`. La §5 du document reçu, qui écrit
`{LocalApiUrl}/api/measurements`, est fausse.

### Authentification : aucune aujourd'hui

Le client **n'envoie aucun header**. Pas de `X-API-Key`, rien. Si on protège
cet endpoint comme `/api/alerts`, le siège prend un `401`, `EnsureSuccessStatusCode`
lève, et l'entrepôt est sauté. **L'auth doit être convenue avant qu'ils
branchent**, et ajoutée des deux côtés en même temps.

### `warehouseId` : toujours pas envoyé

`FetchMeasurementsAsync(Guid warehouseId, ...)` reçoit bien l'identifiant mais
ne le met pas dans l'URL. Le siège itère sur ses entrepôts et attribue la
réponse à celui qu'il traite. Une seule URL sert donc tous les entrepôts.

Nos clés sont des `uuid` et le siège des `Guid`, mais il ne connaît pas les
nôtres : si le paramètre est ajouté un jour, c'est `warehouses.warehouse_ref`
qu'il faut passer, la clé de rapprochement prévue au MLD.

### Idempotence

Index unique sur `(WarehouseId, MeasDate)` côté siège. `measDate` doit
représenter **une journée**, pas un instant — ce que `DateOnly` impose de toute
façon. Renvoyer la veille complète plutôt que le jour en cours rend l'agrégat
définitif et l'idempotence gratuite.

Intervalle configuré : `IntervalMinutes: 1440`, soit une fois par jour.

### Agrégation attendue

Moyenne / max / min sur les relevés de tous les capteurs **actifs** de
l'entrepôt sur la période. Source : `measurements` joint à `sensors`
(`sensors.warehouse_id`, `sensors.is_active`).

### État côté siège

`MeasurementSync:UseMockData` est à **`true`** et `LocalApiUrl` pointe sur leur
propre `MockMeasurementsController` (`/api/mock/measurements`). Le siège ne nous
appelle donc pas encore : il fabrique ses données en mémoire. Pour un essai
réel, il faut passer `UseMockData` à `false` et pointer l'URL sur nous.

Depuis #48, le siège ne fait plus que consommer : il **persiste** ce qu'il pull
(`MeasurementConfiguration`, unique sur `(WarehouseId, MeasDate)`) et le
ré-expose par son propre `MeasurementsController`. Notre réponse devient donc
une donnée stockée chez eux, pas un affichage éphémère : un agrégat renvoyé une
fois n'est plus corrigeable depuis notre base.

---

## Points encore ouverts

1. **Auth** — rien n'est envoyé aujourd'hui. À convenir avant branchement.
2. **Journée courante ou veille** — le document laisse le choix ; la veille est
   plus sûre pour leur index unique.
3. **Fuseau du découpage journalier** — UTC ou heure locale du pays (Brésil
   UTC-3) : change les min/max sur un cycle jour/nuit.
4. **`warehouseRef` en paramètre** — nécessaire dès qu'un deuxième entrepôt
   existe, puisqu'une seule URL sert tout le monde.

---

## Vocabulaire des statuts : `batch_status`

Les deux bases nomment le même cycle de vie différemment, et rien ne convertit
aujourd'hui.

| Où | Valeurs | Porté par |
|---|---|---|
| Base pays (ici) | `stored` / `shipped` / `delivered` / `expired` | `BATCH_STATUSES`, `app/models.py` |
| Siège (.NET) | `Stored` / `Shipped` / `Delivered` / `Expired` | `enum BatchStatus` + `HasConversion<string>()` — stocké tel quel en base |

Le MLD (`Documentation/diagrammes/mld_warehouse.puml`) type la colonne en
`varchar(16)` sans fixer le vocabulaire : il ne tranche pas.

**Règle retenue** — minuscules en base pays, PascalCase au siège, et la
conversion est une simple bascule de casse faite **à la frontière**, au moment
de servir le siège, jamais en base. Un lot créé depuis un fichier de réception
ERP entre en `stored`.

> ⚠️ Cette règle est cohérente avec le code en place (`OrderService.ShipOrderAsync`
> écrit `BatchStatus.Shipped`, la création écrit `BatchStatus.Stored`) mais n'a
> jamais été actée collectivement : à confirmer avant que la synchronisation des
> lots soit codée. À noter aussi, le frontend utilise un vocabulaire différent
> **et sur un autre axe** — `compliant` / `alert` / `expired`, de la conformité
> et non du cycle de vie, hérité d'un schéma jamais mergé. Divergence réelle, à
> traiter séparément.

---

## Contrat maison : `POST /api/batches`

Enregistre un lot décrit par un **fichier de réception ERP**. Contrairement à
`/api/measurements`, ce contrat n'est imposé par personne : aucun appelant
n'existe encore (voir plus bas). Il est donc choisi, et modifiable tant que
personne ne l'a branché.

### Requête

En-tête `X-API-Key` obligatoire.

```json
{
  "batchRef":     "BR-2026-00042",
  "farmRef":      "BR-EXP-01",
  "warehouseRef": "BR-ENT-01",
  "storedAt":     "2026-07-30",
  "qualityGrade": "a"
}
```

`qualityGrade` est facultatif. Toute clé supplémentaire est **ignorée** :
`batchId`, `batchStatus` et `shippedAt` appartiennent au serveur, un fichier qui
les porte est honoré pour le reste et les perd silencieusement.

### Réponse `201 Created`

Le lot complet — `batchId`, `batchRef`, `farmId`, `warehouseId`, `storedAt`,
`shippedAt`, `qualityGrade`, `batchStatus` — pour que le watcher puisse
journaliser ce qu'il a créé sans seconde requête.

### Rejets

| Code | Cas | `detail.code` |
|---|---|---|
| `401` | clé absente ou invalide | `invalid_api_key` |
| `409` | `batchRef` déjà en base | `batch_already_exists` |
| `422` | exploitation inconnue | `farm_ref_unknown` |
| `422` | entrepôt inconnu | `warehouse_ref_unknown` |
| `422` | payload malformé | `schema_invalid` |

La séparation `409` / `422` est faite pour le watcher : `409` signifie « fichier
déjà traité, à archiver », `422` signifie « fichier fautif, à router vers
`error/` ». Les deux portent leur raison dans `detail.code`, jamais en prose.

`422` plutôt que `404` pour une référence inconnue : l'URL existe, c'est le
contenu du fichier qui décrit quelque chose qu'on ne connaît pas.

### camelCase

Comme `/api/measurements`, et comme le type `Batch` du frontend
(`frontend/lib/api/types.ts`, qui écrit déjà `batchRef` et `qualityGrade`). Tous
les consommateurs du projet parlent camelCase. `populate_by_name` laisse le
snake_case accepté en entrée : personne n'est puni pour avoir lu `models.py`
d'abord.

### Authentification : `X-API-Key`, ici et pas sur les mesures

Cette route **écrit**. Les raisons qui laissent `/api/measurements` ouvert — leur
client n'envoie aucun en-tête, un `401` ferait sauter l'entrepôt en silence — ne
valent pas pour un appelant qu'on écrit nous-mêmes.

`LOCAL_API_KEY` non configurée fait **refuser de servir** plutôt que servir
ouvert : une variable absente est une erreur de déploiement, elle doit en avoir
l'air.

### `quality_grade` devient nullable

Migration `a0a66fe318a0`. Odoo déduit le grade du code produit
(`_compute_quality_grade`) et envoie `null` pour tout produit qui n'est pas
`COFFEE-A/B/C`. Un lot existe physiquement que son grade soit connu ou non ;
refuser le lot coûterait la traçabilité de l'ensemble pour un attribut
secondaire.

Odoo exporte `"a"` en minuscule, notre vocabulaire est `("A","B","C")` : la
bascule de casse se fait **à la frontière**, comme pour `batch_status`. Une
chaîne vide vaut « pas de grade », pas un grade à part entière.

Aucune contrainte `CHECK` ne protège la colonne en base — la PR #54 les a
renvoyées à une PR de suite — donc le validateur Pydantic est aujourd'hui le
seul rempart contre un grade inventé.

### ⚠️ Aucun appelant ne peut encore utiliser cette route

Le payload réel d'Odoo (`odoo-addons/future_kawa_erp/models/sale_order.py`) est :

```
orderId, client, orderDate, country, qualityGrade, batchReferences, lines
```

Ni exploitation, ni entrepôt, ni date de stockage. Le siège s'en sort en
**fabriquant** des valeurs par défaut (`EnsureDefaultBatchDependenciesAsync` dans
`OrderService.cs`, puis `StoredAt = DateTime.UtcNow.Date`).

Nos colonnes `warehouse_id` et `farm_id` sont `NOT NULL` : on ne *peut pas*
accepter un lot sans elles. Le contrat est donc imposé par notre propre schéma,
et c'est à l'appelant de résoudre ce qu'Odoo ne dit pas — exactement comme le
siège le fait de son côté.

Le watcher de fichier de réception, lui, n'existe nulle part dans le dépôt.
