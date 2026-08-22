# CLAUDE.md — API locale (pays / warehouse)

## Contrat imposé : `GET /api/measurements`

Le backend Head Office (.NET) consomme cet endpoint. Le contrat est imposé par
le siège : toute déviation casse l'intégration, et elle la casse **en silence**.

> **Ce document décrit le code, pas le document de spécification reçu.** Les
> deux divergent sur plusieurs points, dont un bloquant. Source de vérité :
> `backend/FutureKawaSiege.Business/Services/LocalMeasurementApiService.cs` et
> `backend/FutureKawaSiege.Commons/Models/API/Responses/LocalMeasurementDto.cs`,
> sur la branche `features/48_retrieve-local-measurement`. Les comportements
> ci-dessous ont été vérifiés en exécutant le désérialiseur .NET avec le DTO et
> les options réels, pas déduits.

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

### ⚠️ `measDate` : le document reçu est faux

Le document annonce `"measDate": "2026-08-11T00:00:00Z"` et « ISO 8601, UTC ».
Le DTO déclare `DateOnly`, dont le convertisseur n'accepte que `yyyy-MM-dd`.
Vérifié :

```
"measDate":"2026-08-11T00:00:00Z"   ->  JsonException
                                        "could not be converted to System.DateOnly"
"measDate":"2026-08-11"             ->  OK
```

Et l'appel est enveloppé dans `try { ... } catch (Exception) { return null; }` :
l'exception est avalée, l'entrepôt est sauté, la seule trace est une ligne de
log. Suivre le document à la lettre produit exactement l'échec silencieux
contre lequel il met en garde.

**Renvoyer `"2026-08-11"`.**

### Casse des noms

`System.Text.Json` avec `PropertyNameCaseInsensitive = true` : la casse est
tolérée, **le nom lui-même ne l'est pas**. Un champ mal nommé n'est pas une
erreur — il arrive à `0` côté siège.

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

---

## Points encore ouverts

1. **Auth** — rien n'est envoyé aujourd'hui. À convenir avant branchement.
2. **Journée courante ou veille** — le document laisse le choix ; la veille est
   plus sûre pour leur index unique.
3. **Fuseau du découpage journalier** — UTC ou heure locale du pays (Brésil
   UTC-3) : change les min/max sur un cycle jour/nuit.
4. **`warehouseRef` en paramètre** — nécessaire dès qu'un deuxième entrepôt
   existe, puisqu'une seule URL sert tout le monde.
