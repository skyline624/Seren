# =============================================================================
# Seren Web — Multi-stage Dockerfile
# Build context: src/ui/
# =============================================================================

# Stage 1: Build
FROM node:22-alpine AS build
WORKDIR /app

RUN corepack enable && corepack prepare pnpm@10.0.0 --activate

# Copy workspace root files for dependency resolution
COPY package.json pnpm-workspace.yaml pnpm-lock.yaml .npmrc ./

# Copy all package.json files for workspace resolution
COPY packages/seren-sdk/package.json packages/seren-sdk/
COPY packages/seren-i18n/package.json packages/seren-i18n/
COPY packages/seren-ui-shared/package.json packages/seren-ui-shared/
COPY packages/seren-ui-three/package.json packages/seren-ui-three/
COPY packages/seren-ui-live2d/package.json packages/seren-ui-live2d/
COPY packages/seren-ui-audio/package.json packages/seren-ui-audio/
COPY apps/seren-web/package.json apps/seren-web/

RUN pnpm install --frozen-lockfile

# Copy full source
COPY . .

# Build packages first, then the web app
RUN pnpm build:packages && pnpm -F @seren/web build

# Stage 2: Serve with nginx
FROM nginx:alpine AS runtime

COPY --from=build /app/apps/seren-web/dist /usr/share/nginx/html
COPY apps/seren-web/nginx.conf /etc/nginx/conf.d/default.conf

EXPOSE 80
