# Provisioning — 3 VMs k3s sur le Proxmox du labo EPSI

Provider `bpg/proxmox`. Crée 3 VMs par **clone lié** du template cloud-init de la
promo I1, dans le pool `I1`. Tout passe par l'API Proxmox : les étudiants n'ont
pas de SSH sur les hyperviseurs, donc ni snippet cloud-init ni téléchargement
d'image. k3s s'installe ensuite en SSH sur les VMs (voir plus bas).

| VM | VMID | Hyperviseur | IP |
|---|---|---|---|
| `i1-dev2-mspr-grp1-k3s-1` | 4100 | hv-epyc-02 | 172.16.146.101 |
| `i1-dev2-mspr-grp1-k3s-2` | 4101 | hv-epyc-01 | 172.16.146.102 |
| `i1-dev2-mspr-grp1-k3s-3` | 4102 | hv-epyc-02 | 172.16.146.103 |

Règles du labo respectées : nom préfixé par la promo, VMID dans 4000-4999 (pair
sur hv-epyc-02, impair sur hv-epyc-01), IP dans 172.16.140.0-149.255, /16,
passerelle 172.16.255.254, template 937 `deb-trixie-cloud-i1`, storage `I1-12T`.

## Prérequis
- Être sur le réseau du labo (ou VPN Netbird) : `proxmox.labo.loc` doit répondre.
- Un compte LDAP de la promo. Identifiants **hors dépôt**, dans l'environnement.
- Une clé SSH : l'authentification par mot de passe est désactivée dans le template.

## Usage
```bash
export PROXMOX_VE_USERNAME='i1-dev@openldap'
read -rs PROXMOX_VE_PASSWORD && export PROXMOX_VE_PASSWORD
terraform init
terraform plan -var-file=labo-epsi.tfvars -out=labo.tfplan
terraform apply labo.tfplan
```

## Ensuite — k3s HA (etcd embarqué)
```bash
TOKEN=$(openssl rand -hex 32)   # à conserver hors dépôt
ssh futurekawa@172.16.146.101 "curl -sfL https://get.k3s.io | sudo K3S_TOKEN=$TOKEN sh -s - server \
  --cluster-init --node-ip 172.16.146.101 --tls-san 172.16.146.101"
for ip in 102 103; do
  ssh futurekawa@172.16.146.$ip "curl -sfL https://get.k3s.io | sudo K3S_TOKEN=$TOKEN sh -s - server \
    --server https://172.16.146.101:6443 --node-ip 172.16.146.$ip --tls-san 172.16.146.101"
done
ssh futurekawa@172.16.146.101 sudo cat /etc/rancher/k3s/k3s.yaml \
  | sed 's#127.0.0.1#172.16.146.101#' > kubeconfig
```

Puis, depuis `database/` : `make operators INSTALL_LONGHORN=false`, images
importées sur chaque nœud (`docker save | sudo k3s ctr images import -`), et
déploiement avec `deploy/helm/values-labo.yaml` + un fichier de secrets hors dépôt.
Avec Helm 4, passer `--wait=legacy` : l'attente par défaut ne reconnaît pas
l'état prêt d'un cluster CloudNativePG et bloque jusqu'au timeout.

## Limites
- **2 VMs sur hv-epyc-02** : la perte de cet hyperviseur fait perdre le quorum etcd.
- **Stockage `local-path`** : les volumes hors CloudNativePG (Mosquitto, Odoo)
  sont liés à leur nœud. Longhorn est trop lourd pour des VMs de 4 Go.
- **Sauvegardes** : non configurées (`postgres.backup.enabled: false`).
