# syntax=docker/dockerfile:1.7
ARG NODE_IMAGE=node:22-alpine
ARG NGINX_IMAGE=nginxinc/nginx-unprivileged:1.27-alpine
FROM ${NODE_IMAGE} AS build
WORKDIR /w
COPY package.json package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY . .
RUN npm run build

# nginx-unprivileged runs as a non-root user (uid 101) on port 8080.
FROM ${NGINX_IMAGE} AS runtime
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /w/dist /usr/share/nginx/html
EXPOSE 8080
