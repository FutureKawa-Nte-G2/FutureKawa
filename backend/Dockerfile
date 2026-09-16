# syntax=docker/dockerfile:1

# One image, three uses:
#   dotnet FutureKawaSiege.API.dll             -> the API
#   dotnet FutureKawaSiege.API.dll --migrate   -> EF migrations, then exit
#   dotnet FutureKawaSiege.API.dll --seed      -> demo dataset, then exit
# Build context: ./backend

ARG DOTNET_VERSION=10.0

FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS build
WORKDIR /src

# .csproj files only first: the restore stays cached as long as no dependency
# changes, even when all the code has changed.
COPY FutureKawaSiege.Commons/FutureKawaSiege.Commons.csproj   FutureKawaSiege.Commons/
COPY FutureKawaSiege.Data/FutureKawaSiege.Data.csproj         FutureKawaSiege.Data/
COPY FutureKawaSiege.Business/FutureKawaSiege.Business.csproj FutureKawaSiege.Business/
COPY FutureKawaSiege.API/FutureKawaSiege.API.csproj           FutureKawaSiege.API/

# The API project, not the solution: its ProjectReferences pull in Business,
# Data and Commons, and leave the two test projects out.
RUN dotnet restore FutureKawaSiege.API/FutureKawaSiege.API.csproj

COPY . .
RUN dotnet publish FutureKawaSiege.API/FutureKawaSiege.API.csproj \
      --configuration Release --no-restore --output /app/publish -p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS runtime
WORKDIR /app

# The aspnet image has no HTTP client, and the compose healthcheck needs one.
RUN apt-get update \
 && apt-get install -y --no-install-recommends curl \
 && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

# The --seed container runs in Development, whose Serilog configuration
# writes to ./logs: without this writable directory, the logger fails at
# startup, before it even reaches the seeder.
RUN mkdir -p /app/logs && chown -R app:app /app/logs

# Do not set ASPNETCORE_HTTPS_PORTS or an https ASPNETCORE_URLS: without a
# bound HTTPS endpoint, UseHttpsRedirection() stays inert. Setting one would
# redirect every request to a closed port.
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=8080

USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "FutureKawaSiege.API.dll"]