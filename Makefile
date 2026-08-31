# Stack Docker FutureKawa. `make` seul affiche les cibles disponibles.

SHELL := /bin/bash
COMPOSE := docker compose

.DEFAULT_GOAL := help

.PHONY: help env up db ps logs down clean validate

help: ## Affiche cette aide
	@grep -E '^[a-zA-Z_-]+:.*?## .*$$' $(MAKEFILE_LIST) \
	  | awk 'BEGIN {FS = ":.*?## "}; {printf "\033[36m%-10s\033[0m %s\n", $$1, $$2}'

env: ## Crée .env depuis .env.example (n'écrase jamais un .env existant)
	@[ -f .env ] || cp .env.example .env
	@echo ".env prêt"

up: env ## Démarre toute la stack
	$(COMPOSE) up -d
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
