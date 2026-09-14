# Credentials come from the environment, never from files:
#   PROXMOX_VE_USERNAME, PROXMOX_VE_PASSWORD
# No SSH block: students have no shell on the hypervisors, so everything
# goes through the API (clone + cloud-init fields, no snippet upload).
provider "proxmox" {
  endpoint = var.proxmox_endpoint
  insecure = var.proxmox_insecure
}
