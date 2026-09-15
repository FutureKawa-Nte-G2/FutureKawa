# Contrats de l'API pays (warehouse)

Ce que l'API pays échange avec le siège et les autres systèmes : les contrats
qu'on nous impose, ceux qu'on a choisis, et les décisions de vocabulaire qui
vont avec. Le fonctionnement et le démarrage de l'API sont dans
[Api/README.md](../Api/README.md).

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

> ⚠️ **À revérifier.** Côté pays, `GET /api/measurements` accepte désormais un
> paramètre facultatif `warehouse_ref`. Le siège, lui, ne l'envoie toujours pas.

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

> ⚠️ **À revérifier.** Ce paragraphe est antérieur à la conteneurisation :
> `UseMockData` n'est plus à `true` que dans `appsettings.json`. Il vaut `false`
> dans `appsettings.Development.json`, et le `docker-compose.yml` le force à
> `false` en pointant `LocalApiUrl` sur `country-api`.

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

## Contrat imposé : `POST /api/alerts` (envoi au siège)

Sens inverse des mesures : c'est l'API pays qui appelle le siège, dès qu'une
alerte `condition` est commitée (#80). Source de vérité côté siège :
`backend/FutureKawaSiege.Commons/Models/API/Requests/CreateAlertRequest.cs` et
`AlertService.ReceiveAlertAsync`, mergés par #82.

### Requête

En-tête `X-API-Key` : la clé du pays, `LOCAL_API_KEY` chez nous,
`LocalApi:Countries:{code}:ApiKey` au siège. C'est la même valeur dans les deux
sens.

```json
{
  "warehouseReference": "WH-BR-SANTOS",
  "type": "temperature",
  "measuredAt": "2026-08-10T12:00:00Z",
  "sourceAlertId": "7f1c…"
}
```

| Champ | Source côté pays | Contrainte |
|---|---|---|
| `warehouseReference` | `warehouses.warehouse_ref` | doit exister au siège **et** appartenir au pays de la clé |
| `type` | grandeur ayant franchi son seuil en premier | `temperature` ou `humidity`, jamais `condition` : le frontend n'affiche que ces deux-là |
| `measuredAt` | `alerts.measured_at`, l'horodatage du relevé | UTC explicite ; **jamais** `created_at` |
| `sourceAlertId` | `alerts.alert_id` | le siège le garde pour renvoyer la résolution sur `PATCH /api/alerts/{id}/resolve` |

### Choix de `type`

Une alerte `condition` porte sur la salle, le siège veut une grandeur :

1. une seule grandeur hors bande → celle-là ;
2. les deux hors bande sur le même relevé → le relevé précédent du capteur
   (15 min au plus) départage : la grandeur déjà hors bande, sinon celle dont
   l'instant de franchissement, interpolé linéairement, est le plus précoce ;
3. rien pour départager → le plus gros écart relatif,
   `|valeur − nominal| / tolérance`.

### Réponses

| Code | Sens | Ce que fait l'API pays |
|---|---|---|
| `200` | reçue, ou déjà active au siège pour cet entrepôt et ce type (idempotent) | rien de plus |
| `400` | type invalide | abandon, log d'erreur |
| `401` | clé absente, inconnue, ou non autorisée pour cet entrepôt | abandon, log d'erreur |
| `404` | entrepôt inconnu du siège | abandon, log d'erreur |
| `429` | limite de débit (60/min par clé par défaut) | nouvelle tentative, `Retry-After` respecté |
| `5xx`, erreur réseau | siège indisponible | nouvelle tentative, délai croissant, 5 essais |

Les tentatives sont en mémoire : un envoi en cours au moment d'un arrêt du
consumer est perdu, mais tout abandon se termine par un log d'erreur qui nomme
l'alerte.

### Limites connues

- **Une seule alerte `condition` active par entrepôt côté pays.** Si l'humidité
  déborde alors que la température a déjà ouvert l'alerte, rien n'est créé,
  donc rien n'est poussé.
- **Le siège dédoublonne par `(entrepôt, type)` actif** et répond `200` sans
  enregistrer le nouveau `sourceAlertId`.
- **La résolution renvoyée par le siège n'est tentée qu'une fois.** Si elle
  échoue, l'alerte reste active côté pays, et la salle ne peut plus en lever
  d'autre.

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

> Cette règle est cohérente avec le code en place (`OrderService.ShipOrderAsync`
> écrit `BatchStatus.Shipped`, la création écrit `BatchStatus.Stored`) et la
> synchronisation des lots entre siège et pays (`ILocalBatchPushClient`) s'y
> conforme. À noter aussi, le frontend utilise un vocabulaire différent **et sur
> un autre axe** — `compliant` / `alert` / `expired`, de la conformité et non du
> cycle de vie, hérité d'un schéma jamais mergé. Divergence réelle, à traiter
> séparément.

---

## Contrat maison : `POST /api/batches`

Enregistre un lot décrit par un **fichier de réception ERP**. Appelé par le
siège (.NET) à la création d'un lot — voir plus bas.

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

### Appelant : le siège, à la création d'un lot

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

`OrderService.ResolveBatchesAsync` appelle cette route via `ILocalBatchPushClient`
(`LocalBatchPushClient.cs`) pour chaque référence de lot **nouvellement créée**
— pas pour un lot FIFO déjà existant réutilisé, déjà transmis lors de sa
création. `409 batch_already_exists` (rejeu) est traité comme un succès ; un
`422` (référence inconnue) est loggé en erreur et n'est pas retenté
immédiatement, la donnée référentielle devant être corrigée en amont.

Les lots `WH-XX-DEFAULT` / `FM-XX-DEFAULT` fabriqués par
`EnsureDefaultBatchDependenciesAsync` n'existent pas forcément côté pays :
`422` attendu tant que ce cas n'est pas traité côté référentiels.

Le watcher de fichier de réception, lui, n'existe nulle part dans le dépôt.

---

## Contrat maison : `PATCH /api/batches/{batch_ref}/ship`

Marque un lot comme expédié. Appelée par le siège depuis
`OrderService.ShipOrderAsync`, pour chaque lot dont le statut passe à
`Shipped`, en parallèle de la notification Odoo existante
(`NotifyOrderShippedAsync`).

### Requête

En-tête `X-API-Key` obligatoire. Le lot est désigné par **`batch_ref`**, jamais
par `batch_id` : passer l'UUID renvoyé par `POST /api/batches` donne un `404
batch_not_found`.

```json
{ "shippedAt": "2026-09-15" }
```

### Réponse `200 OK`

Idempotente : un rejeu garde la première `shippedAt`.

### Rejets

| Code | Cas |
|---|---|
| `401` | clé absente ou invalide |
| `404` | `batch_not_found` — lot jamais transmis côté pays (le `POST` initial a
échoué), ou `batch_ref` mal formé |
| `422` | corps invalide |

Si le `POST` initial échoue silencieusement, le `PATCH` ultérieur renverra
`404 batch_not_found` : les deux échecs sont loggués séparément côté siège pour
permettre le diagnostic.
