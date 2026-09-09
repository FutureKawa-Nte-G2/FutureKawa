# Stack Docker FutureKawa. `make` seul affiche les cibles disponibles.

SHELL := /bin/bash
COMPOSE := docker compose
ODOO_DB_NAME ?= futurekawa

.DEFAULT_GOAL := help

.PHONY: help env up db ps logs down clean validate migrate odoo-module odoo-webhook-compose odoo-webhook-native \
        test test-back test-front test-api test-smoke simulate

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

simulate: ## Génère et publie une campagne de relevés simulés (2 capteurs, 48 h)
	@echo "Le firmware embarqué ne publie pas encore sur le broker : ce simulateur"
	@echo "tient sa place pour exercer la chaîne. Voir tools/sensor_simulator.py."
	@mkdir -p .simdata
	python3 tools/sensor_simulator.py \
	  --sensors SENSOR-BR-01,SENSOR-BR-02 \
	  --drift SENSOR-BR-02 \
	  --out .simdata
	@for s in SENSOR-BR-01 SENSOR-BR-02; do \
	  echo "publication de $$s..."; \
	  $(COMPOSE) exec -T mosquitto mosquitto_pub -h localhost -q 1 \
	    -t "futurekawa/$$s" -l < .simdata/$$s.jsonl; \
	done
	@echo "Publication terminée."

test-smoke: ## Bout en bout : fixture, campagne simulée, vérification des effets
	@echo "== 1/3 fixture : capteurs actifs assignés à des lots =="
	@$(COMPOSE) exec -T warehouse-db psql -q -U $${WAREHOUSE_DB_USER:-futurekawa} \
	  -d $${WAREHOUSE_DB_NAME:-futurekawa_br} -v ON_ERROR_STOP=1 \
	  < tools/fixture_country_smoke.sql
	@echo "== 2/3 campagne de relevés simulés =="
	@$(MAKE) --no-print-directory simulate
	@echo "attente du traitement par les consumers..."
	@sleep 15
	@echo "== 3/3 effets attendus en base =="
	@$(COMPOSE) exec -T warehouse-db psql -U $${WAREHOUSE_DB_USER:-futurekawa} \
	  -d $${WAREHOUSE_DB_NAME:-futurekawa_br} \
	  -c 'SELECT s.code, count(*) AS releves, round(max(m.meas_temp),1) AS temp_max FROM measurements m JOIN sensors s USING (sensor_id) GROUP BY s.code ORDER BY s.code;' \
	  -c 'SELECT batch_ref, is_compliant FROM batches ORDER BY batch_ref;' \
	  -c 'SELECT notification_type, count(*) FROM notifications GROUP BY notification_type;'
	@echo ""
	@echo "Attendu : BR-2026-0001 conforme, BR-2026-0002 non conforme, et UNE"
	@echo "seule notification malgre les nombreux releves hors seuil."

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
