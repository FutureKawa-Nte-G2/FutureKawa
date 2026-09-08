# syntax=docker/dockerfile:1

# Frontend Next.js du siège. Contexte de build : ./frontend

ARG NODE_VERSION=22

FROM node:${NODE_VERSION}-alpine AS deps
WORKDIR /app
# Le lockfile seul d'abord : npm ci reste en cache tant qu'il ne bouge pas.
COPY package.json package-lock.json ./
RUN npm ci

FROM node:${NODE_VERSION}-alpine AS build
WORKDIR /app
COPY --from=deps /app/node_modules node_modules/
COPY . .

# Les NEXT_PUBLIC_* sont inlinées dans le bundle au moment du build, pas lues au
# démarrage : les passer en ARG est la seule façon de les faire arriver jusqu'au
# navigateur. Conséquence : changer l'URL de l'API impose de rebuilder l'image.
ARG NEXT_PUBLIC_API_BASE_URL
ARG NEXT_PUBLIC_USE_MOCKS
ENV NEXT_PUBLIC_API_BASE_URL=${NEXT_PUBLIC_API_BASE_URL} \
    NEXT_PUBLIC_USE_MOCKS=${NEXT_PUBLIC_USE_MOCKS} \
    NEXT_TELEMETRY_DISABLED=1

RUN npm run build

FROM node:${NODE_VERSION}-alpine AS runtime
WORKDIR /app
ENV NODE_ENV=production \
    NEXT_TELEMETRY_DISABLED=1

# next.config.ts n'active pas output:"standalone" — on embarque donc .next, le
# public et les node_modules de production plutôt qu'un serveur autonome. Éditer
# la config du frontend pour gagner en taille d'image relève de leur PR, pas de
# l'intégration.
COPY --from=build /app/node_modules node_modules/
COPY --from=build /app/.next .next/
COPY --from=build /app/public public/
COPY --from=build /app/package.json ./
COPY --from=build /app/next.config.ts ./

USER node
EXPOSE 3000
CMD ["npm", "run", "start"]
