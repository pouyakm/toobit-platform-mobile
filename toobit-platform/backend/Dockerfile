# syntax=docker/dockerfile:1.7
# Pin by digest in CI: `docker buildx imagetools inspect mcr.microsoft.com/dotnet/sdk:8.0` then replace the tags below
# with tag@sha256:<digest>. The CD workflow fails if a base image is not digest-pinned (see .github/workflows/cd.yml).
ARG SDK_IMAGE=mcr.microsoft.com/dotnet/sdk:8.0
ARG RUNTIME_IMAGE=mcr.microsoft.com/dotnet/aspnet:8.0

FROM ${SDK_IMAGE} AS build
WORKDIR /src
COPY Toobit.sln ./
COPY src/ src/
RUN dotnet restore src/Api/Api.csproj
RUN dotnet publish src/Api/Api.csproj -c Release -o /app --no-restore /p:UseAppHost=false

FROM ${RUNTIME_IMAGE} AS runtime
WORKDIR /app
# Non-root: the aspnet image ships a predefined unprivileged "app" user (uid 1654).
USER app
ENV ASPNETCORE_URLS=http://+:8080 DOTNET_EnableDiagnostics=0
COPY --from=build --chown=app:app /app .
EXPOSE 8080
# Health is probed by docker-compose / k8s against /health/live and /health/ready (the image has no curl).
ENTRYPOINT ["dotnet", "Api.dll"]
