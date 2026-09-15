# Lab EPSI values. No secrets here: credentials come from the environment.
nodes = [
  { vmid = 4100, node = "hv-epyc-02", ip = "172.16.146.101" },
  { vmid = 4101, node = "hv-epyc-01", ip = "172.16.146.102" },
  { vmid = 4102, node = "hv-epyc-02", ip = "172.16.146.103" },
]

ssh_public_keys = [
  "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIGDO+N6L9aa/I5ueRWMxvc2ec5bWF+NssSG8lBnbj3CV alexis.coheleach@ecoles-epsi.net",
]
