# FutureKawaSiege.API — Authentification JWT

## Variables d'environnement

| Variable                     | Description                                                       | Obligatoire                |
| ---------------------------- | ----------------------------------------------------------------- | -------------------------- |
| `ConnectionStrings__Default` | Chaîne de connexion PostgreSQL                                    | Oui                        |
| `ASPNETCORE_ENVIRONMENT`     | `Development` / `Production`                                      | Non (défaut: `Production`) |
| `JWT_SECRET`                 | Clé secrète HMAC-SHA256 pour la signature JWT (min 32 caractères) | Oui (prod)                 |

> **Note** : En développement, la clé est lue depuis `appsettings.json` → `Jwt:Secret`.

## Rate Limiting

| Endpoint                 | Limite      | Fenêtre     |
| ------------------------ | ----------- | ----------- |
| `POST /api/auth/login`   | 5 requêtes  | 60 secondes |
| `POST /api/auth/refresh` | 10 requêtes | 60 secondes |

Configurable via `appsettings.json` → section `RateLimiting`.

## Endpoints

| Méthode | Route               | Auth       | Description                                     |
| ------- | ------------------- | ---------- | ----------------------------------------------- |
| `POST`  | `/api/auth/login`   | Non        | Login email/mot de passe → JWT + cookie refresh |
| `POST`  | `/api/auth/refresh` | Cookie     | Échange le refresh token → nouveau JWT + cookie |
| `POST`  | `/api/auth/logout`  | Cookie     | Révoque le refresh token, supprime le cookie    |
| `GET`   | `/api/auth/me`      | JWT Bearer | Infos de l'utilisateur connecté                 |

### Format des réponses

Toutes les réponses utilisent le wrapper `ApiResponse<T>` :

```json
{
  "success": true,
  "data": { ... },
  "message": null,
  "errors": null
}
```

## Développement local

```bash
# Restaurer + builder
dotnet restore && dotnet build

# Appliquer les migrations
dotnet ef database update --project FutureKawaSiege.Data --startup-project FutureKawaSiege.API

# Lancer l'API
dotnet run --project FutureKawaSiege.API

# Tests
dotnet test
```

## Documenter et tester l'API avec Scalar

En environnement de **développement**, l'API expose une documentation interactive **Scalar** qui liste tous les endpoints et permet de les tester directement depuis le navigateur, sans copier/coller de token :

| Élément          | Valeur                                             |
| ---------------- | -------------------------------------------------- |
| URL              | `https://localhost:55648/scalar/v1`                |
| Authentification | JWT `Bearer` pré-rempli automatiquement (dev only) |
| Utilisateur      | `test@futurekawa.com` (rôle `Admin`)               |
| Validité du JWT  | 30 jours, régénéré à chaque démarrage de l'API     |

- Les endpoints protégés affichent un cadenas ; clique sur un endpoint puis **Test Request** pour l'exécuter.
- Aucun `login` manuel ni copier/coller de token n'est nécessaire : le header `Authorization: Bearer …` est déjà appliqué à toutes les requêtes.

> **Note** : ce pré-remplissage est actif uniquement en `Development` (Scalar n'est pas mappé en `Production`). Le token pré-rempli correspond au user seedé via `--seed`.

## Tester les endpoints avec les fichiers `.http`

En complément de Scalar, deux fichiers `.http` sont fournis dans `FutureKawaSiege.API/` pour tester manuellement l'API sans outil externe (Postman, curl, etc.) :

| Fichier                       | Contenu                                                            |
| ----------------------------- | ------------------------------------------------------------------ |
| `FutureKawaSiegeBackend.http` | Authentification (login/me/logout) et synchronisation des mesures  |
| `FutureKawaSiegeOdoo.http`    | Webhook de réception des commandes Odoo et consultation des orders |

### Prérequis

L'API doit être démarrée (`dotnet run --project FutureKawaSiege.API`) avant d'envoyer des requêtes — le fichier `.http` ne fait qu'appeler une API déjà en cours d'exécution, il ne la lance pas.

### ⚠️ Utiliser l'URL HTTPS, pas HTTP

Toujours utiliser `@HostAddress = https://localhost:55648` (pas le port HTTP `55649`). L'API redirige automatiquement le HTTP vers HTTPS (`UseHttpsRedirection`), et la plupart des clients HTTP (Visual Studio, curl, `HttpClient`) ne retransmettent pas le header `Authorization` lors d'une redirection vers un port différent, par sécurité — ce qui se traduit par un `401 Unauthorized` silencieux même avec un token valide. Si tu obtiens un 401 alors que ton token semble correct, vérifie en premier lieu que tu utilises bien l'URL HTTPS.

### Exécution dans Visual Studio

Depuis Visual Studio 2022 (17.6+), les fichiers `.http` s'ouvrent directement dans un éditeur dédié — pas besoin d'extension. Une flèche "Send Request" apparaît au-dessus de chaque bloc de requête ; clique dessus pour l'exécuter et voir la réponse dans un panneau à côté.

### Enchaîner les requêtes (authentification automatique)

Chaque fichier commence par une requête `login` nommée (`# @name login`). Les requêtes suivantes réutilisent automatiquement le token qu'elle retourne via `{{login.response.body.$.data.accessToken}}`, sans avoir à le copier-coller à la main. Il faut cependant avoir exécuté `login` au moins une fois dans la session en cours avant d'envoyer une requête qui en dépend — et comme le chaînage n'est valable qu'à l'intérieur d'un même fichier, chaque fichier `.http` a sa propre requête `login`.

⚠️ Le JWT expire après 15 minutes (`Jwt:AccessTokenLifetimeMinutes`)

### Peupler des données de test

Les fichiers `.http` supposent que la base contient déjà un utilisateur de test, des countries et des warehouses. Si ce n'est pas le cas (nouvelle base vide), lance :

```bash
dotnet run --project FutureKawaSiege.API -- --seed
```

```

```
