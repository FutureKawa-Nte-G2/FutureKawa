# Stack Docker FutureKawa. `make` seul affiche les cibles disponibles.

SHELL := /bin/bash
COMPOSE := docker compose
ODOO_DB_NAME ?= futurekawa

.DEFAULT_GOAL := help

.PHONY: help env up db ps logs down clean validate migrate odoo-module odoo-webhook-compose odoo-webhook-native \
        test test-back test-front test-api test-smoke

help: ## Affiche cette aide
	@grep -E '^[a-zA-Z_-]+:.*?## .*$$' $(MAKEFILE_LIST) \
	  | awk 'BEGIN {FS = ":.*?## "}; {printf "\033[36m%-10s\033[0m %s\n", $$1, $$2}'

env: ## Crée .env depuis .env.example (n'écrase jamais un .env existant)
	@[ -f .env ] || cp .env.example .env
	@missing=$$(comm -23 \
	  <(grep -oE '^[A-Z_]+=' .env.example | sort) \
	  <(grep -oE '^[A-Z_]+=' .env | sort)); \
	if [ -n "$$missing" ]; then \
	  echo "Variables absentes de ton .env (voir .env.example) :"; \
	  echo "$$missing" | sed 's/=$$//;s/^/  - /'; \
	  exit 1; \
	fi
	@echo ".env prêt"

up: env ## Démarre toute la stack (reconstruit les images si besoin)
	$(COMPOSE) up -d --build
	@$(COMPOSE) ps

db: env ## Démarre uniquement les bases de données
	$(COMPOSE) up -d odoo-db siege-db warehouse-db
	@$(COMPOSE) ps

ps: ## Liste l'état des services
	@$(COMPOSE) ps

logs: ## Suit les logs de tous les services
	$(COMPOSE) logs -f

down: ## Arrête la stack (les données sont conservées)
	$(COMPOSE) down

clean: ## Arrête la stack ET supprime les volumes (perte des données)
	$(COMPOSE) down -v

# ---- Tests ----
# Chaque suite tourne dans un conteneur jetable : aucune dépendance à installer
# sur la machine (ni .NET, ni Node, ni Python), et la même version que la CI.

test: test-back test-front test-api ## Lance les trois suites de tests
	@echo ""
	@echo "Les trois suites sont passées."

test-back: ## Tests du backend siège (.NET)
	docker run --rm -v "$(PWD)/backend":/src:ro -w /tmp/bk \
	  mcr.microsoft.com/dotnet/sdk:10.0 \
	  sh -c "cp -r /src/. /tmp/bk && dotnet test --nologo -v q"

test-front: ## Tests du frontend (Vitest)
	docker run --rm -v "$(PWD)/frontend":/src:ro -w /tmp/fr \
	  node:24-alpine \
	  sh -c "cp -r /src/. /tmp/fr && npm ci --silent && npm run test"

test-api: ## Tests de l'API pays (pytest)
	docker run --rm -v "$(PWD)/Api":/src:ro -w /tmp/api \
	  python:3.13-slim \
	  sh -c "cp -r /src/. /tmp/api && pip install -q -r requirements-dev.txt && python -m pytest tests -q"

test-smoke: ## Bout en bout : publie un relevé MQTT et vérifie toute la chaîne
	@echo "Pré-requis : la stack doit tourner (make up) et la base pays contenir"
	@echo "un capteur actif assigné à un lot. Voir TESTING.md."
	@TS=$$(date -u +%Y-%m-%dT%H:%M:%SZ); \
	 $(COMPOSE) exec -T mosquitto mosquitto_pub -h localhost -q 1 \
	   -t "futurekawa/$${SENSOR:-SENSOR-BR-01}" \
	   -m "{\"measuredAt\":\"$$TS\",\"temp\":34.0,\"humidity\":88.0}"
	@echo "Relevé hors tolérance publié, attente du traitement..."
	@sleep 12
	@$(COMPOSE) exec -T warehouse-db psql -U $${WAREHOUSE_DB_USER:-futurekawa} -d $${WAREHOUSE_DB_NAME:-futurekawa_br} \
	  -c 'SELECT s.code, m.meas_date, m.meas_temp, m.meas_humidity FROM measurements m JOIN sensors s USING (sensor_id) ORDER BY m.meas_date DESC LIMIT 3;' \
	  -c 'SELECT batch_ref, is_compliant FROM batches ORDER BY 1;' \
	  -c 'SELECT notification_type, created_at FROM notifications ORDER BY created_at DESC LIMIT 3;'

validate: ## Vérifie la syntaxe du docker-compose
	@$(COMPOSE) config -q && echo "docker-compose OK"

migrate: env ## Applique les migrations EF du siège
	$(COMPOSE) run --rm --build siege-migrate

odoo-module: ## Met à jour le module Odoo (réécrit aussi ses paramètres système)
	$(COMPOSE) exec odoo odoo -u future_kawa_erp -d $(ODOO_DB_NAME) --stop-after-init

odoo-webhook-compose: ## Pointe le webhook Odoo vers le backend conteneurisé
	@$(COMPOSE) exec -T odoo-db psql -U $${POSTGRES_USER:-odoo} -d $(ODOO_DB_NAME) -c \
	  "UPDATE ir_config_parameter SET value='http://siege-api:8080/api/integration/odoo/orders' WHERE key='future_kawa.webhook_url';"

odoo-webhook-native: ## Pointe le webhook Odoo vers un backend lancé avec dotnet run
	@$(COMPOSE) exec -T odoo-db psql -U $${POSTGRES_USER:-odoo} -d $(ODOO_DB_NAME) -c \
	  "UPDATE ir_config_parameter SET value='https://host.docker.internal:55648/api/integration/odoo/orders' WHERE key='future_kawa.webhook_url';"
