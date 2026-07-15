# Standards techniques — FutureKawaSiege (Back .NET 10)

> **Stack cible** — Certains packages listés ci-dessous ne sont pas encore installés (FluentValidation, Mapster, Serilog, EF Core, NSubstitute). Ce document décrit la stack **visée** ; les packages seront ajoutés au fil de l'implémentation.
>
> Source de vérité unique pour les agents et tout contributeur humain. **À lire en premier** par tout agent avant d'agir.

## 1. Stack imposée

| Sujet                | Imposé                                                                            | Notes                                                          |
| -------------------- | --------------------------------------------------------------------------------- | -------------------------------------------------------------- |
| Langage              | C# / .NET 10                                                                      | `nullable enabled` partout                                     |
| Architecture         | Clean Architecture (couches ci-dessous)                                           | Pas de logique métier hors `Business`                          |
| API                  | Controllers ASP.NET héritant de `ControllerBase`. Réponses via `ActionResult<T>`. |                                                                |
| Validation           | **FluentValidation**                                                              | Un validator par DTO d'entrée                                  |
| Mapping DTO ↔ Entity | **Mapster**                                                                       | Pas de mapping manuel inutile                                  |
| Logging              | **Serilog** côté hôte, **`ILogger<T>`** dans le code applicatif                   | Jamais `Console.WriteLine`, jamais Serilog en direct           |
| ORM                  | **EF Core**                                                                       | Migrations versionnées                                         |
| Persistance          | **Repository custom**                                                             | `DbContext` jamais exposé à `Business`                         |
| Auth par défaut      | **JWT Bearer**                                                                    | 99% des endpoints                                              |
| Tests                | **xUnit v3 + NSubstitute**                                                        | FluentAssertions si déjà présent dans le projet de tests cible |
| Coverage min         | **80%**                                                                           | Non régressif vs `develop`                                     |
| Style                | EditorConfig + analyzers .NET                                                     | `EnforceCodeStyleInBuild=true`, `AnalysisLevel=latest`         |

## 2. Architecture en couches (stricte)

```
FutureKawaSiege.API            — HTTP binding, auth, validation, mapping. AUCUNE logique métier.
FutureKawaSiege.Business       — Services métier, règles, orchestration des repositories.
FutureKawaSiege.Data           — Entités EF Core, configurations, DbContext, migrations.
FutureKawaSiege.Commons        — DTOs, modèles d'API, helpers partagés.
*.Tests                        — xUnit + NSubstitute, un projet de tests par couche.
```

Toute logique placée dans la mauvaise couche est un **défaut bloquant**.

## 3. Conventions de code

- **Naming** : PascalCase pour types/méthodes/propriétés, camelCase pour locales/paramètres, `_camelCase` pour champs privés (sauf si EditorConfig dit autre).
- **Async** : suffixe `Async` sur toute méthode asynchrone. Jamais `.Result` ni `.Wait()`.
- **Nullable** : pas de `!` (null-forgiving) injustifié.
- **`var`** : autorisé quand le type est évident, à défaut explicite (suivre EditorConfig).
- **Catch** : jamais silencieux. Toujours log ou rethrow.
- **DI** : injection par constructeur classique côté .NET.
- **Pas de secret en dur** : `appsettings.*.json` propres, secrets via configuration externe.
- **Réponses API** : utiliser les méthodes standard de `ControllerBase` (`Ok()`, `NotFound()`, `BadRequest()`, `Unauthorized()`, etc.).
- **Documentation des interfaces** : chaque méthode d'interface (dans les dossiers `Abstraction/` et `Repositories/`) doit avoir un `<summary>` XML documentant son rôle, ses paramètres et sa valeur de retour. Chaque implémentation de ces méthodes doit porter un `/// <inheritdoc/>` pour hériter du résumé.

## 4. Tests

- Framework : **xUnit v3**. Mocking : **NSubstitute**. Pas d'autre framework introduit.
- Nommage strict : `MethodName_Should_Behavior_When_Condition`.
- Un test = une assertion logique (plusieurs `Assert` OK si même comportement).
- Pas de dépendance horloge/réseau/FS sans mock.
- Tests déterministes, indépendants, parallélisables.
- Coverage cible **≥ 80%** sur les fichiers modifiés.

## 5. Migrations EF Core

- Nom explicite : `AddXxx`, `UpdateYyy`, `RemoveZzz`.
- Script SQL généré revu (pas de `DROP` accidentel).
- Compatible avec données existantes (backfill prévu si besoin).
- Migration toujours présente dans le plan de l'archi avant exécution.

## 6. Sécurité (OWASP)

- Validation FluentValidation **avant** tout traitement.
- Pas de SQL brut non paramétré.
- Pas de secret/token en dur.
- Logs ne fuitent ni mots de passe, ni tokens, ni PII.
- Exceptions non gérées ne fuitent pas de stack traces côté client (gestion via middleware d'exception).

## 7. Build & vérification locale

```bash
dotnet restore
dotnet build
dotnet test
```

Coverage : `coverlet.runsettings` à la racine de la solution.

---

> Les standards techniques du projet vivent dans [`standards.md`](./standards.md) — source de vérité unique pour les agents et tout contributeur.
>
> Les instructions comportementales pour les agents IA sont dans [`AGENTS.md`](./AGENTS.md).
