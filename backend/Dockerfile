# syntax=docker/dockerfile:1

# Une image, trois usages :
#   dotnet FutureKawaSiege.API.dll             -> l'API
#   dotnet FutureKawaSiege.API.dll --migrate   -> migrations EF, puis sortie
#   dotnet FutureKawaSiege.API.dll --seed      -> jeu de démo, puis sortie
# Contexte de build : ./backend

ARG DOTNET_VERSION=10.0

FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS build
WORKDIR /src

# Les .csproj seuls d'abord : le restore reste en cache tant qu'aucune
# dépendance ne change, même quand tout le code a changé.
COPY FutureKawaSiege.Commons/FutureKawaSiege.Commons.csproj   FutureKawaSiege.Commons/
COPY FutureKawaSiege.Data/FutureKawaSiege.Data.csproj         FutureKawaSiege.Data/
COPY FutureKawaSiege.Business/FutureKawaSiege.Business.csproj FutureKawaSiege.Business/
COPY FutureKawaSiege.API/FutureKawaSiege.API.csproj           FutureKawaSiege.API/

# Le projet API, pas la solution : ses ProjectReference tirent Business, Data
# et Commons, et laissent les deux projets de tests dehors.
RUN dotnet restore FutureKawaSiege.API/FutureKawaSiege.API.csproj

COPY . .
RUN dotnet publish FutureKawaSiege.API/FutureKawaSiege.API.csproj \
      --configuration Release --no-restore --output /app/publish -p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS runtime
WORKDIR /app

# L'image aspnet n'a pas de client HTTP, et le healthcheck compose en a besoin.
RUN apt-get update \
 && apt-get install -y --no-install-recommends curl \
 && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

# Le conteneur --seed tourne en Development, dont la configuration Serilog
# écrit dans ./logs : sans ce dossier inscriptible, le logger échoue au
# démarrage, avant même d'atteindre le seeder.
RUN mkdir -p /app/logs && chown -R app:app /app/logs

# Ne pas définir ASPNETCORE_HTTPS_PORTS ni une ASPNETCORE_URLS en https :
# sans endpoint HTTPS lié, UseHttpsRedirection() reste inerte. Le renseigner
# ferait rediriger toutes les requêtes vers un port fermé.
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=8080

USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "FutureKawaSiege.API.dll"]
