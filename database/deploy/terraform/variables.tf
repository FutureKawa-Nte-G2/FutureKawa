variable "proxmox_endpoint" {
  type    = string
  default = "https://proxmox.labo.loc/"
}

variable "proxmox_insecure" {
  type        = bool
  default     = true
  description = "Self-signed certificate on labo.loc"
}

variable "pool" {
  type    = string
  default = "I1"
}

variable "datastore" {
  type    = string
  default = "I1-12T"
}

variable "template_vmid" {
  type        = number
  default     = 937
  description = "deb-trixie-cloud-i1 (Debian 13, cloud-init)"
}

variable "template_node" {
  type    = string
  default = "hv-epyc-01"
}

variable "bridge" {
  type    = string
  default = "vmbr0"
}

variable "gateway" {
  type    = string
  default = "172.16.255.254"
}

variable "prefix" {
  type        = string
  default     = "i1-dev2-mspr-grp1-k3s"
  description = "Lab rule: VM names must start with the class"
}

variable "nodes" {
  description = "One entry per k3s node. Even VMID on hv-epyc-02, odd on hv-epyc-01 (lab rule)."
  type = list(object({
    vmid = number
    node = string
    ip   = string
  }))
}

variable "cores" {
  type    = number
  default = 2
}

variable "memory_mb" {
  type    = number
  default = 4096
}

variable "disk_gb" {
  type    = number
  default = 30
}

variable "ci_user" {
  type    = string
  default = "futurekawa"
}

variable "ssh_public_keys" {
  type        = list(string)
  description = "Password SSH login is disabled in the lab templates"
}
