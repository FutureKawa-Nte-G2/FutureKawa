# Stack Docker FutureKawa. `make` seul affiche les cibles disponibles.

SHELL := /bin/bash
COMPOSE := docker compose
ODOO_DB_NAME ?= futurekawa

.DEFAULT_GOAL := help

.PHONY: help env up db ps logs down clean validate migrate odoo-module odoo-webhook-compose odoo-webhook-native

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
	$(COMPOSE) up -d odoo-db siege-db
	@$(COMPOSE) ps

ps: ## Liste l'état des services
	@$(COMPOSE) ps

logs: ## Suit les logs de tous les services
	$(COMPOSE) logs -f

down: ## Arrête la stack (les données sont conservées)
	$(COMPOSE) down

clean: ## Arrête la stack ET supprime les volumes (perte des données)
	$(COMPOSE) down -v

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
