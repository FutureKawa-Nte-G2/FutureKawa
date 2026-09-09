# --- Accès Proxmox ---
variable "proxmox_endpoint" {
  type        = string
  description = "URL de l'API Proxmox, ex. https://192.168.1.10:8006/"
}

variable "proxmox_api_token" {
  type        = string
  sensitive   = true
  description = "Token API : user@realm!tokenid=uuid"
}

variable "proxmox_insecure" {
  type        = bool
  default     = false
  description = "true si certificat auto-signé"
}

variable "proxmox_ssh_username" {
  type    = string
  default = "root"
}

variable "proxmox_node" {
  type        = string
  description = "Nom du nœud Proxmox cible (ex. pve)"
}

# --- Stockage / réseau ---
variable "datastore_id" {
  type    = string
  default = "local-lvm"
}

variable "snippets_datastore_id" {
  type        = string
  default     = "local"
  description = "Datastore acceptant les snippets (cloud-init) et images"
}

variable "network_bridge" {
  type    = string
  default = "vmbr0"
}

# --- Cluster k3s multi-VM (multi-hôte logique sur 1 serveur physique ; SPOF hôte assumé) ---
variable "node_count" {
  type        = number
  default     = 3
  description = "Nombre de VMs = nœuds k3s serveurs. ≥3 pour le QUORUM ETCD du plan de contrôle k3s (l'anti-affinité CNPG à 2 instances, elle, n'exige que 2 nœuds)."
  validation {
    condition     = var.node_count >= 1 && var.node_count % 2 == 1
    error_message = "node_count doit être impair et >= 1 (quorum etcd : 1, 3, 5...)."
  }
}

variable "cluster_name" {
  type    = string
  default = "futurekawa-k3s"
}

variable "vm_id_base" {
  type        = number
  default     = 9000
  description = "vm_id du nœud i = vm_id_base + i"
}

variable "vm_cpu_cores" {
  type    = number
  default = 4
}

variable "vm_memory_mb" {
  type    = number
  default = 8192
}

variable "vm_disk_gb" {
  type    = number
  default = 40
}

# IPs statiques REQUISES en multi-nœuds : les nœuds 1..n rejoignent le nœud 0 à une adresse connue.
variable "vm_ipv4_addresses" {
  type        = list(string)
  description = "Une IP CIDR par nœud, ex. [\"192.168.1.50/24\",\"192.168.1.51/24\",\"192.168.1.52/24\"]. Longueur = node_count."
}

variable "vm_gateway" {
  type        = string
  description = "Passerelle du sous-réseau (ex. 192.168.1.1)"
}

# --- cloud-init / k3s ---
variable "ci_user" {
  type    = string
  default = "futurekawa"
}

variable "ssh_public_keys" {
  type        = list(string)
  default     = []
  description = "Clés SSH publiques autorisées sur les VMs"
}

variable "ubuntu_image_url" {
  type    = string
  default = "https://cloud-images.ubuntu.com/noble/current/noble-server-cloudimg-amd64.img"
}

variable "k3s_channel" {
  type    = string
  default = "stable"
}

variable "k3s_token" {
  type        = string
  sensitive   = true
  description = "Secret partagé de jonction du cluster k3s. Générer : openssl rand -hex 32"
}

variable "namespaces" {
  type        = list(string)
  default     = ["futurekawa"]
  description = "Namespaces du socle partagé, pré-créés au boot (par le nœud 0)"
}
