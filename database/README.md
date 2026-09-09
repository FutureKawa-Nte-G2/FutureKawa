# Déploiement de la stack pays

> **Périmètre** : du message MQTT reçu sur le broker jusqu'à une base prête à être
> requêtée, et le déploiement Kubernetes de cet ensemble. Le firmware du capteur est
> en amont, le backend siège et le frontend en aval.

## Ce que ce dossier contient

| | |
|---|---|
| `deploy/helm/futurekawa-data/` | Chart de la stack **pays** — un release par pays |
| `deploy/helm/futurekawa-siege/` | Chart de la stack **siège** — un seul release, central |
| `deploy/terraform/` | Provisionnement des VMs Proxmox + k3s |
| `Dockerfile.timescaledb` | Image CNPG **+ TimescaleDB** — l'image CNPG standard ne l'embarque pas |
| `infra/mosquitto/` | Configuration du broker |
| `docs/perimetre-donnees.md` | Frontière de responsabilité et contrat de lecture |

**La démo locale n'est pas ici** : le `docker-compose.yml` à la racine du dépôt monte la
stack complète (siège, frontend, Odoo compris). Ce dossier ne couvre que Kubernetes.

## Source de vérité du schéma

`Api/app/models.py` + `Api/alembic/`, conformément à
[ADR-001](../Documentation/adr/001-gouvernance-schema-donnees.md). Il n'y a **pas** de
migrations SQL dans ce dossier : le chart applique Alembic via un Job qui utilise
l'image de l'API. Le schéma ne peut donc pas diverger du code qui le lit.

## Ce que les charts déploient

Deux charts et non un seul : la stack pays se déploie **une fois par pays**, le siège
est **unique**. Les fondre ensemble obligerait à redéployer le siège à chaque pays ajouté.

**`futurekawa-data`** (par pays) :
- **PostgreSQL + TimescaleDB** via CloudNativePG (HA, réplication par streaming)
- **Mosquitto** — broker MQTT
- **API pays** (FastAPI) — 2 réplicas derrière un Service
- **Consumers MQTT** — persistance des relevés et évaluation des seuils (US #32)
- **Job Alembic** — migrations, en hook `post-install`/`post-upgrade`

**`futurekawa-siege`** (central) :
- **PostgreSQL** via CloudNativePG (sans TimescaleDB : le siège ne stocke que l'agrégat journalier)
- **Backend .NET** — 2 réplicas, plus les Jobs `--migrate` et `--seed`
- **Frontend Next.js** — 2 réplicas
- **Odoo** + sa base
- **Ingress** servant le frontend sur `/` et l'API sur `/api`, **sur la même origine** :
  le navigateur n'émet alors aucune requête cross-origin, et le cookie de refresh
  `SameSite=Strict` est bien transmis

## Prérequis cluster

- opérateur **CloudNativePG** (CRDs `postgresql.cnpg.io`) — `make operators`
- image `cnpg-timescaledb` construite et disponible — `make images`
- image de l'API pays disponible — `make images`
- en multi-nœuds, un StorageClass réattachable (Longhorn) pour Mosquitto :
  `local-path` épingle le pod à son nœud et l'ingestion ne survit pas à la perte de la VM

## Déployer

```bash
make images                 # les 4 images
make operators              # CNPG (+ Longhorn)
make deploy PAYS=BR         # stack pays — répéter par pays
make deploy-siege           # stack siège — une seule fois
```

En local, de bout en bout sur un cluster k3d jetable :

```bash
make k3d                    # cluster + images + opérateurs + les deux stacks
curl -H 'Host: futurekawa.localhost' http://localhost/
make k3d-down               # supprime tout
```

> **Le frontend fige l'URL de l'API au build.** `NEXT_PUBLIC_API_BASE_URL` est inlinée
> dans le bundle, pas lue au démarrage : changer `INGRESS_HOST` impose de reconstruire
> l'image (`make images` s'en charge).

## Déploiement continu

Deux étages, dans `.github/workflows/cd-deploy.yml`.

**Environnement éphémère** — automatique, à chaque push et chaque PR vers `develop`.
Construit les quatre images, monte un cluster k3d dans le runner, installe CNPG, applique
les deux charts, puis vérifie par smoke test que le frontend répond, que le login rend un
JWT, et qu'un relevé MQTT hors tolérance produit ses trois effets en base. Un chart qui ne
s'installe pas casse ici, avant la fusion.

**Production** — déclenchement manuel (`workflow_dispatch`, cible `production`).

### Pourquoi un runner auto-hébergé

Le Proxmox n'est joignable que depuis le réseau interne. Un runner GitHub est une machine
éphémère hébergée par Microsoft : **aucun secret ne lui donnera accès à un réseau privé**.
La seule voie propre est un runner qui vit *dans* le réseau — il s'enregistre en connexion
**sortante** vers GitHub, donc rien n'a besoin d'être exposé sur Internet.

### Mise en place (une fois)

1. Sur une VM du réseau interne — idéalement un nœud du cluster :

   ```bash
   # Jeton à récupérer dans Settings > Actions > Runners > New self-hosted runner
   mkdir actions-runner && cd actions-runner
   curl -o runner.tar.gz -L https://github.com/actions/runner/releases/latest/download/actions-runner-linux-x64.tar.gz
   tar xzf runner.tar.gz
   ./config.sh --url https://github.com/<org>/<repo> --token <TOKEN> --labels futurekawa-prod
   sudo ./svc.sh install && sudo ./svc.sh start
   ```

   Le label `futurekawa-prod` est ce que cible `runs-on` — sans lui le job reste en attente.

2. Donner au runner l'accès au cluster, au choix :
   - le kubeconfig est déjà sur la VM (cas d'un nœud du cluster) : rien à faire ;
   - sinon, créer le secret `KUBECONFIG` (base64) dans les paramètres du dépôt :

     ```bash
     base64 -w0 ~/.kube/config    # coller le résultat dans le secret
     ```

3. Lancer : **Actions → CD-DEPLOY → Run workflow → cible `production`**.

Sans kubeconfig ni secret, le job échoue avec un message explicite plutôt que de déployer
dans le vide.

> **État actuel :** ce job n'a jamais été exécuté — l'accès VPN au Proxmox n'est pas encore
> disponible et aucun cluster cible n'existe. Le chemin de déploiement est en revanche
> prouvé : le job éphémère le rejoue intégralement à chaque PR, sur la **même distribution**
> (k3d = k3s dans Docker) que celle installée par le Terraform.

## Vérifier

Le smoke test suppose un capteur **actif** et **assigné à un lot** : un relevé dont le
capteur ne suivait aucun lot au moment de la mesure est délibérément ignoré (cf.
`Api/app/services/ingestion.py`). Poser le minimum :

```sql
INSERT INTO countries VALUES ('...','Brésil','BR',20,3,55,10);   -- seuils : 20°C ±3, 55% ±10
INSERT INTO warehouses VALUES ('...', <country_id>,'Entrepôt Santos','WH-BR-001');
INSERT INTO farms      VALUES ('...', <country_id>,'Fazenda Cerrado','FARM-BR-001');
INSERT INTO batches (batch_id,warehouse_id,farm_id,batch_ref,stored_at,batch_status,is_compliant)
  VALUES ('...', <warehouse_id>, <farm_id>,'BR-2026-0001',CURRENT_DATE-30,'in_stock',true);
INSERT INTO sensors (sensor_id,warehouse_id,code,is_active)
  VALUES ('...', <warehouse_id>,'SENSOR-BR-01',true);
INSERT INTO sensor_assignments (sensor_assignment_id,sensor_id,batch_id,assigned_at,released_at)
  VALUES ('...', <sensor_id>, <batch_id>, NOW()-INTERVAL '30 days', NULL);
```

puis `make k3d-smoke`, qui publie un relevé hors tolérance et affiche les trois effets
attendus : la mesure écrite, le lot passé `is_compliant=false`, la notification créée.

## Contrat capteur

```
topic   : futurekawa/<code capteur>
payload : {"measuredAt":"2026-09-06T12:00:00Z","temp":21.5,"humidity":54.1}
```

Le code du capteur fait autorité **côté topic** et non dans le corps : un firmware ne peut
pas usurper le topic sur lequel le broker l'a autorisé à publier, alors qu'il écrit son
corps librement.

## Pièges rencontrés, et pourquoi ils sont épinglés

- **Le loader TimescaleDB doit être épinglé à la même version que l'extension.** Le paquet
  loader fournit `timescaledb.control`, donc le `default_version`. Non contraint, apt prend
  le dernier publié, le control annonce une version dont aucun script d'installation n'est
  présent, et `CREATE EXTENSION` échoue à l'`initdb` — le cluster ne démarre jamais.
- **Base `-bookworm` explicite.** Le tag `16` nu pointait sur une Debian bullseye dont le
  dépôt de sécurité est expiré : `apt-get update` y échoue en dur.
- **`CREATE EXTENSION timescaledb` est exécutée par le superuser** (`postInitApplicationSQL`)
  et non par les migrations : le rôle applicatif n'a pas le droit de charger une extension.
- **Un seul réplica de consumers.** Deux abonnements partageant un `client_id` MQTT
  écriraient chaque relevé deux fois, faute de shared subscriptions (`$share`).
