# Déploiement sur le labo EPSI — tutoriel

Ce qui tourne : 3 VM Debian sur le Proxmox de l'école, un cluster **k3s**, et dedans la stack pays **Brésil** (release Helm `fk`), le **siège** et **Odoo** (release `siege`), dans le namespace `futurekawa`.

Tout se fait **depuis le réseau de l'école** (ou le VPN Netbird du labo).

Sommaire :

- [0. Avant de commencer](#0-avant-de-commencer)
- [1. Installer tout de zéro](#1-installer-tout-de-zéro)
- [2. Mettre à jour une application](#2-mettre-à-jour-une-application)
- [3. Redémarrer](#3-redémarrer)
- [4. Couper sans rien perdre](#4-couper-sans-rien-perdre)
- [5. Supprimer](#5-supprimer)
- [6. Dépannage](#6-dépannage)

---

## 0. Avant de commencer

### Repères

| Quoi | Valeur |
| --- | --- |
| VM | `i1-dev2-mspr-grp1-k3s-1` à `-3`, VMID 4100 à 4102 |
| IP | 172.16.146.101, .102, .103 |
| Utilisateur SSH | `futurekawa` (clé SSH uniquement) |
| Front siège | https://futurekawa.172.16.146.101.nip.io |
| Odoo | https://odoo.172.16.146.101.nip.io |
| API pays BR | https://br.172.16.146.101.nip.io/docs |

| Release Helm | Deployments | Base CloudNativePG |
| --- | --- | --- |
| `fk` (pays BR) | `fk-futurekawa-data-api`, `-consumers`, `-mosquitto` | `fk-futurekawa-data-postgres` |
| `siege` | `siege-futurekawa-siege-api`, `-frontend`, `-odoo`, `-odoo-db` | `siege-futurekawa-siege-db` |

### Outils sur son poste

`kubectl`, `helm` (v4), `python3`, et pour l'installation seulement `terraform` (≥ 1.6).

### Fichiers hors dépôt

Ils ne vont **jamais** dans Git. Les demander à Alexis.

| Fichier | Sert à |
| --- | --- |
| `kubeconfig-labo` | piloter le cluster avec `kubectl` et `helm` |
| `helm-labo-secrets.yaml` | clés et mots de passe passés à Helm |
| `proxmox.env` | identifiants Proxmox (installation et suppression des VM) |
| `terraform.tfstate` | état Terraform : sans lui, Terraform ne sait plus quelles VM il a créées |
| `k3s-token` | ajouter un nœud au cluster |

Dans la suite, on suppose ces fichiers dans `~/.config/futurekawa/`, et :

```bash
export KUBECONFIG=~/.config/futurekawa/kubeconfig-labo
SECRETS=~/.config/futurekawa/helm-labo-secrets.yaml
```

### Obtenir l'accès SSH aux VM

Envoyer sa **clé publique** (`~/.ssh/id_ed25519.pub`) à quelqu'un qui a déjà accès. Il l'ajoute sur les 3 VM :

```bash
for ip in 101 102 103; do
  ssh futurekawa@172.16.146.$ip "echo 'ssh-ed25519 AAAA... prenom' >> ~/.ssh/authorized_keys"
done
```

### Branches

Les charts et les valeurs du labo sont sur `infra/proxmox-labo` (jusqu'à leur fusion). Les images applicatives se construisent depuis `develop`.

---

## 1. Installer tout de zéro

> Déjà fait le 14/09. À refaire seulement si les VM ont été supprimées.

### 1.1 Créer les 3 VM (Terraform)

```bash
cd database/deploy/terraform
cp ~/.config/futurekawa/terraform-labo/terraform.tfstate .   # si on reprend un état existant
set -a; . ~/.config/futurekawa/proxmox.env; set +a
export PROXMOX_VE_USERNAME="$PM_USER" PROXMOX_VE_PASSWORD="$PM_PASS"; unset PM_PASS
terraform init
terraform plan -var-file=labo-epsi.tfvars -out=labo.tfplan   # relire : 3 à créer, 0 à modifier, 0 à supprimer
terraform apply labo.tfplan
cp terraform.tfstate ~/.config/futurekawa/terraform-labo/    # sauvegarder l'état
```

Pour que plusieurs personnes aient l'accès SSH dès la création, ajouter leurs clés dans `ssh_public_keys` de `labo-epsi.tfvars` avant l'`apply`.

**Vérifier** : `ssh futurekawa@172.16.146.101 hostname` répond `i1-dev2-mspr-grp1-k3s-1` (idem .102 et .103).

### 1.2 Installer k3s (3 nœuds, etcd embarqué)

```bash
umask 077; openssl rand -hex 32 > ~/.config/futurekawa/k3s-token
TOKEN=$(cat ~/.config/futurekawa/k3s-token)

# Nœud 1 : crée le cluster
ssh futurekawa@172.16.146.101 "curl -sfL https://get.k3s.io | sudo INSTALL_K3S_CHANNEL=stable K3S_TOKEN=$TOKEN sh -s - server \
  --cluster-init --node-ip 172.16.146.101 --tls-san 172.16.146.101 --write-kubeconfig-mode 600"

# Nœuds 2 et 3 : rejoignent le cluster
for ip in 102 103; do
  ssh futurekawa@172.16.146.$ip "curl -sfL https://get.k3s.io | sudo INSTALL_K3S_CHANNEL=stable K3S_TOKEN=$TOKEN sh -s - server \
    --server https://172.16.146.101:6443 --node-ip 172.16.146.$ip --tls-san 172.16.146.101 --write-kubeconfig-mode 600"
done

# Récupérer le kubeconfig
ssh futurekawa@172.16.146.101 "sudo cat /etc/rancher/k3s/k3s.yaml" \
  | sed 's#https://127.0.0.1:6443#https://172.16.146.101:6443#' > ~/.config/futurekawa/kubeconfig-labo
chmod 600 ~/.config/futurekawa/kubeconfig-labo
```

**Vérifier** : `kubectl get nodes` montre 3 nœuds `Ready`.

### 1.3 Installer Docker sur le nœud 1

Il n'y a pas de registre d'images : on construit les images sur le nœud 1.

```bash
ssh futurekawa@172.16.146.101 "curl -fsSL https://get.docker.com | sudo sh"
```

### 1.4 Construire et importer les 5 images

```bash
# Envoyer le code applicatif (develop) et database/ (pas encore sur develop) sur le nœud 1
git fetch origin
git archive origin/develop | ssh futurekawa@172.16.146.101 'rm -rf fk && mkdir fk && tar -x -C fk'
git archive origin/infra/proxmox-labo database | ssh futurekawa@172.16.146.101 'tar -x -C fk'

# Construire
ssh futurekawa@172.16.146.101 'cd fk && set -e
  sudo docker build -f database/Dockerfile.timescaledb -t futurekawa/cnpg-timescaledb:16-ts database/
  sudo docker build -t futurekawa/country-api:labo Api/
  sudo docker build -t futurekawa/siege-api:labo backend/
  sudo docker build -t futurekawa/frontend:labo \
    --build-arg NEXT_PUBLIC_API_BASE_URL=https://futurekawa.172.16.146.101.nip.io \
    --build-arg NEXT_PUBLIC_USE_MOCKS=false frontend/
  sudo docker build -f database/Dockerfile.odoo -t futurekawa/odoo:labo odoo-addons/'

# Importer chaque image sur les 3 nœuds
for img in futurekawa/cnpg-timescaledb:16-ts futurekawa/country-api:labo futurekawa/siege-api:labo \
           futurekawa/frontend:labo futurekawa/odoo:labo; do
  for ip in 101 102 103; do
    ssh futurekawa@172.16.146.101 "sudo docker save $img" \
      | ssh futurekawa@172.16.146.$ip "sudo k3s ctr images import -" && echo "$ip $img OK"
  done
done
```

Compter 10 à 15 minutes (le frontend et le backend sont les plus longs).

### 1.5 Installer l'opérateur de bases (CloudNativePG)

```bash
git switch infra/proxmox-labo && cd database
make operators INSTALL_LONGHORN=false
```

**Vérifier** : `kubectl -n cnpg-system get pods` → 1 pod `Running`.

### 1.6 Préparer le fichier de secrets

À faire une seule fois (sinon, reprendre celui existant) :

```bash
umask 077
K=$(openssl rand -hex 32)
cat > ~/.config/futurekawa/helm-labo-secrets.yaml <<EOF
siegeApi:
  jwtSecret: "$(openssl rand -hex 32)"
postgres:
  roles:
    apiReadPassword: "$(openssl rand -hex 16)"
localApiKey: "$K"
localApi:
  countries:
    BR:
      apiKey: "$K"
      baseUrl: http://fk-futurekawa-data-api:8000
odoo:
  adminPassword: "$(openssl rand -hex 16)"
  masterPassword: "$(openssl rand -hex 16)"
  webhookToken: "$(openssl rand -hex 32)"
EOF
```

`localApiKey` et `localApi.countries.BR.apiKey` doivent avoir **la même valeur**.

### 1.7 Déployer la stack pays, puis le siège

Depuis `database/` :

```bash
helm upgrade --install fk deploy/helm/futurekawa-data -n futurekawa --create-namespace \
  --wait=legacy --timeout 10m -f deploy/helm/values-labo.yaml -f $SECRETS

helm upgrade --install siege deploy/helm/futurekawa-siege -n futurekawa \
  --wait=legacy --timeout 12m -f deploy/helm/values-labo.yaml -f $SECRETS
```

- **`--wait=legacy` est obligatoire** avec Helm 4 : l'attente par défaut ne reconnaît pas l'état « prêt » de CloudNativePG et bloque jusqu'au timeout.
- Le déploiement du siège lance aussi les Jobs de migration, de seed et `odoo-init` (installation du module Odoo, compte admin, webhook).

**Vérifier** :

```bash
kubectl -n futurekawa get pods          # tout en Running, les Jobs en Completed
kubectl -n futurekawa get cluster       # 2 bases "Cluster in healthy state"
```

### 1.8 Ouvrir les accès web

```bash
kubectl apply -f deploy/k8s/labo-ingress.yaml
```

**Vérifier** : les 3 URL du tableau de la [section 0](#repères) répondent, et on se connecte au front avec `test@futurekawa.com` / `TestPass123`.

### 1.9 Données de test et relevés simulés

```bash
PG=$(kubectl -n futurekawa get pod -l cnpg.io/instanceRole=primary,cnpg.io/cluster=fk-futurekawa-data-postgres -o jsonpath='{.items[0].metadata.name}')
git show origin/develop:tools/fixture_country_smoke.sql \
  | kubectl -n futurekawa exec -i $PG -c postgres -- psql -q -U postgres -d futurekawa -v ON_ERROR_STOP=1
```

Puis publier des relevés : voir [6. Dépannage → simuler des relevés](#simuler-des-relevés-capteurs).

---

## 2. Mettre à jour une application

Exemple avec le backend siège. Remplacer `v2` par un **nouveau tag à chaque fois** : avec le même tag, Kubernetes garde l'ancienne image.

### 2.1 Construire la nouvelle image

```bash
git fetch origin
git archive origin/develop | ssh futurekawa@172.16.146.101 'rm -rf fk && mkdir fk && tar -x -C fk'
git archive origin/infra/proxmox-labo database | ssh futurekawa@172.16.146.101 'tar -x -C fk'   # utile pour Odoo
ssh futurekawa@172.16.146.101 'cd fk && sudo docker build -t futurekawa/siege-api:v2 backend/'
```

| Application | Commande de build (dans `fk/`) |
| --- | --- |
| Backend siège | `sudo docker build -t futurekawa/siege-api:v2 backend/` |
| API pays + consumers | `sudo docker build -t futurekawa/country-api:v2 Api/` |
| Frontend | `sudo docker build -t futurekawa/frontend:v2 --build-arg NEXT_PUBLIC_API_BASE_URL=https://futurekawa.172.16.146.101.nip.io --build-arg NEXT_PUBLIC_USE_MOCKS=false frontend/` |
| Odoo (module) | `sudo docker build -f database/Dockerfile.odoo -t futurekawa/odoo:v2 odoo-addons/` |

### 2.2 L'importer sur les 3 nœuds

```bash
IMG=futurekawa/siege-api:v2
for ip in 101 102 103; do
  ssh futurekawa@172.16.146.101 "sudo docker save $IMG" \
    | ssh futurekawa@172.16.146.$ip "sudo k3s ctr images import -" && echo "$ip OK"
done
```

### 2.3 Mettre à jour la release

Depuis `database/` :

```bash
helm upgrade siege deploy/helm/futurekawa-siege -n futurekawa --wait=legacy --timeout 10m \
  -f deploy/helm/values-labo.yaml -f $SECRETS \
  --set siegeApi.image=futurekawa/siege-api:v2
```

| Application | Release | Paramètre |
| --- | --- | --- |
| Backend siège | `siege` | `--set siegeApi.image=...` |
| Frontend | `siege` | `--set frontend.image=...` |
| Odoo | `siege` | `--set odoo.image=...` |
| API pays + consumers | `fk` (chart `futurekawa-data`) | `--set countryApi.image=... --set consumers.image=...` |

> **Attention** : un `helm upgrade` lancé plus tard **sans** ce `--set` remet l'image de `values-labo.yaml` (`:labo`). Pour garder la nouvelle version, repasser le `--set`, ou mettre à jour le tag dans `values-labo.yaml`.

**Vérifier** : `kubectl -n futurekawa get pods` → les nouveaux pods sont `Running`, puis tester dans le navigateur.

**Revenir en arrière** : `helm -n futurekawa rollback siege` (revient à la révision précédente).

---

## 3. Redémarrer

### Une application

```bash
kubectl -n futurekawa rollout restart deploy/siege-futurekawa-siege-api
kubectl -n futurekawa rollout status deploy/siege-futurekawa-siege-api
```

Noms des Deployments : voir le tableau de la [section 0](#repères).

### Toute la stack applicative

```bash
kubectl -n futurekawa rollout restart deploy
```

### Une base de données (CloudNativePG)

Rarement utile. Supprimer le pod d'une instance : CloudNativePG le recrée. Commencer par le réplica ; si on supprime le primaire, un réplica prend le relais.

```bash
kubectl -n futurekawa get pods -l cnpg.io/cluster=fk-futurekawa-data-postgres -L cnpg.io/instanceRole
kubectl -n futurekawa delete pod fk-futurekawa-data-postgres-2
```

### Une VM

Une VM à la fois, pour garder le quorum etcd (2 nœuds sur 3).

```bash
ssh futurekawa@172.16.146.102 "sudo reboot"
kubectl get nodes    # attendre que le nœud revienne Ready avant de passer au suivant
```

---

## 4. Couper sans rien perdre

### Option A — éteindre les 3 VM (recommandé)

Libère la RAM du Proxmox pour les autres promos. Les données restent sur les disques.

**Éteindre** :

```bash
for ip in 103 102 101; do ssh futurekawa@172.16.146.$ip "sudo poweroff"; done
```

**Rallumer** : interface Proxmox (https://proxmox.labo.loc) → pool `I1` → sélectionner chaque VM `i1-dev2-mspr-grp1-k3s-*` → **Start**. Démarrer les 3 en même temps.

**Vérifier** (compter 2 à 3 minutes) :

```bash
kubectl get nodes                        # 3 Ready
kubectl -n futurekawa get pods           # tout revient en Running
kubectl -n futurekawa get cluster        # bases "healthy"
```

> Ne pas lancer `terraform apply` pendant que les VM sont éteintes : Terraform voudrait les redémarrer.

### Option B — arrêter seulement l'applicatif

Les VM et k3s restent allumés.

**Arrêter** :

```bash
kubectl -n futurekawa scale deploy --all --replicas=0
kubectl -n futurekawa annotate cluster fk-futurekawa-data-postgres siege-futurekawa-siege-db --overwrite cnpg.io/hibernation=on
```

**Relancer** :

```bash
kubectl -n futurekawa annotate cluster fk-futurekawa-data-postgres siege-futurekawa-siege-db --overwrite cnpg.io/hibernation=off
kubectl -n futurekawa scale deploy fk-futurekawa-data-api siege-futurekawa-siege-api siege-futurekawa-siege-frontend --replicas=2
kubectl -n futurekawa scale deploy fk-futurekawa-data-consumers fk-futurekawa-data-mosquitto siege-futurekawa-siege-odoo siege-futurekawa-siege-odoo-db --replicas=1
```

---

## 5. Supprimer

> **Irréversible** : les données (bases pays, siège, Odoo) sont perdues.

### 5.1 Supprimer l'applicatif, garder le cluster

Depuis `database/` :

```bash
kubectl delete -f deploy/k8s/labo-ingress.yaml
helm -n futurekawa uninstall siege fk --wait
kubectl -n futurekawa delete pvc --all
kubectl delete namespace futurekawa
```

Pour réinstaller : reprendre à l'[étape 1.7](#17-déployer-la-stack-pays-puis-le-siège).

### 5.2 Tout supprimer, VM comprises

Nécessite `proxmox.env` et **l'état Terraform** (`terraform.tfstate`).

```bash
cd database/deploy/terraform
cp ~/.config/futurekawa/terraform-labo/terraform.tfstate .
set -a; . ~/.config/futurekawa/proxmox.env; set +a
export PROXMOX_VE_USERNAME="$PM_USER" PROXMOX_VE_PASSWORD="$PM_PASS"; unset PM_PASS
terraform plan -destroy -var-file=labo-epsi.tfvars    # relire : 3 à supprimer, et rien d'autre
terraform destroy -var-file=labo-epsi.tfvars
```

Puis nettoyer son poste :

```bash
for ip in 101 102 103; do ssh-keygen -R 172.16.146.$ip; done
rm ~/.config/futurekawa/kubeconfig-labo ~/.config/futurekawa/k3s-token
```

Sans l'état Terraform : supprimer les VM à la main dans l'interface Proxmox (arrêter la VM → **More** → **Remove**), en vérifiant bien le VMID (4100, 4101, 4102) pour ne pas toucher aux VM des autres.

---

## 6. Dépannage

### Voir les logs

```bash
kubectl -n futurekawa logs -l app.kubernetes.io/component=siege-api --tail=50
```

Composants : `siege-api`, `frontend`, `odoo`, `country-api`, `consumers`, `mosquitto`.

### Un pod ne démarre pas

```bash
kubectl -n futurekawa describe pod <nom-du-pod> | tail -20
```

- `ErrImageNeverPull` / `ImagePullBackOff` : l'image n'a pas été importée sur ce nœud → refaire l'[étape 2.2](#22-limporter-sur-les-3-nœuds).
- `CrashLoopBackOff` : lire les logs du pod.

### `helm upgrade` bloque jusqu'au timeout

Oubli de `--wait=legacy`. Si la release reste en `pending-install` ou `pending-upgrade` :

```bash
helm -n futurekawa history siege
helm -n futurekawa rollback siege     # revient à la dernière révision valide
```

### Simuler des relevés capteurs

Mosquitto n'est pas exposé hors du cluster : on publie depuis son pod. Un relevé n'est gardé que si son capteur est connu en base ([étape 1.9](#19-données-de-test-et-relevés-simulés)).

```bash
git show origin/develop:tools/sensor_simulator.py > /tmp/sensor_simulator.py
python3 /tmp/sensor_simulator.py --sensors SENSOR-BR-01,SENSOR-BR-02 --drift SENSOR-BR-02 --out /tmp
MQ=$(kubectl -n futurekawa get pod -l app.kubernetes.io/component=mosquitto -o jsonpath='{.items[0].metadata.name}')
for s in SENSOR-BR-01 SENSOR-BR-02; do
  kubectl -n futurekawa exec -i $MQ -- mosquitto_pub -h localhost -q 1 -t futurekawa/$s -l < /tmp/$s.jsonl
done
```

### Forcer la synchro des mesures vers le siège

```bash
H=https://futurekawa.172.16.146.101.nip.io
TOKEN=$(curl -sk -H 'Content-Type: application/json' -d '{"email":"test@futurekawa.com","password":"TestPass123"}' \
  $H/api/auth/login | python3 -c 'import sys,json;print(json.load(sys.stdin)["data"]["accessToken"])')
curl -sk -X POST -H "Authorization: Bearer $TOKEN" $H/api/measurements/sync
```

### Limites connues

- 2 VM sur 3 sont sur l'hyperviseur `hv-epyc-02` : s'il tombe, le cluster perd son quorum.
- Pas de sauvegarde. Les volumes d'Odoo et de Mosquitto sont liés à un nœud (`local-path`).
- Les VM ont 4 Go de RAM : éviter de construire plusieurs images en même temps sur le nœud 1.
