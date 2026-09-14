# Notification email des alertes — ce qu'il faudra ajouter pour Kubernetes

Le backend siège (`siege-api`) envoie un email à la réception de chaque
alerte (`AlertEmailService`, cf. [README.md](../README.md#email-de-notification-dalerte)).
En local/Docker Compose, la configuration passe par des variables d'env
`Email__*` sur le service `siege-api`, avec [Mailtrap Email Testing](https://mailtrap.io)
(sandbox, offre gratuite) comme relais pour le POC : les emails sont
capturés dans un inbox de test, jamais réellement délivrés — n'importe quel
destinataire est accepté, pas de domaine à vérifier.

Il n'existe pas encore de manifests Kubernetes dans ce repo (seul
`docker-compose.yml` couvre le déploiement aujourd'hui). Ce document liste ce
qu'il faudra ajouter le jour où `siege-api` sera déployé sur le cluster
k8s/Proxmox, en plus de ce qui existe déjà pour la partie email.

## Rappel : les clés de config

`AlertEmailService` lit, via `IConfiguration` :

| Clé                        | Sensible ? | Exemple (POC Mailtrap)        |
| --------------------------- | :--------: | ------------------------------ |
| `Email:Smtp:Host`           | Non        | `sandbox.smtp.mailtrap.io`     |
| `Email:Smtp:Port`           | Non        | `2525`                         |
| `Email:Smtp:EnableSsl`      | Non        | `true`                         |
| `Email:Smtp:Username`       | **Oui**    | (identifiant de l'inbox)       |
| `Email:Smtp:Password`       | **Oui**    | (mot de passe de l'inbox)      |
| `Email:From:Address`        | Non        | `alerts@futurekawa.local`      |
| `Email:From:Name`           | Non        | `FutureKawa Alerts`            |
| `Email:AlertRecipients`     | Non        | `["test@futurekawa.local"]`    |

ASP.NET Core mappe ces clés sur des variables d'environnement avec `__` comme
séparateur de niveau (déjà utilisé pour `Jwt__Secret`, `Odoo__Password`,
etc. dans `docker-compose.yml`). Un tableau se mappe avec un index numérique :
`Email__AlertRecipients__0`, `Email__AlertRecipients__1`, ...

## Ce qui change en Kubernetes

### 1. Séparer secrets et config

Contrairement au `docker-compose.yml` actuel (où tout part d'un `.env` non
committé mais où secrets et non-secrets sont mélangés dans le même bloc
`environment`), en k8s il faut distinguer explicitly :

- **`Secret`** pour `Email:Smtp:Username` et `Email:Smtp:Password` (les
  identifiants de l'inbox Mailtrap) — comme le `Secret` qui portera déjà
  `Jwt__Secret` et les mots de passe DB.
- **`ConfigMap`** (ou `env` en clair dans le `Deployment`) pour le reste
  (`Host`, `Port`, `EnableSsl`, `From:*`, `AlertRecipients`) : ce ne sont pas
  des données sensibles.

Cluster de POC, pas de prod réelle : pas besoin de Vault / sealed-secrets /
external-secrets pour ça. Un manifest `Secret` classique (`stringData`) avec
les identifiants de l'inbox Mailtrap du POC, committé dans le repo d'infra du
cluster, suffit — ce sont des identifiants de test qui ne délivrent aucun
vrai email.

Exemple (à adapter à la structure réelle des manifests le jour où ils
existeront), avec les mêmes valeurs que le `.env` local :

```yaml
apiVersion: v1
kind: Secret
metadata:
  name: siege-api-email-secret
type: Opaque
stringData:
  Email__Smtp__Username: "change-me" # identifiant de l'inbox Mailtrap du POC
  Email__Smtp__Password: "change-me" # mot de passe de l'inbox Mailtrap du POC
---
apiVersion: v1
kind: ConfigMap
metadata:
  name: siege-api-email-config
data:
  Email__Smtp__Host: "sandbox.smtp.mailtrap.io"
  Email__Smtp__Port: "2525"
  Email__Smtp__EnableSsl: "true"
  Email__From__Address: "alerts@futurekawa.local"
  Email__From__Name: "FutureKawa Alerts"
  Email__AlertRecipients__0: "test@futurekawa.local"
```

Et dans le `Deployment` de `siege-api` :

```yaml
        envFrom:
          - configMapRef:
              name: siege-api-email-config
          - secretRef:
              name: siege-api-email-secret
```

> Si plusieurs destinataires sont nécessaires, ajouter une clé
> `Email__AlertRecipients__1`, `__2`, etc. — il n'y a pas de syntaxe "liste"
> native en `ConfigMap`/`Secret`, chaque entrée est une clé séparée.

### 2. Egress réseau (cluster sur Proxmox)

Le pod `siege-api` doit pouvoir joindre le relais SMTP en sortant du cluster
(port `2525`/`587` selon le choix, plus DNS). Si le cluster applique une
`NetworkPolicy` par défaut restrictive (deny-all egress), il faudra une
règle explicite pour `siege-api`, par exemple :

```yaml
apiVersion: networking.k8s.io/v1
kind: NetworkPolicy
metadata:
  name: siege-api-allow-smtp-egress
spec:
  podSelector:
    matchLabels:
      app: siege-api
  policyTypes: [Egress]
  egress:
    - to: []
      ports:
        - protocol: TCP
          port: 2525 # ou 587 selon le port SMTP choisi
        - protocol: UDP
          port: 53 # DNS, nécessaire pour résoudre le host SMTP
```

À vérifier aussi côté Proxmox/réseau du cluster : si un firewall périmétrique
bloque le trafic sortant par défaut, le port SMTP choisi doit être
explicitement autorisé en sortie (comme n'importe quel autre appel HTTP
sortant du cluster, ex. Odoo webhook).

### 3. Rotation et redémarrage

Changer les valeurs d'un `Secret`/`ConfigMap` ne redémarre pas
automatiquement le pod (pas de "reload" natif) — un `kubectl rollout
restart deployment/siege-api` est nécessaire après une rotation de mot de
passe SMTP, sauf si un contrôleur type
[Reloader](https://github.com/stakater/Reloader) est déjà en place sur le
cluster pour d'autres secrets (auquel cas l'ajouter à ceux-ci suffit).

### 4. Passage du POC à la prod

Mailtrap Email Testing (sandbox) ne délivre jamais réellement les emails —
c'est fait pour la démo/les tests, pas pour de vrais destinataires. Avant une
mise en production réelle (hors cadre du MSPR), il faudra remplacer les
valeurs `Email:Smtp:*` par un vrai relais (SMTP interne au Proxmox, ou un
fournisseur comme Mailgun/SES/Gmail avec un compte applicatif dédié) — seule
la valeur des clés change, aucun code à toucher côté `AlertEmailService`.
