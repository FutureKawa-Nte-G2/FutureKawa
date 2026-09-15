# Déploiement de la stack pays FutureKawa. `make` affiche les cibles.
#
# La démo locale ne vit plus ici : c'est le docker-compose de la racine du dépôt
# (`make up` à la racine) qui monte la stack complète, siège et frontend compris.
# Ce Makefile ne couvre que le déploiement Kubernetes.
SHELL := /bin/bash
CHART       := deploy/helm/futurekawa-data
SIEGE_CHART := deploy/helm/futurekawa-siege
TF          := deploy/terraform

REGISTRY  ?= ghcr.io/futurekawa-nte-g2
TS_IMAGE  ?= $(REGISTRY)/cnpg-timescaledb:16-ts
# Même image que le compose : API et consumers partagent le binaire, seule la
# commande change — et le Job de migration l'utilise aussi, ce qui garantit que
# le schéma ne diverge jamais du code qui le lit (ADR-001).
API_IMAGE ?= futurekawa/country-api:dev
SIEGE_IMAGE    ?= futurekawa/siege-api:dev
FRONTEND_IMAGE ?= futurekawa/frontend:dev

# Hôte servant frontend et API sur la même origine. Le frontend inline cette
# valeur dans son bundle au build : la changer impose de reconstruire l'image,
# ce que fait la cible `images`.
INGRESS_HOST ?= futurekawa.localhost

PAYS ?= BR
NS   ?= futurekawa

K3D_CLUSTER      ?= futurekawa
MOSQUITTO_SC     ?= longhorn   # k3d : passer MOSQUITTO_SC= (local-path)
INSTALL_LONGHORN ?= true       # k3d : passer INSTALL_LONGHORN=false

.DEFAULT_GOAL := help

help: ## Affiche cette aide
	@grep -E '^[a-zA-Z_-]+:.*?## ' $(MAKEFILE_LIST) \
	  | awk 'BEGIN{FS=":.*?## "}{printf "  \033[36m%-14s\033[0m %s\n",$$1,$$2}'

# ---- Validation ----
validate: ## Valide les charts Helm et le Terraform
	@helm lint $(CHART) >/dev/null && helm template t $(CHART) >/dev/null && echo "helm pays OK"
	@helm lint $(SIEGE_CHART) >/dev/null && helm template t $(SIEGE_CHART) >/dev/null && echo "helm siège OK"
	@cd $(TF) && terraform fmt -check >/dev/null && terraform validate >/dev/null && echo "terraform OK"

# ---- Images ----
images: ## Build les 4 images (TimescaleDB, API pays, siège, frontend)
	docker build -f Dockerfile.timescaledb -t $(TS_IMAGE) .
	docker build -t $(API_IMAGE) ../Api
	docker build -t $(SIEGE_IMAGE) ../backend
	docker build -t $(FRONTEND_IMAGE) \
	  --build-arg NEXT_PUBLIC_API_BASE_URL=http://$(INGRESS_HOST) \
	  --build-arg NEXT_PUBLIC_USE_MOCKS=false ../frontend

# ---- k8s ----
operators: ## Installe CNPG (+ Longhorn si INSTALL_LONGHORN=true) ; KUBECONFIG requis
	helm repo add cnpg https://cloudnative-pg.github.io/charts >/dev/null
	helm repo update cnpg >/dev/null
	helm upgrade --install cnpg cnpg/cloudnative-pg -n cnpg-system --create-namespace --wait
	@if [ "$(INSTALL_LONGHORN)" = "true" ]; then \
	  helm repo add longhorn https://charts.longhorn.io >/dev/null; helm repo update longhorn >/dev/null; \
	  helm upgrade --install longhorn longhorn/longhorn -n longhorn-system --create-namespace; \
	else echo "Longhorn ignoré (INSTALL_LONGHORN=false)"; fi

deploy: ## Déploie la stack d'un pays (make deploy PAYS=BR)
	helm upgrade --install fk-$(shell echo $(PAYS) | tr '[:upper:]' '[:lower:]') $(CHART) \
	  -n $(NS) --create-namespace --wait --timeout 10m \
	  --set pays=$(PAYS) \
	  --set postgres.imageName=$(TS_IMAGE) \
	  --set countryApi.image=$(API_IMAGE) \
	  --set consumers.image=$(API_IMAGE) \
	  --set mosquitto.storageClass=$(MOSQUITTO_SC)

deploy-siege: ## Déploie la stack siège (un seul release, central)
	helm upgrade --install siege $(SIEGE_CHART) \
	  -n $(NS) --create-namespace --wait --timeout 10m \
	  --set siegeApi.image=$(SIEGE_IMAGE) \
	  --set frontend.image=$(FRONTEND_IMAGE) \
	  --set ingress.host=$(INGRESS_HOST)

infra: ## Terraform : provisionne les VMs + k3s (nécessite terraform.tfvars)
	cd $(TF) && terraform init && terraform apply

k8s: operators deploy deploy-siege ## Opérateurs + stack pays + stack siège

# ---- Chemin complet en local (k3d) ----
k3d-up: ## Crée un cluster k3d local (HTTP sur :80, MQTT sur :1883)
	k3d cluster create $(K3D_CLUSTER) --servers 1 --agents 2 \
	  -p '80:80@loadbalancer' -p '1883:1883@loadbalancer' --wait

k3d-load: images ## Importe les images dans le cluster k3d
	k3d image import $(TS_IMAGE) $(API_IMAGE) $(SIEGE_IMAGE) $(FRONTEND_IMAGE) -c $(K3D_CLUSTER)

k3d: k3d-up k3d-load ## Cluster + images + opérateurs + déploiement, en local
	$(MAKE) operators INSTALL_LONGHORN=false
	$(MAKE) deploy MOSQUITTO_SC=
	$(MAKE) deploy-siege
	@echo ">>> Frontend : curl -H 'Host: $(INGRESS_HOST)' http://localhost/"
	@echo ">>> 'make k3d-smoke' pour tester la chaîne IoT, 'make k3d-down' pour supprimer."

k3d-smoke: ## Publie un relevé hors tolérance et vérifie toute la chaîne
	@PGPOD=$$(kubectl -n $(NS) get pod -l cnpg.io/instanceRole=primary -o jsonpath='{.items[0].metadata.name}'); \
	 MPOD=$$(kubectl -n $(NS) get pod -l app.kubernetes.io/component=mosquitto -o jsonpath='{.items[0].metadata.name}'); \
	 echo "Le smoke test suppose un capteur actif assigné à un lot (cf. README §Vérifier)."; \
	 TS=$$(date -u +%Y-%m-%dT%H:%M:%SZ); \
	 kubectl -n $(NS) exec $$MPOD -- mosquitto_pub -h localhost -q 1 \
	   -t "futurekawa/$${SENSOR:-SENSOR-BR-01}" \
	   -m "{\"measuredAt\":\"$$TS\",\"temp\":34.0,\"humidity\":88.0}"; \
	 sleep 12; \
	 kubectl -n $(NS) exec $$PGPOD -- psql -U postgres -d futurekawa \
	   -c 'SELECT s.code, m.meas_date, m.meas_temp, m.meas_humidity FROM measurements m JOIN sensors s USING (sensor_id) ORDER BY m.meas_date DESC LIMIT 5;' \
	   -c 'SELECT batch_ref, is_compliant FROM batches ORDER BY 1;' \
	   -c 'SELECT notification_type, created_at FROM notifications ORDER BY created_at DESC LIMIT 5;'

k3d-down: ## Supprime le cluster k3d local
	k3d cluster delete $(K3D_CLUSTER)

.PHONY: help validate images operators deploy deploy-siege infra k8s \
        k3d-up k3d-load k3d k3d-smoke k3d-down
