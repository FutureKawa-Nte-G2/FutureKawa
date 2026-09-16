resource "proxmox_virtual_environment_vm" "k3s" {
  count = length(var.nodes)

  name        = "${var.prefix}-${count.index + 1}"
  vm_id       = var.nodes[count.index].vmid
  node_name   = var.nodes[count.index].node
  pool_id     = var.pool
  description = "FutureKawa MSPR, groupe 1, dev2 - k3s node ${count.index + 1}"
  tags        = ["futurekawa", "mspr", "k3s"]

  # Linked clone: fast and cheap on the shared storage (lab recommendation).
  clone {
    vm_id     = var.template_vmid
    node_name = var.template_node
    full      = false
  }

  agent {
    enabled = true
  }

  # x86-64-v2 is required by some container images; supported by both EPYC hosts.
  cpu {
    cores = var.cores
    type  = "x86-64-v2-AES"
  }

  memory {
    dedicated = var.memory_mb
  }

  disk {
    datastore_id = var.datastore
    interface    = "scsi0"
    size         = var.disk_gb
  }

  # Template 937 has a serial console only.
  serial_device {
    device = "socket"
  }

  vga {
    type = "serial0"
  }

  network_device {
    bridge = var.bridge
  }

  initialization {
    datastore_id = var.datastore
    # Template 937 carries its cloud-init drive on ide0.
    interface = "ide0"
    upgrade   = false

    ip_config {
      ipv4 {
        address = "${var.nodes[count.index].ip}/16"
        gateway = var.gateway
      }
    }

    user_account {
      username = var.ci_user
      keys     = var.ssh_public_keys
    }
  }

  stop_on_destroy = true
}
