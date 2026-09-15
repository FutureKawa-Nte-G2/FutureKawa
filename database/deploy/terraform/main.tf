locals {
  # Le nœud 0 initialise le cluster ; son IP (sans masque) sert de point de jonction aux autres.
  server_ip = split("/", var.vm_ipv4_addresses[0])[0]
}

# Image cloud Ubuntu (base des VMs).
resource "proxmox_download_file" "ubuntu" {
  content_type = "iso"
  datastore_id = var.snippets_datastore_id
  node_name    = var.proxmox_node
  url          = var.ubuntu_image_url
  file_name    = "noble-server-cloudimg-amd64.img"
  overwrite    = false
}

# cloud-init par nœud : nœud 0 = k3s --cluster-init, nœuds 1..n = join.
resource "proxmox_virtual_environment_file" "cloud_init" {
  count        = var.node_count
  content_type = "snippets"
  datastore_id = var.snippets_datastore_id
  node_name    = var.proxmox_node

  source_raw {
    file_name = "${var.cluster_name}-${count.index}-cloud-init.yaml"
    data = templatefile("${path.module}/cloud-init.tftpl", {
      vm_name         = "${var.cluster_name}-${count.index}"
      ci_user         = var.ci_user
      ssh_public_keys = var.ssh_public_keys
      k3s_channel     = var.k3s_channel
      k3s_token       = var.k3s_token
      is_first        = count.index == 0
      server_ip       = local.server_ip
      apply_manifests = count.index == 0
      namespaces      = var.namespaces
    })
  }
}

# N VMs = N nœuds k3s serveurs (HA etcd embarqué). Multi-hôte logique sur 1 serveur physique.
resource "proxmox_virtual_environment_vm" "k3s" {
  count     = var.node_count
  name      = "${var.cluster_name}-${count.index}"
  node_name = var.proxmox_node
  vm_id     = var.vm_id_base + count.index

  agent {
    enabled = true
  }

  cpu {
    cores = var.vm_cpu_cores
    type  = "host"
  }

  memory {
    dedicated = var.vm_memory_mb
  }

  disk {
    datastore_id = var.datastore_id
    file_id      = proxmox_download_file.ubuntu.id
    interface    = "scsi0"
    size         = var.vm_disk_gb
  }

  initialization {
    ip_config {
      ipv4 {
        address = var.vm_ipv4_addresses[count.index]
        gateway = var.vm_gateway
      }
    }
    user_data_file_id = proxmox_virtual_environment_file.cloud_init[count.index].id
  }

  network_device {
    bridge = var.network_bridge
  }

  lifecycle {
    precondition {
      condition     = length(var.vm_ipv4_addresses) == var.node_count
      error_message = "vm_ipv4_addresses doit contenir exactement node_count entrées (une IP CIDR par VM)."
    }
    ignore_changes = [initialization[0].user_data_file_id]
  }
}
