output "nodes" {
  value = {
    for vm in proxmox_virtual_environment_vm.k3s :
    vm.name => { vmid = vm.vm_id, node = vm.node_name }
  }
}
