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
