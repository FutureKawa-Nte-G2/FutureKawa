# Provisioning — k3s HA multi-VM sur Proxmox (phase E)

Provider `bpg/proxmox`. Provisionne **N VMs (défaut 3)** sur **un** hôte Proxmox → cluster
k3s multi-nœuds en **HA etcd embarqué** (nœud 0 `--cluster-init`, les autres rejoignent).
Multi-hôte *logique* : HA au niveau nœud/VM. Le **serveur physique reste un SPOF assumé**,
couvert par les backups hors-VM (diagramme 8 pour la HA multi-hôte réelle).

## Prérequis
- Un nœud Proxmox joignable + un **token API**.
- Datastore `snippets` activé (cloud-init) — Datacenter → Storage → contenu « Snippets ».
- IPs statiques libres (une par VM) : la jonction du cluster vise le nœud 0 à une adresse connue.
- `k3s_token` : `openssl rand -hex 32`.

## Usage
```bash
cp terraform.tfvars.example terraform.tfvars   # adapter (IPs, token, clé SSH)
terraform init
terraform plan
terraform apply
terraform output kubeconfig_hint               # récupérer le kubeconfig du nœud 0
```

## Ensuite — socle applicatif (opérateurs, une fois)
```bash
export KUBECONFIG=./kubeconfig

# Opérateur CloudNativePG (Postgres HA du chart)
helm repo add cnpg https://cloudnative-pg.github.io/charts
helm install cnpg cnpg/cloudnative-pg -n cnpg-system --create-namespace

# Longhorn : StorageClass réattachable pour Mosquitto (rescheduling sur perte de VM ; Postgres se réplique via CNPG)
helm repo add longhorn https://charts.longhorn.io
helm install longhorn longhorn/longhorn -n longhorn-system --create-namespace

# Puis le chart data (image de migrations importée, cf. database/README.md)
helm install fk-br ../helm/futurekawa-data -n futurekawa --set pays=BR
```

## Portée / limites
- **Socle partagé** (référent infra) : ces VMs + k3s + namespaces sont la base ; API/siège/
  frontend y déploient leurs workloads.
- **HA nœud/VM** : survit au crash d'une VM, panne de nœud, upgrade roulant, failover CNPG.
- **SPOF hôte physique assumé** (+ disque unique le cas échéant) → durabilité = backups hors-VM.
  HA multi-hôte réelle (3 hôtes Proxmox + Ceph) = évolution documentée (diagramme 8).
