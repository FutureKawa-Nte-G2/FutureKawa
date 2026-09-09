output "nodes" {
  description = "Nœuds k3s : nom + IP statique configurée"
  value = {
    for i, vm in proxmox_virtual_environment_vm.k3s :
    vm.name => var.vm_ipv4_addresses[i]
  }
}

output "server_ip" {
  description = "IP du nœud 0 (point de jonction + source du kubeconfig)"
  value       = local.server_ip
}

output "kubeconfig_hint" {
  description = "Récupérer le kubeconfig depuis le nœud 0 (portable Linux/macOS : pipe sed, pas de -i)"
  value       = "ssh ${var.ci_user}@${local.server_ip} 'sudo cat /etc/rancher/k3s/k3s.yaml' | sed 's/127.0.0.1/${local.server_ip}/' > kubeconfig"
}
